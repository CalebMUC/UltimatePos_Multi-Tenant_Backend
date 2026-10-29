using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UltimatePos.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Etims : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EtimsCategoryProfiles",
                columns: table => new
                {
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemClassCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastUpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastUpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EtimsCategoryProfiles", x => x.CategoryId);
                    table.ForeignKey(
                        name: "FK_EtimsCategoryProfiles_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "BusinessId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EtimsCategoryProfiles_Categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "Categories",
                        principalColumn: "CategoryId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EtimsDevices",
                columns: table => new
                {
                    EtimsDeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    Tin = table.Column<string>(type: "character varying(11)", maxLength: 11, nullable: false),
                    BhfId = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    DeviceSerialNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CmcKey = table.Column<string>(type: "text", nullable: false),
                    SdcId = table.Column<string>(type: "text", nullable: true),
                    MrcNo = table.Column<string>(type: "text", nullable: true),
                    DeviceId = table.Column<string>(type: "text", nullable: true),
                    TaxpayerName = table.Column<string>(type: "text", nullable: true),
                    KraBranchName = table.Column<string>(type: "text", nullable: true),
                    InitializationResponse = table.Column<string>(type: "jsonb", nullable: true),
                    DefaultItemClassCode = table.Column<string>(type: "text", nullable: true),
                    ReceiptTopMessage = table.Column<string>(type: "text", nullable: true),
                    ReceiptBottomMessage = table.Column<string>(type: "text", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ActivatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SweptUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastInvoiceNumber = table.Column<long>(type: "bigint", nullable: false),
                    LastStockAdjustmentNumber = table.Column<long>(type: "bigint", nullable: false),
                    LastItemSequence = table.Column<long>(type: "bigint", nullable: false),
                    OpeningStockQueuedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastCodeSyncAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ItemsCheckedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    LastUpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastUpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EtimsDevices", x => x.EtimsDeviceId);
                    table.ForeignKey(
                        name: "FK_EtimsDevices_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "BranchId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EtimsDevices_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "BusinessId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EtimsProductProfiles",
                columns: table => new
                {
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemCode = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    ItemClassCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    PackagingUnitCode = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    QuantityUnitCode = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    OriginCountryCode = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastUpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastUpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EtimsProductProfiles", x => x.ProductId);
                    table.ForeignKey(
                        name: "FK_EtimsProductProfiles_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "BusinessId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EtimsProductProfiles_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "ProductId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EtimsReferenceData",
                columns: table => new
                {
                    EtimsReferenceDataId = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    EtimsDeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Payload = table.Column<string>(type: "jsonb", nullable: false),
                    FetchedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EtimsReferenceData", x => x.EtimsReferenceDataId);
                    table.ForeignKey(
                        name: "FK_EtimsReferenceData_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "BusinessId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EtimsReferenceData_EtimsDevices_EtimsDeviceId",
                        column: x => x.EtimsDeviceId,
                        principalTable: "EtimsDevices",
                        principalColumn: "EtimsDeviceId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EtimsStockBalances",
                columns: table => new
                {
                    EtimsDeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric", nullable: false),
                    LastUpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EtimsStockBalances", x => new { x.EtimsDeviceId, x.ProductId });
                    table.ForeignKey(
                        name: "FK_EtimsStockBalances_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "BusinessId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EtimsStockBalances_EtimsDevices_EtimsDeviceId",
                        column: x => x.EtimsDeviceId,
                        principalTable: "EtimsDevices",
                        principalColumn: "EtimsDeviceId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EtimsStockBalances_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "ProductId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EtimsSubmissions",
                columns: table => new
                {
                    EtimsSubmissionId = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    EtimsDeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SourceType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    SourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceKey = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    DocumentNumber = table.Column<long>(type: "bigint", nullable: true),
                    ProductIds = table.Column<List<Guid>>(type: "uuid[]", nullable: false),
                    DependsOnSubmissionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Endpoint = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    RequestJson = table.Column<string>(type: "jsonb", nullable: false),
                    ResponseJson = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ResultCode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    ResultMessage = table.Column<string>(type: "text", nullable: true),
                    ReceiptNumber = table.Column<long>(type: "bigint", nullable: true),
                    TotalReceiptNumber = table.Column<long>(type: "bigint", nullable: true),
                    InternalData = table.Column<string>(type: "text", nullable: true),
                    ReceiptSignature = table.Column<string>(type: "text", nullable: true),
                    SdcDateTime = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastUpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EtimsSubmissions", x => x.EtimsSubmissionId);
                    table.ForeignKey(
                        name: "FK_EtimsSubmissions_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "BusinessId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EtimsSubmissions_EtimsDevices_EtimsDeviceId",
                        column: x => x.EtimsDeviceId,
                        principalTable: "EtimsDevices",
                        principalColumn: "EtimsDeviceId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EtimsCategoryProfiles_BusinessId",
                table: "EtimsCategoryProfiles",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_EtimsDevices_BranchId",
                table: "EtimsDevices",
                column: "BranchId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EtimsDevices_BusinessId",
                table: "EtimsDevices",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_EtimsProductProfiles_BusinessId_ItemCode",
                table: "EtimsProductProfiles",
                columns: new[] { "BusinessId", "ItemCode" },
                unique: true,
                filter: "\"ItemCode\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_EtimsReferenceData_BusinessId",
                table: "EtimsReferenceData",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_EtimsReferenceData_EtimsDeviceId_Kind",
                table: "EtimsReferenceData",
                columns: new[] { "EtimsDeviceId", "Kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EtimsStockBalances_BusinessId",
                table: "EtimsStockBalances",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_EtimsStockBalances_ProductId",
                table: "EtimsStockBalances",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_EtimsSubmissions_BusinessId_CreatedAt",
                table: "EtimsSubmissions",
                columns: new[] { "BusinessId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_EtimsSubmissions_Due",
                table: "EtimsSubmissions",
                columns: new[] { "EtimsDeviceId", "NextAttemptAt" },
                filter: "\"Status\" IN ('Pending', 'Failed')");

            migrationBuilder.CreateIndex(
                name: "IX_EtimsSubmissions_EtimsDeviceId_SourceKey",
                table: "EtimsSubmissions",
                columns: new[] { "EtimsDeviceId", "SourceKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EtimsSubmissions_SourceId_DocumentType",
                table: "EtimsSubmissions",
                columns: new[] { "SourceId", "DocumentType" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EtimsCategoryProfiles");

            migrationBuilder.DropTable(
                name: "EtimsProductProfiles");

            migrationBuilder.DropTable(
                name: "EtimsReferenceData");

            migrationBuilder.DropTable(
                name: "EtimsStockBalances");

            migrationBuilder.DropTable(
                name: "EtimsSubmissions");

            migrationBuilder.DropTable(
                name: "EtimsDevices");
        }
    }
}
