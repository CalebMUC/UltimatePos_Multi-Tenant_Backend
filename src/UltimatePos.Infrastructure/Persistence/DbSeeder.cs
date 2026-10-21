using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using UltimatePos.Application.Common.Interfaces;
using UltimatePos.Application.Identity;
using UltimatePos.Domain.Entities;

namespace UltimatePos.Infrastructure.Persistence;

/// <summary>
/// Development seeding: migrates the local database and adds the dev accounts — a dev super user (who belongs to no business and
/// registers businesses) and the dev admin account a business can be handed to. Permissions and the Super User role are kept up to
/// date in every environment by PlatformBootstrapper; this calls the same code so a fresh local database is usable at once.
/// </summary>
public static class DbSeeder
{
    /// <summary>A plain account for development: it gets access only by being made the owner or admin of a business.</summary>
    public const string DevAdminEmail = "admin@ultimatepos.local";
    public const string DevAdminPassword = "Admin@12345";
    /// <summary>The development super user. Belongs to no business.</summary>
    public const string DevSuperUserEmail = "superuser@ultimatepos.local";
    public const string DevSuperUserPassword = "SuperUser@12345";

    public static async Task SeedAsync(IServiceProvider services)
    {
        var context = services.GetRequiredService<UltimatePosDbContext>();
        var hasher = services.GetRequiredService<IPasswordHasher>();

        await context.Database.MigrateAsync();
        await PlatformBootstrapper.SyncPermissionsAsync(context);

        // ---- Dev accounts ----
        if (!await context.Users.AnyAsync(u => u.Email == DevAdminEmail))
        {
            var (hash, salt) = hasher.HashPassword(DevAdminPassword);
            context.Users.Add(new User { Email = DevAdminEmail, Username = "admin", PasswordHash = hash, PasswordSalt = salt });
            await context.SaveChangesAsync();
        }

        await PlatformBootstrapper.EnsureSuperUserAsync(context, hasher, DevSuperUserEmail, "Super User", DevSuperUserPassword);
    }
}

/// <summary>
/// Runs at every startup, in every environment: brings the permission list up to date (PermissionCatalog is the source of truth, so
/// a release that adds a permission needs no data migration), then makes sure the platform has a super user. Production has no
/// seeder, so the first one comes from configuration — Platform:BootstrapSuperUser:Email / FullName / Password (set it as user-secrets or environment variables, sign
/// in, change the password, then remove it). Nothing happens once an active super user exists.
/// </summary>
public static class PlatformBootstrapper
{
    public static async Task RunAsync(IServiceProvider services, IConfiguration configuration, ILogger logger)
    {
        var context = services.GetRequiredService<UltimatePosDbContext>();
        await SyncPermissionsAsync(context);

        var hasActive = await context.UserRoles.IgnoreQueryFilters()
            .AnyAsync(ur => ur.Role.BusinessId == null && ur.Role.RoleName == PermissionCatalog.SuperUserRole && ur.User.IsActive);
        if (hasActive)
            return;

        var email = configuration["Platform:BootstrapSuperUser:Email"];
        var password = configuration["Platform:BootstrapSuperUser:Password"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            logger.LogWarning("There is no active super user, so nobody can register businesses or manage licences. " +
                              "Set Platform:BootstrapSuperUser:Email and :Password and restart to create one.");
            return;
        }

        var created = await EnsureSuperUserAsync(context, services.GetRequiredService<IPasswordHasher>(), email.Trim(),
            configuration["Platform:BootstrapSuperUser:FullName"] ?? "Super User", password);
        if (created)
            logger.LogWarning("Created super user {Email} from Platform:BootstrapSuperUser. Sign in, change the password, and remove the setting.", email);
        else
            logger.LogError("Platform:BootstrapSuperUser {Email} already exists as a business account; it was NOT made a super user. Use a separate email.", email);
    }

    /// <summary>
    /// Adds every permission in PermissionCatalog that the database does not have yet, gives the Super User role every platform
    /// permission, and gives each business's Admin role the new business permissions (Admin has every business permission).
    /// Nothing is removed, and running it again changes nothing.
    /// </summary>
    public static async Task SyncPermissionsAsync(UltimatePosDbContext context)
    {
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

        await EnsureSuperUserRoleAsync(context);

        var newBusinessPermissions = newPermissions.Where(p => !PermissionCatalog.IsPlatform(p.Name)).ToList();
        if (newBusinessPermissions.Count > 0)
        {
            var businessAdminRoleIds = await context.Roles.IgnoreQueryFilters()
                .Where(r => r.BusinessId != null && r.RoleName == PermissionCatalog.BusinessAdminRole)
                .Select(r => r.RoleId)
                .ToListAsync();
            foreach (var roleId in businessAdminRoleIds)
                context.RolePermissions.AddRange(newBusinessPermissions.Select(p => new RolePermission { RoleId = roleId, PermissionId = p.PermissionId }));
            await context.SaveChangesAsync();
        }
    }

    internal static async Task<Role> EnsureSuperUserRoleAsync(UltimatePosDbContext context)
    {
        var role = await context.Roles.IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.BusinessId == null && r.RoleName == PermissionCatalog.SuperUserRole);
        if (role is null)
        {
            role = new Role { RoleName = PermissionCatalog.SuperUserRole, Description = "Runs the platform: businesses, licences, support. Belongs to no business." };
            context.Roles.Add(role);
            await context.SaveChangesAsync();
        }

        var platformNames = PermissionCatalog.Platform.Select(p => p.Name).ToList();
        var missing = await context.Permissions
            .Where(p => platformNames.Contains(p.Name) && !context.RolePermissions.IgnoreQueryFilters().Any(rp => rp.RoleId == role.RoleId && rp.PermissionId == p.PermissionId))
            .ToListAsync();
        if (missing.Count > 0)
        {
            context.RolePermissions.AddRange(missing.Select(p => new RolePermission { RoleId = role.RoleId, PermissionId = p.PermissionId }));
            await context.SaveChangesAsync();
        }
        return role;
    }

    /// <returns>False when the email belongs to a business account (which must never become a super user).</returns>
    internal static async Task<bool> EnsureSuperUserAsync(UltimatePosDbContext context, IPasswordHasher hasher, string email, string fullName, string password)
    {
        var role = await EnsureSuperUserRoleAsync(context);
        var user = await context.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (user is null)
        {
            var (hash, salt) = hasher.HashPassword(password);
            user = new User { Email = email, Username = fullName, PasswordHash = hash, PasswordSalt = salt };
            context.Users.Add(user);
            await context.SaveChangesAsync();
        }
        else if (await context.UserBusinesses.AnyAsync(m => m.UserId == user.UserId))
            return false;

        if (!await context.UserRoles.IgnoreQueryFilters().AnyAsync(ur => ur.UserId == user.UserId && ur.RoleId == role.RoleId))
        {
            context.UserRoles.Add(new UserRole { UserId = user.UserId, RoleId = role.RoleId });
            await context.SaveChangesAsync();
        }
        return true;
    }
}
