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

            if (!user.IsActive || !_passwordHasher.VerifyPassword(request.Password, user.PasswordHash, user.PasswordSalt))
                throw new InvalidCredentialsException();

            var roles = (await _authRepository.GetUserRolesAsync(user.UserId)).ToList();
            var permissions = (await _authRepository.GetUserPermissionsAsync(user.UserId)).ToList();

            var token = _tokenService.GenerateToken(user, permissions, roles);
            var userDto = new UserDto(user.UserId, user.Email, user.Username,user.PhoneNumber, roles, permissions);

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
            return new UserDto(createdUser.UserId, createdUser.Email, createdUser.Username, createdUser.PhoneNumber, roles, permissions);
        }

        public async Task<IEnumerable<RoleDto>> GetRolesAsync()
        {
            var roles = await _authRepository.GetRolesAsync();
            return roles.Select(r => new RoleDto(r.RoleId, r.RoleName, r.Description,r.IsActive));
        }

        public async Task<RoleDto> CreateRoleAsync(CreateRoleRequestDto request)
        {
            var role = new Role
            {
                RoleName = request.RoleName,
                Description = request.Description,
                CreatedBy = _currentUser.UserId,
                IsActive = true
            };
            var created = await _authRepository.CreateRoleAsync(role);
            return new RoleDto(created.RoleId, created.RoleName, created.Description);
        }

    }
}
