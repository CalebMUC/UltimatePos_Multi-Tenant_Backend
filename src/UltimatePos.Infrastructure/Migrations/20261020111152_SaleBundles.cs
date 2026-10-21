using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UltimatePos.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SaleBundles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BundleName",
                table: "SaleLines",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "BundleQuantity",
                table: "SaleLines",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProductBundleId",
                table: "SaleLines",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ProductBundles",
                columns: table => new
                {
                    ProductBundleId = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    RetailPrice = table.Column<decimal>(type: "numeric", nullable: false),
                    WholesalePrice = table.Column<decimal>(type: "numeric", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    LastUpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastUpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductBundles", x => x.ProductBundleId);
                    table.ForeignKey(
                        name: "FK_ProductBundles_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "BusinessId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductBundleComponents",
                columns: table => new
                {
                    ProductBundleComponentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductBundleId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    UnitOfMeasureId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductBundleComponents", x => x.ProductBundleComponentId);
                    table.ForeignKey(
                        name: "FK_ProductBundleComponents_ProductBundles_ProductBundleId",
                        column: x => x.ProductBundleId,
                        principalTable: "ProductBundles",
                        principalColumn: "ProductBundleId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProductBundleComponents_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "ProductId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductBundleComponents_UnitsOfMeasure_UnitOfMeasureId",
                        column: x => x.UnitOfMeasureId,
                        principalTable: "UnitsOfMeasure",
                        principalColumn: "UnitOfMeasureId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SaleLines_ProductBundleId",
                table: "SaleLines",
                column: "ProductBundleId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductBundleComponents_ProductBundleId_ProductId_UnitOfMea~",
                table: "ProductBundleComponents",
                columns: new[] { "ProductBundleId", "ProductId", "UnitOfMeasureId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductBundleComponents_ProductId",
                table: "ProductBundleComponents",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductBundleComponents_UnitOfMeasureId",
                table: "ProductBundleComponents",
                column: "UnitOfMeasureId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductBundles_BusinessId_Code",
                table: "ProductBundles",
                columns: new[] { "BusinessId", "Code" },
                unique: true,
                filter: "\"Code\" IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_SaleLines_ProductBundles_ProductBundleId",
                table: "SaleLines",
                column: "ProductBundleId",
                principalTable: "ProductBundles",
                principalColumn: "ProductBundleId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SaleLines_ProductBundles_ProductBundleId",
                table: "SaleLines");

            migrationBuilder.DropTable(
                name: "ProductBundleComponents");

            migrationBuilder.DropTable(
                name: "ProductBundles");

            migrationBuilder.DropIndex(
                name: "IX_SaleLines_ProductBundleId",
                table: "SaleLines");

            migrationBuilder.DropColumn(
                name: "BundleName",
                table: "SaleLines");

            migrationBuilder.DropColumn(
                name: "BundleQuantity",
                table: "SaleLines");

            migrationBuilder.DropColumn(
                name: "ProductBundleId",
                table: "SaleLines");
        }
    }
}
