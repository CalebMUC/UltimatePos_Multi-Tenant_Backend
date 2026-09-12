using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UltimatePos.Application.Identity;
using UltimatePos.Domain.Entities;

namespace UltimatePos.Infrastructure.Persistence.Repositories
{
    public class AuthenticationRepository : IAuthRepository
    {
        private readonly UltimatePosDbContext _context;
        public AuthenticationRepository(UltimatePosDbContext context) => _context = context;

        public async Task<User?> GetUserAsync(string email)
        {
            return await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Email == email);
        }
        public async Task<User> CreateUserAsync(User user, string roleName)
        {
            var role = await _context.Roles.FirstOrDefaultAsync(r => r.RoleName == roleName)
                ?? throw new InvalidOperationException($"Role '{roleName}' does not exist.");

            _context.Users.Add(user);
            await _context.SaveChangesAsync(); // need user.Id before creating the join row

            _context.UserRoles.Add(new UserRole { UserId = user.UserId, RoleId = role.RoleId });
            await _context.SaveChangesAsync();

            return user;
        }

        public async Task<IEnumerable<string>> GetUserRolesAsync(Guid userId)
        {
            return await _context.UserRoles
                .Where(ur => ur.UserId == userId)
                .Select(ur => ur.Role.RoleName)
                .ToListAsync();
        }

        public async Task<IEnumerable<string>> GetUserPermissionsAsync(Guid userId)
        {
            return await _context.UserRoles
                .Where(ur => ur.UserId == userId)
                .SelectMany(ur => ur.Role.RolePermissions)
                .Select(rp => rp.Permission.Name)
                .Distinct()
                .ToListAsync();
        }
    }
}
