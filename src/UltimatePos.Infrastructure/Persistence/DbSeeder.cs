using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using UltimatePos.Application.Common.Interfaces;
using UltimatePos.Application.Identity;
using UltimatePos.Domain.Entities;

namespace UltimatePos.Infrastructure.Persistence;

/// <summary>
/// Development seeding. Production environments get the same permissions and roles from migrations; this only adds what a fresh
/// local database needs to be usable: every permission, the Platform Admin role, and a platform admin who can register the first
/// business (which then gets its own Admin role through onboarding).
/// </summary>
public static class DbSeeder
{
    public const string DevAdminEmail = "admin@ultimatepos.local";
    public const string DevAdminPassword = "Admin@12345";

    public static async Task SeedAsync(IServiceProvider services)
    {
        var context = services.GetRequiredService<UltimatePosDbContext>();
        var hasher = services.GetRequiredService<IPasswordHasher>();

        await context.Database.MigrateAsync();

        // ---- Permissions ----
        var existingNames = await context.Permissions.Select(p => p.Name).ToListAsync();
        var newPermissions = PermissionCatalog.All
            .Where(p => !existingNames.Contains(p.Name))
            .Select(p => new Permission { Name = p.Name, Module = p.Module })
            .ToList();
        if (newPermissions.Count > 0)
        {
            context.Permissions.AddRange(newPermissions);
            await context.SaveChangesAsync();
        }

        // ---- Platform Admin (BusinessId null) ----
        var platformRole = await context.Roles.IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.BusinessId == null && r.RoleName == PermissionCatalog.PlatformAdminRole);
        var platformRoleCreated = platformRole is null;
        if (platformRole is null)
        {
            platformRole = new Role { RoleName = PermissionCatalog.PlatformAdminRole, Description = "Registers and manages businesses." };
            context.Roles.Add(platformRole);
            await context.SaveChangesAsync();
        }

        // A new role gets every permission of its kind; an existing one only the permissions created just now, so a permission
        // deliberately taken away stays taken away.
        var platformNames = PermissionCatalog.Platform.Select(p => p.Name).ToList();
        var platformGrants = platformRoleCreated
            ? await context.Permissions.Where(p => platformNames.Contains(p.Name)).ToListAsync()
            : newPermissions.Where(p => PermissionCatalog.IsPlatform(p.Name)).ToList();
        context.RolePermissions.AddRange(platformGrants.Select(p => new RolePermission { RoleId = platformRole.RoleId, PermissionId = p.PermissionId }));

        // ---- Each business's Admin role picks up new business permissions ----
        var newBusinessPermissions = newPermissions.Where(p => !PermissionCatalog.IsPlatform(p.Name)).ToList();
        if (newBusinessPermissions.Count > 0)
        {
            var businessAdminRoleIds = await context.Roles.IgnoreQueryFilters()
                .Where(r => r.BusinessId != null && r.RoleName == PermissionCatalog.BusinessAdminRole)
                .Select(r => r.RoleId)
                .ToListAsync();
            foreach (var roleId in businessAdminRoleIds)
                context.RolePermissions.AddRange(newBusinessPermissions.Select(p => new RolePermission { RoleId = roleId, PermissionId = p.PermissionId }));
        }
        await context.SaveChangesAsync();

        // ---- Dev platform admin ----
        var adminUser = await context.Users.FirstOrDefaultAsync(u => u.Email == DevAdminEmail);
        if (adminUser is null)
        {
            var (hash, salt) = hasher.HashPassword(DevAdminPassword);
            adminUser = new User
            {
                Email = DevAdminEmail,
                Username = "admin",
                PasswordHash = hash,
                PasswordSalt = salt
            };
            context.Users.Add(adminUser);
            await context.SaveChangesAsync();
        }

        if (!await context.UserRoles.IgnoreQueryFilters().AnyAsync(ur => ur.UserId == adminUser.UserId && ur.RoleId == platformRole.RoleId))
        {
            context.UserRoles.Add(new UserRole { UserId = adminUser.UserId, RoleId = platformRole.RoleId });
            await context.SaveChangesAsync();
        }
    }
}
