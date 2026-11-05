using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UltimatePos.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class LiquorAgeRestrictedSales : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AgeCheck",
                table: "Sales",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsAgeRestricted",
                table: "Products",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "MinimumPurchaseAge",
                table: "Businesses",
                type: "integer",
                nullable: false,
                defaultValue: 18); // the legal age today: existing businesses start with it

            migrationBuilder.AddColumn<TimeOnly>(
                name: "RestrictedSalesFrom",
                table: "Businesses",
                type: "time without time zone",
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "RestrictedSalesTo",
                table: "Businesses",
                type: "time without time zone",
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "RestrictedSalesWeekendFrom",
                table: "Businesses",
                type: "time without time zone",
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "RestrictedSalesWeekendTo",
                table: "Businesses",
                type: "time without time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AgeCheck",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "IsAgeRestricted",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "MinimumPurchaseAge",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "RestrictedSalesFrom",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "RestrictedSalesTo",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "RestrictedSalesWeekendFrom",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "RestrictedSalesWeekendTo",
                table: "Businesses");
        }
    }
}
