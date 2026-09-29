using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using UltimatePos.Application.Common.Interfaces;
using UltimatePos.Domain.Entities;

namespace UltimatePos.Infrastructure.Persistence;

public static class DbSeeder
{
    private static readonly (string Name, string Module)[] RequiredPermissions =
    {
        ("Users.Create", "Users"), ("Users.View", "Users"), ("Users.AssignRoles", "Users"),
        ("Users.Deactivate", "Users"), ("Users.Lock", "Users"),
        ("Roles.View", "Roles"), ("Roles.Create", "Roles"), ("Roles.Update", "Roles"),
        ("Roles.Deactivate", "Roles"), ("Roles.AssignPermissions", "Roles"),
        ("Permissions.View", "Permissions"),
        ("Sales.Create", "Sales"),("Businesses.Register", "Businesses"), ("Businesses.View", "Businesses"),
        ("Businesses.Update", "Businesses"), ("Businesses.Deactivate", "Businesses"),
        ("Customers.Register", "Customers"), ("Customers.View", "Customers"),
        ("Customers.Update", "Customers"), ("Customers.Deactivate", "Customers"),
        ("Categories.View", "Categories"), ("Categories.Create", "Categories"), ("Categories.BulkImport", "Categories"),
        ("Units.View", "Units"), ("Units.Create", "Units"),
        ("Products.View", "Products"), ("Products.Create", "Products"), ("Products.Update", "Products"),
        ("Products.ManagePricing", "Products"), ("Products.BulkImport", "Products"),
    };

    public static async Task SeedAsync(IServiceProvider services)
    {
        var context = services.GetRequiredService<UltimatePosDbContext>();
        var hasher = services.GetRequiredService<IPasswordHasher>();
        //var logger = services.GetRequiredService<ILogger>();

        await context.Database.MigrateAsync();

        var adminRole = await context.Roles.FirstOrDefaultAsync(r => r.RoleName == "Admin");
        if (adminRole is null)
        {
            adminRole = new Role { RoleName = "Admin" };
            context.Roles.Add(adminRole);
            await context.SaveChangesAsync();
            //logger.LogInformation("DbSeeder: created Admin role.");
        }

        var existingNames = await context.Permissions.Select(p => p.Name).ToListAsync();
        var missing = RequiredPermissions.Where(p => !existingNames.Contains(p.Name)).ToList();

        if (missing.Count > 0)
        {
            var newPermissions = missing.Select(p => new Permission { Name = p.Name, Module = p.Module }).ToList();
            context.Permissions.AddRange(newPermissions);
            await context.SaveChangesAsync();

            foreach (var permission in newPermissions)
                context.RolePermissions.Add(new RolePermission { RoleId = adminRole.RoleId, PermissionId = permission.PermissionId });

            await context.SaveChangesAsync();
            //logger.LogInformation("DbSeeder: added {Count} missing permission(s) to Admin role: {Names}",
            //    newPermissions.Count, string.Join(", ", newPermissions.Select(p => p.Name)));
        }

        if (!await context.Users.AnyAsync(u => u.Email == "admin@ultimatepos.local"))
        {
            var (hash, salt) = hasher.HashPassword("Admin@12345");
            var adminUser = new User
            {
                Email = "admin@ultimatepos.local",
                Username = "admin",
                PasswordHash = hash,
                PasswordSalt = salt
            };
            context.Users.Add(adminUser);
            await context.SaveChangesAsync();

            context.UserRoles.Add(new UserRole { UserId = adminUser.UserId, RoleId = adminRole.RoleId });
            await context.SaveChangesAsync();
            //logger.LogInformation("DbSeeder: created default admin user.");
        }
    }
}