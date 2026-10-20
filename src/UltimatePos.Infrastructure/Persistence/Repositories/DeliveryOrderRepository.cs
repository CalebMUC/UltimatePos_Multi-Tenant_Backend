using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using UltimatePos.Application.Deliveries;
using UltimatePos.Domain.Entities;

namespace UltimatePos.Infrastructure.Persistence.Repositories
{
    /// <summary>
    /// Delivery orders. Status changes are single conditional updates (only from the state they leave), so two people dispatching or
    /// cancelling the same order at once cannot both succeed. Delivery itself happens in SalesRepository, inside the sale's transaction.
    /// </summary>
    public class DeliveryOrderRepository : IDeliveryOrderRepository
    {
        private readonly UltimatePosDbContext _context;

        public DeliveryOrderRepository(UltimatePosDbContext context) => _context = context;

        public async Task<int> NextNumberAsync() => await _context.NextSequenceNumberAsync("DEL");

        public async Task CreateAsync(DeliveryOrder order)
        {
            _context.DeliveryOrders.Add(order);
            await _context.SaveChangesAsync();
        }

        private IQueryable<DeliveryOrder> WithDetails() =>
            _context.DeliveryOrders.AsNoTracking()
                .Include(o => o.Customer)
                .Include(o => o.Branch)
                .Include(o => o.Sale)
                .Include(o => o.Lines).ThenInclude(l => l.Product)
                .Include(o => o.Lines).ThenInclude(l => l.UnitOfMeasure)
                .AsSplitQuery();

        public async Task<DeliveryOrder?> GetAsync(Guid deliveryOrderId) =>
            await WithDetails().FirstOrDefaultAsync(o => o.DeliveryOrderId == deliveryOrderId);

        public async Task<(IReadOnlyList<DeliveryOrder> Items, int TotalCount)> GetOrdersAsync(int page, int pageSize, DeliveryOrderQuery filter,
            DateTime? fromUtc, DateTime? toUtcExclusive)
        {
            var query = _context.DeliveryOrders.AsNoTracking().AsQueryable();
            if (filter.Status.HasValue)
                query = query.Where(o => o.Status == filter.Status.Value);
            if (filter.CustomerId.HasValue)
                query = query.Where(o => o.CustomerId == filter.CustomerId.Value);
            if (!string.IsNullOrWhiteSpace(filter.RiderName))
                query = query.Where(o => o.RiderName != null && EF.Functions.ILike(o.RiderName, filter.RiderName.Trim()));
            if (fromUtc.HasValue)
                query = query.Where(o => o.CreatedAt >= fromUtc.Value);
            if (toUtcExclusive.HasValue)
                query = query.Where(o => o.CreatedAt < toUtcExclusive.Value);

            var total = await query.CountAsync();
            var ids = await query.OrderByDescending(o => o.CreatedAt).ThenBy(o => o.DeliveryOrderId)
                .Skip((page - 1) * pageSize).Take(pageSize).Select(o => o.DeliveryOrderId).ToListAsync();
            var items = await WithDetails().Where(o => ids.Contains(o.DeliveryOrderId)).ToListAsync();
            return (items.OrderByDescending(o => o.CreatedAt).ThenBy(o => o.DeliveryOrderId).ToList(), total);
        }

        public Task<bool> DispatchAsync(Guid deliveryOrderId, string riderName, Guid? userId) =>
            ChangeStatusAsync(_context.DeliveryOrders.Where(o => o.DeliveryOrderId == deliveryOrderId && o.Status == DeliveryOrderStatus.Pending),
                s => s.SetProperty(o => o.Status, DeliveryOrderStatus.OutForDelivery)
                      .SetProperty(o => o.RiderName, riderName)
                      .SetProperty(o => o.DispatchedAt, DateTime.UtcNow)
                      .SetProperty(o => o.DispatchedBy, userId)
                      .SetProperty(o => o.LastUpdatedAt, DateTime.UtcNow)
                      .SetProperty(o => o.LastUpdatedBy, userId));

        public Task<bool> CancelAsync(Guid deliveryOrderId, string reason, Guid? userId) =>
            ChangeStatusAsync(_context.DeliveryOrders.Where(o => o.DeliveryOrderId == deliveryOrderId
                    && (o.Status == DeliveryOrderStatus.Pending || o.Status == DeliveryOrderStatus.OutForDelivery)),
                s => s.SetProperty(o => o.Status, DeliveryOrderStatus.Cancelled)
                      .SetProperty(o => o.CancelReason, reason)
                      .SetProperty(o => o.CancelledAt, DateTime.UtcNow)
                      .SetProperty(o => o.CancelledBy, userId)
                      .SetProperty(o => o.LastUpdatedAt, DateTime.UtcNow)
                      .SetProperty(o => o.LastUpdatedBy, userId));

        /// <summary>The conditional update, and the events recorded for it, in one transaction: both or neither.</summary>
        private async Task<bool> ChangeStatusAsync(IQueryable<DeliveryOrder> match,
            Expression<Func<SetPropertyCalls<DeliveryOrder>, SetPropertyCalls<DeliveryOrder>>> set)
        {
            await using var tx = await _context.Database.BeginTransactionAsync();
            if (await match.ExecuteUpdateAsync(set) == 0)
                return false; // rolled back on dispose
            await _context.SaveChangesAsync();
            await tx.CommitAsync();
            return true;
        }

        public async Task<IReadOnlyList<DeliveryOrder>> GetForSummaryAsync(DateTime fromUtc, DateTime toUtcExclusive) =>
            await _context.DeliveryOrders.AsNoTracking()
                .Include(o => o.Sale)
                .Where(o => o.Status == DeliveryOrderStatus.Pending || o.Status == DeliveryOrderStatus.OutForDelivery
                            || (o.DeliveredAt >= fromUtc && o.DeliveredAt < toUtcExclusive)
                            || (o.CancelledAt >= fromUtc && o.CancelledAt < toUtcExclusive))
                .ToListAsync();
    }
}
