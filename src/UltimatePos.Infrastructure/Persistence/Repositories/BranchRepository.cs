using Microsoft.EntityFrameworkCore;
using UltimatePos.Application.Branches;
using UltimatePos.Domain.Entities;
using UltimatePos.Domain.Exceptions;

namespace UltimatePos.Infrastructure.Persistence.Repositories
{
    public class BranchRepository : IBranchRepository
    {
        private readonly UltimatePosDbContext _context;
        public BranchRepository(UltimatePosDbContext context) => _context = context;

        // ---- Branches of the business in scope (the tenant filter scopes them) ----

        public async Task<IReadOnlyList<Branch>> GetBranchesAsync(bool includeInactive) =>
            await _context.Branches.AsNoTracking()
                .Where(b => includeInactive || b.IsActive)
                .OrderBy(b => b.BranchNumber)
                .ToListAsync();

        public async Task<Branch?> GetBranchAsync(Guid branchId) =>
            await _context.Branches.AsNoTracking().FirstOrDefaultAsync(b => b.BranchId == branchId);

        public async Task<int> NextBranchNumberAsync() =>
            (await _context.Branches.MaxAsync(b => (int?)b.BranchNumber) ?? -1) + 1;

        public async Task<bool> BranchNumberExistsAsync(int branchNumber) =>
            await _context.Branches.AnyAsync(b => b.BranchNumber == branchNumber);

        public async Task<bool> CodeExistsAsync(string code, Guid? exceptBranchId = null) =>
            await _context.Branches.AnyAsync(b => b.Code == code && b.BranchId != exceptBranchId);

        public async Task<bool> MpesaShortCodeInUseAsync(string shortCode, Guid? exceptBranchId = null) =>
            await _context.Branches.IgnoreQueryFilters().AnyAsync(b => b.MpesaShortCode == shortCode && b.BranchId != exceptBranchId);

        public async Task<int> CountActiveBranchesAsync() =>
            await _context.Branches.CountAsync(b => b.IsActive);

        public async Task<Branch> CreateBranchAsync(Branch branch)
        {
            _context.Branches.Add(branch);
            await _context.SaveChangesAsync();
            return branch;
        }

        public async Task<Branch> UpdateBranchAsync(Guid branchId, Action<Branch> change)
        {
            var branch = await _context.Branches.FirstOrDefaultAsync(b => b.BranchId == branchId)
                ?? throw new NotFoundException($"Branch '{branchId}' not found.");
            change(branch);
            await _context.SaveChangesAsync();
            return branch;
        }

        public async Task<bool> HasStockAsync(Guid branchId) =>
            await _context.StockLevels.IgnoreQueryFilters().AnyAsync(s => s.BranchId == branchId && s.QuantityOnHand != 0)
            || await _context.StockBatches.IgnoreQueryFilters().AnyAsync(b => b.BranchId == branchId && b.QuantityOnHand != 0);

        // ---- Across businesses (sign-in happens before one is in scope) ----

        public async Task<Branch?> GetHeadOfficeAsync(Guid businessId) =>
            await _context.Branches.IgnoreQueryFilters().AsNoTracking()
                .FirstOrDefaultAsync(b => b.BusinessId == businessId && b.BranchNumber == 0);

        public async Task<IReadOnlyList<Branch>> GetActiveBranchesAsync(Guid businessId) =>
            await _context.Branches.IgnoreQueryFilters().AsNoTracking()
                .Where(b => b.BusinessId == businessId && b.IsActive)
                .OrderBy(b => b.BranchNumber)
                .ToListAsync();

        public async Task<IReadOnlyList<UserBranch>> GetUserBranchesAsync(Guid userId, Guid businessId) =>
            await _context.UserBranches.IgnoreQueryFilters().AsNoTracking()
                .Include(ub => ub.Branch)
                .Where(ub => ub.UserId == userId && ub.Branch.BusinessId == businessId && ub.Branch.IsActive)
                .OrderByDescending(ub => ub.IsDefault).ThenBy(ub => ub.Branch.BranchNumber)
                .ToListAsync();

        public async Task SetUserBranchesAsync(Guid userId, Guid businessId, IReadOnlyCollection<Guid> branchIds, Guid? defaultBranchId, Guid? changedBy)
        {
            var existing = await _context.UserBranches.IgnoreQueryFilters()
                .Where(ub => ub.UserId == userId && ub.Branch.BusinessId == businessId)
                .ToListAsync();
            _context.UserBranches.RemoveRange(existing.Where(e => !branchIds.Contains(e.BranchId)));

            foreach (var branchId in branchIds)
            {
                var row = existing.FirstOrDefault(e => e.BranchId == branchId);
                if (row is null)
                    _context.UserBranches.Add(row = new UserBranch { UserId = userId, BranchId = branchId, CreatedBy = changedBy });
                row.IsDefault = branchId == defaultBranchId;
            }
            await _context.SaveChangesAsync();
        }

        public async Task<Branch?> GetBranchByMpesaShortCodeAsync(string shortCode) =>
            await _context.Branches.IgnoreQueryFilters().AsNoTracking()
                .FirstOrDefaultAsync(b => b.MpesaShortCode == shortCode && b.IsActive);
    }
}
