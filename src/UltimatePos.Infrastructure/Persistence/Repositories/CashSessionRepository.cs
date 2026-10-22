using Microsoft.EntityFrameworkCore;
using Npgsql;
using UltimatePos.Application.CashSessions;
using UltimatePos.Domain.Entities;
using static UltimatePos.Domain.Enums.PosEnums;

namespace UltimatePos.Infrastructure.Persistence.Repositories
{
    /// <summary>
    /// Cash sessions. One open per branch is held by a unique index; closing is a conditional update, and a movement is added only while
    /// the session is open (under a row lock), so nothing lands on a day that has been counted.
    /// </summary>
    public class CashSessionRepository : ICashSessionRepository
    {
        private readonly UltimatePosDbContext _context;

        public CashSessionRepository(UltimatePosDbContext context) => _context = context;

        public async Task<int> NextNumberAsync() => await _context.NextSequenceNumberAsync("CSH");

        public async Task<bool> OpenAsync(CashSession session)
        {
            _context.CashSessions.Add(session);
            try
            {
                await _context.SaveChangesAsync();
                return true;
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException
                { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: UltimatePosDbContext.CashSessionOpenIndex })
            {
                _context.ChangeTracker.Clear();
                return false;
            }
        }

        private IQueryable<CashSession> WithDetails() =>
            _context.CashSessions.AsNoTracking().Include(s => s.Branch).Include(s => s.Movements).AsSplitQuery();

        public async Task<CashSession?> GetAsync(Guid cashSessionId) =>
            await WithDetails().FirstOrDefaultAsync(s => s.CashSessionId == cashSessionId);

        public async Task<CashSession?> GetOpenAsync(Guid branchId) =>
            await WithDetails().FirstOrDefaultAsync(s => s.BranchId == branchId && s.Status == CashSessionStatus.Open);

        public async Task<(IReadOnlyList<CashSession> Items, int TotalCount)> GetSessionsAsync(int page, int pageSize, CashSessionStatus? status,
            DateTime? fromUtc, DateTime? toUtcExclusive)
        {
            var query = _context.CashSessions.AsNoTracking().AsQueryable();
            if (status.HasValue)
                query = query.Where(s => s.Status == status.Value);
            if (fromUtc.HasValue)
                query = query.Where(s => s.OpenedAt >= fromUtc.Value);
            if (toUtcExclusive.HasValue)
                query = query.Where(s => s.OpenedAt < toUtcExclusive.Value);

            var total = await query.CountAsync();
            var ids = await query.OrderByDescending(s => s.OpenedAt).ThenBy(s => s.CashSessionId)
                .Skip((page - 1) * pageSize).Take(pageSize).Select(s => s.CashSessionId).ToListAsync();
            var items = await WithDetails().Where(s => ids.Contains(s.CashSessionId)).ToListAsync();
            return (items.OrderByDescending(s => s.OpenedAt).ThenBy(s => s.CashSessionId).ToList(), total);
        }

        public async Task<bool> AddMovementAsync(CashSessionMovement movement)
        {
            await using var tx = await _context.Database.BeginTransactionAsync();
            // Locked, so a close cannot slip in between the check and the insert.
            var open = await _context.Database.SqlQuery<int>($"""
                SELECT 1 AS "Value" FROM "CashSessions"
                WHERE "CashSessionId" = {movement.CashSessionId} AND "Status" = {CashSessionStatus.Open.ToString()}
                FOR UPDATE
                """).ToListAsync();
            if (open.Count == 0)
                return false;
            _context.CashSessionMovements.Add(movement);
            await _context.SaveChangesAsync();
            await tx.CommitAsync();
            return true;
        }

        public async Task<bool> CloseAsync(Guid cashSessionId, decimal expectedCash, decimal countedCash, string? notes, Guid? userId)
        {
            await using var tx = await _context.Database.BeginTransactionAsync();
            var now = DateTime.UtcNow;
            var closed = await _context.CashSessions
                .Where(s => s.CashSessionId == cashSessionId && s.Status == CashSessionStatus.Open)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(c => c.Status, CashSessionStatus.Closed)
                    .SetProperty(c => c.ExpectedCash, expectedCash)
                    .SetProperty(c => c.CountedCash, countedCash)
                    .SetProperty(c => c.Variance, countedCash - expectedCash)
                    .SetProperty(c => c.ClosedAt, now)
                    .SetProperty(c => c.ClosedBy, userId)
                    .SetProperty(c => c.ClosingNotes, notes));
            if (closed == 0)
                return false; // rolled back on dispose
            await _context.SaveChangesAsync();
            await tx.CommitAsync();
            return true;
        }

        public async Task<CashFlowTotals> GetCashFlowsAsync(Guid branchId, DateTime fromUtc, DateTime toUtcExclusive)
        {
            // Cash sales net of change: TotalAmount is what stayed in the drawer. A sale voided since gave its cash back, so it is left out.
            var sales = await _context.Sales.AsNoTracking()
                .Where(s => s.BranchId == branchId && s.PaymentMethod == PaymentMethod.Cash && s.Status == SaleStatus.Completed
                            && s.CreatedAt >= fromUtc && s.CreatedAt < toUtcExclusive)
                .GroupBy(_ => 1)
                .Select(g => new { Total = g.Sum(s => s.TotalAmount), Count = g.Count() })
                .FirstOrDefaultAsync();
            var refunds = await _context.SaleReturns.AsNoTracking()
                .Where(r => r.BranchId == branchId && r.RefundMethod == RefundMethod.Cash && r.CreatedAt >= fromUtc && r.CreatedAt < toUtcExclusive)
                .SumAsync(r => (decimal?)r.TotalAmount) ?? 0m;
            var customerPayments = await _context.CustomerLedgerEntries.AsNoTracking()
                .Where(e => e.BranchId == branchId && e.EntryType == CustomerLedgerEntryType.Payment && e.PaymentMethod == SettlementMethod.Cash
                            && e.CreatedAt >= fromUtc && e.CreatedAt < toUtcExclusive)
                .SumAsync(e => (decimal?)e.Amount) ?? 0m;
            var supplierPayments = await _context.SupplierLedgerEntries.AsNoTracking()
                .Where(e => e.BranchId == branchId && e.EntryType == SupplierLedgerEntryType.Payment && e.PaymentMethod == SettlementMethod.Cash
                            && e.CreatedAt >= fromUtc && e.CreatedAt < toUtcExclusive)
                .SumAsync(e => (decimal?)e.Amount) ?? 0m;
            // Cash expenses recorded at this branch while the drawer was open were paid out of it.
            var expenses = await _context.Expenses.AsNoTracking()
                .Where(e => e.BranchId == branchId && e.PaymentMethod == ExpensePaymentMethod.Cash && e.Status == ExpenseStatus.Posted
                            && e.CreatedAt >= fromUtc && e.CreatedAt < toUtcExclusive)
                .SumAsync(e => (decimal?)e.Amount) ?? 0m;
            var withRiders = await _context.DeliveryOrders.AsNoTracking()
                .Where(o => o.BranchId == branchId && o.Status == DeliveryOrderStatus.OutForDelivery
                            && (o.ExpectedPaymentMethod == null || o.ExpectedPaymentMethod == PaymentMethod.Cash))
                .GroupBy(_ => 1)
                .Select(g => new { Total = g.Sum(o => o.EstimatedTotal), Count = g.Count() })
                .FirstOrDefaultAsync();

            return new CashFlowTotals(sales?.Total ?? 0m, sales?.Count ?? 0, customerPayments, refunds, expenses, supplierPayments,
                withRiders?.Total ?? 0m, withRiders?.Count ?? 0);
        }

        public async Task<Dictionary<Guid, string>> GetUserNamesAsync(IEnumerable<Guid> userIds)
        {
            var ids = userIds.Distinct().ToList();
            if (ids.Count == 0)
                return new Dictionary<Guid, string>();
            return await _context.Users.AsNoTracking()
                .Where(u => ids.Contains(u.UserId))
                .Select(u => new { u.UserId, u.Username })
                .ToDictionaryAsync(u => u.UserId, u => u.Username);
        }
    }
}
