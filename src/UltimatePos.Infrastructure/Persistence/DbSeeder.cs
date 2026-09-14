using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using UltimatePos.Application.Common.Interfaces;
using UltimatePos.Domain.Entities;

namespace UltimatePos.Infrastructure.Persistence;

public static class DbSeeder
{
    public static async Task SeedAsync(IServiceProvider services)
    {
        var context = services.GetRequiredService<UltimatePosDbContext>();
        var hasher = services.GetRequiredService<IPasswordHasher>();
        //var logger = services.GetRequiredService<ILogger<Program>>();
        //var logger = services.GetRequiredService<ILogger<DbSeeder>>();

        await context.Database.MigrateAsync();

        if (await context.Roles.AnyAsync())
        {
            //logger.LogInformation("DbSeeder: roles already exist, skipping seed.");
            return;
        }

        //logger.LogInformation("DbSeeder: no roles found, seeding default Admin role and user.");

        var adminRole = new Role { RoleName = "Admin" };
        context.Roles.Add(adminRole);

        var permissions = new[]
        {
            new Permission { Name = "Users.Create", Module = "Users" },
            new Permission { Name = "Roles.View", Module = "Roles" },
            new Permission { Name = "Roles.Create", Module = "Roles" },
            new Permission { Name = "Products.Create", Module = "Products" },
            new Permission { Name = "Sales.Create", Module = "Sales" }
        };
        context.Permissions.AddRange(permissions);
        await context.SaveChangesAsync();

        foreach (var permission in permissions)
            context.RolePermissions.Add(new RolePermission { RoleId = adminRole.RoleId, PermissionId = permission.PermissionId });

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
    }
}