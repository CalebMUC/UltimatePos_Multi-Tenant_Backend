using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UltimatePos.Domain.Entities;

namespace UltimatePos.Application.Common.Interfaces
{
    public interface ITokenService
    {
        /// <summary>A signed access token carrying the user, the business it is for (if any), their roles and permissions in it.</summary>
        (string Token, DateTime ExpiresAt) GenerateToken(User user, Guid? businessId, Guid? branchId, IEnumerable<Guid> branchIds,
            IEnumerable<string> permissions, IEnumerable<string> roles);

        /// <summary>A new opaque refresh token, and when it expires. Only its hash is stored.</summary>
        (string Token, DateTime ExpiresAt) GenerateRefreshToken();

        string HashRefreshToken(string refreshToken);
    }
}
