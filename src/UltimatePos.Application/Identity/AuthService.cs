using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UltimatePos.Application.Branches;
using UltimatePos.Application.Common;
using UltimatePos.Application.Common.Interfaces;
using UltimatePos.Application.Business;
using UltimatePos.Application.Licensing;
using UltimatePos.Application.Notifications;
using  UltimatePos.Application.Identity.Dtos;
using UltimatePos.Domain.Entities;
using UltimatePos.Domain.Exceptions;


namespace UltimatePos.Application.Identity
{
    public class AuthService
    {
        private const int MaxFailedLoginAttempts = 5;
        private const int MinPasswordLength = 8;

        private readonly IAuthRepository _authRepository;
        private readonly IPasswordHasher _passwordHasher;
        private readonly ITokenService _tokenService;
        private readonly ICurrentUser _currentUser;
        private readonly ITenantContext _tenant;
        private readonly ISystemEventRecorder _events;
        private readonly IBusinessAccessGate _gate;
        private readonly IBusinessRepository _businesses;
        private readonly IBranchRepository _branchRepository;
        private readonly IBranchContext _branches;
        public AuthService(IAuthRepository authRepository,
            IPasswordHasher hasher,
            ITokenService tokenService,
            ICurrentUser currentUser,
            ITenantContext tenant,
            ISystemEventRecorder events,
            IBusinessAccessGate gate,
            IBusinessRepository businesses,
            IBranchRepository branchRepository,
            IBranchContext branches)
        {
            _branchRepository = branchRepository;
            _branches = branches;
            _events = events;
            _gate = gate;
            _businesses = businesses;
            _authRepository = authRepository;
            _passwordHasher = hasher;
            _tokenService = tokenService;
            _currentUser = currentUser;
            _tenant = tenant;
        }

        // ---- Sign-in and sessions ----

        public async Task<LoginResponseDto> LoginAsync(LoginRequest request, string? deviceInfo = null, string? ipAddress = null)
        {
                var user = await _authRepository.GetUserAsync(request.Email)
                ?? throw new InvalidCredentialsException();

            if (!user.IsActive || user.IsLocked)
                throw new InvalidCredentialsException();

            if (!_passwordHasher.VerifyPassword(request.Password, user.PasswordHash, user.PasswordSalt))
            {
                user.FailedLoginAttempts++;
                if (user.FailedLoginAttempts >= MaxFailedLoginAttempts)
                    user.IsLocked = true;

                // Recorded with the counter update, in every business the user belongs to, so each business's admins see it.
                var userBusinesses = (await _authRepository.GetMembershipsAsync(user.UserId)).Select(m => (Guid?)m.BusinessId).DefaultIfEmpty(null);
                foreach (var memberOf in userBusinesses)
                    _events.Record(user.IsLocked
                        ? new SystemEventDraft(SystemEventTypes.UserAccountLocked,
                            $"{user.Username} ({user.Email}) was locked out after {user.FailedLoginAttempts} failed sign-in attempts.",
                            EventSeverity.Critical, "User", user.UserId, new { user.Email, ipAddress }, memberOf, ActorUserId: user.UserId,
                            SubjectUserId: user.UserId)
                        : new SystemEventDraft(SystemEventTypes.UserLoginFailed,
                            $"Failed sign-in for {user.Email} (attempt {user.FailedLoginAttempts} of {MaxFailedLoginAttempts}).",
                            EventSeverity.Warning, "User", user.UserId, new { user.Email, ipAddress, deviceInfo }, memberOf,
                            ActorUserId: user.UserId, SubjectUserId: user.UserId));

                await _authRepository.UpdateUserAsync(user);
                throw new InvalidCredentialsException();
            }

            if (user.FailedLoginAttempts > 0)
            {
                user.FailedLoginAttempts = 0;
                await _authRepository.UpdateUserAsync(user);
            }

            var memberships = await _authRepository.GetMembershipsAsync(user.UserId);
            var businessId = await ChooseBusinessAsync(user.UserId, memberships, request.BusinessId);

            _events.Record(new SystemEventDraft(SystemEventTypes.UserLoggedIn, $"{user.Username} signed in.",
                EntityType: "User", EntityId: user.UserId, Data: new { ipAddress, deviceInfo }, BusinessId: businessId,
                ActorUserId: user.UserId, SubjectUserId: user.UserId));
            return await IssueSessionAsync(user, businessId, memberships, deviceInfo, ipAddress, request.BranchId);
        }

        /// <summary>Swaps a refresh token for a new pair (rotation). Roles and permissions are re-read, so changes apply without signing in again.</summary>
        public async Task<LoginResponseDto> RefreshAsync(RefreshTokenRequest request, string? deviceInfo = null, string? ipAddress = null)
        {
            var session = await GetValidSessionAsync(request.RefreshToken);
            var user = await _authRepository.GetUserForUpdateAsync(session.UserId);
            if (user is null || !user.IsActive || user.IsLocked)
            {
                await _authRepository.RevokeSessionAsync(session.UserSessionId);
                throw new InvalidTokenException("This session has ended. Sign in again.");
            }

            var memberships = await _authRepository.GetMembershipsAsync(user.UserId);
            if (session.BusinessId is { } sessionBusiness && memberships.All(m => m.BusinessId != sessionBusiness))
            {
                await _authRepository.RevokeSessionAsync(session.UserSessionId);
                throw new InvalidTokenException("You no longer have access to this business. Sign in again.");
            }

            if (session.BusinessId is { } refreshBusiness)
                await EnsureAccessibleAsync(refreshBusiness);

            await _authRepository.RevokeSessionAsync(session.UserSessionId);
            // The same branch while the user may still work there; otherwise their default (branches can be closed or reassigned).
            return await IssueSessionAsync(user, session.BusinessId, memberships, deviceInfo ?? session.DeviceInfo, ipAddress ?? session.IpAddress,
                session.BranchId, requestedIsPreference: true);
        }

        public async Task<LoginResponseDto> SwitchBusinessAsync(SwitchBusinessRequest request, string? deviceInfo = null, string? ipAddress = null)
        {
            var userId = RequireUserId();
            var user = await _authRepository.GetUserForUpdateAsync(userId)
                ?? throw new InvalidTokenException("This session has ended. Sign in again.");

            var memberships = await _authRepository.GetMembershipsAsync(userId);
            var businessId = await ChooseBusinessAsync(userId, memberships, request.BusinessId);

            if (!string.IsNullOrWhiteSpace(request.RefreshToken))
            {
                var old = await _authRepository.GetActiveSessionAsync(_tokenService.HashRefreshToken(request.RefreshToken));
                if (old is not null && old.UserId == userId)
                    await _authRepository.RevokeSessionAsync(old.UserSessionId);
            }

            _events.Record(new SystemEventDraft(SystemEventTypes.UserSwitchedBusiness, $"{user.Username} switched to this business.",
                EntityType: "User", EntityId: userId, BusinessId: businessId, ActorUserId: userId, SubjectUserId: userId));
            return await IssueSessionAsync(user, businessId, memberships, deviceInfo, ipAddress, request.BranchId);
        }

        /// <summary>Moves the user to another branch of the business they are signed in to: new tokens for that branch.</summary>
        public async Task<LoginResponseDto> SwitchBranchAsync(SwitchBranchRequest request, string? deviceInfo = null, string? ipAddress = null)
        {
            var userId = RequireUserId();
            var businessId = _currentUser.BusinessId
                ?? throw new ForbiddenException("Sign in to a business before choosing a branch.");
            var user = await _authRepository.GetUserForUpdateAsync(userId)
                ?? throw new InvalidTokenException("This session has ended. Sign in again.");

            var memberships = await _authRepository.GetMembershipsAsync(userId);
            if (memberships.All(m => m.BusinessId != businessId))
                throw new InvalidTokenException("You no longer have access to this business. Sign in again.");
            await EnsureAccessibleAsync(businessId);

            if (!string.IsNullOrWhiteSpace(request.RefreshToken))
            {
                var old = await _authRepository.GetActiveSessionAsync(_tokenService.HashRefreshToken(request.RefreshToken));
                if (old is not null && old.UserId == userId)
                    await _authRepository.RevokeSessionAsync(old.UserSessionId);
            }

            var response = await IssueSessionAsync(user, businessId, memberships, deviceInfo, ipAddress, request.BranchId);
            var branch = response.Branches?.FirstOrDefault(b => b.BranchId == response.ActiveBranchId);
            await _events.RecordNowAsync(new SystemEventDraft(SystemEventTypes.UserSwitchedBranch,
                $"{user.Username} switched to branch {branch?.KraBranchId} {branch?.Name}.",
                EntityType: "User", EntityId: userId, BusinessId: businessId, ActorUserId: userId, SubjectUserId: userId,
                BranchId: response.ActiveBranchId));
            return response;
        }

        public async Task LogoutAsync(LogoutRequest request)
        {
            var session = await _authRepository.GetActiveSessionAsync(_tokenService.HashRefreshToken(request.RefreshToken ?? string.Empty));
            if (session is not null && session.UserId == _currentUser.UserId)
            {
                await _authRepository.RevokeSessionAsync(session.UserSessionId);
                await _events.RecordNowAsync(new SystemEventDraft(SystemEventTypes.UserLoggedOut, "Signed out.",
                    EntityType: "User", EntityId: session.UserId, BusinessId: session.BusinessId, SubjectUserId: session.UserId));
            }
        }

        public async Task<MeDto> GetMeAsync()
        {
            var userId = RequireUserId();
            var user = await _authRepository.GetUserForUpdateAsync(userId)
                ?? throw new InvalidTokenException("This session has ended. Sign in again.");

            var businessId = _currentUser.BusinessId;
            var memberships = await _authRepository.GetMembershipsAsync(userId);
            var roles = await _authRepository.GetUserRolesAsync(userId, businessId);
            var permissions = await _authRepository.GetUserPermissionsAsync(userId, businessId);

            var branches = businessId is { } bid ? await AllowedBranchesAsync(userId, bid, permissions) : new List<BranchMembershipDto>();
            return new MeDto(new UserDto(user.UserId, user.Email, user.Username, user.PhoneNumber, user.IsActive, user.IsLocked, roles, permissions),
                businessId, await MembershipDtosAsync(memberships), user.MustChangePassword,
                businessId is { } b ? Platform.PlatformService.ToAccessDto(await _gate.CheckAsync(b)) : null,
                _currentUser.BranchId, branches);
        }

        public async Task ChangePasswordAsync(ChangePasswordRequest request)
        {
            var userId = RequireUserId();
            var user = await _authRepository.GetUserForUpdateAsync(userId)
                ?? throw new InvalidTokenException("This session has ended. Sign in again.");

            if (!_passwordHasher.VerifyPassword(request.CurrentPassword ?? string.Empty, user.PasswordHash, user.PasswordSalt))
                throw new ValidationException("currentPassword", "The current password is not correct.");

            SetPassword(user, request.NewPassword, userId);
            user.MustChangePassword = false;
            _events.Record(new SystemEventDraft(SystemEventTypes.UserPasswordChanged,
                $"The password for {user.Email} was changed. If this wasn't you, contact your administrator now.",
                EventSeverity.Warning, "User", userId, SubjectUserId: userId));
            await _authRepository.UpdateUserAsync(user);
            // Signs out every other device; the caller's access token stays valid until it expires.
            await _authRepository.RevokeAllSessionsAsync(userId);
        }

        /// <summary>
        /// The business to sign in to: the one asked for, else the default, else the first the user can actually use today. A
        /// suspended, closed or unlicensed business is refused with its reason, so the user knows to call, not retry.
        /// </summary>
        private async Task<Guid?> ChooseBusinessAsync(Guid userId, IReadOnlyList<UserBusiness> memberships, Guid? requested)
        {
            if (requested is { } wanted)
            {
                if (memberships.All(m => m.BusinessId != wanted))
                    throw new ForbiddenException("You do not have access to that business, or it has been deactivated.");
                await EnsureAccessibleAsync(wanted);
                return wanted;
            }

            BusinessAccess? firstRefusal = null;
            foreach (var membership in memberships.OrderByDescending(m => m.IsDefault))
            {
                var access = await _gate.CheckAsync(membership.BusinessId);
                if (access.Allowed)
                    return membership.BusinessId;
                firstRefusal ??= access;
            }
            if (firstRefusal is not null)
                throw new BusinessAccessDeniedException(firstRefusal.Code!, firstRefusal.Message!);

            // No business: allowed only for platform users (they can register the first business).
            if ((await _authRepository.GetUserPermissionsAsync(userId, null)).Count > 0)
                return null;

            throw new ForbiddenException("Your account is not linked to any active business. Ask an administrator for access.");
        }

        private async Task EnsureAccessibleAsync(Guid businessId)
        {
            var access = await _gate.CheckAsync(businessId);
            if (!access.Allowed)
                throw new BusinessAccessDeniedException(access.Code!, access.Message!);
        }

        /// <summary>The licence's user limit, checked before a member is added or reactivated.</summary>
        private async Task EnsureSeatAvailableAsync(Guid businessId)
        {
            var business = await _businesses.GetBusinessByIdAsync(businessId);
            if (business?.LicenseMaxUsers is not { } limit)
                return;
            var active = (await _authRepository.GetBusinessUsersAsync(businessId)).Count(m => m.MembershipActive && m.User.IsActive);
            if (active >= limit)
                throw new BusinessAccessDeniedException("USER_LIMIT_REACHED",
                    $"The licence allows {limit} active user{(limit == 1 ? "" : "s")}, and {active} are active. Deactivate someone, or ask for a larger licence.");
        }

        private async Task<IReadOnlyList<BusinessMembershipDto>> MembershipDtosAsync(IReadOnlyList<UserBusiness> memberships)
        {
            var result = new List<BusinessMembershipDto>();
            foreach (var m in memberships)
                result.Add(ToMembershipDto(m) with { AccessStatus = (await _gate.CheckAsync(m.BusinessId)).Status });
            return result;
        }

        /// <summary>
        /// The branches the user may work at in the business: every open branch with Branches.AccessAll, otherwise the open branches
        /// they are assigned to. Default first.
        /// </summary>
        private async Task<List<BranchMembershipDto>> AllowedBranchesAsync(Guid userId, Guid businessId, IReadOnlyCollection<string> permissions)
        {
            var assigned = await _branchRepository.GetUserBranchesAsync(userId, businessId);
            var defaultId = assigned.FirstOrDefault(a => a.IsDefault)?.BranchId;
            IEnumerable<Branch> branches = permissions.Contains(BranchContext.AccessAllPermission)
                ? await _branchRepository.GetActiveBranchesAsync(businessId)
                : assigned.Select(a => a.Branch);
            return branches
                .Select(b => new BranchMembershipDto(b.BranchId, b.BranchNumber, b.KraBranchId, b.Name, b.Code, b.BranchId == defaultId))
                .OrderByDescending(b => b.IsDefault).ThenBy(b => b.BranchNumber)
                .ToList();
        }

        /// <summary>
        /// The branch to work at: the one asked for, else the default, else the first allowed (the head office first, for those who
        /// may use every branch). A user with no branch can't work in the business yet.
        /// </summary>
        private static Guid ChooseBranch(IReadOnlyList<BranchMembershipDto> allowed, Guid? requested, bool requestedIsPreference)
        {
            if (requested is { } wanted)
            {
                if (allowed.Any(b => b.BranchId == wanted))
                    return wanted;
                if (!requestedIsPreference)
                    throw new ForbiddenException("You do not work at that branch, or it has been closed.");
            }
            if (allowed.Count == 0)
                throw new BusinessAccessDeniedException("NO_BRANCH",
                    "You are not assigned to any open branch of this business. Ask an administrator to assign you one.");
            return (allowed.FirstOrDefault(b => b.IsDefault) ?? allowed[0]).BranchId;
        }

        private async Task<LoginResponseDto> IssueSessionAsync(User user, Guid? businessId, IReadOnlyList<UserBusiness> memberships,
            string? deviceInfo, string? ipAddress, Guid? requestedBranchId = null, bool requestedIsPreference = false)
        {
            var roles = await _authRepository.GetUserRolesAsync(user.UserId, businessId);
            var permissions = await _authRepository.GetUserPermissionsAsync(user.UserId, businessId);

            List<BranchMembershipDto> branches = new();
            Guid? branchId = null;
            if (businessId is { } inBusiness)
            {
                branches = await AllowedBranchesAsync(user.UserId, inBusiness, permissions);
                branchId = ChooseBranch(branches, requestedBranchId, requestedIsPreference);
            }
            // Only assigned branches go in the token: with Branches.AccessAll every branch is reachable anyway.
            var tokenBranches = permissions.Contains(BranchContext.AccessAllPermission)
                ? Enumerable.Empty<Guid>()
                : branches.Select(b => b.BranchId);

            var (token, expiresAt) = _tokenService.GenerateToken(user, businessId, branchId, tokenBranches, permissions, roles);
            var (refreshToken, refreshExpiresAt) = _tokenService.GenerateRefreshToken();

            await _authRepository.CreateSessionAsync(new UserSession
            {
                UserSessionId = Guid.NewGuid(),
                UserId = user.UserId,
                BusinessId = businessId,
                BranchId = branchId,
                SessionToken = _tokenService.HashRefreshToken(refreshToken),
                DeviceInfo = Truncate(deviceInfo, 500),
                IpAddress = Truncate(ipAddress, 64),
                LoginAt = DateTime.UtcNow,
                ExpiresAt = refreshExpiresAt,
                LastAccessedAt = DateTime.UtcNow,
                IsActive = true
            });

            var userDto = new UserDto(user.UserId, user.Email, user.Username, user.PhoneNumber, user.IsActive, user.IsLocked, roles, permissions);
            var warning = businessId is { } b ? (await _gate.CheckAsync(b)).Warning : null;
            return new LoginResponseDto(token, expiresAt, userDto, refreshToken, refreshExpiresAt, businessId, await MembershipDtosAsync(memberships),
                user.MustChangePassword, warning, branchId, branches);
        }

        private async Task<UserSession> GetValidSessionAsync(string? refreshToken)
        {
            if (string.IsNullOrWhiteSpace(refreshToken))
                throw new InvalidTokenException("A refresh token is required.");

            var session = await _authRepository.GetActiveSessionAsync(_tokenService.HashRefreshToken(refreshToken));
            if (session is null || session.ExpiresAt <= DateTime.UtcNow)
                throw new InvalidTokenException("This session has ended. Sign in again.");
            return session;
        }

        // ---- Users of the business signed in to ----

        public async Task<UserDto> RegisterAsync(RegisterRequest request)
        {
            var businessId = _tenant.RequireBusinessId();

            var errors = new Dictionary<string, string[]>();
            if (string.IsNullOrWhiteSpace(request.Email) || !request.Email.Contains('@'))
                errors["email"] = new[] { "A valid email address is required." };
            if (string.IsNullOrWhiteSpace(request.FullName))
                errors["fullName"] = new[] { "Full name is required." };
            if (string.IsNullOrWhiteSpace(request.RoleName))
                errors["roleName"] = new[] { "A role is required." };
            var passwordError = PasswordError(request.Password);
            if (passwordError is not null)
                errors["password"] = new[] { passwordError };
            if (errors.Count > 0)
                throw new ValidationException(errors);

            var email = request.Email.Trim();
            var existingUser = await _authRepository.GetUserAsync(email);
            if (existingUser != null)
                throw new UserAlreadyExistsException();
            await EnsureSeatAvailableAsync(businessId);
            var (branchIds, defaultBranchId) = await ResolveNewUserBranchesAsync(request.BranchIds, request.DefaultBranchId);
            var (passwordHash, passwordSalt) = _passwordHasher.HashPassword(request.Password);
            var newUser = new User
            {
                UserId = Guid.NewGuid(),
                Email = email,
                Username = request.FullName.Trim(),
                PhoneNumber = request.PhoneNumber,
                PasswordHash = passwordHash,
                PasswordSalt = passwordSalt,
                IsActive = true,
                MustChangePassword = true,
                CreatedBy = _currentUser.UserId
            };
            var createdUser = await _authRepository.CreateUserAsync(newUser, businessId, request.RoleName.Trim());
            await _branchRepository.SetUserBranchesAsync(createdUser.UserId, businessId, branchIds, defaultBranchId, _currentUser.UserId);
            await _events.RecordNowAsync(new SystemEventDraft(SystemEventTypes.UserCreated,
                $"{createdUser.Username} ({createdUser.Email}) was added with the {request.RoleName.Trim()} role. Sign in at Ultimate POS with this email address.",
                EntityType: "User", EntityId: createdUser.UserId, Data: new { createdUser.Email, role = request.RoleName.Trim() },
                SubjectUserId: createdUser.UserId));
            return await GetUserByIdAsync(createdUser.UserId);
        }

        /// <summary>The new user's branches: those asked for (open, and ones the caller can see), else the caller's current branch.</summary>
        private async Task<(List<Guid> Ids, Guid? DefaultId)> ResolveNewUserBranchesAsync(IEnumerable<Guid>? requested, Guid? requestedDefault)
        {
            var ids = (requested ?? Enumerable.Empty<Guid>()).Distinct().ToList();
            if (ids.Count == 0 && _branches.ActiveBranchId is { } current)
                ids.Add(current);

            var open = (await _branchRepository.GetBranchesAsync(includeInactive: false)).Select(b => b.BranchId).ToHashSet();
            if (ids.Any(id => !open.Contains(id) || !_branches.CanSee(id)))
                throw new ValidationException("branchIds", "One or more branches don't exist in this business, are closed, or aren't yours to assign.");
            if (requestedDefault is { } d && !ids.Contains(d))
                throw new ValidationException("defaultBranchId", "The default branch must be one of the user's branches.");
            return (ids, requestedDefault ?? (ids.Count > 0 ? (Guid?)ids[0] : null));
        }

        public async Task<IEnumerable<UserDto>> GetUsersAsync()
        {
            var users = await _authRepository.GetBusinessUsersAsync(_tenant.RequireBusinessId());
            return users.Select(u => ToUserDto(u.User, u.MembershipActive));
        }

        public async Task<UserDto> GetUserByIdAsync(Guid userId)
        {
            var member = await _authRepository.GetBusinessUserAsync(_tenant.RequireBusinessId(), userId)
                ?? throw new NotFoundException($"User '{userId}' not found.");
            return ToUserDto(member.User, member.MembershipActive);
        }

        public async Task<UserDto> UpdateUserAsync(Guid userId, UpdateUserRequestDto request)
        {
            await GetUserByIdAsync(userId); // must belong to this business

            var errors = new Dictionary<string, string[]>();
            if (string.IsNullOrWhiteSpace(request.FullName))
                errors["fullName"] = new[] { "Full name is required." };
            if (string.IsNullOrWhiteSpace(request.Email) || !request.Email.Contains('@'))
                errors["email"] = new[] { "A valid email address is required." };
            if (errors.Count > 0)
                throw new ValidationException(errors);

            var email = request.Email.Trim();
            var holder = await _authRepository.GetUserAsync(email);
            if (holder is not null && holder.UserId != userId)
                throw new UserAlreadyExistsException();

            var user = (await _authRepository.GetUserForUpdateAsync(userId))!;
            user.Username = request.FullName.Trim();
            user.Email = email;
            user.PhoneNumber = request.PhoneNumber;
            user.LastUpdatedBy = _currentUser.UserId;
            user.LastUpdatedAt = DateTime.UtcNow;
            _events.Record(new SystemEventDraft(SystemEventTypes.UserUpdated, $"{user.Username}'s profile was updated.",
                EntityType: "User", EntityId: userId, Data: new { user.Email, user.PhoneNumber }, SubjectUserId: userId));
            await _authRepository.UpdateUserAsync(user);
            return await GetUserByIdAsync(userId);
        }

        /// <summary>An administrator sets a new password. Unlocks the account and signs the user out everywhere.</summary>
        public async Task<UserDto> ResetPasswordAsync(Guid userId, ResetPasswordRequest request)
        {
            await GetUserByIdAsync(userId);
            var user = (await _authRepository.GetUserForUpdateAsync(userId))!;
            SetPassword(user, request.NewPassword, _currentUser.UserId);
            user.MustChangePassword = true;
            user.IsLocked = false;
            user.FailedLoginAttempts = 0;
            _events.Record(new SystemEventDraft(SystemEventTypes.UserPasswordReset,
                $"An administrator reset the password for {user.Email}. Ask them for the new password, then change it after signing in.",
                EventSeverity.Warning, "User", userId, SubjectUserId: userId));
            await _authRepository.UpdateUserAsync(user);
            await _authRepository.RevokeAllSessionsAsync(userId);
            return await GetUserByIdAsync(userId);
        }

        public async Task<UserDto> AssignRolesToUserAsync(Guid userId, AssignRolesRequestDto request)
        {
            var businessId = _tenant.RequireBusinessId();
            await GetUserByIdAsync(userId);
            await _authRepository.AssignRolesToUserAsync(userId, businessId, request.RoleIds ?? Enumerable.Empty<Guid>());
            var updated = await GetUserByIdAsync(userId);
            await _events.RecordNowAsync(new SystemEventDraft(SystemEventTypes.UserRolesChanged,
                $"{updated.FullName}'s roles are now: {(updated.Roles.Any() ? string.Join(", ", updated.Roles) : "none")}.",
                EntityType: "User", EntityId: userId, Data: new { roles = updated.Roles }, SubjectUserId: userId));
            return updated;
        }

        /// <summary>
        /// Deactivating removes access to THIS business only — users can belong to several businesses, and one business must not be
        /// able to lock someone out of another. Their sessions in this business end.
        /// </summary>
        public async Task<UserDto> SetUserActiveStatusAsync(Guid userId, bool isActive)
        {
            var businessId = _tenant.RequireBusinessId();
            await GetUserByIdAsync(userId);
            if (!isActive && userId == _currentUser.UserId)
                throw new InvalidAssignmentException("You cannot deactivate your own access.");
            if (isActive && !(await GetUserByIdAsync(userId)).IsActive)
                await EnsureSeatAvailableAsync(businessId);

            await _authRepository.SetMembershipActiveAsync(userId, businessId, isActive, _currentUser.UserId);
            if (!isActive)
                await _authRepository.RevokeAllSessionsAsync(userId);
            var result = await GetUserByIdAsync(userId);
            await _events.RecordNowAsync(new SystemEventDraft(isActive ? SystemEventTypes.UserActivated : SystemEventTypes.UserDeactivated,
                $"{result.FullName} was {(isActive ? "given access to" : "removed from")} this business.",
                EntityType: "User", EntityId: userId, SubjectUserId: userId));
            return result;
        }

        public async Task<UserDto> SetUserLockStatusAsync(Guid userId, bool isLocked)
        {
            await GetUserByIdAsync(userId);
            if (isLocked && userId == _currentUser.UserId)
                throw new InvalidAssignmentException("You cannot lock your own account.");

            await _authRepository.SetUserLockStatusAsync(userId, isLocked, _currentUser.UserId);
            if (isLocked)
                await _authRepository.RevokeAllSessionsAsync(userId);
            var result = await GetUserByIdAsync(userId);
            await _events.RecordNowAsync(new SystemEventDraft(isLocked ? SystemEventTypes.UserLocked : SystemEventTypes.UserUnlocked,
                $"{result.FullName}'s account was {(isLocked ? "locked" : "unlocked")} by an administrator.",
                isLocked ? EventSeverity.Warning : EventSeverity.Info, "User", userId, SubjectUserId: userId));
            return result;
        }

        // ---- Roles of the business signed in to ----

        public async Task<IEnumerable<RoleDto>> GetRolesAsync()
        {
            _tenant.RequireBusinessId();
            var roles = await _authRepository.GetRolesAsync();
            return roles.Select(ToRoleDto);
        }
        public async Task<RoleDto> CreateRoleAsync(CreateRoleRequestDto request)
        {
            _tenant.RequireBusinessId();
            if (string.IsNullOrWhiteSpace(request.RoleName))
                throw new ValidationException("roleName", "Role name is required.");
            if (await _authRepository.RoleNameExistsAsync(request.RoleName.Trim()))
                throw new ValidationException("roleName", $"A role named '{request.RoleName.Trim()}' already exists.");

            var role = new Role { RoleId = Guid.NewGuid(), RoleName = request.RoleName.Trim(), Description = request.Description, CreatedBy = _currentUser.UserId };
            _events.Record(new SystemEventDraft(SystemEventTypes.RoleCreated, $"Role '{role.RoleName}' was created.",
                EntityType: "Role", EntityId: role.RoleId));
            var created = await _authRepository.CreateRoleAsync(role);
            return ToRoleDto(created);
        }
        public async Task<RoleDto> GetRoleByIdAsync(Guid roleId)
        {
            _tenant.RequireBusinessId();
            var role = await _authRepository.GetRoleByIdAsync(roleId)
                ?? throw new NotFoundException($"Role '{roleId}' not found.");
            return ToRoleDto(role);
        }

        public async Task<RoleDto> UpdateRoleAsync(Guid roleId, UpdateRoleRequestDto request)
        {
            await GetRoleByIdAsync(roleId);
            if (string.IsNullOrWhiteSpace(request.RoleName))
                throw new ValidationException("roleName", "Role name is required.");
            if (await _authRepository.RoleNameExistsAsync(request.RoleName.Trim(), roleId))
                throw new ValidationException("roleName", $"A role named '{request.RoleName.Trim()}' already exists.");

            var updated = await _authRepository.UpdateRoleAsync(roleId, request.RoleName.Trim(), request.Description, _currentUser.UserId);
            return await GetRoleByIdAsync(updated.RoleId);
        }

        public async Task<RoleDto> SetRoleActiveStatusAsync(Guid roleId, bool isActive)
        {
            await GetRoleByIdAsync(roleId);
            var updated = await _authRepository.SetRoleActiveStatusAsync(roleId, isActive, _currentUser.UserId);
            return await GetRoleByIdAsync(updated.RoleId);
        }

        public async Task<RoleDto> AssignPermissionsToRoleAsync(Guid roleId, AssignPermissionsRequestDto request)
        {
            await GetRoleByIdAsync(roleId);

            var ids = (request.PermissionIds ?? Enumerable.Empty<Guid>()).Distinct().ToList();
            var platform = (await _authRepository.GetPermissionsAsync())
                .Where(p => ids.Contains(p.PermissionId) && PermissionCatalog.IsPlatform(p.Name))
                .Select(p => p.Name)
                .ToList();
            if (platform.Count > 0)
                throw new ForbiddenException(
                    $"Platform permissions cannot be given to a business role: {string.Join(", ", platform)}.");

            await _authRepository.AssignPermissionsToRoleAsync(roleId, ids);
            var role = await GetRoleByIdAsync(roleId);
            await _events.RecordNowAsync(new SystemEventDraft(SystemEventTypes.RolePermissionsChanged,
                $"Role '{role.RoleName}' now has {role.Permissions.Count()} permission(s). Holders get the change at their next sign-in or token refresh.",
                EntityType: "Role", EntityId: roleId, Data: new { permissions = role.Permissions }));
            return role;
        }

        /// <summary>The permissions a business role may hold (platform permissions are left out — they cannot be granted here).</summary>
        public async Task<IEnumerable<PermissionDto>> GetPermissionsAsync()
        {
            var permissions = await _authRepository.GetPermissionsAsync();
            return permissions.Where(p => !PermissionCatalog.IsPlatform(p.Name))
                .Select(p => new PermissionDto(p.PermissionId, p.Name, p.Module, p.IsActive));
        }
        public async Task<IEnumerable<PermissionModuleDto>> GetPermissionModulesAsync() =>
            (await _authRepository.GetPermissionModulesAsync())
                .Select(m => m with { Permissions = m.Permissions.Where(p => !PermissionCatalog.IsPlatform(p)).ToList() })
                .Where(m => m.Permissions.Any());

        // ---- Helpers ----

        private Guid RequireUserId() =>
            _currentUser.UserId is { } id && id != Guid.Empty ? id : throw new InvalidTokenException("Sign in first.");

        private void SetPassword(User user, string? newPassword, Guid? changedBy)
        {
            var error = PasswordError(newPassword);
            if (error is not null)
                throw new ValidationException("newPassword", error);

            var (hash, salt) = _passwordHasher.HashPassword(newPassword!);
            user.PasswordHash = hash;
            user.PasswordSalt = salt;
            user.LastUpdatedBy = changedBy;
            user.LastUpdatedAt = DateTime.UtcNow;
        }

        internal static string? PasswordError(string? password) =>
            string.IsNullOrEmpty(password) || password.Length < MinPasswordLength
                ? $"Password must be at least {MinPasswordLength} characters."
                : !password.Any(char.IsLetter) || !password.Any(char.IsDigit)
                    ? "Password must contain at least one letter and one digit."
                    : null;

        private static string Truncate(string? value, int max) =>
            string.IsNullOrEmpty(value) ? string.Empty : value.Length <= max ? value : value[..max];

        private static BusinessMembershipDto ToMembershipDto(UserBusiness m) =>
            new(m.BusinessId, m.Business.BusinessName, m.Business.TradingName, m.Business.Kind, m.Business.BusinessType, m.IsDefault,
                Sector: m.Business.Sector);

        private static UserDto ToUserDto(User user, bool membershipActive) =>
            new(user.UserId, user.Email, user.Username,user.PhoneNumber, user.IsActive && membershipActive, user.IsLocked,
                user.UserRoles.Where(ur => ur.Role is not null).Select(ur => ur.Role.RoleName), Array.Empty<string>());

        private static RoleDto ToRoleDto(Role role) =>
            new(role.RoleId, role.RoleName, role.Description, role.IsActive, role.RolePermissions.Select(rp => rp.Permission.Name));
    }
}
