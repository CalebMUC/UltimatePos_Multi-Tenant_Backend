using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UltimatePos.Application.Common.Interfaces;
using UltimatePos.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace UltimatePos.Infrastructure.Persistence
{
    public class DbSeeder
    {
        public static async Task SeedAsync(IServiceProvider services)
        {
            var context = services.GetRequiredService<UltimatePosDbContext>();
            var hasher = services.GetRequiredService<IPasswordHasher>();

            await context.Database.MigrateAsync();

            if (await context.Roles.AnyAsync()) return; // already seeded

            var adminRole = new Role { RoleName = "Admin" };
            context.Roles.Add(adminRole);

            var permissions = new[]
            {
            new Permission { Name = "Users.Create", Module = "Users" },
            new Permission { Name = "Products.Create", Module = "Products" },
            new Permission { Name = "Sales.Create", Module = "Sales" }
        };
            context.Permissions.AddRange(permissions);
            await context.SaveChangesAsync();

            foreach (var permission in permissions)
                context.RolePermissions.Add(new RolePermission { RoleId = adminRole.RoleId, PermissionId = permission.PermissionId });

            var (hash, salt) = hasher.HashPassword("Admin@12345");
            var adminUser = new User { Email = "admin@ultimatepos.local", Username = "Default Admin", PasswordHash = hash, PasswordSalt = salt };
            context.Users.Add(adminUser);
            await context.SaveChangesAsync();

            context.UserRoles.Add(new UserRole { UserId = adminUser.UserId, RoleId = adminRole.RoleId });
            await context.SaveChangesAsync();
        }
    }
}
