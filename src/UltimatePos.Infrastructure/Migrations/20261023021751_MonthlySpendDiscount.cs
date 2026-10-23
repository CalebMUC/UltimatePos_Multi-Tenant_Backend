using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UltimatePos.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MonthlySpendDiscount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CustomerDiscountSource",
                table: "Sales",
                type: "text",
                nullable: true);

            // Customer discounts given before this release were all standing discounts.
            migrationBuilder.Sql("""UPDATE "Sales" SET "CustomerDiscountSource" = 'Standing' WHERE "CustomerDiscountAmount" > 0""");

            migrationBuilder.AddColumn<decimal>(
                name: "MonthlyDiscountPercent",
                table: "Businesses",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MonthlyDiscountScope",
                table: "Businesses",
                type: "text",
                nullable: false,
                defaultValue: "RestOfMonth");

            migrationBuilder.AddColumn<decimal>(
                name: "MonthlyDiscountThreshold",
                table: "Businesses",
                type: "numeric",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CustomerDiscountSource",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "MonthlyDiscountPercent",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "MonthlyDiscountScope",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "MonthlyDiscountThreshold",
                table: "Businesses");
        }
    }
}
