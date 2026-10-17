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
        Task<User?> GetUserForUpdateAsync(Guid userId);
        /// <summary>Creates the user, makes them a member of <paramref name="businessId"/> and gives them <paramref name="roleName"/> there.</summary>
        Task<User> CreateUserAsync(User user, Guid businessId, string roleName);
        Task UpdateUserAsync(User user);

        /// <summary>Active memberships of active businesses, default first.</summary>
        Task<IReadOnlyList<UserBusiness>> GetMembershipsAsync(Guid userId);
        Task<UserBusiness?> GetMembershipAsync(Guid userId, Guid businessId);
        Task AddMembershipAsync(UserBusiness membership);
        Task SetMembershipActiveAsync(Guid userId, Guid businessId, bool isActive, Guid? updatedBy);

        /// <summary>Roles the user holds in the business plus their platform roles. Null business = platform roles only.</summary>
        Task<IReadOnlyList<string>> GetUserRolesAsync(Guid userId, Guid? businessId);
        Task<IReadOnlyList<string>> GetUserPermissionsAsync(Guid userId, Guid? businessId);

        /// <summary>Members of the business (active or not) with their roles in it.</summary>
        Task<IReadOnlyList<(User User, bool MembershipActive)>> GetBusinessUsersAsync(Guid businessId);
        Task<(User User, bool MembershipActive)?> GetBusinessUserAsync(Guid businessId, Guid userId);
        Task<User> SetUserLockStatusAsync(Guid userId, bool isLocked, Guid? updatedBy);
        /// <summary>Replaces the user's roles in the business. Their roles in other businesses and platform roles are untouched.</summary>
        Task AssignRolesToUserAsync(Guid userId, Guid businessId, IEnumerable<Guid> roleIds);

        Task CreateSessionAsync(UserSession session);
        Task<UserSession?> GetActiveSessionAsync(string tokenHash);
        Task RevokeSessionAsync(Guid userSessionId);
        Task RevokeAllSessionsAsync(Guid userId, Guid? exceptSessionId = null);

        Task<IEnumerable<Role>> GetRolesAsync();
        Task<Role?> GetRoleByIdAsync(Guid roleId);
        Task<bool> RoleNameExistsAsync(string roleName, Guid? exceptRoleId = null);
        Task<Role> CreateRoleAsync(Role role);
        Task<Role> UpdateRoleAsync(Guid roleId, string roleName, string? description, Guid? updatedBy);
        Task<Role> SetRoleActiveStatusAsync(Guid roleId, bool isActive, Guid? updatedBy);
        Task AssignPermissionsToRoleAsync(Guid roleId, IEnumerable<Guid> permissionIds);
        Task<IEnumerable<PermissionModuleDto>> GetPermissionModulesAsync();
        Task<IEnumerable<Permission>> GetPermissionsAsync();
    }
}
