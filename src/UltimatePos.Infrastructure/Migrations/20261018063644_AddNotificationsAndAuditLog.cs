using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UltimatePos.Infrastructure.Migrations
{
    /// <summary>
    /// System events (the outbox and audit log), in-app notifications, email/SMS deliveries and alert state, plus the AuditLog.View
    /// permission. Only adds tables: existing data is untouched.
    /// </summary>
    public partial class AddNotificationsAndAuditLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AlertStates",
                columns: table => new
                {
                    AlertStateId = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    AlertKey = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    RaisedByEventId = table.Column<Guid>(type: "uuid", nullable: true),
                    RaisedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ClearedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AlertStates", x => x.AlertStateId);
                    table.ForeignKey(
                        name: "FK_AlertStates_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "BusinessId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SystemEvents",
                columns: table => new
                {
                    SystemEventId = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: true),
                    EventType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Severity = table.Column<string>(type: "text", nullable: false),
                    Summary = table.Column<string>(type: "text", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    SubjectUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    EntityType = table.Column<string>(type: "text", nullable: true),
                    EntityId = table.Column<Guid>(type: "uuid", nullable: true),
                    Data = table.Column<string>(type: "jsonb", nullable: true),
                    AlertKey = table.Column<string>(type: "text", nullable: true),
                    ClearsAlertKeys = table.Column<string>(type: "text", nullable: true),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ProcessedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    LastError = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemEvents", x => x.SystemEventId);
                });

            migrationBuilder.CreateTable(
                name: "NotificationDeliveries",
                columns: table => new
                {
                    NotificationDeliveryId = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: true),
                    NotificationId = table.Column<Guid>(type: "uuid", nullable: true),
                    SystemEventId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Channel = table.Column<string>(type: "text", nullable: false),
                    Recipient = table.Column<string>(type: "text", nullable: false),
                    Subject = table.Column<string>(type: "text", nullable: true),
                    Body = table.Column<string>(type: "text", nullable: false),
                    HtmlBody = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "text", nullable: true),
                    ProviderMessageId = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationDeliveries", x => x.NotificationDeliveryId);
                    table.ForeignKey(
                        name: "FK_NotificationDeliveries_SystemEvents_SystemEventId",
                        column: x => x.SystemEventId,
                        principalTable: "SystemEvents",
                        principalColumn: "SystemEventId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Notifications",
                columns: table => new
                {
                    NotificationId = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SystemEventId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "text", nullable: false),
                    Severity = table.Column<string>(type: "text", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Message = table.Column<string>(type: "text", nullable: false),
                    Link = table.Column<string>(type: "text", nullable: true),
                    IsRead = table.Column<bool>(type: "boolean", nullable: false),
                    ReadAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.NotificationId);
                    table.ForeignKey(
                        name: "FK_Notifications_SystemEvents_SystemEventId",
                        column: x => x.SystemEventId,
                        principalTable: "SystemEvents",
                        principalColumn: "SystemEventId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Notifications_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AlertStates_BusinessId_AlertKey",
                table: "AlertStates",
                columns: new[] { "BusinessId", "AlertKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDeliveries_Status_NextAttemptAt",
                table: "NotificationDeliveries",
                columns: new[] { "Status", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDeliveries_SystemEventId",
                table: "NotificationDeliveries",
                column: "SystemEventId");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_SystemEventId",
                table: "Notifications",
                column: "SystemEventId");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_UserId_IsRead_CreatedAt",
                table: "Notifications",
                columns: new[] { "UserId", "IsRead", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SystemEvents_BusinessId_EventType_OccurredAt",
                table: "SystemEvents",
                columns: new[] { "BusinessId", "EventType", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SystemEvents_BusinessId_OccurredAt",
                table: "SystemEvents",
                columns: new[] { "BusinessId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SystemEvents_EntityType_EntityId",
                table: "SystemEvents",
                columns: new[] { "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_SystemEvents_Unprocessed",
                table: "SystemEvents",
                column: "OccurredAt",
                filter: "\"ProcessedAt\" IS NULL");
                    // The audit-log permission (DbSeeder adds it in Development; databases built from migrations get it here), granted to
            // every business's Admin role. Keep in step with PermissionCatalog.
            migrationBuilder.Sql(@"
                INSERT INTO ""Permissions"" (""PermissionId"", ""Name"", ""Module"", ""IsActive"", ""CreatedAt"")
                VALUES (gen_random_uuid(), 'AuditLog.View', 'Audit', TRUE, now())
                ON CONFLICT (""Name"") DO NOTHING;

                INSERT INTO ""RolePermissions"" (""RoleId"", ""PermissionId"", ""IsActive"", ""CreatedAt"")
                SELECT r.""RoleId"", p.""PermissionId"", TRUE, now()
                FROM ""Roles"" r CROSS JOIN ""Permissions"" p
                WHERE r.""BusinessId"" IS NOT NULL AND r.""RoleName"" = 'Admin' AND p.""Name"" = 'AuditLog.View'
                ON CONFLICT (""RoleId"", ""PermissionId"") DO NOTHING;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AlertStates");

            migrationBuilder.DropTable(
                name: "NotificationDeliveries");

            migrationBuilder.DropTable(
                name: "Notifications");

            migrationBuilder.DropTable(
                name: "SystemEvents");
        }
    }
}
