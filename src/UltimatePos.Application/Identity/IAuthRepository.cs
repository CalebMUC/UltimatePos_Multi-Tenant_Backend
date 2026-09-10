using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UltimatePos.Domain.Entities;

namespace UltimatePos.Application.Identity
{
    public interface IAuthRepository
    {
        Task<User?> GetUserAsync(string email);
        Task<User> CreateUserAsync(User user, string roleName);
        Task<IEnumerable<string>> GetUserRolesAsync(Guid userId);
        Task<IEnumerable<string>> GetUserPermissionsAsync(Guid userId);
    }
}
