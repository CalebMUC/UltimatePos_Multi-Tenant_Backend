using Microsoft.EntityFrameworkCore;
using UltimatePos.Application.Common.Interfaces;
using UltimatePos.Application.Notifications;
using UltimatePos.Domain.Entities;
using UltimatePos.Infrastructure.Persistence;
using static UltimatePos.Domain.Enums.PosEnums;

namespace UltimatePos.Infrastructure.Notifications
{
    public class NotificationRepository : INotificationRepository
    {
        private readonly UltimatePosDbContext _context;
        private readonly ITenantContext _tenant;

        public NotificationRepository(UltimatePosDbContext context, ITenantContext tenant)
        {
            _context = context;
            _tenant = tenant;
        }

        // ---------------------------------------------------------------- Dispatcher (across businesses)

        public async Task<IReadOnlyList<SystemEvent>> GetUnprocessedEventsAsync(int max, int maxAttempts) =>
            await _context.SystemEvents.IgnoreQueryFilters().AsNoTracking()
                .Where(e => e.ProcessedAt == null && e.Attempts < maxAttempts)
                .OrderBy(e => e.OccurredAt).ThenBy(e => e.SystemEventId)
                .Take(max)
                .ToListAsync();

        public async Task CompleteEventAsync(SystemEvent systemEvent, IEnumerable<Notification> notifications,
            IEnumerable<NotificationDelivery> deliveries, string? openAlertKey, IEnumerable<string> closeAlertKeys)
        {
            await using var tx = await _context.Database.BeginTransactionAsync();
            var now = DateTime.UtcNow;

            if (systemEvent.BusinessId is { } businessId)
            {
                var closing = closeAlertKeys.ToList();
                if (closing.Count > 0)
                    await _context.AlertStates.IgnoreQueryFilters()
                        .Where(a => a.BusinessId == businessId && a.IsActive && closing.Contains(a.AlertKey))
                        .ExecuteUpdateAsync(u => u.SetProperty(a => a.IsActive, false).SetProperty(a => a.ClearedAt, now));

                if (openAlertKey is not null)
                {
                    // Re-open the existing row for the key, or create it. The unique index (BusinessId, AlertKey) keeps one row per condition.
                    var reopened = await _context.AlertStates.IgnoreQueryFilters()
                        .Where(a => a.BusinessId == businessId && a.AlertKey == openAlertKey)
                        .ExecuteUpdateAsync(u => u.SetProperty(a => a.IsActive, true).SetProperty(a => a.RaisedAt, now)
                            .SetProperty(a => a.ClearedAt, (DateTime?)null).SetProperty(a => a.RaisedByEventId, systemEvent.SystemEventId));
                    if (reopened == 0)
                        _context.AlertStates.Add(new AlertState
                        {
                            AlertStateId = Guid.NewGuid(), BusinessId = businessId, AlertKey = openAlertKey, IsActive = true,
                            RaisedAt = now, RaisedByEventId = systemEvent.SystemEventId,
                        });
                }
            }

            _context.Notifications.AddRange(notifications);
            _context.NotificationDeliveries.AddRange(deliveries);
            await _context.SaveChangesAsync();

            await _context.SystemEvents.IgnoreQueryFilters()
                .Where(e => e.SystemEventId == systemEvent.SystemEventId)
                .ExecuteUpdateAsync(u => u.SetProperty(e => e.ProcessedAt, now).SetProperty(e => e.Attempts, e => e.Attempts + 1)
                    .SetProperty(e => e.LastError, (string?)null));

            await tx.CommitAsync();
        }

        public async Task MarkEventFailedAsync(Guid systemEventId, string error)
        {
            _context.ChangeTracker.Clear(); // whatever the failed attempt added must not be saved
            var message = error.Length <= 1000 ? error : error[..1000];
            await _context.SystemEvents.IgnoreQueryFilters()
                .Where(e => e.SystemEventId == systemEventId)
                .ExecuteUpdateAsync(u => u.SetProperty(e => e.Attempts, e => e.Attempts + 1).SetProperty(e => e.LastError, message));
        }

        public async Task<bool> IsAlertOpenAsync(Guid businessId, string alertKey) =>
            await _context.AlertStates.IgnoreQueryFilters().AnyAsync(a => a.BusinessId == businessId && a.AlertKey == alertKey && a.IsActive);

        public async Task<IReadOnlyList<NotificationRecipient>> GetRecipientsAsync(Guid? businessId, IReadOnlyCollection<string> permissions)
        {
            var names = permissions.ToList();
            var rows = await _context.UserRoles.IgnoreQueryFilters().AsNoTracking()
                .Where(ur => ur.User.IsActive && ur.Role.IsActive
                             && ur.Role.RolePermissions.Any(rp => rp.IsActive && rp.Permission.IsActive && names.Contains(rp.Permission.Name))
                             && (ur.Role.BusinessId == null
                                 || (businessId != null && ur.Role.BusinessId == businessId
                                     && _context.UserBusinesses.Any(m => m.UserId == ur.UserId && m.BusinessId == businessId && m.IsActive))))
                .Select(ur => new { ur.User.UserId, ur.User.Username, ur.User.Email, ur.User.PhoneNumber, Platform = ur.Role.BusinessId == null })
                .ToListAsync();

            // Platform roles only carry platform permissions; a business role only business ones (as in sign-in).
            return rows
                .Where(r => names.Any(n => Application.Identity.PermissionCatalog.IsPlatform(n) == r.Platform))
                .GroupBy(r => r.UserId)
                .Select(g => new NotificationRecipient(g.Key, g.First().Username, g.First().Email, g.First().PhoneNumber,
                    ViaPlatformRole: g.All(r => r.Platform)))
                .ToList();
        }

        public async Task<NotificationRecipient?> GetRecipientAsync(Guid userId, Guid? businessId)
        {
            var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserId == userId && u.IsActive);
            if (user is null)
                return null;
            if (businessId is { } b && !await _context.UserBusinesses.AnyAsync(m => m.UserId == userId && m.BusinessId == b && m.IsActive))
                return null;
            return new NotificationRecipient(user.UserId, user.Username, user.Email, user.PhoneNumber);
        }

        public async Task<string?> GetBusinessNameAsync(Guid businessId) =>
            await _context.Businesses.AsNoTracking().Where(b => b.BusinessId == businessId)
                .Select(b => b.TradingName ?? b.BusinessName).FirstOrDefaultAsync();

        public async Task<IReadOnlyList<NotificationDelivery>> GetDueDeliveriesAsync(int max, DateTime now) =>
            await _context.NotificationDeliveries.IgnoreQueryFilters()
                .Where(d => d.Status == DeliveryStatus.Pending && (d.NextAttemptAt == null || d.NextAttemptAt <= now))
                .OrderBy(d => d.CreatedAt)
                .Take(max)
                .ToListAsync();

        public async Task UpdateDeliveryAsync(NotificationDelivery delivery) => await _context.SaveChangesAsync();

        // ---------------------------------------------------------------- Sweep (business in scope)

        public async Task<IReadOnlyList<string>> GetOpenAlertKeysAsync() =>
            await _context.AlertStates.AsNoTracking().Where(a => a.IsActive).Select(a => a.AlertKey).ToListAsync();

        public async Task CloseAlertsAsync(IEnumerable<string> alertKeys)
        {
            var keys = alertKeys.ToList();
            var now = DateTime.UtcNow;
            await _context.AlertStates
                .Where(a => a.IsActive && keys.Contains(a.AlertKey))
                .ExecuteUpdateAsync(u => u.SetProperty(a => a.IsActive, false).SetProperty(a => a.ClearedAt, now));
        }

        // ---------------------------------------------------------------- Signed-in user

        private IQueryable<Notification> Mine(Guid userId) => _context.Notifications.AsNoTracking().Where(n => n.UserId == userId);

        public async Task<(IReadOnlyList<Notification> Items, int TotalCount)> GetNotificationsAsync(Guid userId, bool unreadOnly, int page, int pageSize)
        {
            var query = Mine(userId);
            if (unreadOnly)
                query = query.Where(n => !n.IsRead);
            var total = await query.CountAsync();
            var items = await query.OrderByDescending(n => n.CreatedAt).ThenBy(n => n.NotificationId)
                .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
            return (items, total);
        }

        public async Task<int> GetUnreadCountAsync(Guid userId) => await Mine(userId).CountAsync(n => !n.IsRead);

        public async Task<bool> MarkReadAsync(Guid userId, Guid notificationId)
        {
            var notification = await _context.Notifications.FirstOrDefaultAsync(n => n.NotificationId == notificationId && n.UserId == userId);
            if (notification is null)
                return false;
            if (!notification.IsRead)
            {
                notification.IsRead = true;
                notification.ReadAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
            }
            return true;
        }

        public async Task<int> MarkAllReadAsync(Guid userId)
        {
            var now = DateTime.UtcNow;
            return await _context.Notifications
                .Where(n => n.UserId == userId && !n.IsRead)
                .ExecuteUpdateAsync(u => u.SetProperty(n => n.IsRead, true).SetProperty(n => n.ReadAt, now));
        }

        // ---------------------------------------------------------------- Audit log

        public async Task<(IReadOnlyList<SystemEvent> Items, int TotalCount)> GetEventsAsync(AuditLogQuery filter, int page, int pageSize)
        {
            var query = _context.SystemEvents.AsNoTracking();
            if (filter.FromUtc.HasValue) query = query.Where(e => e.OccurredAt >= filter.FromUtc.Value);
            if (filter.ToUtcExclusive.HasValue) query = query.Where(e => e.OccurredAt < filter.ToUtcExclusive.Value);
            if (filter.EventType is { } type)
                query = type.EndsWith('.') || !type.Contains('.')
                    ? query.Where(e => e.EventType.StartsWith(type.TrimEnd('.') + "."))   // "Stock" or "Stock." = the whole area
                    : query.Where(e => e.EventType == type);
            if (filter.ActorUserId.HasValue) query = query.Where(e => e.ActorUserId == filter.ActorUserId.Value);
            if (filter.EntityType is { } entityType) query = query.Where(e => e.EntityType == entityType);
            if (filter.EntityId.HasValue) query = query.Where(e => e.EntityId == filter.EntityId.Value);
            if (filter.MinimumSeverity is { } min)
            {
                var levels = Enum.GetValues<EventSeverity>().Where(s => s >= min).ToList();
                query = query.Where(e => levels.Contains(e.Severity));
            }
            if (filter.Search is { } search)
                query = query.Where(e => EF.Functions.ILike(e.Summary, $"%{search}%"));

            var total = await query.CountAsync();
            var items = await query.OrderByDescending(e => e.OccurredAt).ThenBy(e => e.SystemEventId)
                .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
            return (items, total);
        }

        public async Task<Dictionary<Guid, string>> GetUserNamesAsync(IEnumerable<Guid> userIds)
        {
            var ids = userIds.Distinct().ToList();
            if (ids.Count == 0)
                return new Dictionary<Guid, string>();
            return await _context.Users.AsNoTracking().Where(u => ids.Contains(u.UserId))
                .Select(u => new { u.UserId, u.Username }).ToDictionaryAsync(u => u.UserId, u => u.Username);
        }
    }

    /// <summary>The conditions the alert sweep checks, read with one business in scope (the tenant filters do the scoping).</summary>
    public class AlertSweepRepository : IAlertSweepRepository
    {
        private readonly UltimatePosDbContext _context;
        private readonly ITenantContext _tenant;
        private readonly Application.Licensing.IBusinessAccessGate _gate;

        public AlertSweepRepository(UltimatePosDbContext context, ITenantContext tenant, Application.Licensing.IBusinessAccessGate gate)
        {
            _context = context;
            _tenant = tenant;
            _gate = gate;
        }

        public async Task<BusinessProfile?> GetCurrentBusinessAsync()
        {
            var businessId = _tenant.RequireBusinessId();
            var business = await _context.Businesses.FirstOrDefaultAsync(b => b.BusinessId == businessId);
            if (business is null)
                return null;

            var licenses = await _context.BusinessLicenses.AsNoTracking().ToListAsync();
            var (endsOn, maxUsers) = Application.Licensing.LicensePolicy.Coverage(licenses, Application.Licensing.LicensePolicy.Today);
            if (business.LicenseEndsOn != endsOn || business.LicenseMaxUsers != maxUsers)
            {
                business.LicenseEndsOn = endsOn;
                business.LicenseMaxUsers = maxUsers;
                await _context.SaveChangesAsync();
                _gate.Invalidate(businessId);
            }
            return business;
        }

        public async Task<IReadOnlyList<LowStockRow>> GetLowStockAsync() =>
            await _context.StockLevels.AsNoTracking()
                .Where(s => s.Product.IsActive && s.Product.ReorderLevel > 0 && s.QuantityOnHand <= s.Product.ReorderLevel)
                .Select(s => new LowStockRow(s.ProductId, s.Product.Name, s.Product.BaseUnitOfMeasure.Symbol, s.QuantityOnHand, s.Product.ReorderLevel))
                .ToListAsync();

        public async Task<IReadOnlyList<ExpiringBatchRow>> GetExpiringBatchesAsync(DateOnly horizon) =>
            await _context.StockBatches.AsNoTracking()
                .Where(b => b.QuantityOnHand > 0 && b.ExpiryDate != null && b.ExpiryDate <= horizon)
                .Select(b => new ExpiringBatchRow(b.StockBatchId, b.ProductId, b.Product.Name, b.BatchNumber, b.ExpiryDate!.Value,
                    b.QuantityOnHand, b.Product.BaseUnitOfMeasure.Symbol))
                .ToListAsync();

        public async Task<IReadOnlyList<OverdueOrderRow>> GetOverdueOrdersAsync(DateTime nowUtc) =>
            await _context.PurchaseOrders.AsNoTracking()
                .Where(o => o.IsActive && o.ExpectedDeliveryDate != null && o.ExpectedDeliveryDate < nowUtc
                            && (o.Status == PurchaseOrderStatus.Ordered || o.Status == PurchaseOrderStatus.PartiallyReceived))
                .Select(o => new OverdueOrderRow(o.PurchaseOrderId, o.OrderNumber, o.Supplier.SupplierName, o.ExpectedDeliveryDate!.Value))
                .ToListAsync();

        public async Task<IReadOnlyList<UnmatchedPaymentRow>> GetUnmatchedTillPaymentsAsync(DateTime receivedBeforeUtc) =>
            await _context.MpesaTransactions.AsNoTracking()
                .Where(t => t.TransactionType == MpesaTransactionType.C2B && t.Status == MpesaTransactionStatus.Received
                            && t.CreatedAt < receivedBeforeUtc)
                .Select(t => new UnmatchedPaymentRow(t.MpesaTransactionId, t.MpesaReceiptNumber, t.PhoneNumber, t.Amount, t.CreatedAt))
                .ToListAsync();

        public async Task<IReadOnlyList<Guid>> GetActiveBusinessIdsAsync() =>
            await _context.Businesses.AsNoTracking().Where(b => b.IsActive).Select(b => b.BusinessId).ToListAsync();
    }
}
