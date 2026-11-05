using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UltimatePos.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AgeRestrictedSalesSwitch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // On for every existing business: a liquor store keeps its age checks until it switches them off. It only applies to Liquor.
            migrationBuilder.AddColumn<bool>(
                name: "AgeRestrictedSalesEnabled",
                table: "Businesses",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AgeRestrictedSalesEnabled",
                table: "Businesses");
        }
    }
}
