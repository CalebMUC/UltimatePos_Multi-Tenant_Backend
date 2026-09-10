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
        public AuthService(IAuthRepository authRepository,IPasswordHasher hasher,ITokenService tokenService)
        {
            _authRepository = authRepository;
            _passwordHasher = hasher;
            _tokenService = tokenService;
        }
       
        public async Task<LoginResponseDto> LoginAsync(LoginRequest request)
        {
            var user = await _authRepository.GetUserAsync(request.Email);
            if (user == null || !_passwordHasher.VerifyPassword(request.Password, user.PasswordHash))
            {
                throw new UnauthorizedAccessException("Invalid email or password.");
            }
            var roles = await _authRepository.GetUserRolesAsync(user.Id);
            var permissions = await _authRepository.GetUserPermissionsAsync(user.Id);
            var token = _tokenService.GenerateToken(user, permissions, roles);
            return new LoginResponseDto
            {
                Token = token,
                UserId = user.Id,
                Email = user.Email,
                Roles = roles.ToList(),
                Permissions = permissions.ToList()
            };
        }

    }
}
