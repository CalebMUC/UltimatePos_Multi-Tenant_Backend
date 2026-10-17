using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UltimatePos.Infrastructure.Migrations
{
    /// <summary>
    /// Multi-business: every business-owned table gets a BusinessId (existing rows go to the oldest business), per-business
    /// unique numbers, memberships, platform vs business roles and refresh-token sessions. Pharmacy: stock batches with expiry,
    /// batch allocations on sale lines, prescriptions, returns, discounts and VAT per line, barcodes and medicine attributes.
    /// Accounts: supplier payables, customer credit limits, payment methods on customer payments.
    /// Down drops the new structures but cannot split merged data back apart.
    /// </summary>
    public partial class MultiBusinessAndPharmacy : Migration
    {
        // Every permission as of this migration (PermissionCatalog).
        private const string AllPermissions = @"
            ('Businesses.Register', 'Businesses'), ('Businesses.ViewAll', 'Businesses'), ('Businesses.Deactivate', 'Businesses'),
            ('Users.Create', 'Users'), ('Users.View', 'Users'), ('Users.Update', 'Users'), ('Users.AssignRoles', 'Users'),
            ('Users.Deactivate', 'Users'), ('Users.Lock', 'Users'), ('Users.ResetPassword', 'Users'),
            ('Roles.View', 'Roles'), ('Roles.Create', 'Roles'), ('Roles.Update', 'Roles'), ('Roles.Deactivate', 'Roles'), ('Roles.AssignPermissions', 'Roles'),
            ('Permissions.View', 'Permissions'),
            ('Businesses.View', 'Businesses'), ('Businesses.Update', 'Businesses'),
            ('Customers.Register', 'Customers'), ('Customers.View', 'Customers'), ('Customers.Update', 'Customers'),
            ('Customers.Deactivate', 'Customers'), ('Customers.ViewLedger', 'Customers'), ('Customers.RecordPayment', 'Customers'),
            ('Categories.View', 'Categories'), ('Categories.Create', 'Categories'), ('Categories.Update', 'Categories'),
            ('Categories.Deactivate', 'Categories'), ('Categories.BulkImport', 'Categories'),
            ('Units.View', 'Units'), ('Units.Create', 'Units'), ('Units.Update', 'Units'), ('Units.Deactivate', 'Units'),
            ('Products.View', 'Products'), ('Products.Create', 'Products'), ('Products.Update', 'Products'), ('Products.Deactivate', 'Products'),
            ('Products.ManagePricing', 'Products'), ('Products.BulkImport', 'Products'), ('Products.ManageUnitConversions', 'Products'),
            ('Suppliers.Register', 'Suppliers'), ('Suppliers.View', 'Suppliers'), ('Suppliers.Update', 'Suppliers'),
            ('Suppliers.Deactivate', 'Suppliers'), ('Suppliers.ViewLedger', 'Suppliers'), ('Suppliers.RecordPayment', 'Suppliers'),
            ('PurchaseOrders.Create', 'PurchaseOrders'), ('PurchaseOrders.View', 'PurchaseOrders'),
            ('PurchaseOrders.Receive', 'PurchaseOrders'), ('PurchaseOrders.Cancel', 'PurchaseOrders'),
            ('Stock.View', 'Stock'), ('Stock.Adjust', 'Stock'),
            ('Formulas.Create', 'Formulas'), ('Formulas.View', 'Formulas'), ('Formulas.Deactivate', 'Formulas'),
            ('Production.Run', 'Production'), ('Production.View', 'Production'),
            ('Sales.Create', 'Sales'), ('Sales.View', 'Sales'), ('Sales.Void', 'Sales'),
            ('Sales.Return', 'Sales'), ('Sales.ApplyDiscount', 'Sales'), ('Sales.OverrideCreditLimit', 'Sales'),
            ('Prescriptions.Dispense', 'Prescriptions'), ('Prescriptions.View', 'Prescriptions'),
            ('Mpesa.Configure', 'Mpesa'), ('Mpesa.View', 'Mpesa'),
            ('Costing.View', 'Costing'), ('Costing.Revalue', 'Costing'),
            ('ExpenseCategories.Create', 'Expenses'), ('ExpenseCategories.View', 'Expenses'),
            ('ExpenseCategories.Update', 'Expenses'), ('ExpenseCategories.Deactivate', 'Expenses'),
            ('Expenses.Create', 'Expenses'), ('Expenses.View', 'Expenses'), ('Expenses.Update', 'Expenses'), ('Expenses.Void', 'Expenses'),
            ('Dashboard.View', 'Dashboard'),
            ('SalesReports.View', 'Reports'), ('CustomerReports.View', 'Reports'), ('ProductionReports.View', 'Reports'),
            ('InventoryReports.View', 'Reports'), ('PurchaseReports.View', 'Reports')";

        private const string PlatformPermissionNames = "'Businesses.Register', 'Businesses.ViewAll', 'Businesses.Deactivate'";

        // Business permissions added by this migration: granted to every business's Admin role.
        private const string NewBusinessPermissionNames =
            "'Users.Update', 'Users.ResetPassword', 'Categories.Update', 'Categories.Deactivate', 'Units.Update', 'Units.Deactivate', " +
            "'Products.Deactivate', 'Suppliers.ViewLedger', 'Suppliers.RecordPayment', 'Sales.Return', 'Sales.ApplyDiscount', " +
            "'Sales.OverrideCreditLimit', 'Prescriptions.Dispense', 'Prescriptions.View'";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UnitsOfMeasure_Symbol",
                table: "UnitsOfMeasure");

            migrationBuilder.DropPrimaryKey(
                name: "PK_SkuSequences",
                table: "SkuSequences");

            migrationBuilder.DropIndex(
                name: "IX_Sales_SaleNumber",
                table: "Sales");

            migrationBuilder.DropIndex(
                name: "IX_Roles_RoleName",
                table: "Roles");

            migrationBuilder.DropIndex(
                name: "IX_Products_Sku",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_ProductionRuns_RunNumber",
                table: "ProductionRuns");

            migrationBuilder.DropIndex(
                name: "IX_Expenses_ExpenseNumber",
                table: "Expenses");

            migrationBuilder.DropIndex(
                name: "IX_ExpenseCategories_Code",
                table: "ExpenseCategories");

            migrationBuilder.DropIndex(
                name: "IX_ExpenseCategories_Name",
                table: "ExpenseCategories");

            migrationBuilder.DropIndex(
                name: "IX_Categories_Code",
                table: "Categories");

            migrationBuilder.AddColumn<Guid>(
                name: "BusinessId",
                table: "UserSessions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RevokedAt",
                table: "UserSessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "BusinessId",
                table: "UnitsOfMeasure",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "StockBatchId",
                table: "StockMovements",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "BusinessId",
                table: "SkuSequences",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "BusinessId",
                table: "Sales",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<decimal>(
                name: "DiscountTotal",
                table: "Sales",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ReturnedAmount",
                table: "Sales",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "Subtotal",
                table: "Sales",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TaxTotal",
                table: "Sales",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "DiscountAmount",
                table: "SaleLines",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "QuantityReturned",
                table: "SaleLines",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TaxAmount",
                table: "SaleLines",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TaxRate",
                table: "SaleLines",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "BusinessId",
                table: "Roles",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "BusinessId",
                table: "PurchaseOrders",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "Barcode",
                table: "Products",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "BusinessId",
                table: "Products",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "DosageForm",
                table: "Products",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GenericName",
                table: "Products",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsBatchTracked",
                table: "Products",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Manufacturer",
                table: "Products",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RequiresPrescription",
                table: "Products",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Strength",
                table: "Products",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "BusinessId",
                table: "ProductionRuns",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "BusinessId",
                table: "MpesaTransactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "BusinessId",
                table: "Formulas",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "BusinessId",
                table: "Expenses",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "BusinessId",
                table: "ExpenseCategories",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<decimal>(
                name: "CreditLimit",
                table: "Customers",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentMethod",
                table: "CustomerLedgerEntries",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentReference",
                table: "CustomerLedgerEntries",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "BusinessId",
                table: "Categories",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<bool>(
                name: "IsVatRegistered",
                table: "Businesses",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "MaxDiscountPercent",
                table: "Businesses",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MpesaShortCode",
                table: "Businesses",
                type: "text",
                nullable: true);

            // ---- Hand back every existing row to a business ------------------------------------------------------------------
            // Until now the data had no owner. It all belongs to the oldest business (the first client). A database holding data but
            // no business at all gets a placeholder business to own it, to be renamed through the API. Legacy roles become that
            // business's roles and every existing user becomes a member of it.
            migrationBuilder.Sql(@"
DO $$
DECLARE owner uuid;
BEGIN
    SELECT ""BusinessId"" INTO owner FROM ""Businesses"" ORDER BY ""CreatedAt"", ""BusinessId"" LIMIT 1;

    IF owner IS NULL AND (
           EXISTS (SELECT 1 FROM ""Products"") OR EXISTS (SELECT 1 FROM ""Categories"") OR EXISTS (SELECT 1 FROM ""UnitsOfMeasure"")
        OR EXISTS (SELECT 1 FROM ""Sales"") OR EXISTS (SELECT 1 FROM ""PurchaseOrders"") OR EXISTS (SELECT 1 FROM ""Formulas"")
        OR EXISTS (SELECT 1 FROM ""ProductionRuns"") OR EXISTS (SELECT 1 FROM ""Expenses"") OR EXISTS (SELECT 1 FROM ""ExpenseCategories"")
        OR EXISTS (SELECT 1 FROM ""SkuSequences"") OR EXISTS (SELECT 1 FROM ""MpesaTransactions"")) THEN
        owner := gen_random_uuid();
        INSERT INTO ""Businesses"" (""BusinessId"", ""BusinessName"", ""BusinessType"", ""Kind"", ""KraPin"", ""IsActive"", ""IsVatRegistered"", ""CreatedAt"")
        VALUES (owner, 'Existing business', 'Retailer', 'Trader', 'UNASSIGNED-' || substr(owner::text, 1, 8), TRUE, FALSE, now());
    END IF;

    IF owner IS NOT NULL THEN
        UPDATE ""Categories"" SET ""BusinessId"" = owner;
        UPDATE ""UnitsOfMeasure"" SET ""BusinessId"" = owner;
        UPDATE ""Products"" SET ""BusinessId"" = owner;
        UPDATE ""PurchaseOrders"" SET ""BusinessId"" = owner;
        UPDATE ""Formulas"" SET ""BusinessId"" = owner;
        UPDATE ""ProductionRuns"" SET ""BusinessId"" = owner;
        UPDATE ""Sales"" SET ""BusinessId"" = owner;
        UPDATE ""Expenses"" SET ""BusinessId"" = owner;
        UPDATE ""ExpenseCategories"" SET ""BusinessId"" = owner;
        UPDATE ""SkuSequences"" SET ""BusinessId"" = owner;
        UPDATE ""MpesaTransactions"" t
           SET ""BusinessId"" = COALESCE((SELECT s.""BusinessId"" FROM ""Sales"" s WHERE s.""SaleId"" = t.""SaleId""), owner);
        UPDATE ""Roles"" SET ""BusinessId"" = owner WHERE ""BusinessId"" IS NULL;
    END IF;
END $$;");

            // Sales recorded before discounts existed: what was charged is the subtotal.
            migrationBuilder.Sql(@"UPDATE ""Sales"" SET ""Subtotal"" = ""TotalAmount"" WHERE ""Subtotal"" = 0;");

            migrationBuilder.AddPrimaryKey(
                name: "PK_SkuSequences",
                table: "SkuSequences",
                columns: new[] { "BusinessId", "Prefix" });

            migrationBuilder.CreateTable(
                name: "SalePrescriptions",
                columns: table => new
                {
                    SaleId = table.Column<Guid>(type: "uuid", nullable: false),
                    PrescriptionNumber = table.Column<string>(type: "text", nullable: true),
                    PrescriberName = table.Column<string>(type: "text", nullable: false),
                    PrescriberRegistrationNumber = table.Column<string>(type: "text", nullable: true),
                    PrescriberFacility = table.Column<string>(type: "text", nullable: true),
                    PatientName = table.Column<string>(type: "text", nullable: false),
                    PatientPhoneNumber = table.Column<string>(type: "text", nullable: true),
                    PatientAgeYears = table.Column<int>(type: "integer", nullable: true),
                    PrescriptionDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    DispensedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalePrescriptions", x => x.SaleId);
                    table.ForeignKey(
                        name: "FK_SalePrescriptions_Sales_SaleId",
                        column: x => x.SaleId,
                        principalTable: "Sales",
                        principalColumn: "SaleId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SaleReturns",
                columns: table => new
                {
                    SaleReturnId = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReturnNumber = table.Column<string>(type: "text", nullable: false),
                    SaleId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: false),
                    RefundMethod = table.Column<string>(type: "text", nullable: false),
                    RefundReference = table.Column<string>(type: "text", nullable: true),
                    TotalAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    TaxAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SaleReturns", x => x.SaleReturnId);
                    table.ForeignKey(
                        name: "FK_SaleReturns_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "BusinessId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SaleReturns_Sales_SaleId",
                        column: x => x.SaleId,
                        principalTable: "Sales",
                        principalColumn: "SaleId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StockBatches",
                columns: table => new
                {
                    StockBatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    BatchNumber = table.Column<string>(type: "text", nullable: false),
                    ExpiryDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ManufactureDate = table.Column<DateOnly>(type: "date", nullable: true),
                    QuantityOnHand = table.Column<decimal>(type: "numeric", nullable: false),
                    UnitCost = table.Column<decimal>(type: "numeric", nullable: true),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: true),
                    PurchaseOrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    LastUpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockBatches", x => x.StockBatchId);
                    table.ForeignKey(
                        name: "FK_StockBatches_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "BusinessId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockBatches_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "ProductId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SupplierBalances",
                columns: table => new
                {
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: false),
                    OutstandingBalance = table.Column<decimal>(type: "numeric", nullable: false),
                    LastUpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplierBalances", x => x.SupplierId);
                    table.ForeignKey(
                        name: "FK_SupplierBalances_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "SupplierId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SupplierLedgerEntries",
                columns: table => new
                {
                    SupplierLedgerEntryId = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: false),
                    EntryType = table.Column<string>(type: "text", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric", nullable: false),
                    ReferenceType = table.Column<string>(type: "text", nullable: false),
                    ReferenceId = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierInvoiceNumber = table.Column<string>(type: "text", nullable: true),
                    PaymentMethod = table.Column<string>(type: "text", nullable: true),
                    PaymentReference = table.Column<string>(type: "text", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplierLedgerEntries", x => x.SupplierLedgerEntryId);
                    table.ForeignKey(
                        name: "FK_SupplierLedgerEntries_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "SupplierId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UserBusinesses",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    LastUpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastUpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserBusinesses", x => new { x.UserId, x.BusinessId });
                    table.ForeignKey(
                        name: "FK_UserBusinesses_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "BusinessId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserBusinesses_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SaleReturnLines",
                columns: table => new
                {
                    SaleReturnLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    SaleReturnId = table.Column<Guid>(type: "uuid", nullable: false),
                    SaleLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric", nullable: false),
                    QuantityInBaseUnits = table.Column<decimal>(type: "numeric", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric", nullable: false),
                    TaxAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    UnitCost = table.Column<decimal>(type: "numeric", nullable: true),
                    Restock = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SaleReturnLines", x => x.SaleReturnLineId);
                    table.ForeignKey(
                        name: "FK_SaleReturnLines_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "ProductId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SaleReturnLines_SaleLines_SaleLineId",
                        column: x => x.SaleLineId,
                        principalTable: "SaleLines",
                        principalColumn: "SaleLineId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SaleReturnLines_SaleReturns_SaleReturnId",
                        column: x => x.SaleReturnId,
                        principalTable: "SaleReturns",
                        principalColumn: "SaleReturnId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SaleLineBatchAllocations",
                columns: table => new
                {
                    SaleLineBatchAllocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    SaleLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    StockBatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    QuantityInBaseUnits = table.Column<decimal>(type: "numeric", nullable: false),
                    QuantityReturnedInBaseUnits = table.Column<decimal>(type: "numeric", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SaleLineBatchAllocations", x => x.SaleLineBatchAllocationId);
                    table.ForeignKey(
                        name: "FK_SaleLineBatchAllocations_SaleLines_SaleLineId",
                        column: x => x.SaleLineId,
                        principalTable: "SaleLines",
                        principalColumn: "SaleLineId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SaleLineBatchAllocations_StockBatches_StockBatchId",
                        column: x => x.StockBatchId,
                        principalTable: "StockBatches",
                        principalColumn: "StockBatchId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserSessions_SessionToken",
                table: "UserSessions",
                column: "SessionToken",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UnitsOfMeasure_BusinessId_Symbol",
                table: "UnitsOfMeasure",
                columns: new[] { "BusinessId", "Symbol" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_StockBatchId",
                table: "StockMovements",
                column: "StockBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_Sales_BusinessId_CreatedAt",
                table: "Sales",
                columns: new[] { "BusinessId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Sales_BusinessId_SaleNumber",
                table: "Sales",
                columns: new[] { "BusinessId", "SaleNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Roles_BusinessId_RoleName",
                table: "Roles",
                columns: new[] { "BusinessId", "RoleName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Roles_RoleName_Platform",
                table: "Roles",
                column: "RoleName",
                unique: true,
                filter: "\"BusinessId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrders_BusinessId_OrderNumber",
                table: "PurchaseOrders",
                columns: new[] { "BusinessId", "OrderNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Products_BusinessId_Barcode",
                table: "Products",
                columns: new[] { "BusinessId", "Barcode" },
                unique: true,
                filter: "\"Barcode\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Products_BusinessId_Sku",
                table: "Products",
                columns: new[] { "BusinessId", "Sku" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductionRuns_BusinessId_RunNumber",
                table: "ProductionRuns",
                columns: new[] { "BusinessId", "RunNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MpesaTransactions_BusinessId",
                table: "MpesaTransactions",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_Formulas_BusinessId",
                table: "Formulas",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_BusinessId_ExpenseNumber",
                table: "Expenses",
                columns: new[] { "BusinessId", "ExpenseNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseCategories_BusinessId_Code",
                table: "ExpenseCategories",
                columns: new[] { "BusinessId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseCategories_BusinessId_Name",
                table: "ExpenseCategories",
                columns: new[] { "BusinessId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Categories_BusinessId_Code",
                table: "Categories",
                columns: new[] { "BusinessId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Businesses_MpesaShortCode",
                table: "Businesses",
                column: "MpesaShortCode",
                unique: true,
                filter: "\"MpesaShortCode\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SaleLineBatchAllocations_SaleLineId",
                table: "SaleLineBatchAllocations",
                column: "SaleLineId");

            migrationBuilder.CreateIndex(
                name: "IX_SaleLineBatchAllocations_StockBatchId",
                table: "SaleLineBatchAllocations",
                column: "StockBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_SalePrescriptions_PrescriptionDate",
                table: "SalePrescriptions",
                column: "PrescriptionDate");

            migrationBuilder.CreateIndex(
                name: "IX_SaleReturnLines_ProductId",
                table: "SaleReturnLines",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_SaleReturnLines_SaleLineId",
                table: "SaleReturnLines",
                column: "SaleLineId");

            migrationBuilder.CreateIndex(
                name: "IX_SaleReturnLines_SaleReturnId",
                table: "SaleReturnLines",
                column: "SaleReturnId");

            migrationBuilder.CreateIndex(
                name: "IX_SaleReturns_BusinessId_ReturnNumber",
                table: "SaleReturns",
                columns: new[] { "BusinessId", "ReturnNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SaleReturns_SaleId",
                table: "SaleReturns",
                column: "SaleId");

            migrationBuilder.CreateIndex(
                name: "IX_StockBatches_BusinessId",
                table: "StockBatches",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_StockBatches_ProductId_BatchNumber",
                table: "StockBatches",
                columns: new[] { "ProductId", "BatchNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockBatches_ProductId_ExpiryDate",
                table: "StockBatches",
                columns: new[] { "ProductId", "ExpiryDate" });

            migrationBuilder.CreateIndex(
                name: "IX_SupplierLedgerEntries_SupplierId_CreatedAt",
                table: "SupplierLedgerEntries",
                columns: new[] { "SupplierId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_UserBusinesses_BusinessId",
                table: "UserBusinesses",
                column: "BusinessId");

            migrationBuilder.AddForeignKey(
                name: "FK_Categories_Businesses_BusinessId",
                table: "Categories",
                column: "BusinessId",
                principalTable: "Businesses",
                principalColumn: "BusinessId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ExpenseCategories_Businesses_BusinessId",
                table: "ExpenseCategories",
                column: "BusinessId",
                principalTable: "Businesses",
                principalColumn: "BusinessId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Expenses_Businesses_BusinessId",
                table: "Expenses",
                column: "BusinessId",
                principalTable: "Businesses",
                principalColumn: "BusinessId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Formulas_Businesses_BusinessId",
                table: "Formulas",
                column: "BusinessId",
                principalTable: "Businesses",
                principalColumn: "BusinessId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_MpesaTransactions_Businesses_BusinessId",
                table: "MpesaTransactions",
                column: "BusinessId",
                principalTable: "Businesses",
                principalColumn: "BusinessId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ProductionRuns_Businesses_BusinessId",
                table: "ProductionRuns",
                column: "BusinessId",
                principalTable: "Businesses",
                principalColumn: "BusinessId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Products_Businesses_BusinessId",
                table: "Products",
                column: "BusinessId",
                principalTable: "Businesses",
                principalColumn: "BusinessId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseOrders_Businesses_BusinessId",
                table: "PurchaseOrders",
                column: "BusinessId",
                principalTable: "Businesses",
                principalColumn: "BusinessId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Roles_Businesses_BusinessId",
                table: "Roles",
                column: "BusinessId",
                principalTable: "Businesses",
                principalColumn: "BusinessId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Sales_Businesses_BusinessId",
                table: "Sales",
                column: "BusinessId",
                principalTable: "Businesses",
                principalColumn: "BusinessId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_StockMovements_StockBatches_StockBatchId",
                table: "StockMovements",
                column: "StockBatchId",
                principalTable: "StockBatches",
                principalColumn: "StockBatchId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_UnitsOfMeasure_Businesses_BusinessId",
                table: "UnitsOfMeasure",
                column: "BusinessId",
                principalTable: "Businesses",
                principalColumn: "BusinessId",
                onDelete: ReferentialAction.Restrict);

            // Every existing user becomes a member of the business that now owns the existing data (the one their roles moved to).
            migrationBuilder.Sql(@"
                INSERT INTO ""UserBusinesses"" (""UserId"", ""BusinessId"", ""IsActive"", ""IsDefault"", ""CreatedAt"")
                SELECT u.""UserId"", b.""BusinessId"", TRUE, TRUE, now()
                FROM ""Users"" u
                CROSS JOIN (SELECT ""BusinessId"" FROM ""Businesses"" ORDER BY ""CreatedAt"", ""BusinessId"" LIMIT 1) b
                ON CONFLICT DO NOTHING;");

            // ---- Permissions and the Platform Admin role ----------------------------------------------------------------------
            // DbSeeder only runs in Development, so a database built from migrations gets its permissions here. Keep in step with
            // PermissionCatalog. Existing rows are left alone.
            migrationBuilder.Sql($@"
                INSERT INTO ""Permissions"" (""PermissionId"", ""Name"", ""Module"", ""IsActive"", ""CreatedAt"")
                SELECT gen_random_uuid(), v.name, v.module, TRUE, now()
                FROM (VALUES {AllPermissions}) AS v(name, module)
                ON CONFLICT (""Name"") DO NOTHING;");

            // The platform role: registers and manages businesses, whichever business its holders are signed in to.
            migrationBuilder.Sql(@"
                INSERT INTO ""Roles"" (""RoleId"", ""BusinessId"", ""RoleName"", ""Description"", ""IsActive"", ""CreatedAt"")
                SELECT gen_random_uuid(), NULL, 'Platform Admin', 'Registers and manages businesses.', TRUE, now()
                WHERE NOT EXISTS (SELECT 1 FROM ""Roles"" WHERE ""BusinessId"" IS NULL AND ""RoleName"" = 'Platform Admin');");

            migrationBuilder.Sql($@"
                INSERT INTO ""RolePermissions"" (""RoleId"", ""PermissionId"", ""IsActive"", ""CreatedAt"")
                SELECT r.""RoleId"", p.""PermissionId"", TRUE, now()
                FROM ""Roles"" r CROSS JOIN ""Permissions"" p
                WHERE r.""BusinessId"" IS NULL AND r.""RoleName"" = 'Platform Admin' AND p.""Name"" IN ({PlatformPermissionNames})
                ON CONFLICT (""RoleId"", ""PermissionId"") DO NOTHING;");

            // Whoever administered the single business keeps the power to register businesses.
            migrationBuilder.Sql(@"
                INSERT INTO ""UserRoles"" (""UserId"", ""RoleId"")
                SELECT DISTINCT ur.""UserId"", pa.""RoleId""
                FROM ""UserRoles"" ur
                JOIN ""Roles"" r ON r.""RoleId"" = ur.""RoleId"" AND r.""RoleName"" = 'Admin'
                CROSS JOIN ""Roles"" pa
                WHERE pa.""BusinessId"" IS NULL AND pa.""RoleName"" = 'Platform Admin'
                ON CONFLICT DO NOTHING;");

            // Platform permissions never sit on a business role; a business's Admin gets every new business permission.
            migrationBuilder.Sql($@"
                DELETE FROM ""RolePermissions"" rp
                USING ""Roles"" r, ""Permissions"" p
                WHERE rp.""RoleId"" = r.""RoleId"" AND rp.""PermissionId"" = p.""PermissionId""
                  AND r.""BusinessId"" IS NOT NULL AND p.""Name"" IN ({PlatformPermissionNames});

                INSERT INTO ""RolePermissions"" (""RoleId"", ""PermissionId"", ""IsActive"", ""CreatedAt"")
                SELECT r.""RoleId"", p.""PermissionId"", TRUE, now()
                FROM ""Roles"" r CROSS JOIN ""Permissions"" p
                WHERE r.""BusinessId"" IS NOT NULL AND r.""RoleName"" = 'Admin' AND p.""Name"" IN ({NewBusinessPermissionNames})
                ON CONFLICT (""RoleId"", ""PermissionId"") DO NOTHING;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Categories_Businesses_BusinessId",
                table: "Categories");

            migrationBuilder.DropForeignKey(
                name: "FK_ExpenseCategories_Businesses_BusinessId",
                table: "ExpenseCategories");

            migrationBuilder.DropForeignKey(
                name: "FK_Expenses_Businesses_BusinessId",
                table: "Expenses");

            migrationBuilder.DropForeignKey(
                name: "FK_Formulas_Businesses_BusinessId",
                table: "Formulas");

            migrationBuilder.DropForeignKey(
                name: "FK_MpesaTransactions_Businesses_BusinessId",
                table: "MpesaTransactions");

            migrationBuilder.DropForeignKey(
                name: "FK_ProductionRuns_Businesses_BusinessId",
                table: "ProductionRuns");

            migrationBuilder.DropForeignKey(
                name: "FK_Products_Businesses_BusinessId",
                table: "Products");

            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseOrders_Businesses_BusinessId",
                table: "PurchaseOrders");

            migrationBuilder.DropForeignKey(
                name: "FK_Roles_Businesses_BusinessId",
                table: "Roles");

            migrationBuilder.DropForeignKey(
                name: "FK_Sales_Businesses_BusinessId",
                table: "Sales");

            migrationBuilder.DropForeignKey(
                name: "FK_StockMovements_StockBatches_StockBatchId",
                table: "StockMovements");

            migrationBuilder.DropForeignKey(
                name: "FK_UnitsOfMeasure_Businesses_BusinessId",
                table: "UnitsOfMeasure");

            migrationBuilder.DropTable(
                name: "SaleLineBatchAllocations");

            migrationBuilder.DropTable(
                name: "SalePrescriptions");

            migrationBuilder.DropTable(
                name: "SaleReturnLines");

            migrationBuilder.DropTable(
                name: "SupplierBalances");

            migrationBuilder.DropTable(
                name: "SupplierLedgerEntries");

            migrationBuilder.DropTable(
                name: "UserBusinesses");

            migrationBuilder.DropTable(
                name: "StockBatches");

            migrationBuilder.DropTable(
                name: "SaleReturns");

            migrationBuilder.DropIndex(
                name: "IX_UserSessions_SessionToken",
                table: "UserSessions");

            migrationBuilder.DropIndex(
                name: "IX_UnitsOfMeasure_BusinessId_Symbol",
                table: "UnitsOfMeasure");

            migrationBuilder.DropIndex(
                name: "IX_StockMovements_StockBatchId",
                table: "StockMovements");

            migrationBuilder.DropPrimaryKey(
                name: "PK_SkuSequences",
                table: "SkuSequences");

            migrationBuilder.DropIndex(
                name: "IX_Sales_BusinessId_CreatedAt",
                table: "Sales");

            migrationBuilder.DropIndex(
                name: "IX_Sales_BusinessId_SaleNumber",
                table: "Sales");

            migrationBuilder.DropIndex(
                name: "IX_Roles_BusinessId_RoleName",
                table: "Roles");

            migrationBuilder.DropIndex(
                name: "IX_Roles_RoleName_Platform",
                table: "Roles");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseOrders_BusinessId_OrderNumber",
                table: "PurchaseOrders");

            migrationBuilder.DropIndex(
                name: "IX_Products_BusinessId_Barcode",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_Products_BusinessId_Sku",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_ProductionRuns_BusinessId_RunNumber",
                table: "ProductionRuns");

            migrationBuilder.DropIndex(
                name: "IX_MpesaTransactions_BusinessId",
                table: "MpesaTransactions");

            migrationBuilder.DropIndex(
                name: "IX_Formulas_BusinessId",
                table: "Formulas");

            migrationBuilder.DropIndex(
                name: "IX_Expenses_BusinessId_ExpenseNumber",
                table: "Expenses");

            migrationBuilder.DropIndex(
                name: "IX_ExpenseCategories_BusinessId_Code",
                table: "ExpenseCategories");

            migrationBuilder.DropIndex(
                name: "IX_ExpenseCategories_BusinessId_Name",
                table: "ExpenseCategories");

            migrationBuilder.DropIndex(
                name: "IX_Categories_BusinessId_Code",
                table: "Categories");

            migrationBuilder.DropIndex(
                name: "IX_Businesses_MpesaShortCode",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "BusinessId",
                table: "UserSessions");

            migrationBuilder.DropColumn(
                name: "RevokedAt",
                table: "UserSessions");

            migrationBuilder.DropColumn(
                name: "BusinessId",
                table: "UnitsOfMeasure");

            migrationBuilder.DropColumn(
                name: "StockBatchId",
                table: "StockMovements");

            migrationBuilder.DropColumn(
                name: "BusinessId",
                table: "SkuSequences");

            migrationBuilder.DropColumn(
                name: "BusinessId",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "DiscountTotal",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "ReturnedAmount",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "Subtotal",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "TaxTotal",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "DiscountAmount",
                table: "SaleLines");

            migrationBuilder.DropColumn(
                name: "QuantityReturned",
                table: "SaleLines");

            migrationBuilder.DropColumn(
                name: "TaxAmount",
                table: "SaleLines");

            migrationBuilder.DropColumn(
                name: "TaxRate",
                table: "SaleLines");

            migrationBuilder.DropColumn(
                name: "BusinessId",
                table: "Roles");

            migrationBuilder.DropColumn(
                name: "BusinessId",
                table: "PurchaseOrders");

            migrationBuilder.DropColumn(
                name: "Barcode",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "BusinessId",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "DosageForm",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "GenericName",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "IsBatchTracked",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "Manufacturer",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "RequiresPrescription",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "Strength",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "BusinessId",
                table: "ProductionRuns");

            migrationBuilder.DropColumn(
                name: "BusinessId",
                table: "MpesaTransactions");

            migrationBuilder.DropColumn(
                name: "BusinessId",
                table: "Formulas");

            migrationBuilder.DropColumn(
                name: "BusinessId",
                table: "Expenses");

            migrationBuilder.DropColumn(
                name: "BusinessId",
                table: "ExpenseCategories");

            migrationBuilder.DropColumn(
                name: "CreditLimit",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "PaymentMethod",
                table: "CustomerLedgerEntries");

            migrationBuilder.DropColumn(
                name: "PaymentReference",
                table: "CustomerLedgerEntries");

            migrationBuilder.DropColumn(
                name: "BusinessId",
                table: "Categories");

            migrationBuilder.DropColumn(
                name: "IsVatRegistered",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "MaxDiscountPercent",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "MpesaShortCode",
                table: "Businesses");

            migrationBuilder.AddPrimaryKey(
                name: "PK_SkuSequences",
                table: "SkuSequences",
                column: "Prefix");

            migrationBuilder.CreateIndex(
                name: "IX_UnitsOfMeasure_Symbol",
                table: "UnitsOfMeasure",
                column: "Symbol",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sales_SaleNumber",
                table: "Sales",
                column: "SaleNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Roles_RoleName",
                table: "Roles",
                column: "RoleName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Products_Sku",
                table: "Products",
                column: "Sku",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductionRuns_RunNumber",
                table: "ProductionRuns",
                column: "RunNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_ExpenseNumber",
                table: "Expenses",
                column: "ExpenseNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseCategories_Code",
                table: "ExpenseCategories",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseCategories_Name",
                table: "ExpenseCategories",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Categories_Code",
                table: "Categories",
                column: "Code",
                unique: true);
        }
    }
}
