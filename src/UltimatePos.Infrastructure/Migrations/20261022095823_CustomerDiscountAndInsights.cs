using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UltimatePos.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CustomerDiscountAndInsights : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CustomerDiscountAmount",
                table: "Sales",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "CustomerDiscountPercent",
                table: "Sales",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DiscountNote",
                table: "Customers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DiscountPercent",
                table: "Customers",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DiscountSetAt",
                table: "Customers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DiscountSetBy",
                table: "Customers",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CustomerDiscountAmount",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "CustomerDiscountPercent",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "DiscountNote",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "DiscountPercent",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "DiscountSetAt",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "DiscountSetBy",
                table: "Customers");
        }
    }
}
