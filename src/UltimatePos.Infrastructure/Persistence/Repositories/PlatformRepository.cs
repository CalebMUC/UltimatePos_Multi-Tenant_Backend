using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using UltimatePos.Application.Business;
using UltimatePos.Application.Identity;
using UltimatePos.Application.Licensing;
using UltimatePos.Application.Notifications;
using UltimatePos.Application.Platform;
using UltimatePos.Domain.Entities;
using UltimatePos.Domain.Exceptions;
using static UltimatePos.Domain.Enums.PosEnums;

namespace UltimatePos.Infrastructure.Persistence.Repositories
{
    /// <summary>Super-user data access across businesses: reads past the tenant filters on purpose, always naming the business.</summary>
    public class PlatformRepository : IPlatformRepository
    {
        private readonly UltimatePosDbContext _context;
        public PlatformRepository(UltimatePosDbContext context) => _context = context;

        // ---------------------------------------------------------------- Businesses

        public async Task<IReadOnlyList<BusinessProfile>> GetBusinessesAsync() =>
            await _context.Businesses.AsNoTracking().OrderBy(b => b.BusinessName).ToListAsync();

        public async Task<BusinessProfile?> GetBusinessAsync(Guid businessId) =>
            await _context.Businesses.AsNoTracking().FirstOrDefaultAsync(b => b.BusinessId == businessId);

        public async Task<IReadOnlyDictionary<Guid, BusinessActivityRow>> GetActivityAsync(DateTime fromUtc, DateTime toUtcExclusive, Guid? businessId = null)
        {
            var members = await _context.UserBusinesses.AsNoTracking()
                .Where(m => m.IsActive && m.User.IsActive && (businessId == null || m.BusinessId == businessId))
                .GroupBy(m => m.BusinessId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count);

            var products = await _context.Products.IgnoreQueryFilters().AsNoTracking()
                .Where(p => p.IsActive && (businessId == null || p.BusinessId == businessId))
                .GroupBy(p => p.BusinessId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count);

            var sales = await _context.Sales.IgnoreQueryFilters().AsNoTracking()
                .Where(s => s.Status == SaleStatus.Completed && s.CreatedAt >= fromUtc && s.CreatedAt < toUtcExclusive
                            && (businessId == null || s.BusinessId == businessId))
                .GroupBy(s => s.BusinessId)
                .Select(g => new { g.Key, Count = g.Count(), Revenue = g.Sum(s => s.TotalAmount - s.ReturnedAmount) })
                .ToDictionaryAsync(g => g.Key);

            var lastSale = await _context.Sales.IgnoreQueryFilters().AsNoTracking()
                .Where(s => s.Status == SaleStatus.Completed && (businessId == null || s.BusinessId == businessId))
                .GroupBy(s => s.BusinessId).Select(g => new { g.Key, At = g.Max(s => s.CreatedAt) }).ToDictionaryAsync(g => g.Key, g => g.At);

            var lastSignIn = await _context.SystemEvents.IgnoreQueryFilters().AsNoTracking()
                .Where(e => e.EventType == SystemEventTypes.UserLoggedIn && e.BusinessId != null && (businessId == null || e.BusinessId == businessId))
                .GroupBy(e => e.BusinessId!.Value).Select(g => new { g.Key, At = g.Max(e => e.OccurredAt) }).ToDictionaryAsync(g => g.Key, g => g.At);

            var ids = members.Keys.Concat(products.Keys).Concat(sales.Keys).Concat(lastSale.Keys).Concat(lastSignIn.Keys).Distinct();
            return ids.ToDictionary(id => id, id => new BusinessActivityRow(id,
                members.GetValueOrDefault(id), products.GetValueOrDefault(id),
                sales.TryGetValue(id, out var s) ? s.Count : 0, s?.Revenue ?? 0m,
                lastSale.TryGetValue(id, out var ls) ? ls : null, lastSignIn.TryGetValue(id, out var si) ? si : null));
        }

        public async Task<decimal> GetLicenseIncomeAsync(DateTime fromUtc, DateTime toUtcExclusive) =>
            await _context.BusinessLicenses.IgnoreQueryFilters()
                .Where(l => l.State == LicenseState.Active && l.CreatedAt >= fromUtc && l.CreatedAt < toUtcExclusive)
                .SumAsync(l => (decimal?)l.Amount) ?? 0m;

        public async Task<int> CountActiveMembersAsync(Guid businessId) =>
            await _context.UserBusinesses.CountAsync(m => m.BusinessId == businessId && m.IsActive && m.User.IsActive);

        public async Task<BusinessProfile> UpdateBusinessStatusAsync(Guid businessId, Action<BusinessProfile> change)
        {
            var business = await _context.Businesses.FirstOrDefaultAsync(b => b.BusinessId == businessId)
                ?? throw new NotFoundException($"Business '{businessId}' not found.");
            change(business);
            business.LastUpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return business;
        }

        // ---------------------------------------------------------------- Licences

        public async Task<IReadOnlyList<BusinessLicense>> GetLicensesAsync(Guid businessId) =>
            await _context.BusinessLicenses.IgnoreQueryFilters().AsNoTracking()
                .Where(l => l.BusinessId == businessId)
                .OrderByDescending(l => l.StartsOn).ThenByDescending(l => l.CreatedAt)
                .ToListAsync();

        public async Task<BusinessLicense?> GetLicenseAsync(Guid licenseId) =>
            await _context.BusinessLicenses.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(l => l.BusinessLicenseId == licenseId);

        /// <remarks>Called with the licence's business in scope (the numbering is per business).</remarks>
        public async Task<BusinessLicense> AddLicenseAsync(BusinessLicense license, DateOnly today)
        {
            await using var tx = await _context.Database.BeginTransactionAsync();
            license.LicenseNumber = $"LIC-{await _context.NextSequenceNumberAsync("LIC"):D6}";
            _context.BusinessLicenses.Add(license);
            await _context.SaveChangesAsync();
            await RefreshCoverageAsync(license.BusinessId, today);
            await tx.CommitAsync();
            return license;
        }

        public async Task<BusinessLicense> CancelLicenseAsync(Guid licenseId, string reason, Guid? cancelledBy, DateOnly today)
        {
            await using var tx = await _context.Database.BeginTransactionAsync();
            var license = await _context.BusinessLicenses.IgnoreQueryFilters().FirstOrDefaultAsync(l => l.BusinessLicenseId == licenseId)
                ?? throw new NotFoundException($"Licence '{licenseId}' not found.");
            license.State = LicenseState.Cancelled;
            license.CancelledAt = DateTime.UtcNow;
            license.CancelledBy = cancelledBy;
            license.CancelReason = reason;
            await _context.SaveChangesAsync();
            await RefreshCoverageAsync(license.BusinessId, today);
            await tx.CommitAsync();
            return license;
        }

        public async Task<IReadOnlyList<BusinessLicense>> GetLicensesEndingBetweenAsync(DateOnly from, DateOnly to)
        {
            var ending = await _context.BusinessLicenses.IgnoreQueryFilters().AsNoTracking()
                .Where(l => l.State == LicenseState.Active && l.EndsOn >= from && l.EndsOn <= to)
                .ToListAsync();
            var businessIds = ending.Select(l => l.BusinessId).Distinct().ToList();
            var all = await _context.BusinessLicenses.IgnoreQueryFilters().AsNoTracking()
                .Where(l => l.State == LicenseState.Active && businessIds.Contains(l.BusinessId))
                .ToListAsync();

            // Only the period the business is actually on: one already followed by a renewal is not "ending".
            return ending
                .Where(l => !all.Any(n => n.BusinessId == l.BusinessId && n.StartsOn > l.StartsOn && n.StartsOn <= l.EndsOn.AddDays(1)))
                .OrderBy(l => l.EndsOn)
                .ToList();
        }

        /// <summary>Keeps BusinessProfile.LicenseEndsOn / LicenseMaxUsers in step with the licences (see LicensePolicy.Coverage).</summary>
        public async Task RefreshCoverageAsync(Guid businessId, DateOnly today)
        {
            var licenses = await _context.BusinessLicenses.IgnoreQueryFilters().AsNoTracking().Where(l => l.BusinessId == businessId).ToListAsync();
            var (endsOn, maxUsers) = LicensePolicy.Coverage(licenses, today);
            await _context.Businesses.Where(b => b.BusinessId == businessId)
                .ExecuteUpdateAsync(u => u.SetProperty(b => b.LicenseEndsOn, endsOn).SetProperty(b => b.LicenseMaxUsers, maxUsers));
        }

        // ---------------------------------------------------------------- Onboarding

        /// <remarks>Called with the new business in scope, so everything it starts with is stamped as its own.</remarks>
        public async Task<BusinessProfile> CreateBusinessAsync(BusinessProfile business, User owner, bool ownerIsNew, BusinessLicense? trialLicense, DateOnly today)
        {
            await using var tx = await _context.Database.BeginTransactionAsync();

            if (trialLicense is not null)
            {
                var (endsOn, maxUsers) = LicensePolicy.Coverage(new[] { trialLicense }, today);
                business.LicenseEndsOn = endsOn;
                business.LicenseMaxUsers = maxUsers;
            }
            _context.Businesses.Add(business);

            var businessPermissionNames = PermissionCatalog.Business.Select(p => p.Name).ToList();
            var permissions = await _context.Permissions.Where(p => businessPermissionNames.Contains(p.Name)).ToListAsync();
            var adminRole = new Role
            {
                RoleId = Guid.NewGuid(),
                BusinessId = business.BusinessId,
                RoleName = PermissionCatalog.BusinessAdminRole,
                Description = "Every business permission. Created with the business.",
                CreatedBy = business.CreatedBy
            };
            _context.Roles.Add(adminRole);
            _context.RolePermissions.AddRange(permissions.Select(p => new RolePermission { RoleId = adminRole.RoleId, PermissionId = p.PermissionId }));

            if (ownerIsNew)
                _context.Users.Add(owner);
            var hasDefault = !ownerIsNew && await _context.UserBusinesses.AnyAsync(m => m.UserId == owner.UserId && m.IsDefault);
            _context.UserBusinesses.Add(new UserBusiness { UserId = owner.UserId, BusinessId = business.BusinessId, IsDefault = !hasDefault, CreatedBy = business.CreatedBy });
            _context.UserRoles.Add(new UserRole { UserId = owner.UserId, RoleId = adminRole.RoleId });

            _context.UnitsOfMeasure.AddRange(BusinessDefaults.UnitsOfMeasure.Select(u => new UnitOfMeasure
            {
                BusinessId = business.BusinessId, Name = u.Name, Symbol = u.Symbol, CreatedBy = business.CreatedBy
            }));
            _context.ExpenseCategories.AddRange(BusinessDefaults.ExpenseCategories.Select(c => new ExpenseCategory
            {
                BusinessId = business.BusinessId, Code = c.Code, Name = c.Name, Description = c.Description, CreatedBy = business.CreatedBy
            }));

            await _context.SaveChangesAsync();

            if (trialLicense is not null)
            {
                trialLicense.LicenseNumber = $"LIC-{await _context.NextSequenceNumberAsync("LIC"):D6}";
                _context.BusinessLicenses.Add(trialLicense);
                await _context.SaveChangesAsync();
            }

            await tx.CommitAsync();
            return business;
        }

        public async Task AddBusinessAdminAsync(Guid businessId, User user, bool userIsNew)
        {
            await using var tx = await _context.Database.BeginTransactionAsync();
            var adminRole = await _context.Roles.IgnoreQueryFilters()
                .FirstOrDefaultAsync(r => r.BusinessId == businessId && r.RoleName == PermissionCatalog.BusinessAdminRole)
                ?? throw new NotFoundException("This business has no Admin role.");

            if (userIsNew)
                _context.Users.Add(user);

            var membership = userIsNew ? null : await _context.UserBusinesses.FirstOrDefaultAsync(m => m.UserId == user.UserId && m.BusinessId == businessId);
            if (membership is null)
            {
                var hasDefault = !userIsNew && await _context.UserBusinesses.AnyAsync(m => m.UserId == user.UserId && m.IsDefault);
                _context.UserBusinesses.Add(new UserBusiness { UserId = user.UserId, BusinessId = businessId, IsDefault = !hasDefault, CreatedBy = user.CreatedBy });
            }
            else
                membership.IsActive = true;

            if (userIsNew || !await _context.UserRoles.IgnoreQueryFilters().AnyAsync(ur => ur.UserId == user.UserId && ur.RoleId == adminRole.RoleId))
                _context.UserRoles.Add(new UserRole { UserId = user.UserId, RoleId = adminRole.RoleId });

            await _context.SaveChangesAsync();
            await tx.CommitAsync();
        }

        // ---------------------------------------------------------------- Super users

        private IQueryable<UserRole> SuperUserRoles() =>
            _context.UserRoles.IgnoreQueryFilters().Where(ur => ur.Role.BusinessId == null && ur.Role.RoleName == PermissionCatalog.SuperUserRole);

        public async Task<IReadOnlyList<User>> GetSuperUsersAsync() =>
            await SuperUserRoles().AsNoTracking().Select(ur => ur.User).Distinct().OrderBy(u => u.Username).ToListAsync();

        public async Task<User> CreateSuperUserAsync(User user)
        {
            var role = await _context.Roles.IgnoreQueryFilters().FirstOrDefaultAsync(r => r.BusinessId == null && r.RoleName == PermissionCatalog.SuperUserRole)
                ?? throw new NotFoundException("The Super User role does not exist — run the migrations.");
            _context.Users.Add(user);
            _context.UserRoles.Add(new UserRole { UserId = user.UserId, RoleId = role.RoleId });
            await _context.SaveChangesAsync();
            return user;
        }

        public async Task<bool> IsSuperUserAsync(Guid userId) =>
            await _context.UserRoles.IgnoreQueryFilters().AnyAsync(ur => ur.UserId == userId && ur.Role.BusinessId == null);

        public async Task<bool> HasBusinessMembershipsAsync(Guid userId) =>
            await _context.UserBusinesses.AnyAsync(m => m.UserId == userId);

        public async Task<int> CountActiveSuperUsersAsync() =>
            await SuperUserRoles().Where(ur => ur.User.IsActive && !ur.User.IsLocked).Select(ur => ur.UserId).Distinct().CountAsync();

        public async Task<Dictionary<Guid, DateTime>> GetLastSignInsAsync(IEnumerable<Guid> userIds)
        {
            var ids = userIds.Distinct().ToList();
            return await _context.SystemEvents.IgnoreQueryFilters().AsNoTracking()
                .Where(e => e.EventType == SystemEventTypes.UserLoggedIn && e.ActorUserId != null && ids.Contains(e.ActorUserId.Value))
                .GroupBy(e => e.ActorUserId!.Value).Select(g => new { g.Key, At = g.Max(e => e.OccurredAt) })
                .ToDictionaryAsync(g => g.Key, g => g.At);
        }
    }

    /// <summary>
    /// Whether a business may be used right now, decided by LicensePolicy and cached for a minute — it is asked on every request
    /// made in a business. Platform changes (suspend, licence) invalidate the entry so they take effect at once.
    /// </summary>
    public class BusinessAccessGate : IBusinessAccessGate
    {
        private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(60);

        private readonly UltimatePosDbContext _context;
        private readonly IMemoryCache _cache;
        private readonly LicensingSettings _settings;

        public BusinessAccessGate(UltimatePosDbContext context, IMemoryCache cache, LicensingSettings settings)
        {
            _context = context;
            _cache = cache;
            _settings = settings;
        }

        private static string Key(Guid businessId) => $"business-access:{businessId}";

        public async Task<BusinessAccess> CheckAsync(Guid businessId)
        {
            if (_cache.TryGetValue(Key(businessId), out BusinessAccess? cached) && cached is not null)
                return cached;

            var business = await _context.Businesses.AsNoTracking().FirstOrDefaultAsync(b => b.BusinessId == businessId);
            var access = business is null
                ? new BusinessAccess(false, "Closed", "BUSINESS_CLOSED", "This business no longer exists.", null, null)
                : LicensePolicy.Evaluate(business, _settings, LicensePolicy.Today);

            _cache.Set(Key(businessId), access, CacheFor);
            return access;
        }

        public void Invalidate(Guid businessId) => _cache.Remove(Key(businessId));
    }
}
