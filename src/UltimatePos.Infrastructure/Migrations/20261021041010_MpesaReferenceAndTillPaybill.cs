using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UltimatePos.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MpesaReferenceAndTillPaybill : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MpesaReference",
                table: "Sales",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MpesaPaybillNumber",
                table: "Businesses",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MpesaTillNumber",
                table: "Businesses",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            // The old field held "till or paybill": it becomes the till. An admin moves it to Paybill where it was a paybill.
            migrationBuilder.Sql(@"UPDATE ""Businesses"" SET ""MpesaTillNumber"" = ""MpesaShortCode"" WHERE ""MpesaShortCode"" IS NOT NULL;");

            migrationBuilder.CreateIndex(
                name: "IX_Sales_BusinessId_MpesaReference",
                table: "Sales",
                columns: new[] { "BusinessId", "MpesaReference" },
                unique: true,
                filter: "\"MpesaReference\" IS NOT NULL AND \"Status\" NOT IN ('Voided', 'Failed')");

            migrationBuilder.CreateIndex(
                name: "IX_Businesses_MpesaPaybillNumber",
                table: "Businesses",
                column: "MpesaPaybillNumber",
                unique: true,
                filter: "\"MpesaPaybillNumber\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Businesses_MpesaTillNumber",
                table: "Businesses",
                column: "MpesaTillNumber",
                unique: true,
                filter: "\"MpesaTillNumber\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Sales_BusinessId_MpesaReference",
                table: "Sales");

            migrationBuilder.DropIndex(
                name: "IX_Businesses_MpesaPaybillNumber",
                table: "Businesses");

            migrationBuilder.DropIndex(
                name: "IX_Businesses_MpesaTillNumber",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "MpesaReference",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "MpesaPaybillNumber",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "MpesaTillNumber",
                table: "Businesses");
        }
    }
}
