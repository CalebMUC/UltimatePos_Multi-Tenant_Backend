using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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

        public async Task<User?> GetUserAsync(string email)
        {
            return await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Email == email);
        }
        public async Task<User> CreateUserAsync(User user, string roleName)
        {
            var role = await _context.Roles.FirstOrDefaultAsync(r => r.RoleName == roleName)
                ?? throw new InvalidOperationException($"Role '{roleName}' does not exist.");

            _context.Users.Add(user);
            await _context.SaveChangesAsync(); // need user.Id before creating the join row

            _context.UserRoles.Add(new UserRole { UserId = user.UserId, RoleId = role.RoleId });
            await _context.SaveChangesAsync();

            return user;
        }

        public async Task<IEnumerable<string>> GetUserRolesAsync(Guid userId)
        {
            return await _context.UserRoles.AsNoTracking()
            .Where(ur => ur.UserId == userId && ur.Role.IsActive)
            .Select(ur => ur.Role.RoleName)
            .ToListAsync();
        }



        public async Task<IEnumerable<string>> GetUserPermissionsAsync(Guid userId) =>
        await _context.UserRoles.AsNoTracking()
            .Where(ur => ur.UserId == userId && ur.Role.IsActive)
            .SelectMany(ur => ur.Role.RolePermissions
                .Where(rp => rp.IsActive && rp.Permission.IsActive)
                .Select(rp => rp.Permission.Name))
            .Distinct()
            .ToListAsync();

        public async Task UpdateUserAsync(User user)
        {
            _context.Users.Update(user);
            await _context.SaveChangesAsync();
        }

        public async Task<IEnumerable<User>> GetUsersAsync() =>
        await _context.Users.AsNoTracking()
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .ToListAsync();

        public async Task<User?> GetUserByIdAsync(Guid userId) =>
        await _context.Users.AsNoTracking()
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.UserId == userId);

        public async Task<User> SetUserActiveStatusAsync(Guid userId, bool isActive, Guid? updatedBy)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserId == userId)
                ?? throw new NotFoundException($"User '{userId}' not found.");

            user.IsActive = isActive;
            user.LastUpdatedBy = updatedBy;
            user.LastUpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return user;
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

        public async Task AssignRolesToUserAsync(Guid userId, IEnumerable<Guid> roleIds)
        {
            var userExists = await _context.Users.AnyAsync(u => u.UserId == userId);
            if (!userExists)
                throw new NotFoundException($"User '{userId}' not found.");

            var ids = roleIds.Distinct().ToList();
            var validCount = await _context.Roles.CountAsync(r => ids.Contains(r.RoleId) && r.IsActive);
            if (validCount != ids.Count)
                throw new InvalidAssignmentException("One or more role ids are invalid or inactive.");

            _context.UserRoles.RemoveRange(_context.UserRoles.Where(ur => ur.UserId == userId));
            foreach (var id in ids)
                _context.UserRoles.Add(new UserRole { UserId = userId, RoleId = id });

            await _context.SaveChangesAsync();
        }

        public async Task<IEnumerable<Role>> GetRolesAsync() =>
        await _context.Roles.AsNoTracking()
            .Include(r => r.RolePermissions).ThenInclude(rp => rp.Permission)
            .ToListAsync();

        public async Task<Role?> GetRoleByIdAsync(Guid roleId) =>
        await _context.Roles.AsNoTracking()
            .Include(r => r.RolePermissions).ThenInclude(rp => rp.Permission)
            .FirstOrDefaultAsync(r => r.RoleId == roleId);




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

            _context.RolePermissions.RemoveRange(_context.RolePermissions.Where(rp => rp.RoleId == roleId));
            foreach (var id in ids)
                _context.RolePermissions.Add(new RolePermission { RoleId = roleId, PermissionId = id });

            await _context.SaveChangesAsync();
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
