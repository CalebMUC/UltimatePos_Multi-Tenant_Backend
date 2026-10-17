using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UltimatePos.Infrastructure.Migrations
{
    /// <summary>
    /// The dashboard and report permissions, granted to the Admin role. DbSeeder only runs in Development, so without this an
    /// environment built from migrations would have endpoints nobody could ever be given access to. Safe to run against a
    /// database the seeder has already filled: existing permissions and grants are left alone.
    /// Also removes Reports.View, the single permission an earlier draft of the reports used; nothing checks it any more.
    /// Users must sign in again to pick up the new permissions, since permissions travel in the access token.
    /// </summary>
    public partial class AddReportPermissions : Migration
    {
        // Keep in step with DbSeeder.RequiredPermissions.
        private const string NewPermissions = @"
            ('Dashboard.View', 'Dashboard'),
            ('SalesReports.View', 'Reports'),
            ('CustomerReports.View', 'Reports'),
            ('ProductionReports.View', 'Reports'),
            ('InventoryReports.View', 'Reports'),
            ('PurchaseReports.View', 'Reports')";

        private const string NewPermissionNames =
            "'Dashboard.View', 'SalesReports.View', 'CustomerReports.View', 'ProductionReports.View', 'InventoryReports.View', 'PurchaseReports.View'";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($@"
                INSERT INTO ""Permissions"" (""PermissionId"", ""Name"", ""Module"", ""IsActive"", ""CreatedAt"")
                SELECT gen_random_uuid(), v.name, v.module, TRUE, now()
                FROM (VALUES {NewPermissions}) AS v(name, module)
                ON CONFLICT (""Name"") DO NOTHING;");

            migrationBuilder.Sql($@"
                INSERT INTO ""RolePermissions"" (""RoleId"", ""PermissionId"", ""IsActive"", ""CreatedAt"")
                SELECT r.""RoleId"", p.""PermissionId"", TRUE, now()
                FROM ""Roles"" r
                CROSS JOIN ""Permissions"" p
                WHERE r.""RoleName"" = 'Admin' AND p.""Name"" IN ({NewPermissionNames})
                ON CONFLICT (""RoleId"", ""PermissionId"") DO NOTHING;");

            migrationBuilder.Sql(@"
                DELETE FROM ""RolePermissions""
                WHERE ""PermissionId"" IN (SELECT ""PermissionId"" FROM ""Permissions"" WHERE ""Name"" = 'Reports.View');
                DELETE FROM ""Permissions"" WHERE ""Name"" = 'Reports.View';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($@"
                DELETE FROM ""RolePermissions""
                WHERE ""PermissionId"" IN (SELECT ""PermissionId"" FROM ""Permissions"" WHERE ""Name"" IN ({NewPermissionNames}));
                DELETE FROM ""Permissions"" WHERE ""Name"" IN ({NewPermissionNames});");
        }
    }
}
