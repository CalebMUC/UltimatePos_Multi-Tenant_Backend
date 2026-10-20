using UltimatePos.Application.Licensing;
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
        private readonly LicensingSettings _licensing;

        public AlertSweepService(IAlertSweepRepository sweep, INotificationRepository notifications, ISystemEventRecorder events,
            NotificationSettings settings, LicensingSettings licensing)
        {
            _licensing = licensing;
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
                var at = $"at {Branch.FormatKraBranchId(row.BranchNumber)} {row.BranchName}";
                if (row.QuantityOnHand <= 0)
                    Raise(new SystemEventDraft(SystemEventTypes.StockOutOfStock,
                        $"{row.ProductName} is out of stock {at} (reorder level {row.ReorderLevel:0.##}{unit}).",
                        EventSeverity.Critical, "Product", row.ProductId, new { row.QuantityOnHand, row.ReorderLevel, row.BranchId },
                        AlertKey: AlertKeys.StockOut(row.ProductId, row.BranchId), BranchId: row.BranchId));
                else
                    Raise(new SystemEventDraft(SystemEventTypes.StockBelowReorderLevel,
                        $"{row.ProductName} is down to {row.QuantityOnHand:0.##}{unit} {at} (reorder level {row.ReorderLevel:0.##}{unit}).",
                        EventSeverity.Warning, "Product", row.ProductId, new { row.QuantityOnHand, row.ReorderLevel, row.BranchId },
                        AlertKey: AlertKeys.StockLow(row.ProductId, row.BranchId), BranchId: row.BranchId));
            }

            // Batches expired, or expiring within the warning window.
            var today = ReportPeriod.TodayAt(ReportPeriod.DefaultUtcOffsetMinutes);
            foreach (var batch in await _sweep.GetExpiringBatchesAsync(today.AddDays(_settings.ExpiryWarningDays)))
            {
                var quantity = $"{batch.QuantityOnHand:0.##}{(batch.UnitSymbol is null ? string.Empty : " " + batch.UnitSymbol)}";
                var days = batch.ExpiryDate.DayNumber - today.DayNumber;
                if (days < 0)
                    Raise(new SystemEventDraft(SystemEventTypes.BatchExpired,
                        $"Batch {batch.BatchNumber} of {batch.ProductName} expired on {batch.ExpiryDate:yyyy-MM-dd}; {quantity} still in stock at " +
                        $"{Branch.FormatKraBranchId(batch.BranchNumber)} {batch.BranchName} — write it off.",
                        EventSeverity.Critical, "StockBatch", batch.StockBatchId,
                        new { batch.ProductId, batch.BatchNumber, batch.ExpiryDate, batch.QuantityOnHand, batch.BranchId },
                        AlertKey: AlertKeys.BatchExpired(batch.StockBatchId), BranchId: batch.BranchId));
                else
                    Raise(new SystemEventDraft(SystemEventTypes.BatchExpiringSoon,
                        $"Batch {batch.BatchNumber} of {batch.ProductName} expires {(days == 0 ? "today" : $"in {days} day{(days == 1 ? "" : "s")}")} " +
                        $"({batch.ExpiryDate:yyyy-MM-dd}); {quantity} in stock at {Branch.FormatKraBranchId(batch.BranchNumber)} {batch.BranchName}.",
                        EventSeverity.Warning, "StockBatch", batch.StockBatchId,
                        new { batch.ProductId, batch.BatchNumber, batch.ExpiryDate, batch.QuantityOnHand, DaysToExpiry = days, batch.BranchId },
                        AlertKey: AlertKeys.BatchExpiring(batch.StockBatchId), BranchId: batch.BranchId));
            }

            // Purchase orders past their expected delivery date.
            foreach (var order in await _sweep.GetOverdueOrdersAsync(DateTime.UtcNow))
                Raise(new SystemEventDraft(SystemEventTypes.PurchaseOrderOverdue,
                    $"{order.OrderNumber} from {order.SupplierName} was due on {order.ExpectedDeliveryDate:yyyy-MM-dd} and is not fully received.",
                    EventSeverity.Warning, "PurchaseOrder", order.PurchaseOrderId, new { order.OrderNumber, order.ExpectedDeliveryDate },
                    AlertKey: AlertKeys.PurchaseOrderOverdue(order.PurchaseOrderId), BranchId: order.BranchId));

            // Till payments nobody has matched to a sale.
            var cutoff = DateTime.UtcNow.AddMinutes(-_settings.TillUnmatchedMinutes);
            foreach (var payment in await _sweep.GetUnmatchedTillPaymentsAsync(cutoff))
                Raise(new SystemEventDraft(SystemEventTypes.MpesaTillPaymentUnmatched,
                    $"M-Pesa payment {payment.ReceiptNumber} of {payment.Amount:0.00} from {payment.PhoneNumber} has not been matched to a sale.",
                    EventSeverity.Warning, "MpesaTransaction", payment.MpesaTransactionId,
                    new { payment.ReceiptNumber, payment.Amount, payment.PhoneNumber, payment.ReceivedAt },
                    AlertKey: AlertKeys.TillPaymentUnmatched(payment.MpesaTransactionId), BranchId: payment.BranchId));

            // Credit sales still unpaid after the day the customer promised to pay by. Which invoices are still open is worked out the
            // same way as the customer-ageing report (payments settle the oldest invoices first), so a promise kept clears its alert.
            var promises = await _sweep.GetPromisesDueBeforeAsync(today);
            if (promises.Count > 0)
            {
                var offset = TimeSpan.FromMinutes(ReportPeriod.DefaultUtcOffsetMinutes);
                var unpaid = (await _sweep.GetCustomerLedgerAsync(promises.Select(p => p.CustomerId)))
                    .GroupBy(e => e.CustomerId)
                    .SelectMany(g => AgingCalculator.Calculate(g, today, offset).OpenInvoices)
                    .ToDictionary(i => i.InvoiceId, i => i.OutstandingAmount);
                foreach (var promise in promises)
                {
                    if (!unpaid.TryGetValue(promise.SaleId, out var outstanding) || outstanding <= 0)
                        continue;
                    var late = today.DayNumber - promise.PaymentDueDate.DayNumber;
                    Raise(new SystemEventDraft(SystemEventTypes.PaymentPromiseOverdue,
                        $"{promise.CustomerName}{(promise.PhoneNumber is null ? "" : $" ({promise.PhoneNumber})")} promised to pay for {promise.SaleNumber} by " +
                        $"{promise.PaymentDueDate:yyyy-MM-dd}; {outstanding:0.00} is still unpaid, {late} day{(late == 1 ? "" : "s")} late.",
                        EventSeverity.Warning, "Sale", promise.SaleId,
                        new { promise.SaleNumber, promise.CustomerId, promise.PaymentDueDate, outstanding, DaysLate = late },
                        AlertKey: AlertKeys.PaymentPromiseOverdue(promise.SaleId), BranchId: promise.BranchId));
                }
            }

            // The business's own licence: warn before it ends, then again once it has.
            if (await _sweep.GetCurrentBusinessAsync() is { LicenseEndsOn: { } endsOn } business)
            {
                var access = LicensePolicy.Evaluate(business, _licensing, today);
                if (access.Status == "ExpiringSoon")
                    Raise(new SystemEventDraft(SystemEventTypes.LicenseExpiringSoon,
                        $"The Ultimate POS licence for {business.BusinessName} ends on {endsOn:yyyy-MM-dd} ({access.DaysRemaining} day(s) left). Renew to avoid interruption.",
                        EventSeverity.Warning, "Business", business.BusinessId, new { endsOn, access.DaysRemaining },
                        AlertKey: AlertKeys.LicenseExpiring(endsOn)));
                else if (access.Status is "GracePeriod" or "Expired")
                    Raise(new SystemEventDraft(SystemEventTypes.LicenseExpired,
                        $"The Ultimate POS licence for {business.BusinessName} ended on {endsOn:yyyy-MM-dd}. " +
                        (access.Allowed ? $"Access stops after {_licensing.GraceDays} days' grace unless it is renewed." : "Access has stopped until it is renewed."),
                        EventSeverity.Critical, "Business", business.BusinessId, new { endsOn, access.Status },
                        AlertKey: AlertKeys.LicenseExpired(endsOn)));
            }

            // Everything still open whose condition no longer holds is over: it will be announced again if it recurs.
            var swept = new[] { "stock-low:", "stock-out:", "batch-expiring:", "batch-expired:", "po-overdue:", "mpesa-unmatched:",
                "promise-overdue:", "license-expiring:", "license-expired:" };
            var cleared = open.Where(k => swept.Any(p => k.StartsWith(p, StringComparison.Ordinal)) && !stillTrue.Contains(k)).ToList();
            if (cleared.Count > 0)
                await _notifications.CloseAlertsAsync(cleared);
            await _events.FlushAsync();

            return raised;
        }
    }
}
