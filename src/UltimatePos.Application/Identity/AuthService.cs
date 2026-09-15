using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UltimatePos.Application.Common.Interfaces;
using  UltimatePos.Application.Identity.Dtos;
using UltimatePos.Domain.Entities;
using UltimatePos.Domain.Exceptions;


namespace UltimatePos.Application.Identity
{
    public class AuthService
    {
        private const int MaxFailedLoginAttempts = 5;

        private readonly IAuthRepository _authRepository;
        private readonly IPasswordHasher _passwordHasher;
        private readonly ITokenService _tokenService;
        private readonly ICurrentUser _currentUser;
        public AuthService(IAuthRepository authRepository,
            IPasswordHasher hasher,
            ITokenService tokenService,
            ICurrentUser currentUser)
        {
            _authRepository = authRepository;
            _passwordHasher = hasher;
            _tokenService = tokenService;
            _currentUser = currentUser;
        }

        public async Task<LoginResponseDto> LoginAsync(LoginRequest request)
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

                await _authRepository.UpdateUserAsync(user);
                throw new InvalidCredentialsException();
            }

            if (user.FailedLoginAttempts > 0)
            {
                user.FailedLoginAttempts = 0;
                await _authRepository.UpdateUserAsync(user);
            }

            var roles = (await _authRepository.GetUserRolesAsync(user.UserId)).ToList();
            var permissions = (await _authRepository.GetUserPermissionsAsync(user.UserId)).ToList();

            var token = _tokenService.GenerateToken(user, permissions, roles);
            var userDto = new UserDto(user.UserId, user.Email, user.Username,user.PhoneNumber, user.IsActive, user.IsLocked, roles, permissions);

            return new LoginResponseDto(token, DateTime.UtcNow.AddMinutes(30), userDto);
        }

        public async Task<UserDto> RegisterAsync(RegisterRequest request)
        {
            var existingUser = await _authRepository.GetUserAsync(request.Email);
            if (existingUser != null)
                throw new UserAlreadyExistsException();
            var (passwordHash, passwordSalt) = _passwordHasher.HashPassword(request.Password);
            var newUser = new User
            {
                UserId = Guid.NewGuid(),
                Email = request.Email,
                Username = request.FullName,
                PhoneNumber = request.PhoneNumber,
                PasswordHash = passwordHash,
                PasswordSalt = passwordSalt,
                IsActive = true
            };
            var createdUser = await _authRepository.CreateUserAsync(newUser, request.RoleName);
            var roles = (await _authRepository.GetUserRolesAsync(createdUser.UserId)).ToList();
            var permissions = (await _authRepository.GetUserPermissionsAsync(createdUser.UserId)).ToList();
            return new UserDto(createdUser.UserId, createdUser.Email, createdUser.Username, createdUser.PhoneNumber,createdUser.IsActive,createdUser.IsLocked, roles, permissions);
        }

        public async Task<IEnumerable<UserDto>> GetUsersAsync()
        {
            var users = await _authRepository.GetUsersAsync();
            return users.Select(ToUserDto);
        }

        public async Task<UserDto> GetUserByIdAsync(Guid userId)
        {
            var user = await _authRepository.GetUserByIdAsync(userId)
                ?? throw new NotFoundException($"User '{userId}' not found.");
            return ToUserDto(user);
        }

        public async Task<UserDto> AssignRolesToUserAsync(Guid userId, AssignRolesRequestDto request)
        {
            await _authRepository.AssignRolesToUserAsync(userId, request.RoleIds);
            return await GetUserByIdAsync(userId);
        }

        public async Task<IEnumerable<RoleDto>> GetRolesAsync()
        {
            var roles = await _authRepository.GetRolesAsync();
            return roles.Select(ToRoleDto);
        }
        public async Task<UserDto> SetUserActiveStatusAsync(Guid userId, bool isActive)
        {
            var user = await _authRepository.SetUserActiveStatusAsync(userId, isActive, _currentUser.UserId);
            return await GetUserByIdAsync(user.UserId);
        }

        public async Task<UserDto> SetUserLockStatusAsync(Guid userId, bool isLocked)
        {
            var user = await _authRepository.SetUserLockStatusAsync(userId, isLocked, _currentUser.UserId);
            return await GetUserByIdAsync(user.UserId);
        }
        public async Task<RoleDto> CreateRoleAsync(CreateRoleRequestDto request)
        {
            var role = new Role { RoleName = request.RoleName, Description = request.Description, CreatedBy = _currentUser.UserId };
            var created = await _authRepository.CreateRoleAsync(role);
            return ToRoleDto(created);
        }
        public async Task<RoleDto> GetRoleByIdAsync(Guid roleId)
        {
            var role = await _authRepository.GetRoleByIdAsync(roleId)
                ?? throw new NotFoundException($"Role '{roleId}' not found.");
            return ToRoleDto(role);
        }

        public async Task<RoleDto> UpdateRoleAsync(Guid roleId, UpdateRoleRequestDto request)
        {
            var updated = await _authRepository.UpdateRoleAsync(roleId, request.RoleName, request.Description, _currentUser.UserId);
            return await GetRoleByIdAsync(updated.RoleId);
        }

        public async Task<RoleDto> SetRoleActiveStatusAsync(Guid roleId, bool isActive)
        {
            var updated = await _authRepository.SetRoleActiveStatusAsync(roleId, isActive, _currentUser.UserId);
            return await GetRoleByIdAsync(updated.RoleId);
        }

        public async Task<RoleDto> AssignPermissionsToRoleAsync(Guid roleId, AssignPermissionsRequestDto request)
        {
            await _authRepository.AssignPermissionsToRoleAsync(roleId, request.PermissionIds);
            return await GetRoleByIdAsync(roleId);
        }

        public Task<IEnumerable<PermissionModuleDto>> GetPermissionModulesAsync() => _authRepository.GetPermissionModulesAsync();

        private static UserDto ToUserDto(User user) =>
            new(user.UserId, user.Email, user.Username,user.PhoneNumber, user.IsActive, user.IsLocked,
                user.UserRoles.Select(ur => ur.Role.RoleName), Array.Empty<string>());

        private static RoleDto ToRoleDto(Role role) =>
            new(role.RoleId, role.RoleName, role.Description, role.IsActive, role.RolePermissions.Select(rp => rp.Permission.Name));
    }
}
