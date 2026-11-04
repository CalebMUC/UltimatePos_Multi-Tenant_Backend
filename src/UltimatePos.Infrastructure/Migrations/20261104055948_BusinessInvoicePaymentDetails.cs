using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UltimatePos.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BusinessInvoicePaymentDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "InvoiceBankAccountName",
                table: "Businesses",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InvoiceBankAccountNumber",
                table: "Businesses",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InvoiceBankBranch",
                table: "Businesses",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InvoiceBankName",
                table: "Businesses",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InvoiceBankPaybill",
                table: "Businesses",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "InvoiceDueDays",
                table: "Businesses",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InvoiceNote",
                table: "Businesses",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InvoiceSendMoneyPhone",
                table: "Businesses",
                type: "character varying(15)",
                maxLength: 15,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InvoiceTillNumber",
                table: "Businesses",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InvoiceBankAccountName",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "InvoiceBankAccountNumber",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "InvoiceBankBranch",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "InvoiceBankName",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "InvoiceBankPaybill",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "InvoiceDueDays",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "InvoiceNote",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "InvoiceSendMoneyPhone",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "InvoiceTillNumber",
                table: "Businesses");
        }
    }
}
