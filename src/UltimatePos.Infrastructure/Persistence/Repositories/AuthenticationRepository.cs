using Microsoft.EntityFrameworkCore;
using UltimatePos.Application.Identity;
using UltimatePos.Application.Identity.Dtos;
using UltimatePos.Domain.Entities;
using UltimatePos.Domain.Exceptions;

namespace UltimatePos.Infrastructure.Persistence.Repositories
{
    public class AuthenticationRepository : IAuthRepository
    {
        private readonly UltimatePosDbContext _context;
        public AuthenticationRepository(UltimatePosDbContext context) => _context = context;

        // ---- Users ----

        public async Task<User?> GetUserAsync(string email)
        {
            return await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Email == email);
        }

        public async Task<User?> GetUserForUpdateAsync(Guid userId) =>
            await _context.Users.FirstOrDefaultAsync(u => u.UserId == userId);

        public async Task<User> CreateUserAsync(User user, Guid businessId, string roleName)
        {
            // Roles are filtered to the business in scope, which is the business the user is being added to.
            var role = await _context.Roles.FirstOrDefaultAsync(r => r.RoleName == roleName && r.BusinessId == businessId && r.IsActive)
                ?? throw new ValidationException("roleName", $"Role '{roleName}' does not exist in this business, or is inactive.");

            await using var tx = await _context.Database.BeginTransactionAsync();

            _context.Users.Add(user);
            _context.UserBusinesses.Add(new UserBusiness { UserId = user.UserId, BusinessId = businessId, IsDefault = true, CreatedBy = user.CreatedBy });
            _context.UserRoles.Add(new UserRole { UserId = user.UserId, RoleId = role.RoleId });
            await _context.SaveChangesAsync();

            await tx.CommitAsync();
            return user;
        }

        public async Task UpdateUserAsync(User user)
        {
            if (_context.Entry(user).State == EntityState.Detached)
                _context.Users.Update(user);
            await _context.SaveChangesAsync();
        }

        public async Task<User> SetUserLockStatusAsync(Guid userId, bool isLocked, Guid? updatedBy)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserId == userId)
                ?? throw new NotFoundException($"User '{userId}' not found.");

            user.IsLocked = isLocked;
            if (!isLocked)
                user.FailedLoginAttempts = 0; // clear the counter so it doesn't re-lock on the next bad attempt
            user.LastUpdatedBy = updatedBy;
            user.LastUpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return user;
        }

        // ---- Memberships ----

        public async Task<IReadOnlyList<UserBusiness>> GetMembershipsAsync(Guid userId) =>
            await _context.UserBusinesses.AsNoTracking()
                .Include(m => m.Business)
                .Where(m => m.UserId == userId && m.IsActive && m.Business.IsActive)
                .OrderByDescending(m => m.IsDefault).ThenBy(m => m.Business.BusinessName)
                .ToListAsync();

        public async Task<UserBusiness?> GetMembershipAsync(Guid userId, Guid businessId) =>
            await _context.UserBusinesses.AsNoTracking().Include(m => m.Business)
                .FirstOrDefaultAsync(m => m.UserId == userId && m.BusinessId == businessId);

        public async Task AddMembershipAsync(UserBusiness membership)
        {
            _context.UserBusinesses.Add(membership);
            await _context.SaveChangesAsync();
        }

        public async Task SetMembershipActiveAsync(Guid userId, Guid businessId, bool isActive, Guid? updatedBy)
        {
            var membership = await _context.UserBusinesses.FirstOrDefaultAsync(m => m.UserId == userId && m.BusinessId == businessId)
                ?? throw new NotFoundException($"User '{userId}' not found.");

            membership.IsActive = isActive;
            membership.LastUpdatedBy = updatedBy;
            membership.LastUpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }

        // ---- Effective roles and permissions ----
        // Read across the tenant filter on purpose: sign-in happens before any business is in scope.

        private IQueryable<UserRole> EffectiveUserRoles(Guid userId, Guid? businessId) =>
            _context.UserRoles.AsNoTracking().IgnoreQueryFilters()
                .Where(ur => ur.UserId == userId && ur.Role.IsActive
                             && (ur.Role.BusinessId == null || (businessId != null && ur.Role.BusinessId == businessId)));

        public async Task<IReadOnlyList<string>> GetUserRolesAsync(Guid userId, Guid? businessId) =>
            await EffectiveUserRoles(userId, businessId)
                .Select(ur => ur.Role.RoleName)
                .Distinct()
                .ToListAsync();

        public async Task<IReadOnlyList<string>> GetUserPermissionsAsync(Guid userId, Guid? businessId)
        {
            var names = await EffectiveUserRoles(userId, businessId)
                .SelectMany(ur => ur.Role.RolePermissions
                    .Where(rp => rp.IsActive && rp.Permission.IsActive)
                    .Select(rp => new { rp.Permission.Name, Platform = ur.Role.BusinessId == null }))
                .Distinct()
                .ToListAsync();

            // A platform role only ever confers platform permissions, and a business role only business ones — whatever was
            // written to the tables directly.
            return names.Where(n => n.Platform == PermissionCatalog.IsPlatform(n.Name))
                .Select(n => n.Name).Distinct().ToList();
        }

        // ---- Users of a business ----

        public async Task<IReadOnlyList<(User User, bool MembershipActive)>> GetBusinessUsersAsync(Guid businessId)
        {
            var rows = await _context.UserBusinesses.AsNoTracking()
                .Where(m => m.BusinessId == businessId)
                .Select(m => new { m.User, m.IsActive })
                .ToListAsync();

            var userIds = rows.Select(r => r.User.UserId).ToList();
            var roles = await LoadBusinessRolesAsync(userIds, businessId);
            return rows.Select(r => (WithRoles(r.User, roles), r.IsActive)).OrderBy(r => r.Item1.Username).ToList();
        }

        public async Task<(User User, bool MembershipActive)?> GetBusinessUserAsync(Guid businessId, Guid userId)
        {
            var row = await _context.UserBusinesses.AsNoTracking()
                .Where(m => m.BusinessId == businessId && m.UserId == userId)
                .Select(m => new { m.User, m.IsActive })
                .FirstOrDefaultAsync();
            if (row is null)
                return null;

            var roles = await LoadBusinessRolesAsync(new[] { userId }, businessId);
            return (WithRoles(row.User, roles), row.IsActive);
        }

        private async Task<ILookup<Guid, UserRole>> LoadBusinessRolesAsync(IReadOnlyCollection<Guid> userIds, Guid businessId) =>
            (await _context.UserRoles.AsNoTracking().IgnoreQueryFilters()
                .Include(ur => ur.Role)
                .Where(ur => userIds.Contains(ur.UserId) && ur.Role.BusinessId == businessId)
                .ToListAsync())
            .ToLookup(ur => ur.UserId);

        private static User WithRoles(User user, ILookup<Guid, UserRole> roles)
        {
            user.UserRoles = roles[user.UserId].ToList();
            return user;
        }

        public async Task AssignRolesToUserAsync(Guid userId, Guid businessId, IEnumerable<Guid> roleIds)
        {
            var ids = roleIds.Distinct().ToList();
            // Roles is filtered to the business in scope, so a role id from another business simply doesn't count.
            var validCount = await _context.Roles.CountAsync(r => ids.Contains(r.RoleId) && r.BusinessId == businessId && r.IsActive);
            if (validCount != ids.Count)
                throw new InvalidAssignmentException("One or more role ids are invalid, inactive, or belong to another business.");

            await using var tx = await _context.Database.BeginTransactionAsync();
            var current = await _context.UserRoles.IgnoreQueryFilters()
                .Where(ur => ur.UserId == userId && ur.Role.BusinessId == businessId)
                .ToListAsync();
            _context.UserRoles.RemoveRange(current);
            foreach (var id in ids)
                _context.UserRoles.Add(new UserRole { UserId = userId, RoleId = id });

            await _context.SaveChangesAsync();
            await tx.CommitAsync();
        }

        // ---- Sessions (refresh tokens) ----

        public async Task CreateSessionAsync(UserSession session)
        {
            _context.UserSessions.Add(session);
            await _context.SaveChangesAsync();
        }

        public async Task<UserSession?> GetActiveSessionAsync(string tokenHash) =>
            await _context.UserSessions.AsNoTracking()
                .FirstOrDefaultAsync(s => s.SessionToken == tokenHash && s.IsActive && s.RevokedAt == null);

        public async Task RevokeSessionAsync(Guid userSessionId) =>
            await _context.UserSessions
                .Where(s => s.UserSessionId == userSessionId && s.IsActive)
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.IsActive, false).SetProperty(s => s.RevokedAt, DateTime.UtcNow));

        public async Task RevokeAllSessionsAsync(Guid userId, Guid? exceptSessionId = null) =>
            await _context.UserSessions
                .Where(s => s.UserId == userId && s.IsActive && (exceptSessionId == null || s.UserSessionId != exceptSessionId))
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.IsActive, false).SetProperty(s => s.RevokedAt, DateTime.UtcNow));

        // ---- Roles (filtered to the business in scope by the DbContext) ----

        public async Task<IEnumerable<Role>> GetRolesAsync() =>
        await _context.Roles.AsNoTracking()
            .Include(r => r.RolePermissions).ThenInclude(rp => rp.Permission)
            .OrderBy(r => r.RoleName)
            .ToListAsync();

        public async Task<Role?> GetRoleByIdAsync(Guid roleId) =>
        await _context.Roles.AsNoTracking()
            .Include(r => r.RolePermissions).ThenInclude(rp => rp.Permission)
            .FirstOrDefaultAsync(r => r.RoleId == roleId);

        public async Task<bool> RoleNameExistsAsync(string roleName, Guid? exceptRoleId = null) =>
            await _context.Roles.AnyAsync(r => r.RoleName.ToLower() == roleName.ToLower() && (exceptRoleId == null || r.RoleId != exceptRoleId));

        public async Task<Role> CreateRoleAsync(Role role)
        {
            _context.Roles.Add(role);
            await _context.SaveChangesAsync();
            return role;
        }

        public async Task<Role> UpdateRoleAsync(Guid roleId, string roleName, string? description, Guid? updatedBy)
        {
            var role = await _context.Roles.FirstOrDefaultAsync(r => r.RoleId == roleId)
                ?? throw new NotFoundException($"Role '{roleId}' not found.");

            role.RoleName = roleName;
            role.Description = description;
            role.LastUpdatedBy = updatedBy;
            role.LastUpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return role;
        }

        public async Task<Role> SetRoleActiveStatusAsync(Guid roleId, bool isActive, Guid? updatedBy)
        {
            var role = await _context.Roles.FirstOrDefaultAsync(r => r.RoleId == roleId)
                ?? throw new NotFoundException($"Role '{roleId}' not found.");

            role.IsActive = isActive;
            role.LastUpdatedBy = updatedBy;
            role.LastUpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return role;
        }

        public async Task AssignPermissionsToRoleAsync(Guid roleId, IEnumerable<Guid> permissionIds)
        {
            var roleExists = await _context.Roles.AnyAsync(r => r.RoleId == roleId);
            if (!roleExists)
                throw new NotFoundException($"Role '{roleId}' not found.");

            var ids = permissionIds.Distinct().ToList();
            var validCount = await _context.Permissions.CountAsync(p => ids.Contains(p.PermissionId) && p.IsActive);
            if (validCount != ids.Count)
                throw new InvalidAssignmentException("One or more permission ids are invalid or inactive.");

            await using var tx = await _context.Database.BeginTransactionAsync();
            _context.RolePermissions.RemoveRange(_context.RolePermissions.Where(rp => rp.RoleId == roleId));
            foreach (var id in ids)
                _context.RolePermissions.Add(new RolePermission { RoleId = roleId, PermissionId = id });

            await _context.SaveChangesAsync();
            await tx.CommitAsync();
        }

        public async Task<IEnumerable<PermissionModuleDto>> GetPermissionModulesAsync() =>
        await _context.Permissions
            .AsNoTracking()
            .GroupBy(p => p.Module)
            .Select(g => new PermissionModuleDto(g.Key, g.Select(p => p.Name)))
            .ToListAsync();

        public async Task<IEnumerable<Permission>> GetPermissionsAsync() =>
            await _context.Permissions.AsNoTracking().ToListAsync();

    }
}
