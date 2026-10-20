using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UltimatePos.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UnitConversionContainer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ContainerProductId",
                table: "ProductUnitConversions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductUnitConversions_ContainerProductId",
                table: "ProductUnitConversions",
                column: "ContainerProductId");

            migrationBuilder.AddForeignKey(
                name: "FK_ProductUnitConversions_Products_ContainerProductId",
                table: "ProductUnitConversions",
                column: "ContainerProductId",
                principalTable: "Products",
                principalColumn: "ProductId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ProductUnitConversions_Products_ContainerProductId",
                table: "ProductUnitConversions");

            migrationBuilder.DropIndex(
                name: "IX_ProductUnitConversions_ContainerProductId",
                table: "ProductUnitConversions");

            migrationBuilder.DropColumn(
                name: "ContainerProductId",
                table: "ProductUnitConversions");
        }
    }
}
