using UltimatePos.Domain.Entities;

namespace UltimatePos.Application.Notifications
{
    /// <param name="ViaPlatformRole">Reached only through a platform role (a super user, who belongs to no business).</param>
    public sealed record NotificationRecipient(Guid UserId, string Name, string? Email, string? PhoneNumber, bool ViaPlatformRole = false);

    public sealed record AuditLogQuery(DateTime? FromUtc, DateTime? ToUtcExclusive, string? EventType, Guid? ActorUserId,
        string? EntityType, Guid? EntityId, EventSeverity? MinimumSeverity, string? Search);

    /// <summary>
    /// Storage for events, notifications and deliveries. The dispatcher side works ACROSS businesses (it runs with nobody signed in),
    /// so those methods take the business explicitly. The user side is scoped to the signed-in user and business.
    /// </summary>
    public interface INotificationRepository
    {
        // ---- Dispatcher (no business in scope) ----
        Task<IReadOnlyList<SystemEvent>> GetUnprocessedEventsAsync(int max, int maxAttempts);
        /// <summary>Saves the notifications and deliveries, opens/closes alerts, and marks the event processed — one transaction.</summary>
        Task CompleteEventAsync(SystemEvent systemEvent, IEnumerable<Notification> notifications, IEnumerable<NotificationDelivery> deliveries,
            string? openAlertKey, IEnumerable<string> closeAlertKeys);
        Task MarkEventFailedAsync(Guid systemEventId, string error);
        Task<bool> IsAlertOpenAsync(Guid businessId, string alertKey);
        /// <summary>
        /// Active users holding any of the permissions — through a role of the business (as active members) or a platform role. With a
        /// branch, business users must also reach it: assigned to it, or holding Branches.AccessAll.
        /// </summary>
        Task<IReadOnlyList<NotificationRecipient>> GetRecipientsAsync(Guid? businessId, IReadOnlyCollection<string> permissions, Guid? branchId = null);
        /// <summary>The user, if active (and an active member of the business, when one is given).</summary>
        Task<NotificationRecipient?> GetRecipientAsync(Guid userId, Guid? businessId);
        Task<string?> GetBusinessNameAsync(Guid businessId);

        Task<IReadOnlyList<NotificationDelivery>> GetDueDeliveriesAsync(int max, DateTime now);
        Task UpdateDeliveryAsync(NotificationDelivery delivery);

        // ---- Alert sweep (business in scope) ----
        Task<IReadOnlyList<string>> GetOpenAlertKeysAsync();
        Task CloseAlertsAsync(IEnumerable<string> alertKeys);

        // ---- Signed-in user ----
        Task<(IReadOnlyList<Notification> Items, int TotalCount)> GetNotificationsAsync(Guid userId, bool unreadOnly, int page, int pageSize);
        Task<int> GetUnreadCountAsync(Guid userId);
        Task<bool> MarkReadAsync(Guid userId, Guid notificationId);
        Task<int> MarkAllReadAsync(Guid userId);

        // ---- Audit log (business in scope) ----
        Task<(IReadOnlyList<SystemEvent> Items, int TotalCount)> GetEventsAsync(AuditLogQuery query, int page, int pageSize);
        Task<Dictionary<Guid, string>> GetUserNamesAsync(IEnumerable<Guid> userIds);
    }

    // Rows the alert sweep looks at, read with the business in scope. Stock conditions are per branch.
    public sealed record LowStockRow(Guid ProductId, string ProductName, string? UnitSymbol, decimal QuantityOnHand, decimal ReorderLevel,
        Guid BranchId, int BranchNumber, string BranchName);
    public sealed record ExpiringBatchRow(Guid StockBatchId, Guid ProductId, string ProductName, string BatchNumber, DateOnly ExpiryDate,
        decimal QuantityOnHand, string? UnitSymbol, Guid BranchId, int BranchNumber, string BranchName);
    public sealed record OverdueOrderRow(Guid PurchaseOrderId, string OrderNumber, string SupplierName, DateTime ExpectedDeliveryDate, Guid BranchId);
    public sealed record UnmatchedPaymentRow(Guid MpesaTransactionId, string? ReceiptNumber, string PhoneNumber, decimal Amount, DateTime ReceivedAt,
        Guid? BranchId);

    public interface IAlertSweepRepository
    {
        /// <summary>Active products with a reorder level whose stock at a branch is at or below it (including out of stock), per branch.</summary>
        Task<IReadOnlyList<LowStockRow>> GetLowStockAsync();
        /// <summary>Batches with stock that expire on or before <paramref name="horizon"/> (expired ones included).</summary>
        Task<IReadOnlyList<ExpiringBatchRow>> GetExpiringBatchesAsync(DateOnly horizon);
        Task<IReadOnlyList<OverdueOrderRow>> GetOverdueOrdersAsync(DateTime nowUtc);
        Task<IReadOnlyList<UnmatchedPaymentRow>> GetUnmatchedTillPaymentsAsync(DateTime receivedBeforeUtc);
        Task<IReadOnlyList<Guid>> GetActiveBusinessIdsAsync();
        /// <summary>The business in scope.</summary>
        Task<BusinessProfile?> GetCurrentBusinessAsync();
    }
}
