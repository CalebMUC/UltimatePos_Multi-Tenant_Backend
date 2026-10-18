using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UltimatePos.Infrastructure.Migrations
{
    /// <summary>
    /// The Super User platform role (renamed from Platform Admin, with the new platform permissions) and its separation from
    /// businesses: platform roles are removed from anyone who belongs to a business. Business licences, suspension, and the
    /// must-change-password flag. Existing businesses get a 30-day transitional licence.
    /// </summary>
    public partial class SuperUserAndLicensing : Migration
    {
        private const string PlatformPermissions =
            "('Businesses.Register'), ('Businesses.ViewAll'), ('Businesses.Deactivate'), ('Businesses.Suspend'), ('Licenses.View'), " +
            "('Licenses.Manage'), ('Platform.Dashboard.View'), ('Platform.Support'), ('Platform.Users.Manage')";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "MustChangePassword",
                table: "Users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsSuspended",
                table: "Businesses",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateOnly>(
                name: "LicenseEndsOn",
                table: "Businesses",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LicenseMaxUsers",
                table: "Businesses",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SuspendedAt",
                table: "Businesses",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SuspendedBy",
                table: "Businesses",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SuspensionReason",
                table: "Businesses",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BusinessLicenses",
                columns: table => new
                {
                    BusinessLicenseId = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    LicenseNumber = table.Column<string>(type: "text", nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    PlanName = table.Column<string>(type: "text", nullable: false),
                    MaxUsers = table.Column<int>(type: "integer", nullable: true),
                    StartsOn = table.Column<DateOnly>(type: "date", nullable: false),
                    EndsOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric", nullable: false),
                    Currency = table.Column<string>(type: "text", nullable: false),
                    PaymentMethod = table.Column<string>(type: "text", nullable: true),
                    PaymentReference = table.Column<string>(type: "text", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    State = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancelledBy = table.Column<Guid>(type: "uuid", nullable: true),
                    CancelReason = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessLicenses", x => x.BusinessLicenseId);
                    table.ForeignKey(
                        name: "FK_BusinessLicenses_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "BusinessId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BusinessLicenses_BusinessId_EndsOn",
                table: "BusinessLicenses",
                columns: new[] { "BusinessId", "EndsOn" });

            migrationBuilder.CreateIndex(
                name: "IX_BusinessLicenses_BusinessId_LicenseNumber",
                table: "BusinessLicenses",
                columns: new[] { "BusinessId", "LicenseNumber" },
                unique: true);
                    // ---- Super User: the platform role, belonging to no business --------------------------------------------------------
            migrationBuilder.Sql(@"
                UPDATE ""Roles"" SET ""RoleName"" = 'Super User', ""Description"" = 'Runs the platform: businesses, licences, support. Belongs to no business.'
                WHERE ""BusinessId"" IS NULL AND ""RoleName"" = 'Platform Admin'
                  AND NOT EXISTS (SELECT 1 FROM ""Roles"" WHERE ""BusinessId"" IS NULL AND ""RoleName"" = 'Super User');

                INSERT INTO ""Roles"" (""RoleId"", ""BusinessId"", ""RoleName"", ""Description"", ""IsActive"", ""CreatedAt"")
                SELECT gen_random_uuid(), NULL, 'Super User', 'Runs the platform: businesses, licences, support. Belongs to no business.', TRUE, now()
                WHERE NOT EXISTS (SELECT 1 FROM ""Roles"" WHERE ""BusinessId"" IS NULL AND ""RoleName"" = 'Super User');");

            // Platform permissions (keep in step with PermissionCatalog.Platform), all granted to Super User.
            migrationBuilder.Sql($@"
                INSERT INTO ""Permissions"" (""PermissionId"", ""Name"", ""Module"", ""IsActive"", ""CreatedAt"")
                SELECT gen_random_uuid(), v.name, 'Platform', TRUE, now()
                FROM (VALUES {PlatformPermissions}) AS v(name)
                ON CONFLICT (""Name"") DO NOTHING;

                UPDATE ""Permissions"" SET ""Module"" = 'Platform' WHERE ""Name"" IN (SELECT v.name FROM (VALUES {PlatformPermissions}) AS v(name));

                INSERT INTO ""RolePermissions"" (""RoleId"", ""PermissionId"", ""IsActive"", ""CreatedAt"")
                SELECT r.""RoleId"", p.""PermissionId"", TRUE, now()
                FROM ""Roles"" r CROSS JOIN ""Permissions"" p
                WHERE r.""BusinessId"" IS NULL AND r.""RoleName"" = 'Super User' AND p.""Module"" = 'Platform'
                ON CONFLICT (""RoleId"", ""PermissionId"") DO NOTHING;");

            // A super user belongs to no business. Anyone who works in a business loses the platform role (they keep their business
            // access); the platform gets a separate super user account, from DbSeeder (development) or Platform:BootstrapSuperUser.
            migrationBuilder.Sql(@"
                DELETE FROM ""UserRoles"" ur
                USING ""Roles"" r
                WHERE ur.""RoleId"" = r.""RoleId"" AND r.""BusinessId"" IS NULL
                  AND EXISTS (SELECT 1 FROM ""UserBusinesses"" m WHERE m.""UserId"" = ur.""UserId"");");

            // ---- Licences: existing businesses start on a 30-day transitional licence, so enforcement doesn't lock them out on deploy.
            migrationBuilder.Sql(@"
                INSERT INTO ""BusinessLicenses"" (""BusinessLicenseId"", ""BusinessId"", ""LicenseNumber"", ""Kind"", ""PlanName"", ""StartsOn"", ""EndsOn"",
                                                  ""Amount"", ""Currency"", ""Notes"", ""State"", ""CreatedAt"")
                SELECT gen_random_uuid(), b.""BusinessId"", 'LIC-000001', 'Complimentary', 'Transitional', CURRENT_DATE, CURRENT_DATE + 29,
                       0, 'KES', 'Issued automatically when licensing was introduced. Replace with the business''s real licence.', 'Active', now()
                FROM ""Businesses"" b
                WHERE NOT EXISTS (SELECT 1 FROM ""BusinessLicenses"" l WHERE l.""BusinessId"" = b.""BusinessId"");

                INSERT INTO ""SkuSequences"" (""BusinessId"", ""Prefix"", ""LastNumber"")
                SELECT l.""BusinessId"", 'LIC', 1 FROM ""BusinessLicenses"" l WHERE l.""LicenseNumber"" = 'LIC-000001'
                ON CONFLICT (""BusinessId"", ""Prefix"") DO NOTHING;

                UPDATE ""Businesses"" b
                SET ""LicenseEndsOn"" = (SELECT max(l.""EndsOn"") FROM ""BusinessLicenses"" l WHERE l.""BusinessId"" = b.""BusinessId"" AND l.""State"" = 'Active')
                WHERE b.""LicenseEndsOn"" IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BusinessLicenses");

            migrationBuilder.DropColumn(
                name: "MustChangePassword",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "IsSuspended",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "LicenseEndsOn",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "LicenseMaxUsers",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "SuspendedAt",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "SuspendedBy",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "SuspensionReason",
                table: "Businesses");
        }
    }
}
