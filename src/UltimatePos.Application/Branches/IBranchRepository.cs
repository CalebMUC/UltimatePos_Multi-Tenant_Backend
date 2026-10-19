using UltimatePos.Domain.Entities;

namespace UltimatePos.Application.Branches
{
    public interface IBranchRepository
    {
        // ---- Branches of the business in scope ----
        Task<IReadOnlyList<Branch>> GetBranchesAsync(bool includeInactive);
        Task<Branch?> GetBranchAsync(Guid branchId);
        /// <summary>Highest number ever used + 1 (inactive branches keep their numbers: a number is never reused).</summary>
        Task<int> NextBranchNumberAsync();
        Task<bool> BranchNumberExistsAsync(int branchNumber);
        Task<bool> CodeExistsAsync(string code, Guid? exceptBranchId = null);
        /// <summary>Across every business and branch: a till belongs to exactly one place.</summary>
        Task<bool> MpesaShortCodeInUseAsync(string shortCode, Guid? exceptBranchId = null);
        Task<int> CountActiveBranchesAsync();
        Task<Branch> CreateBranchAsync(Branch branch);
        Task<Branch> UpdateBranchAsync(Guid branchId, Action<Branch> change);
        /// <summary>Any stock (positive or negative) still held at the branch.</summary>
        Task<bool> HasStockAsync(Guid branchId);

        // ---- Across businesses (sign-in happens before one is in scope) ----
        Task<Branch?> GetHeadOfficeAsync(Guid businessId);
        Task<IReadOnlyList<Branch>> GetActiveBranchesAsync(Guid businessId);
        /// <summary>The user's assigned branches in the business that are active, default first.</summary>
        Task<IReadOnlyList<UserBranch>> GetUserBranchesAsync(Guid userId, Guid businessId);
        /// <summary>Replaces the user's branches in the business (their branches in other businesses are untouched).</summary>
        Task SetUserBranchesAsync(Guid userId, Guid businessId, IReadOnlyCollection<Guid> branchIds, Guid? defaultBranchId, Guid? changedBy);
        Task<Branch?> GetBranchByMpesaShortCodeAsync(string shortCode);
    }
}
