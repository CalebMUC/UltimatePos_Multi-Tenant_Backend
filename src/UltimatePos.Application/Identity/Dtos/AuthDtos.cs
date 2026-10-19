using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UltimatePos.Domain.Entities;

namespace UltimatePos.Application.Identity.Dtos
{
        /// <param name="BusinessId">The business to sign in to. Omit to use the user's default business (or their only one).</param>
        /// <param name="BranchId">The branch to work at. Omit to use the user's default branch.</param>
        public record LoginRequest(string Email, string Password, Guid? BusinessId = null, Guid? BranchId = null);
        /// <param name="RoleName">A role of the business the caller is signed in to.</param>
        /// <param name="BranchIds">The branches the user will work in. Omit to use the caller's current branch.</param>
        /// <param name="DefaultBranchId">One of <paramref name="BranchIds"/>; the first is used when omitted.</param>
        public record RegisterRequest(string Email, string Password, string FullName, string PhoneNumber, string RoleName,
            IEnumerable<Guid>? BranchIds = null, Guid? DefaultBranchId = null);
        public record UserDto(Guid Id, string Email, string FullName, string? PhoneNumber,bool IsActive,bool IsLocked, IEnumerable<string> Roles, IEnumerable<string> Permissions);

        /// <param name="AccessStatus">Active, ExpiringSoon, GracePeriod, Expired, Unlicensed, Suspended or Closed — for the business picker.</param>
        public record BusinessMembershipDto(Guid BusinessId, string BusinessName, string? TradingName, BusinessKind Kind, BusinessType BusinessType,
            bool IsDefault, string? AccessStatus = null);

        /// <param name="ActiveBusinessId">The business the access token is for. Null when the user has platform access only.</param>
        /// <param name="MustChangePassword">Someone else set the password: send the user to change it before anything else.</param>
        /// <param name="LicenseWarning">Shown as a banner: the licence ends soon, or has ended and is in its grace period.</param>
        public record LoginResponseDto(string Token, DateTime ExpiresAt, UserDto User,
            string RefreshToken, DateTime RefreshTokenExpiresAt, Guid? ActiveBusinessId, IEnumerable<BusinessMembershipDto> Businesses,
            bool MustChangePassword = false, string? LicenseWarning = null,
            Guid? ActiveBranchId = null, IEnumerable<UltimatePos.Application.Branches.BranchMembershipDto>? Branches = null);

        public record RefreshTokenRequest(string RefreshToken);
        /// <param name="RefreshToken">The current refresh token, which is revoked once the new business's tokens are issued.</param>
        public record SwitchBusinessRequest(Guid BusinessId, string? RefreshToken, Guid? BranchId = null);
        /// <param name="RefreshToken">The current refresh token, which is revoked once the new branch's tokens are issued.</param>
        public record SwitchBranchRequest(Guid BranchId, string? RefreshToken);
        public record LogoutRequest(string RefreshToken);
        public record ChangePasswordRequest(string CurrentPassword, string NewPassword);
        public record ResetPasswordRequest(string NewPassword);
        public record UpdateUserRequestDto(string FullName, string Email, string? PhoneNumber);
        public record MeDto(UserDto User, Guid? ActiveBusinessId, IEnumerable<BusinessMembershipDto> Businesses,
            bool MustChangePassword = false, UltimatePos.Application.Platform.BusinessAccessDto? BusinessAccess = null,
            Guid? ActiveBranchId = null, IEnumerable<UltimatePos.Application.Branches.BranchMembershipDto>? Branches = null);

        public record RoleDto(Guid RoleId, string RoleName, string? Description,bool IsActive, IEnumerable<string> Permissions);
        public record CreateRoleRequestDto(string RoleName, string? Description);
        public record UpdateRoleRequestDto(string RoleName, string? Description);
        public record AssignRolesRequestDto(IEnumerable<Guid> RoleIds);
        public record AssignPermissionsRequestDto(IEnumerable<Guid> PermissionIds);
        public record PermissionModuleDto(string Module, IEnumerable<string> Permissions);
        public record PermissionDto(Guid PermissionId, string Name, string Module, bool IsActive);
}
