using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using UltimatePos.Application.Deliveries;
using UltimatePos.Domain.Entities;
using static UltimatePos.Domain.Enums.PosEnums;

namespace UltimatePos.Infrastructure.Persistence.Repositories
{
    /// <summary>
    /// Delivery orders. Status changes are single conditional updates (only from the state they leave), so two people dispatching,
    /// cancelling or returning the same order at once cannot both succeed. Delivery itself happens in SalesRepository, inside the sale's
    /// transaction.
    /// </summary>
    public class DeliveryOrderRepository : IDeliveryOrderRepository
    {
        private readonly UltimatePosDbContext _context;

        public DeliveryOrderRepository(UltimatePosDbContext context) => _context = context;

        public async Task<int> NextNumberAsync() => await _context.NextSequenceNumberAsync("DEL");

        public async Task<int> NextTripNumberAsync() => await _context.NextSequenceNumberAsync("TRP");

        public async Task CreateAsync(DeliveryOrder order, DeliveryTrip? newTrip)
        {
            if (newTrip is not null)
                _context.DeliveryTrips.Add(newTrip);
            _context.DeliveryOrders.Add(order);
            await _context.SaveChangesAsync();
        }

        private IQueryable<DeliveryOrder> WithDetails() =>
            _context.DeliveryOrders.AsNoTracking()
                .Include(o => o.Customer)
                .Include(o => o.Branch)
                .Include(o => o.Sale)
                .Include(o => o.Trip)
                .Include(o => o.Lines).ThenInclude(l => l.Product)
                .Include(o => o.Lines).ThenInclude(l => l.UnitOfMeasure)
                .Include(o => o.Lines).ThenInclude(l => l.ProductBundle)
                .AsSplitQuery();

        public async Task<DeliveryOrder?> GetAsync(Guid deliveryOrderId) =>
            await WithDetails().FirstOrDefaultAsync(o => o.DeliveryOrderId == deliveryOrderId);

        public async Task<IReadOnlyList<DeliveryOrder>> GetManyAsync(IEnumerable<Guid> deliveryOrderIds)
        {
            var ids = deliveryOrderIds.Distinct().ToList();
            return await _context.DeliveryOrders.AsNoTracking().Where(o => ids.Contains(o.DeliveryOrderId)).ToListAsync();
        }

        public async Task<(IReadOnlyList<DeliveryOrder> Items, int TotalCount)> GetOrdersAsync(int page, int pageSize, DeliveryOrderQuery filter,
            DateTime? fromUtc, DateTime? toUtcExclusive)
        {
            var query = _context.DeliveryOrders.AsNoTracking().AsQueryable();
            if (filter.Status.HasValue)
                query = query.Where(o => o.Status == filter.Status.Value);
            if (filter.CustomerId.HasValue)
                query = query.Where(o => o.CustomerId == filter.CustomerId.Value);
            if (filter.DeliveryTripId.HasValue)
                query = query.Where(o => o.DeliveryTripId == filter.DeliveryTripId.Value);
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

        private IQueryable<DeliveryTrip> TripsWithOrders() =>
            _context.DeliveryTrips.AsNoTracking()
                .Include(t => t.Orders).ThenInclude(o => o.Customer)
                .Include(t => t.Orders).ThenInclude(o => o.Sale)
                .AsSplitQuery();

        public async Task<DeliveryTrip?> GetTripAsync(Guid deliveryTripId) =>
            await TripsWithOrders().FirstOrDefaultAsync(t => t.DeliveryTripId == deliveryTripId);

        public async Task<bool> DispatchAsync(IReadOnlyCollection<Guid> deliveryOrderIds, DeliveryTrip trip, bool newTrip, Guid? userId)
        {
            await using var tx = await _context.Database.BeginTransactionAsync();
            // The trip (when new) and the events first: the orders point at the trip.
            if (newTrip)
                _context.DeliveryTrips.Add(trip);
            await _context.SaveChangesAsync();

            var now = DateTime.UtcNow;
            var ids = deliveryOrderIds.ToList();
            var dispatched = await _context.DeliveryOrders
                .Where(o => ids.Contains(o.DeliveryOrderId) && o.Status == DeliveryOrderStatus.Pending && o.BranchId == trip.BranchId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(o => o.Status, DeliveryOrderStatus.OutForDelivery)
                    .SetProperty(o => o.RiderName, trip.RiderName)
                    .SetProperty(o => o.DeliveryTripId, trip.DeliveryTripId)
                    .SetProperty(o => o.DispatchedAt, now)
                    .SetProperty(o => o.DispatchedBy, userId)
                    .SetProperty(o => o.LastUpdatedAt, now)
                    .SetProperty(o => o.LastUpdatedBy, userId));
            if (dispatched != ids.Count)
                return false; // rolled back on dispose: no order moves, and a new trip is not kept

            await tx.CommitAsync();
            return true;
        }

        public Task<bool> CancelAsync(Guid deliveryOrderId, string reason, Guid? userId) =>
            ChangeStatusAsync(_context.DeliveryOrders.Where(o => o.DeliveryOrderId == deliveryOrderId
                    && (o.Status == DeliveryOrderStatus.Pending || o.Status == DeliveryOrderStatus.OutForDelivery)),
                s => s.SetProperty(o => o.Status, DeliveryOrderStatus.Cancelled)
                      .SetProperty(o => o.CancelReason, reason)
                      .SetProperty(o => o.CancelledAt, DateTime.UtcNow)
                      .SetProperty(o => o.CancelledBy, userId)
                      .SetProperty(o => o.LastUpdatedAt, DateTime.UtcNow)
                      .SetProperty(o => o.LastUpdatedBy, userId));

        public Task<bool> ReturnAsync(Guid deliveryOrderId, string reason, Guid? userId) =>
            ChangeStatusAsync(_context.DeliveryOrders.Where(o => o.DeliveryOrderId == deliveryOrderId && o.Status == DeliveryOrderStatus.OutForDelivery),
                s => s.SetProperty(o => o.Status, DeliveryOrderStatus.Returned)
                      .SetProperty(o => o.ReturnReason, reason)
                      .SetProperty(o => o.ReturnedAt, DateTime.UtcNow)
                      .SetProperty(o => o.ReturnedBy, userId)
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
                            || (o.CancelledAt >= fromUtc && o.CancelledAt < toUtcExclusive)
                            || (o.ReturnedAt >= fromUtc && o.ReturnedAt < toUtcExclusive))
                .ToListAsync();

        public async Task<IReadOnlyList<DeliveryTrip>> GetTripsAsync(DateTime fromUtc, DateTime toUtcExclusive, string? riderName)
        {
            var query = TripsWithOrders().Where(t => t.DispatchedAt >= fromUtc && t.DispatchedAt < toUtcExclusive);
            if (riderName is not null)
                query = query.Where(t => EF.Functions.ILike(t.RiderName, riderName));
            return await query.OrderBy(t => t.DispatchedAt).ToListAsync();
        }

        public async Task<IReadOnlyList<RiderExpenseRow>> GetRiderExpensesAsync(DateOnly from, DateOnly toInclusive, Guid? expenseCategoryId)
        {
            var query = _context.Expenses.AsNoTracking()
                .Where(e => e.Status == ExpenseStatus.Posted && e.RiderName != null && e.ExpenseDate >= from && e.ExpenseDate <= toInclusive);
            if (expenseCategoryId.HasValue)
                query = query.Where(e => e.ExpenseCategoryId == expenseCategoryId.Value);
            return await query.Select(e => new RiderExpenseRow(e.RiderName!, e.Amount)).ToListAsync();
        }
    }
}
