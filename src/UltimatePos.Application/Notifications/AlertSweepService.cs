using UltimatePos.Application.Reports;
using UltimatePos.Domain.Entities;

namespace UltimatePos.Application.Notifications
{
    /// <summary>
    /// Raises the alerts that are about a STATE rather than a moment — stock below its reorder level, a batch nearing or past expiry,
    /// an order past its delivery date, a till payment nobody has matched — and closes alerts whose condition has gone away. Run for
    /// one business at a time, with that business in scope. Low stock is also raised immediately by the stock ledger when a sale
    /// crosses the line; the sweep catches everything else (and anything that predates this feature).
    /// </summary>
    public class AlertSweepService
    {
        private readonly IAlertSweepRepository _sweep;
        private readonly INotificationRepository _notifications;
        private readonly ISystemEventRecorder _events;
        private readonly NotificationSettings _settings;

        public AlertSweepService(IAlertSweepRepository sweep, INotificationRepository notifications, ISystemEventRecorder events,
            NotificationSettings settings)
        {
            _sweep = sweep;
            _notifications = notifications;
            _events = events;
            _settings = settings;
        }

        /// <returns>How many new alerts were raised.</returns>
        public async Task<int> RunAsync()
        {
            var open = (await _notifications.GetOpenAlertKeysAsync()).ToHashSet(StringComparer.Ordinal);
            var stillTrue = new HashSet<string>(StringComparer.Ordinal);
            var raised = 0;

            void Raise(SystemEventDraft draft)
            {
                stillTrue.Add(draft.AlertKey!);
                if (open.Contains(draft.AlertKey!))
                    return;
                _events.Record(draft);
                raised++;
            }

            // Stock at or below reorder level.
            foreach (var row in await _sweep.GetLowStockAsync())
            {
                var unit = row.UnitSymbol is null ? string.Empty : " " + row.UnitSymbol;
                if (row.QuantityOnHand <= 0)
                    Raise(new SystemEventDraft(SystemEventTypes.StockOutOfStock,
                        $"{row.ProductName} is out of stock (reorder level {row.ReorderLevel:0.##}{unit}).",
                        EventSeverity.Critical, "Product", row.ProductId, new { row.QuantityOnHand, row.ReorderLevel },
                        AlertKey: AlertKeys.StockOut(row.ProductId)));
                else
                    Raise(new SystemEventDraft(SystemEventTypes.StockBelowReorderLevel,
                        $"{row.ProductName} is down to {row.QuantityOnHand:0.##}{unit} (reorder level {row.ReorderLevel:0.##}{unit}).",
                        EventSeverity.Warning, "Product", row.ProductId, new { row.QuantityOnHand, row.ReorderLevel },
                        AlertKey: AlertKeys.StockLow(row.ProductId)));
            }

            // Batches expired, or expiring within the warning window.
            var today = ReportPeriod.TodayAt(ReportPeriod.DefaultUtcOffsetMinutes);
            foreach (var batch in await _sweep.GetExpiringBatchesAsync(today.AddDays(_settings.ExpiryWarningDays)))
            {
                var quantity = $"{batch.QuantityOnHand:0.##}{(batch.UnitSymbol is null ? string.Empty : " " + batch.UnitSymbol)}";
                var days = batch.ExpiryDate.DayNumber - today.DayNumber;
                if (days < 0)
                    Raise(new SystemEventDraft(SystemEventTypes.BatchExpired,
                        $"Batch {batch.BatchNumber} of {batch.ProductName} expired on {batch.ExpiryDate:yyyy-MM-dd}; {quantity} still in stock — write it off.",
                        EventSeverity.Critical, "StockBatch", batch.StockBatchId,
                        new { batch.ProductId, batch.BatchNumber, batch.ExpiryDate, batch.QuantityOnHand },
                        AlertKey: AlertKeys.BatchExpired(batch.StockBatchId)));
                else
                    Raise(new SystemEventDraft(SystemEventTypes.BatchExpiringSoon,
                        $"Batch {batch.BatchNumber} of {batch.ProductName} expires {(days == 0 ? "today" : $"in {days} day{(days == 1 ? "" : "s")}")} " +
                        $"({batch.ExpiryDate:yyyy-MM-dd}); {quantity} in stock.",
                        EventSeverity.Warning, "StockBatch", batch.StockBatchId,
                        new { batch.ProductId, batch.BatchNumber, batch.ExpiryDate, batch.QuantityOnHand, DaysToExpiry = days },
                        AlertKey: AlertKeys.BatchExpiring(batch.StockBatchId)));
            }

            // Purchase orders past their expected delivery date.
            foreach (var order in await _sweep.GetOverdueOrdersAsync(DateTime.UtcNow))
                Raise(new SystemEventDraft(SystemEventTypes.PurchaseOrderOverdue,
                    $"{order.OrderNumber} from {order.SupplierName} was due on {order.ExpectedDeliveryDate:yyyy-MM-dd} and is not fully received.",
                    EventSeverity.Warning, "PurchaseOrder", order.PurchaseOrderId, new { order.OrderNumber, order.ExpectedDeliveryDate },
                    AlertKey: AlertKeys.PurchaseOrderOverdue(order.PurchaseOrderId)));

            // Till payments nobody has matched to a sale.
            var cutoff = DateTime.UtcNow.AddMinutes(-_settings.TillUnmatchedMinutes);
            foreach (var payment in await _sweep.GetUnmatchedTillPaymentsAsync(cutoff))
                Raise(new SystemEventDraft(SystemEventTypes.MpesaTillPaymentUnmatched,
                    $"M-Pesa payment {payment.ReceiptNumber} of {payment.Amount:0.00} from {payment.PhoneNumber} has not been matched to a sale.",
                    EventSeverity.Warning, "MpesaTransaction", payment.MpesaTransactionId,
                    new { payment.ReceiptNumber, payment.Amount, payment.PhoneNumber, payment.ReceivedAt },
                    AlertKey: AlertKeys.TillPaymentUnmatched(payment.MpesaTransactionId)));

            // Everything still open whose condition no longer holds is over: it will be announced again if it recurs.
            var swept = new[] { "stock-low:", "stock-out:", "batch-expiring:", "batch-expired:", "po-overdue:", "mpesa-unmatched:" };
            var cleared = open.Where(k => swept.Any(p => k.StartsWith(p, StringComparison.Ordinal)) && !stillTrue.Contains(k)).ToList();
            if (cleared.Count > 0)
                await _notifications.CloseAlertsAsync(cleared);
            await _events.FlushAsync();

            return raised;
        }
    }
}
