using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using UltimatePos.Application.Common.Interfaces;
using UltimatePos.Domain.Entities;
using UltimatePos.Infrastructure.Configuration;

namespace UltimatePos.Infrastructure.Auth
{
    public class JwtTokenService : ITokenService
    {
        /// <summary>Claim carrying the business the token is for. Read by CurrentUserService.</summary>
        public const string BusinessClaim = "business_id";

        private readonly JwtSettings _settings;
        public JwtTokenService(IOptions<JwtSettings> settings) => _settings = settings.Value;

        public (string Token, DateTime ExpiresAt) GenerateToken(User user, Guid? businessId, IEnumerable<string> permissions, IEnumerable<string> roles)
        {
            var claims = new List<Claim>
            {
                new(JwtRegisteredClaimNames.Sub, user.UserId.ToString()),
                new(JwtRegisteredClaimNames.Email, user.Email),
                new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new("UserName",user.Username),
            };
            if (businessId.HasValue)
                claims.Add(new Claim(BusinessClaim, businessId.Value.ToString()));
            claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
            claims.AddRange(permissions.Select(p => new Claim("Permission", p)));

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.Key));
            var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
            var expiresAt = DateTime.UtcNow.AddMinutes(_settings.ExpiryMinutes);

            var token = new JwtSecurityToken(
                issuer: _settings.Issuer,
                audience: _settings.Audience,
                claims: claims,
                expires: expiresAt,
                signingCredentials: credentials
            );
            return (new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
        }

        public (string Token, DateTime ExpiresAt) GenerateRefreshToken() =>
            (Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(48)), DateTime.UtcNow.AddDays(_settings.RefreshTokenDays));

        public string HashRefreshToken(string refreshToken) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken)));
    }
}
