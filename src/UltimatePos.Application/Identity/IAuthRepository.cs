using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UltimatePos.Application.Identity.Dtos;
using UltimatePos.Domain.Entities;

namespace UltimatePos.Application.Identity
{
    public interface IAuthRepository
    {
        Task<User?> GetUserAsync(string email);
        Task<User> CreateUserAsync(User user, string roleName);
        Task<IEnumerable<string>> GetUserRolesAsync(Guid userId);
        Task<IEnumerable<string>> GetUserPermissionsAsync(Guid userId);
        Task<IEnumerable<Role>> GetRolesAsync();
        Task<Role> CreateRoleAsync(Role role);
        Task UpdateUserAsync(User user);
        Task<IEnumerable<User>> GetUsersAsync();
        Task<User?> GetUserByIdAsync(Guid userId);
        Task<User> SetUserActiveStatusAsync(Guid userId, bool isActive, Guid? updatedBy);
        Task<User> SetUserLockStatusAsync(Guid userId, bool isLocked, Guid? updatedBy);
        Task AssignRolesToUserAsync(Guid userId, IEnumerable<Guid> roleIds);
        Task<Role?> GetRoleByIdAsync(Guid roleId);
        Task<Role> UpdateRoleAsync(Guid roleId, string roleName, string? description, Guid? updatedBy);
        Task<Role> SetRoleActiveStatusAsync(Guid roleId, bool isActive, Guid? updatedBy);
        Task AssignPermissionsToRoleAsync(Guid roleId, IEnumerable<Guid> permissionIds);
        Task<IEnumerable<PermissionModuleDto>> GetPermissionModulesAsync();
        Task<IEnumerable<Permission>> GetPermissionsAsync();

    }
}
