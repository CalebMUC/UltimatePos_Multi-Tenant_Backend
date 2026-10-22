using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UltimatePos.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DeliveryModeTripsReturnsCashSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "BranchId",
                table: "SupplierLedgerEntries",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeliveryMode",
                table: "Sales",
                type: "text",
                nullable: false,
                defaultValue: "Counter");

            // Sales that delivery orders became before the column existed were delivered by a rider.
            migrationBuilder.Sql("""
                UPDATE "Sales" SET "DeliveryMode" = 'Rider'
                WHERE "SaleId" IN (SELECT "SaleId" FROM "DeliveryOrders" WHERE "SaleId" IS NOT NULL)
                """);

            migrationBuilder.AddColumn<string>(
                name: "RiderName",
                table: "Expenses",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DeliveryTripId",
                table: "DeliveryOrders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExpectedPaymentMethod",
                table: "DeliveryOrders",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReturnReason",
                table: "DeliveryOrders",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReturnedAt",
                table: "DeliveryOrders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReturnedBy",
                table: "DeliveryOrders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "BundleQuantity",
                table: "DeliveryOrderLines",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProductBundleId",
                table: "DeliveryOrderLines",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "BranchId",
                table: "CustomerLedgerEntries",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CashSessions",
                columns: table => new
                {
                    CashSessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionNumber = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    OpeningFloat = table.Column<decimal>(type: "numeric", nullable: false),
                    OpenedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    OpenedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    OpeningNotes = table.Column<string>(type: "text", nullable: true),
                    ExpectedCash = table.Column<decimal>(type: "numeric", nullable: true),
                    CountedCash = table.Column<decimal>(type: "numeric", nullable: true),
                    Variance = table.Column<decimal>(type: "numeric", nullable: true),
                    ClosedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ClosedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ClosingNotes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashSessions", x => x.CashSessionId);
                    table.ForeignKey(
                        name: "FK_CashSessions_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "BranchId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CashSessions_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "BusinessId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DeliveryTrips",
                columns: table => new
                {
                    DeliveryTripId = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    TripNumber = table.Column<string>(type: "text", nullable: false),
                    RiderName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DispatchedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DispatchedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeliveryTrips", x => x.DeliveryTripId);
                    table.ForeignKey(
                        name: "FK_DeliveryTrips_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "BranchId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeliveryTrips_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "BusinessId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CashSessionMovements",
                columns: table => new
                {
                    CashSessionMovementId = table.Column<Guid>(type: "uuid", nullable: false),
                    CashSessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    MovementType = table.Column<string>(type: "text", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashSessionMovements", x => x.CashSessionMovementId);
                    table.ForeignKey(
                        name: "FK_CashSessionMovements_CashSessions_CashSessionId",
                        column: x => x.CashSessionId,
                        principalTable: "CashSessions",
                        principalColumn: "CashSessionId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryOrders_DeliveryTripId",
                table: "DeliveryOrders",
                column: "DeliveryTripId");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryOrderLines_ProductBundleId",
                table: "DeliveryOrderLines",
                column: "ProductBundleId");

            migrationBuilder.CreateIndex(
                name: "IX_CashSessionMovements_CashSessionId",
                table: "CashSessionMovements",
                column: "CashSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_CashSessions_BranchId_Open",
                table: "CashSessions",
                column: "BranchId",
                unique: true,
                filter: "\"Status\" = 'Open'");

            migrationBuilder.CreateIndex(
                name: "IX_CashSessions_BranchId_OpenedAt",
                table: "CashSessions",
                columns: new[] { "BranchId", "OpenedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CashSessions_BusinessId_SessionNumber",
                table: "CashSessions",
                columns: new[] { "BusinessId", "SessionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryTrips_BranchId_DispatchedAt",
                table: "DeliveryTrips",
                columns: new[] { "BranchId", "DispatchedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryTrips_BusinessId_TripNumber",
                table: "DeliveryTrips",
                columns: new[] { "BusinessId", "TripNumber" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_DeliveryOrderLines_ProductBundles_ProductBundleId",
                table: "DeliveryOrderLines",
                column: "ProductBundleId",
                principalTable: "ProductBundles",
                principalColumn: "ProductBundleId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_DeliveryOrders_DeliveryTrips_DeliveryTripId",
                table: "DeliveryOrders",
                column: "DeliveryTripId",
                principalTable: "DeliveryTrips",
                principalColumn: "DeliveryTripId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DeliveryOrderLines_ProductBundles_ProductBundleId",
                table: "DeliveryOrderLines");

            migrationBuilder.DropForeignKey(
                name: "FK_DeliveryOrders_DeliveryTrips_DeliveryTripId",
                table: "DeliveryOrders");

            migrationBuilder.DropTable(
                name: "CashSessionMovements");

            migrationBuilder.DropTable(
                name: "DeliveryTrips");

            migrationBuilder.DropTable(
                name: "CashSessions");

            migrationBuilder.DropIndex(
                name: "IX_DeliveryOrders_DeliveryTripId",
                table: "DeliveryOrders");

            migrationBuilder.DropIndex(
                name: "IX_DeliveryOrderLines_ProductBundleId",
                table: "DeliveryOrderLines");

            migrationBuilder.DropColumn(
                name: "BranchId",
                table: "SupplierLedgerEntries");

            migrationBuilder.DropColumn(
                name: "DeliveryMode",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "RiderName",
                table: "Expenses");

            migrationBuilder.DropColumn(
                name: "DeliveryTripId",
                table: "DeliveryOrders");

            migrationBuilder.DropColumn(
                name: "ExpectedPaymentMethod",
                table: "DeliveryOrders");

            migrationBuilder.DropColumn(
                name: "ReturnReason",
                table: "DeliveryOrders");

            migrationBuilder.DropColumn(
                name: "ReturnedAt",
                table: "DeliveryOrders");

            migrationBuilder.DropColumn(
                name: "ReturnedBy",
                table: "DeliveryOrders");

            migrationBuilder.DropColumn(
                name: "BundleQuantity",
                table: "DeliveryOrderLines");

            migrationBuilder.DropColumn(
                name: "ProductBundleId",
                table: "DeliveryOrderLines");

            migrationBuilder.DropColumn(
                name: "BranchId",
                table: "CustomerLedgerEntries");
        }
    }
}
