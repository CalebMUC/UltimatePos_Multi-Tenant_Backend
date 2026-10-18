using UltimatePos.Domain.Entities;

namespace UltimatePos.Application.Platform
{
    public sealed record BusinessActivityRow(Guid BusinessId, int ActiveUsers, int ActiveProducts, int SaleCount, decimal Revenue,
        DateTime? LastSaleAt, DateTime? LastSignInAt);

    /// <summary>Super-user data access. Works ACROSS businesses (no business is in scope for a super user), so it reads past the tenant filters.</summary>
    public interface IPlatformRepository
    {
        Task<IReadOnlyList<BusinessProfile>> GetBusinessesAsync();
        Task<BusinessProfile?> GetBusinessAsync(Guid businessId);
        Task<IReadOnlyDictionary<Guid, BusinessActivityRow>> GetActivityAsync(DateTime fromUtc, DateTime toUtcExclusive, Guid? businessId = null);
        Task<decimal> GetLicenseIncomeAsync(DateTime fromUtc, DateTime toUtcExclusive);
        Task<int> CountActiveMembersAsync(Guid businessId);

        Task<BusinessProfile> UpdateBusinessStatusAsync(Guid businessId, Action<BusinessProfile> change);

        Task<IReadOnlyList<BusinessLicense>> GetLicensesAsync(Guid businessId);
        Task<BusinessLicense?> GetLicenseAsync(Guid licenseId);
        /// <summary>Saves the licence (numbered LIC-000001... per business) and refreshes the business's licence end date and user limit.</summary>
        Task<BusinessLicense> AddLicenseAsync(BusinessLicense license, DateOnly today);
        Task<BusinessLicense> CancelLicenseAsync(Guid licenseId, string reason, Guid? cancelledBy, DateOnly today);
        Task<IReadOnlyList<BusinessLicense>> GetLicensesEndingBetweenAsync(DateOnly from, DateOnly to);

        /// <summary>
        /// Registers the business with its Admin role (every business permission), the default units and expense categories, the
        /// owner as its first member and Admin (created when <paramref name="ownerIsNew"/>), and the trial licence if any — all or nothing.
        /// </summary>
        Task<BusinessProfile> CreateBusinessAsync(BusinessProfile business, User owner, bool ownerIsNew, BusinessLicense? trialLicense, DateOnly today);
        /// <summary>Makes the user an active member of the business holding its Admin role (creating the user when new).</summary>
        Task AddBusinessAdminAsync(Guid businessId, User user, bool userIsNew);

        /// <summary>Users holding the Super User role.</summary>
        Task<IReadOnlyList<User>> GetSuperUsersAsync();
        Task<User> CreateSuperUserAsync(User user);
        /// <summary>True when the user holds a platform role.</summary>
        Task<bool> IsSuperUserAsync(Guid userId);
        Task<bool> HasBusinessMembershipsAsync(Guid userId);
        Task<int> CountActiveSuperUsersAsync();
        Task<Dictionary<Guid, DateTime>> GetLastSignInsAsync(IEnumerable<Guid> userIds);
    }
}
