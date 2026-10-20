using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UltimatePos.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BusinessSector : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Sector",
                table: "Businesses",
                type: "text",
                nullable: false,
                defaultValue: "General"); // existing businesses: unclassified until a super user sets their sector
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Sector",
                table: "Businesses");
        }
    }
}
