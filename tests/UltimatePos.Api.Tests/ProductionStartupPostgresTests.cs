using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using UltimatePos.Application;
using UltimatePos.Application.Business.Dtos;
using UltimatePos.Application.Common.Interfaces;
using UltimatePos.Application.Identity;
using UltimatePos.Application.Platform;
using UltimatePos.Domain.Entities;
using UltimatePos.Infrastructure;
using UltimatePos.Infrastructure.Persistence;

namespace Ultimate.Api.Tests;

/// <summary>
/// A production start: a new database with every migration and no development seeder, then the startup bootstrap. Everyone must
/// still get their permissions, and a release that adds a permission must reach existing businesses' Admin roles.
/// </summary>
public class ProductionStartupPostgresTests
{
    private const string SuperUserEmail = "owner@platform.test";

    [PostgresFact]
    public async Task A_new_production_database_gets_every_permission_and_a_working_super_user()
    {
        var server = Environment.GetEnvironmentVariable(PostgresFactAttribute.ConnectionStringVariable)!;
        var connectionString = new NpgsqlConnectionStringBuilder(server) { Database = $"ultimatepos_production_{Guid.NewGuid():N}" }.ConnectionString;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = connectionString,
            ["Mpesa:BaseUrl"] = "https://localhost",
            ["JwtSettings:Key"] = "test-signing-key-that-is-long-enough-for-hs256-0123456789",
            ["Platform:BootstrapSuperUser:Email"] = SuperUserEmail,
            ["Platform:BootstrapSuperUser:FullName"] = "Platform Owner",
            ["Platform:BootstrapSuperUser:Password"] = "Temporary@12345",
        }).Build();

        var caller = new TestCaller();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplication();
        services.AddInfrastructure(configuration);
        services.RemoveAll<ICurrentUser>();
        services.AddSingleton<ICurrentUser>(caller);
        await using var provider = services.BuildServiceProvider();

        async Task<T> Run<T>(Func<IServiceProvider, Task<T>> call)
        {
            using var scope = provider.CreateScope();
            return await call(scope.ServiceProvider);
        }
        Task Startup() => Run(async sp => { await PlatformBootstrapper.RunAsync(sp, configuration, NullLogger.Instance); return 0; });
        Task<int> PermissionCount(Guid roleId) => Run(sp => sp.GetRequiredService<UltimatePosDbContext>().RolePermissions.IgnoreQueryFilters()
            .CountAsync(rp => rp.RoleId == roleId));

        try
        {
            // What the `migrate` step does: migrations only.
            await Run(async sp => { await sp.GetRequiredService<UltimatePosDbContext>().Database.MigrateAsync(); return 0; });
            await Startup();

            var (superUserId, superRoleId) = await Run(async sp =>
            {
                var db = sp.GetRequiredService<UltimatePosDbContext>();
                var role = await db.Roles.IgnoreQueryFilters().SingleAsync(r => r.BusinessId == null && r.RoleName == PermissionCatalog.SuperUserRole);
                return ((await db.Users.SingleAsync(u => u.Email == SuperUserEmail)).UserId, role.RoleId);
            });
            Assert.Equal(PermissionCatalog.All.Count(), await Run(sp => sp.GetRequiredService<UltimatePosDbContext>().Permissions.CountAsync()));
            Assert.Equal(PermissionCatalog.Platform.Count(), await PermissionCount(superRoleId));

            // The super user can onboard a business, whose Admin gets every business permission.
            caller.UserId = superUserId;
            caller.Permissions = PermissionCatalog.Platform.Select(p => p.Name).ToHashSet();
            var business = await Run(sp => sp.GetRequiredService<PlatformService>().RegisterBusinessAsync(new RegisterBusinessRequestDto(
                "First Customer Ltd", null, BusinessType.Retailer, BusinessKind.Trader, null, "P051999999Z", null, null, null, null, null, null,
                Owner: new BusinessOwnerRequestDto("Owner", "owner@first-customer.test", null))));
            var adminRoleId = await Run(sp => sp.GetRequiredService<UltimatePosDbContext>().Roles.IgnoreQueryFilters()
                .Where(r => r.BusinessId == business.Business.BusinessId && r.RoleName == PermissionCatalog.BusinessAdminRole)
                .Select(r => r.RoleId).SingleAsync());
            var businessPermissions = PermissionCatalog.All.Count(p => !PermissionCatalog.IsPlatform(p.Name));
            Assert.Equal(businessPermissions, await PermissionCount(adminRoleId));

            // A later release adds a permission: the next start adds it and gives it to the existing Admin role.
            await Run(async sp =>
            {
                var db = sp.GetRequiredService<UltimatePosDbContext>();
                var newest = await db.Permissions.SingleAsync(p => p.Name == "Sales.Create");
                db.RolePermissions.RemoveRange(await db.RolePermissions.IgnoreQueryFilters().Where(rp => rp.PermissionId == newest.PermissionId).ToListAsync());
                db.Permissions.Remove(newest);
                return await db.SaveChangesAsync();
            });
            Assert.Equal(businessPermissions - 1, await PermissionCount(adminRoleId));
            await Startup();
            Assert.Equal(businessPermissions, await PermissionCount(adminRoleId));

            // Starting again changes nothing.
            await Startup();
            Assert.Equal(PermissionCatalog.All.Count(), await Run(sp => sp.GetRequiredService<UltimatePosDbContext>().Permissions.CountAsync()));
            Assert.Equal(businessPermissions, await PermissionCount(adminRoleId));
            Assert.Equal(PermissionCatalog.Platform.Count(), await PermissionCount(superRoleId));
        }
        finally
        {
            await Run(async sp => await sp.GetRequiredService<UltimatePosDbContext>().Database.EnsureDeletedAsync());
        }
    }
}
