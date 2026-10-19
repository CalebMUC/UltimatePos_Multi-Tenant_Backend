using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UltimatePos.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Branches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_StockLevels",
                table: "StockLevels");

            migrationBuilder.DropIndex(
                name: "IX_StockBatches_ProductId_BatchNumber",
                table: "StockBatches");

            migrationBuilder.DropIndex(
                name: "IX_StockBatches_ProductId_ExpiryDate",
                table: "StockBatches");

            migrationBuilder.AddColumn<Guid>(
                name: "BranchId",
                table: "UserSessions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "BranchId",
                table: "SystemEvents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "BranchId",
                table: "StockMovements",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "BranchId",
                table: "StockLevels",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "BranchId",
                table: "StockBatches",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "BranchId",
                table: "Sales",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "BranchId",
                table: "SaleReturns",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "BranchId",
                table: "PurchaseOrders",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "BranchId",
                table: "ProductionRuns",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "BranchId",
                table: "MpesaTransactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "BranchId",
                table: "Expenses",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxBranches",
                table: "BusinessLicenses",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LicenseMaxBranches",
                table: "Businesses",
                type: "integer",
                nullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_StockLevels",
                table: "StockLevels",
                columns: new[] { "ProductId", "BranchId" });

            migrationBuilder.CreateTable(
                name: "Branches",
                columns: table => new
                {
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchNumber = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Code = table.Column<string>(type: "text", nullable: true),
                    PhysicalAddress = table.Column<string>(type: "text", nullable: true),
                    County = table.Column<string>(type: "text", nullable: true),
                    PhoneNumber = table.Column<string>(type: "text", nullable: true),
                    Email = table.Column<string>(type: "text", nullable: true),
                    MpesaShortCode = table.Column<string>(type: "text", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    LastUpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastUpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Branches", x => x.BranchId);
                    table.CheckConstraint("CK_Branches_BranchNumber", "\"BranchNumber\" BETWEEN 0 AND 99");
                    table.ForeignKey(
                        name: "FK_Branches_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "BusinessId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StockTransfers",
                columns: table => new
                {
                    StockTransferId = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    TransferNumber = table.Column<string>(type: "text", nullable: false),
                    FromBranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    ToBranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    DispatchedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DispatchedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ReceivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReceivedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ReceiveNotes = table.Column<string>(type: "text", nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancelledBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockTransfers", x => x.StockTransferId);
                    table.ForeignKey(
                        name: "FK_StockTransfers_Branches_FromBranchId",
                        column: x => x.FromBranchId,
                        principalTable: "Branches",
                        principalColumn: "BranchId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockTransfers_Branches_ToBranchId",
                        column: x => x.ToBranchId,
                        principalTable: "Branches",
                        principalColumn: "BranchId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockTransfers_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "BusinessId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UserBranches",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserBranches", x => new { x.UserId, x.BranchId });
                    table.ForeignKey(
                        name: "FK_UserBranches_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "BranchId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserBranches_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StockTransferLines",
                columns: table => new
                {
                    StockTransferLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    StockTransferId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    UnitOfMeasureId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric", nullable: false),
                    QuantityInBaseUnits = table.Column<decimal>(type: "numeric", nullable: false),
                    QuantityReceivedInBaseUnits = table.Column<decimal>(type: "numeric", nullable: true),
                    UnitCost = table.Column<decimal>(type: "numeric", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockTransferLines", x => x.StockTransferLineId);
                    table.ForeignKey(
                        name: "FK_StockTransferLines_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "ProductId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockTransferLines_StockTransfers_StockTransferId",
                        column: x => x.StockTransferId,
                        principalTable: "StockTransfers",
                        principalColumn: "StockTransferId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StockTransferLines_UnitsOfMeasure_UnitOfMeasureId",
                        column: x => x.UnitOfMeasureId,
                        principalTable: "UnitsOfMeasure",
                        principalColumn: "UnitOfMeasureId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StockTransferLineBatches",
                columns: table => new
                {
                    StockTransferLineBatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    StockTransferLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceStockBatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    BatchNumber = table.Column<string>(type: "text", nullable: false),
                    ExpiryDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ManufactureDate = table.Column<DateOnly>(type: "date", nullable: true),
                    QuantityInBaseUnits = table.Column<decimal>(type: "numeric", nullable: false),
                    QuantityReceivedInBaseUnits = table.Column<decimal>(type: "numeric", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockTransferLineBatches", x => x.StockTransferLineBatchId);
                    table.ForeignKey(
                        name: "FK_StockTransferLineBatches_StockTransferLines_StockTransferLi~",
                        column: x => x.StockTransferLineId,
                        principalTable: "StockTransferLines",
                        principalColumn: "StockTransferLineId",
                        onDelete: ReferentialAction.Cascade);
                });

            // ---- Existing data: every business gets its head office (branch 00), and everything that happened so far happened there ----
            migrationBuilder.Sql("""
                INSERT INTO "Branches" ("BranchId", "BusinessId", "BranchNumber", "Name", "PhysicalAddress", "County", "PhoneNumber", "Email",
                                        "IsActive", "CreatedAt")
                SELECT gen_random_uuid(), b."BusinessId", 0, 'Head Office', b."PhysicalAddress", b."County", b."PhoneNumber", b."Email", TRUE, now()
                FROM "Businesses" b;
                """);

            migrationBuilder.Sql("""
                UPDATE "StockLevels" s SET "BranchId" = br."BranchId"
                FROM "Products" p JOIN "Branches" br ON br."BusinessId" = p."BusinessId" AND br."BranchNumber" = 0
                WHERE p."ProductId" = s."ProductId";

                UPDATE "StockMovements" m SET "BranchId" = br."BranchId"
                FROM "Products" p JOIN "Branches" br ON br."BusinessId" = p."BusinessId" AND br."BranchNumber" = 0
                WHERE p."ProductId" = m."ProductId";

                UPDATE "StockBatches" t SET "BranchId" = br."BranchId"
                FROM "Branches" br WHERE br."BusinessId" = t."BusinessId" AND br."BranchNumber" = 0;
                UPDATE "Sales" t SET "BranchId" = br."BranchId"
                FROM "Branches" br WHERE br."BusinessId" = t."BusinessId" AND br."BranchNumber" = 0;
                UPDATE "SaleReturns" t SET "BranchId" = br."BranchId"
                FROM "Branches" br WHERE br."BusinessId" = t."BusinessId" AND br."BranchNumber" = 0;
                UPDATE "PurchaseOrders" t SET "BranchId" = br."BranchId"
                FROM "Branches" br WHERE br."BusinessId" = t."BusinessId" AND br."BranchNumber" = 0;
                UPDATE "ProductionRuns" t SET "BranchId" = br."BranchId"
                FROM "Branches" br WHERE br."BusinessId" = t."BusinessId" AND br."BranchNumber" = 0;

                -- STK pushes belong to their sale's branch. Till (C2B) payments to the business's short code stay branch-less
                -- (any branch may match them); expenses so far stay business-wide.
                UPDATE "MpesaTransactions" t SET "BranchId" = s."BranchId"
                FROM "Sales" s WHERE s."SaleId" = t."SaleId";
                """);

            // Every member works at their business's head office, as their default branch there.
            migrationBuilder.Sql("""
                INSERT INTO "UserBranches" ("UserId", "BranchId", "IsDefault", "CreatedAt")
                SELECT m."UserId", br."BranchId", TRUE, now()
                FROM "UserBusinesses" m JOIN "Branches" br ON br."BusinessId" = m."BusinessId" AND br."BranchNumber" = 0
                ON CONFLICT DO NOTHING;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_BranchId_ProductId_CreatedAt",
                table: "StockMovements",
                columns: new[] { "BranchId", "ProductId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_StockLevels_BranchId",
                table: "StockLevels",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_StockBatches_BranchId_ProductId_BatchNumber",
                table: "StockBatches",
                columns: new[] { "BranchId", "ProductId", "BatchNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockBatches_BranchId_ProductId_ExpiryDate",
                table: "StockBatches",
                columns: new[] { "BranchId", "ProductId", "ExpiryDate" });

            migrationBuilder.CreateIndex(
                name: "IX_StockBatches_ProductId",
                table: "StockBatches",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_Sales_BranchId_CreatedAt",
                table: "Sales",
                columns: new[] { "BranchId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SaleReturns_BranchId",
                table: "SaleReturns",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrders_BranchId",
                table: "PurchaseOrders",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionRuns_BranchId",
                table: "ProductionRuns",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_MpesaTransactions_BranchId",
                table: "MpesaTransactions",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_BranchId",
                table: "Expenses",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_Branches_BusinessId_BranchNumber",
                table: "Branches",
                columns: new[] { "BusinessId", "BranchNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Branches_BusinessId_Code",
                table: "Branches",
                columns: new[] { "BusinessId", "Code" },
                unique: true,
                filter: "\"Code\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Branches_MpesaShortCode",
                table: "Branches",
                column: "MpesaShortCode",
                unique: true,
                filter: "\"MpesaShortCode\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_StockTransferLineBatches_StockTransferLineId",
                table: "StockTransferLineBatches",
                column: "StockTransferLineId");

            migrationBuilder.CreateIndex(
                name: "IX_StockTransferLines_ProductId",
                table: "StockTransferLines",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_StockTransferLines_StockTransferId",
                table: "StockTransferLines",
                column: "StockTransferId");

            migrationBuilder.CreateIndex(
                name: "IX_StockTransferLines_UnitOfMeasureId",
                table: "StockTransferLines",
                column: "UnitOfMeasureId");

            migrationBuilder.CreateIndex(
                name: "IX_StockTransfers_BusinessId_TransferNumber",
                table: "StockTransfers",
                columns: new[] { "BusinessId", "TransferNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockTransfers_FromBranchId_Status",
                table: "StockTransfers",
                columns: new[] { "FromBranchId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_StockTransfers_ToBranchId_Status",
                table: "StockTransfers",
                columns: new[] { "ToBranchId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_UserBranches_BranchId",
                table: "UserBranches",
                column: "BranchId");

            migrationBuilder.AddForeignKey(
                name: "FK_Expenses_Branches_BranchId",
                table: "Expenses",
                column: "BranchId",
                principalTable: "Branches",
                principalColumn: "BranchId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_MpesaTransactions_Branches_BranchId",
                table: "MpesaTransactions",
                column: "BranchId",
                principalTable: "Branches",
                principalColumn: "BranchId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ProductionRuns_Branches_BranchId",
                table: "ProductionRuns",
                column: "BranchId",
                principalTable: "Branches",
                principalColumn: "BranchId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseOrders_Branches_BranchId",
                table: "PurchaseOrders",
                column: "BranchId",
                principalTable: "Branches",
                principalColumn: "BranchId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SaleReturns_Branches_BranchId",
                table: "SaleReturns",
                column: "BranchId",
                principalTable: "Branches",
                principalColumn: "BranchId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Sales_Branches_BranchId",
                table: "Sales",
                column: "BranchId",
                principalTable: "Branches",
                principalColumn: "BranchId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_StockBatches_Branches_BranchId",
                table: "StockBatches",
                column: "BranchId",
                principalTable: "Branches",
                principalColumn: "BranchId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_StockLevels_Branches_BranchId",
                table: "StockLevels",
                column: "BranchId",
                principalTable: "Branches",
                principalColumn: "BranchId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_StockMovements_Branches_BranchId",
                table: "StockMovements",
                column: "BranchId",
                principalTable: "Branches",
                principalColumn: "BranchId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Expenses_Branches_BranchId",
                table: "Expenses");

            migrationBuilder.DropForeignKey(
                name: "FK_MpesaTransactions_Branches_BranchId",
                table: "MpesaTransactions");

            migrationBuilder.DropForeignKey(
                name: "FK_ProductionRuns_Branches_BranchId",
                table: "ProductionRuns");

            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseOrders_Branches_BranchId",
                table: "PurchaseOrders");

            migrationBuilder.DropForeignKey(
                name: "FK_SaleReturns_Branches_BranchId",
                table: "SaleReturns");

            migrationBuilder.DropForeignKey(
                name: "FK_Sales_Branches_BranchId",
                table: "Sales");

            migrationBuilder.DropForeignKey(
                name: "FK_StockBatches_Branches_BranchId",
                table: "StockBatches");

            migrationBuilder.DropForeignKey(
                name: "FK_StockLevels_Branches_BranchId",
                table: "StockLevels");

            migrationBuilder.DropForeignKey(
                name: "FK_StockMovements_Branches_BranchId",
                table: "StockMovements");

            migrationBuilder.DropTable(
                name: "StockTransferLineBatches");

            migrationBuilder.DropTable(
                name: "UserBranches");

            migrationBuilder.DropTable(
                name: "StockTransferLines");

            migrationBuilder.DropTable(
                name: "StockTransfers");

            migrationBuilder.DropTable(
                name: "Branches");

            migrationBuilder.DropIndex(
                name: "IX_StockMovements_BranchId_ProductId_CreatedAt",
                table: "StockMovements");

            migrationBuilder.DropPrimaryKey(
                name: "PK_StockLevels",
                table: "StockLevels");

            migrationBuilder.DropIndex(
                name: "IX_StockLevels_BranchId",
                table: "StockLevels");

            migrationBuilder.DropIndex(
                name: "IX_StockBatches_BranchId_ProductId_BatchNumber",
                table: "StockBatches");

            migrationBuilder.DropIndex(
                name: "IX_StockBatches_BranchId_ProductId_ExpiryDate",
                table: "StockBatches");

            migrationBuilder.DropIndex(
                name: "IX_StockBatches_ProductId",
                table: "StockBatches");

            migrationBuilder.DropIndex(
                name: "IX_Sales_BranchId_CreatedAt",
                table: "Sales");

            migrationBuilder.DropIndex(
                name: "IX_SaleReturns_BranchId",
                table: "SaleReturns");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseOrders_BranchId",
                table: "PurchaseOrders");

            migrationBuilder.DropIndex(
                name: "IX_ProductionRuns_BranchId",
                table: "ProductionRuns");

            migrationBuilder.DropIndex(
                name: "IX_MpesaTransactions_BranchId",
                table: "MpesaTransactions");

            migrationBuilder.DropIndex(
                name: "IX_Expenses_BranchId",
                table: "Expenses");

            migrationBuilder.DropColumn(
                name: "BranchId",
                table: "UserSessions");

            migrationBuilder.DropColumn(
                name: "BranchId",
                table: "SystemEvents");

            migrationBuilder.DropColumn(
                name: "BranchId",
                table: "StockMovements");

            migrationBuilder.DropColumn(
                name: "BranchId",
                table: "StockLevels");

            migrationBuilder.DropColumn(
                name: "BranchId",
                table: "StockBatches");

            migrationBuilder.DropColumn(
                name: "BranchId",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "BranchId",
                table: "SaleReturns");

            migrationBuilder.DropColumn(
                name: "BranchId",
                table: "PurchaseOrders");

            migrationBuilder.DropColumn(
                name: "BranchId",
                table: "ProductionRuns");

            migrationBuilder.DropColumn(
                name: "BranchId",
                table: "MpesaTransactions");

            migrationBuilder.DropColumn(
                name: "BranchId",
                table: "Expenses");

            migrationBuilder.DropColumn(
                name: "MaxBranches",
                table: "BusinessLicenses");

            migrationBuilder.DropColumn(
                name: "LicenseMaxBranches",
                table: "Businesses");

            migrationBuilder.AddPrimaryKey(
                name: "PK_StockLevels",
                table: "StockLevels",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_StockBatches_ProductId_BatchNumber",
                table: "StockBatches",
                columns: new[] { "ProductId", "BatchNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockBatches_ProductId_ExpiryDate",
                table: "StockBatches",
                columns: new[] { "ProductId", "ExpiryDate" });
        }
    }
}
