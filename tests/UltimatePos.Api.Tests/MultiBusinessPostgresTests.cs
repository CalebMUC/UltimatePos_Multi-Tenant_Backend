using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using UltimatePos.Application;
using UltimatePos.Application.Accounting;
using UltimatePos.Application.Accounting.Dtos;
using UltimatePos.Application.Business;
using UltimatePos.Application.Business.Dtos;
using UltimatePos.Application.Catalog;
using UltimatePos.Application.Catalog.Dtos;
using UltimatePos.Application.Common.Interfaces;
using UltimatePos.Application.Identity;
using UltimatePos.Application.Identity.Dtos;
using UltimatePos.Application.Inventory;
using UltimatePos.Application.Purchasing;
using UltimatePos.Application.Purchasing.Dtos;
using UltimatePos.Application.Reports;
using UltimatePos.Application.Sales;
using UltimatePos.Application.Sales.Dtos;
using UltimatePos.Domain.Entities;
using UltimatePos.Domain.Exceptions;
using UltimatePos.Infrastructure;
using UltimatePos.Infrastructure.Persistence;
using static UltimatePos.Domain.Enums.PosEnums;

namespace Ultimate.Api.Tests;

/// <summary>
/// The caller, as the services see it. One instance for the whole fixture: each test says which business it is signed in to and
/// which permissions it holds, exactly as an access token would.
/// </summary>
public sealed class TestCaller : ICurrentUser
{
    public Guid? UserId { get; set; }
    public Guid? BusinessId { get; set; }
    public bool IsAuthenticated => UserId.HasValue;
    /// <summary>Null = every permission.</summary>
    public HashSet<string>? Permissions { get; set; }
    public bool HasPermission(string permission) => Permissions is null || Permissions.Contains(permission);
}

/// <summary>
/// Two businesses on one database: Alvet, the original manufacturer (wholesaler), and Afya, a VAT-registered pharmacy. Every test
/// works in its own products and customers, so the tests can run in any order.
/// </summary>
public sealed class MultiBusinessDatabase : IAsyncLifetime
{
    public ServiceProvider Services { get; private set; } = null!;
    public TestCaller Caller { get; } = new();
    /// <summary>The business owner: Admin of both businesses (a plain account, given them at registration).</summary>
    public Guid AdminUserId { get; private set; }
    /// <summary>The super user who registered them. Belongs to no business.</summary>
    public Guid SuperUserId { get; private set; }
    public Guid Alvet { get; private set; }
    public Guid Afya { get; private set; }

    public async Task InitializeAsync()
    {
        var server = Environment.GetEnvironmentVariable(PostgresFactAttribute.ConnectionStringVariable);
        if (string.IsNullOrWhiteSpace(server))
            return;

        var connectionString = new NpgsqlConnectionStringBuilder(server) { Database = $"ultimatepos_tenancy_{Guid.NewGuid():N}" }.ConnectionString;
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = connectionString,
                ["Mpesa:BaseUrl"] = "https://localhost",
                ["JwtSettings:Key"] = "test-signing-key-that-is-long-enough-for-hs256-0123456789",
                ["JwtSettings:Issuer"] = "test",
                ["JwtSettings:Audience"] = "test",
                ["Notifications:EmailEnabled"] = "true",
                ["Notifications:SmsEnabled"] = "true",
                ["Notifications:FrontendBaseUrl"] = "https://pos.test",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplication();
        services.AddInfrastructure(configuration);
        services.RemoveAll<ICurrentUser>();
        services.AddSingleton<ICurrentUser>(Caller);
        Services = services.BuildServiceProvider();

        using (var scope = Services.CreateScope())
            await DbSeeder.SeedAsync(scope.ServiceProvider);

        using (var scope = Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UltimatePosDbContext>().Users;
            AdminUserId = (await users.SingleAsync(u => u.Email == DbSeeder.DevAdminEmail)).UserId;
            SuperUserId = (await users.SingleAsync(u => u.Email == DbSeeder.DevSuperUserEmail)).UserId;
        }

        // The super user onboards both businesses, signed in to none, and hands each to the same owner.
        Caller.UserId = SuperUserId;
        Caller.BusinessId = null;
        var owner = new UltimatePos.Application.Platform.BusinessOwnerRequestDto("admin", DbSeeder.DevAdminEmail, null);
        Alvet = (await RunAsync(sp => sp.GetRequiredService<UltimatePos.Application.Platform.PlatformService>().RegisterBusinessAsync(new RegisterBusinessRequestDto(
            "Alvet Agrochemicals", null, BusinessType.Wholesaler, BusinessKind.Manufacturer, null, "P051000001A", null, null, null, null, null, null,
            Owner: owner)))).Business.BusinessId;
        Afya = (await RunAsync(sp => sp.GetRequiredService<UltimatePos.Application.Platform.PlatformService>().RegisterBusinessAsync(new RegisterBusinessRequestDto(
            "Afya Pharmacy", null, BusinessType.Retailer, BusinessKind.Trader, null, "P051000002B", null, null, null, null, null, null,
            IsVatRegistered: true, MpesaShortCode: "600999", MaxDiscountPercent: 10, Owner: owner)))).Business.BusinessId;
    }

    public async Task DisposeAsync()
    {
        if (Services is null)
            return;
        if (Environment.GetEnvironmentVariable("ULTIMATEPOS_TEST_POSTGRES_KEEP") != "1")
        {
            using var scope = Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<UltimatePosDbContext>().Database.EnsureDeletedAsync();
        }
        await Services.DisposeAsync();
    }

    /// <summary>Signs the caller in to <paramref name="businessId"/> with every permission, or only <paramref name="permissions"/>.</summary>
    public void SignInTo(Guid businessId, params string[] permissions)
    {
        Caller.UserId = AdminUserId;
        Caller.BusinessId = businessId;
        Caller.Permissions = permissions.Length == 0 ? null : permissions.ToHashSet();
    }

    /// <summary>One request: a fresh scope, so a fresh DbContext and tenant context.</summary>
    public async Task<T> RunAsync<T>(Func<IServiceProvider, Task<T>> call)
    {
        using var scope = Services.CreateScope();
        return await call(scope.ServiceProvider);
    }

    public Task RunAsync(Func<IServiceProvider, Task> call) => RunAsync<object?>(async sp => { await call(sp); return null; });
}

public class MultiBusinessPostgresTests : IClassFixture<MultiBusinessDatabase>
{
    private readonly MultiBusinessDatabase _db;
    public MultiBusinessPostgresTests(MultiBusinessDatabase db) => _db = db;

    private static readonly DateOnly Today = ReportPeriod.TodayAt(ReportPeriod.DefaultUtcOffsetMinutes);

    // ---- Small builders, run as the business signed in to ----

    private Task<Guid> UnitAsync(string symbol) =>
        _db.RunAsync(async sp => (await sp.GetRequiredService<CatalogService>().GetUnitsOfMeasureAsync()).Single(u => u.Symbol == symbol).UnitOfMeasureId);

    private async Task<ProductDto> ProductAsync(string name, decimal retailPrice, TaxClassification tax = TaxClassification.Exempt,
        bool batchTracked = false, bool prescription = false, string? barcode = null)
    {
        var pcs = await UnitAsync("pcs");
        var code = $"C{Guid.NewGuid():N}"[..8].ToUpperInvariant();
        var category = await _db.RunAsync(sp => sp.GetRequiredService<CatalogService>().CreateCategoryAsync(new CreateCategoryRequestDto("Medicines " + code, code, null)));
        var product = await _db.RunAsync(sp => sp.GetRequiredService<CatalogService>().CreateProductAsync(new CreateProductRequestDto(
            name, null, category.CategoryId, ItemType.FinishedGood, tax, pcs, 10, barcode, batchTracked, prescription, GenericName: name + " (generic)")));
        await _db.RunAsync(sp => sp.GetRequiredService<CatalogService>().AddPriceTierAsync(product.ProductId,
            new AddPriceTierRequestDto(pcs, PriceType.Retail, retailPrice, DateTime.UtcNow.AddMinutes(-1))));
        return product;
    }

    private async Task<(SupplierDto Supplier, PurchaseOrderDto Order)> OrderAsync(Guid productId, decimal quantity, decimal unitCost)
    {
        var pcs = await UnitAsync("pcs");
        var supplier = await _db.RunAsync(sp => sp.GetRequiredService<PurchasingService>().RegisterSupplierAsync(
            new CreateSupplierRequestDto(null, "Supplier " + Guid.NewGuid().ToString("N")[..6], null, null, null, null, null, 30)));
        var order = await _db.RunAsync(sp => sp.GetRequiredService<PurchasingService>().CreatePurchaseOrderAsync(
            new CreatePurchaseOrderRequestDto(supplier.SupplierId, null, null, new[] { new CreatePurchaseOrderLineRequestDto(productId, pcs, quantity, unitCost) })));
        return (supplier, order);
    }

    private static SalePrescriptionRequestDto Prescription() =>
        new("RX-1001", "Dr. A. Otieno", "KMPDC-12345", "Kenyatta Hospital", "Jane Wambui", "0712345678", 34, Today, null);

    private async Task<SaleDto> CashSaleAsync(Guid productId, decimal quantity, decimal tendered, SalePrescriptionRequestDto? prescription = null,
        decimal? discountPercent = null)
    {
        var pcs = await UnitAsync("pcs");
        return await _db.RunAsync(sp => sp.GetRequiredService<SalesService>().RecordSaleAsync(new CreateSaleRequestDto(
            null, PaymentMethod.Cash, null, new[] { new CreateSaleLineRequestDto(productId, pcs, quantity, DiscountPercent: discountPercent) },
            tendered, null, null, Prescription: prescription)));
    }

    private Task<Dictionary<string, decimal>> BatchQuantitiesAsync(Guid productId) =>
        _db.RunAsync(async sp => (await sp.GetRequiredService<InventoryService>().GetBatchesAsync(productId, null, includeExpired: true, includeEmpty: true))
            .ToDictionary(b => b.BatchNumber, b => b.QuantityOnHand));

    // ---------------------------------------------------------------- Onboarding

    [PostgresFact]
    public async Task Registering_a_business_gives_it_an_Admin_role_defaults_and_its_creator_as_member()
    {
        _db.SignInTo(_db.Afya);
        var roles = await _db.RunAsync(sp => sp.GetRequiredService<AuthService>().GetRolesAsync());
        var admin = Assert.Single(roles);
        Assert.Equal("Admin", admin.RoleName);
        Assert.Contains("Prescriptions.Dispense", admin.Permissions);
        Assert.DoesNotContain("Businesses.Register", admin.Permissions);

        var units = await _db.RunAsync(sp => sp.GetRequiredService<CatalogService>().GetUnitsOfMeasureAsync());
        Assert.Contains(units, u => u.Symbol == "pcs");

        var users = await _db.RunAsync(sp => sp.GetRequiredService<AuthService>().GetUsersAsync());
        Assert.Contains(users, u => u.Id == _db.AdminUserId && u.Roles.Contains("Admin"));
    }

    // ---------------------------------------------------------------- Isolation

    [PostgresFact]
    public async Task A_business_never_sees_or_changes_another_businesss_records()
    {
        _db.SignInTo(_db.Afya);
        var product = await ProductAsync("Paracetamol 500mg", 5);
        var customer = await _db.RunAsync(sp => sp.GetRequiredService<BusinessService>().RegisterCustomerAsync(
            new RegisterCustomerRequestDto(null, "Afya walk-up account", null, null, null, null, null)));

        _db.SignInTo(_db.Alvet);
        var alvetProducts = await _db.RunAsync(sp => sp.GetRequiredService<CatalogService>().GetProductsAsync(1, 200, new ProductQuery()));
        Assert.DoesNotContain(alvetProducts.Items, p => p.ProductId == product.ProductId);
        Assert.DoesNotContain(await _db.RunAsync(sp => sp.GetRequiredService<BusinessService>().GetCustomersAsync()), c => c.CustomerId == customer.CustomerId);

        await Assert.ThrowsAsync<NotFoundException>(() => _db.RunAsync(sp => sp.GetRequiredService<CatalogService>().GetProductByIdAsync(product.ProductId)));
        await Assert.ThrowsAsync<NotFoundException>(() => _db.RunAsync(sp => sp.GetRequiredService<CatalogService>().SetProductActiveStatusAsync(product.ProductId, false)));
        await Assert.ThrowsAsync<NotFoundException>(() => _db.RunAsync(sp => sp.GetRequiredService<BusinessService>().UpdateCustomerAsync(customer.CustomerId,
            new UpdateCustomerRequestDto("Hijacked", null, null, null, null, null))));
        await Assert.ThrowsAsync<ForbiddenException>(() => _db.RunAsync(sp => sp.GetRequiredService<BusinessService>().GetCustomersAsync(_db.Afya)));
        await Assert.ThrowsAsync<ForbiddenException>(() => _db.RunAsync(sp => sp.GetRequiredService<BusinessService>().UpdateBusinessAsync(_db.Afya,
            new UpdateBusinessRequestDto("Hijacked", null, BusinessType.Retailer, null, "P051000002B", null, null, null, null, null, null))));

        // Document numbers count per business: the first product in each business starts at 000001 within its own prefix.
        _db.SignInTo(_db.Afya);
        var stillThere = await _db.RunAsync(sp => sp.GetRequiredService<CatalogService>().GetProductByIdAsync(product.ProductId));
        Assert.True(stillThere.IsActive);
        Assert.EndsWith("-000001", stillThere.Sku);
    }

    [PostgresFact]
    public async Task With_no_business_selected_business_data_can_be_neither_read_nor_written()
    {
        _db.Caller.UserId = _db.AdminUserId;
        _db.Caller.BusinessId = null;
        await Assert.ThrowsAsync<ForbiddenException>(() => _db.RunAsync(sp => sp.GetRequiredService<BusinessService>().GetCustomersAsync()));
        await Assert.ThrowsAsync<ForbiddenException>(() => _db.RunAsync(sp => sp.GetRequiredService<CatalogService>().CreateCategoryAsync(
            new CreateCategoryRequestDto("Orphan", "ORPH", null))));
        var none = await _db.RunAsync(sp => sp.GetRequiredService<CatalogService>().GetProductsAsync(1, 50, new ProductQuery()));
        Assert.Empty(none.Items);
    }

    // ---------------------------------------------------------------- The pharmacy day

    [PostgresFact]
    public async Task Medicines_are_received_by_batch_sold_first_expiry_first_and_returned_into_their_batches()
    {
        _db.SignInTo(_db.Afya);
        var amoxil = await ProductAsync("Amoxicillin 500mg", 20, TaxClassification.Exempt, batchTracked: true, prescription: true, barcode: "6161100000017");
        var (supplier, order) = await OrderAsync(amoxil.ProductId, 100, 10);

        // Three lots: one already expired, one soon, one later.
        await _db.RunAsync(sp => sp.GetRequiredService<PurchasingService>().ReceivePurchaseOrderAsync(order.PurchaseOrderId, new ReceivePurchaseOrderRequestDto(new[]
        {
            new ReceiveLineRequestDto(order.Lines.Single().PurchaseOrderLineId, 30, "LOT-OLD", Today.AddDays(-1)),
            new ReceiveLineRequestDto(order.Lines.Single().PurchaseOrderLineId, 40, "LOT-SOON", Today.AddDays(60)),
            new ReceiveLineRequestDto(order.Lines.Single().PurchaseOrderLineId, 30, "LOT-LATER", Today.AddDays(300)),
        }, null, "INV-778")));

        var level = await _db.RunAsync(sp => sp.GetRequiredService<InventoryService>().GetStockLevelAsync(amoxil.ProductId));
        Assert.Equal((100m, 70m), (level.QuantityOnHand, level.SellableQuantity!.Value)); // the expired lot cannot be sold

        // The scanner finds it by barcode.
        var scanned = await _db.RunAsync(sp => sp.GetRequiredService<CatalogService>().GetProductByCodeAsync("6161100000017"));
        Assert.Equal(amoxil.ProductId, scanned.ProductId);

        // Prescription-only: refused without one.
        await Assert.ThrowsAsync<PrescriptionRequiredException>(() => CashSaleAsync(amoxil.ProductId, 50, 1000));
        // ...and refused to someone not allowed to dispense.
        _db.SignInTo(_db.Afya, "Sales.Create", "Sales.View");
        await Assert.ThrowsAsync<ForbiddenException>(() => CashSaleAsync(amoxil.ProductId, 50, 1000, Prescription()));
        _db.SignInTo(_db.Afya);

        var sale = await CashSaleAsync(amoxil.ProductId, 50, 1000, Prescription());
        Assert.Equal((SaleStatus.Completed, 1000m, 0m), (sale.Status, sale.TotalAmount, sale.TaxTotal)); // exempt: no VAT
        Assert.NotNull(sale.Prescription);
        var allocated = sale.Lines.Single().Batches!.ToDictionary(b => b.BatchNumber, b => b.QuantityInBaseUnits);
        Assert.Equal(new Dictionary<string, decimal> { ["LOT-SOON"] = 40, ["LOT-LATER"] = 10 }, allocated);
        Assert.Equal(new Dictionary<string, decimal> { ["LOT-OLD"] = 30, ["LOT-SOON"] = 0, ["LOT-LATER"] = 20 }, await BatchQuantitiesAsync(amoxil.ProductId));

        // More than the unexpired lots hold is refused, even though the shelf total (50) would cover it.
        await Assert.ThrowsAsync<InsufficientStockException>(() => CashSaleAsync(amoxil.ProductId, 25, 1000, Prescription()));

        // Return 15: back into the lots they came from, latest expiry first (10 to LOT-LATER, 5 to LOT-SOON).
        var returned = await _db.RunAsync(sp => sp.GetRequiredService<SalesService>().CreateReturnAsync(sale.SaleId, new CreateSaleReturnRequestDto(
            "Patient switched medicine", RefundMethod.Cash, null, new[] { new CreateSaleReturnLineRequestDto(sale.Lines.Single().SaleLineId, 15) })));
        Assert.Equal(300m, returned.TotalAmount);
        Assert.Matches("^RET-\\d{6}$", returned.ReturnNumber);
        Assert.Equal(new Dictionary<string, decimal> { ["LOT-OLD"] = 30, ["LOT-SOON"] = 5, ["LOT-LATER"] = 30 }, await BatchQuantitiesAsync(amoxil.ProductId));

        var afterReturn = await _db.RunAsync(sp => sp.GetRequiredService<SalesService>().GetSaleByIdAsync(sale.SaleId));
        Assert.Equal((300m, 15m), (afterReturn.ReturnedAmount, afterReturn.Lines.Single().QuantityReturned));
        await Assert.ThrowsAsync<InvalidAssignmentException>(() => _db.RunAsync(sp => sp.GetRequiredService<SalesService>().VoidSaleAsync(sale.SaleId)));
        await Assert.ThrowsAsync<ValidationException>(() => _db.RunAsync(sp => sp.GetRequiredService<SalesService>().CreateReturnAsync(sale.SaleId,
            new CreateSaleReturnRequestDto("Too many", RefundMethod.Cash, null, new[] { new CreateSaleReturnLineRequestDto(sale.Lines.Single().SaleLineId, 36) }))));

        // The prescription book has the sale, with the batches dispensed.
        var register = await _db.RunAsync(sp => sp.GetRequiredService<SalesService>().GetPrescriptionRegisterAsync(Today, Today, 180, "Wambui"));
        var entry = Assert.Single(register, r => r.SaleId == sale.SaleId);
        Assert.Equal(new[] { "LOT-LATER", "LOT-SOON" }, entry.Lines.Single().BatchNumbers.OrderBy(b => b));

        // The expired lot shows on the expiry report; writing it off empties it.
        var expiry = await _db.RunAsync(sp => sp.GetRequiredService<InventoryService>().GetExpiryReportAsync(90));
        var oldLot = Assert.Single(expiry.Items, i => i.BatchNumber == "LOT-OLD");
        Assert.Equal(("Expired", 30m), (oldLot.ExpiryStatus, oldLot.QuantityOnHand));
        var pcs = await UnitAsync("pcs");
        await _db.RunAsync(sp => sp.GetRequiredService<InventoryService>().AdjustStockAsync(new UltimatePos.Application.Inventory.Dtos.CreateStockAdjustmentRequestDto(
            amoxil.ProductId, pcs, -30, "Expired — destroyed", StockBatchId: oldLot.StockBatchId)));
        var afterWriteOff = await _db.RunAsync(sp => sp.GetRequiredService<InventoryService>().GetStockLevelAsync(amoxil.ProductId));
        Assert.Equal((35m, 35m), (afterWriteOff.QuantityOnHand, afterWriteOff.SellableQuantity!.Value));

        // Receiving put the goods on the supplier's account; a payment brings it down.
        var owed = await _db.RunAsync(sp => sp.GetRequiredService<AccountingService>().GetSupplierBalanceAsync(supplier.SupplierId));
        Assert.Equal(1000m, owed.OutstandingBalance);
        await Assert.ThrowsAsync<ValidationException>(() => _db.RunAsync(sp => sp.GetRequiredService<AccountingService>().RecordSupplierPaymentAsync(
            supplier.SupplierId, new RecordSupplierPaymentRequestDto(400, SettlementMethod.Mpesa, null, null))));
        var paid = await _db.RunAsync(sp => sp.GetRequiredService<AccountingService>().RecordSupplierPaymentAsync(
            supplier.SupplierId, new RecordSupplierPaymentRequestDto(400, SettlementMethod.Mpesa, "SJK7Q2X9AB", null)));
        Assert.Equal(600m, paid.OutstandingBalance);

        // None of it is visible from the other business.
        _db.SignInTo(_db.Alvet);
        Assert.Empty(await _db.RunAsync(sp => sp.GetRequiredService<InventoryService>().GetBatchesAsync(null, null, true, true)));
        await Assert.ThrowsAsync<NotFoundException>(() => _db.RunAsync(sp => sp.GetRequiredService<SalesService>().GetSaleByIdAsync(sale.SaleId)));
        await Assert.ThrowsAsync<NotFoundException>(() => _db.RunAsync(sp => sp.GetRequiredService<AccountingService>().GetSupplierBalanceAsync(supplier.SupplierId)));
        Assert.Empty(await _db.RunAsync(sp => sp.GetRequiredService<SalesService>().GetPrescriptionRegisterAsync(Today, Today, 180, null)));
    }

    [PostgresFact]
    public async Task VAT_is_shown_on_standard_rated_lines_and_discounts_are_permission_gated_and_capped()
    {
        _db.SignInTo(_db.Afya);
        var vitamins = await ProductAsync("Vitamin C 1000mg", 116, TaxClassification.Standard);
        await _db.RunAsync(sp => sp.GetRequiredService<InventoryService>().AdjustStockAsync(new UltimatePos.Application.Inventory.Dtos.CreateStockAdjustmentRequestDto(
            vitamins.ProductId, vitamins.BaseUnitOfMeasureId, 10, "Opening stock", 60)));

        // 10% off 116 = 104.40, of which VAT is 104.40 x 16/116 = 14.40.
        var sale = await CashSaleAsync(vitamins.ProductId, 1, 200, discountPercent: 10);
        var line = sale.Lines.Single();
        Assert.Equal((116m, 11.60m, 104.40m, 14.40m), (sale.Subtotal, sale.DiscountTotal, sale.TotalAmount, sale.TaxTotal));
        Assert.Equal((16m, 14.40m), (line.TaxRate, line.TaxAmount));
        Assert.Equal(95.60m, sale.ChangeDue);

        await Assert.ThrowsAsync<ValidationException>(() => CashSaleAsync(vitamins.ProductId, 1, 200, discountPercent: 20)); // over the 10% cap
        _db.SignInTo(_db.Afya, "Sales.Create", "Sales.View");
        await Assert.ThrowsAsync<ForbiddenException>(() => CashSaleAsync(vitamins.ProductId, 1, 200, discountPercent: 5));

        // Not VAT-registered (Alvet): the same kind of product carries no VAT.
        _db.SignInTo(_db.Alvet);
        var feed = await ProductAsync("Layers Mash 70kg", 116, TaxClassification.Standard);
        var pcs = await UnitAsync("pcs");
        await _db.RunAsync(sp => sp.GetRequiredService<InventoryService>().AdjustStockAsync(new UltimatePos.Application.Inventory.Dtos.CreateStockAdjustmentRequestDto(
            feed.ProductId, pcs, 5, "Opening stock", 80)));
        var alvetSale = await CashSaleAsync(feed.ProductId, 1, 116);
        Assert.Equal(0m, alvetSale.TaxTotal);
    }

    [PostgresFact]
    public async Task Credit_sales_stop_at_the_credit_limit_unless_overridden_and_credit_notes_reduce_the_balance()
    {
        _db.SignInTo(_db.Afya);
        var product = await ProductAsync("ORS sachets", 50);
        var pcs = await UnitAsync("pcs");
        await _db.RunAsync(sp => sp.GetRequiredService<InventoryService>().AdjustStockAsync(new UltimatePos.Application.Inventory.Dtos.CreateStockAdjustmentRequestDto(
            product.ProductId, pcs, 100, "Opening stock", 20)));
        var clinic = await _db.RunAsync(sp => sp.GetRequiredService<BusinessService>().RegisterCustomerAsync(
            new RegisterCustomerRequestDto(null, "Mji Clinic", null, null, null, null, null, CreditLimit: 1000)));
        Assert.Equal(PriceType.Retail, clinic.PriceType); // a retailer's customers default to retail prices

        CreateSaleRequestDto Credit(decimal qty, bool overrideLimit = false) => new(clinic.CustomerId, PaymentMethod.Credit, null,
            new[] { new CreateSaleLineRequestDto(product.ProductId, pcs, qty) }, null, null, null, OverrideCreditLimit: overrideLimit);

        var first = await _db.RunAsync(sp => sp.GetRequiredService<SalesService>().RecordSaleAsync(Credit(16)));      // 800
        await Assert.ThrowsAsync<CreditLimitExceededException>(() => _db.RunAsync(sp => sp.GetRequiredService<SalesService>().RecordSaleAsync(Credit(10)))); // 1300
        var preview = await _db.RunAsync(sp => sp.GetRequiredService<SalesService>().PreviewSaleAsync(Credit(10)));
        Assert.True(preview.CreditLimitExceeded);

        _db.SignInTo(_db.Afya, "Sales.Create", "Sales.View");
        await Assert.ThrowsAsync<ForbiddenException>(() => _db.RunAsync(sp => sp.GetRequiredService<SalesService>().RecordSaleAsync(Credit(10, overrideLimit: true))));
        _db.SignInTo(_db.Afya);
        await _db.RunAsync(sp => sp.GetRequiredService<SalesService>().RecordSaleAsync(Credit(10, overrideLimit: true)));

        // A credit sale is refunded as a credit note, never cash.
        await Assert.ThrowsAsync<ValidationException>(() => _db.RunAsync(sp => sp.GetRequiredService<SalesService>().CreateReturnAsync(first.SaleId,
            new CreateSaleReturnRequestDto("Damaged", RefundMethod.Cash, null, new[] { new CreateSaleReturnLineRequestDto(first.Lines.Single().SaleLineId, 2) }))));
        await _db.RunAsync(sp => sp.GetRequiredService<SalesService>().CreateReturnAsync(first.SaleId,
            new CreateSaleReturnRequestDto("Damaged", RefundMethod.CreditNote, null, new[] { new CreateSaleReturnLineRequestDto(first.Lines.Single().SaleLineId, 2, Restock: false) })));

        var balance = await _db.RunAsync(sp => sp.GetRequiredService<AccountingService>().GetCustomerBalanceAsync(clinic.CustomerId));
        Assert.Equal((1200m, 1000m, 0m), (balance.OutstandingBalance, balance.CreditLimit!.Value, balance.AvailableCredit!.Value)); // 800 + 500 - 100

        await _db.RunAsync(sp => sp.GetRequiredService<AccountingService>().RecordCustomerPaymentAsync(clinic.CustomerId,
            new RecordCustomerPaymentRequestDto(700, "Paid at counter", SettlementMethod.Mpesa, "SJK0000001")));
        var ledger = await _db.RunAsync(sp => sp.GetRequiredService<AccountingService>().GetCustomerLedgerAsync(clinic.CustomerId));
        Assert.Contains(ledger, e => e.EntryType == CustomerLedgerEntryType.Return && e.Amount == 100m);
        Assert.Contains(ledger, e => e.EntryType == CustomerLedgerEntryType.Payment && e.PaymentReference == "SJK0000001");
    }

    // ---------------------------------------------------------------- M-Pesa routing

    [PostgresFact]
    public async Task A_till_payment_is_routed_by_short_code_to_its_business_and_a_retry_is_ignored()
    {
        // Safaricom calls with nobody signed in.
        _db.Caller.UserId = null;
        _db.Caller.BusinessId = null;
        var receipt = "SMK" + Guid.NewGuid().ToString("N")[..7].ToUpperInvariant();
        var payment = new UltimatePos.Application.Payments.Dtos.C2bConfirmationPayload(receipt, "20261017100000", "150.00", "600999", null, "254712345678", "JANE");
        await _db.RunAsync(sp => sp.GetRequiredService<UltimatePos.Application.Payments.MpesaService>().HandleC2bConfirmationAsync(payment));
        await _db.RunAsync(sp => sp.GetRequiredService<UltimatePos.Application.Payments.MpesaService>().HandleC2bConfirmationAsync(payment)); // retry

        _db.SignInTo(_db.Afya);
        var afya = await _db.RunAsync(sp => sp.GetRequiredService<UltimatePos.Application.Payments.MpesaService>().GetUnmatchedTillTransactionsAsync());
        Assert.Single(afya, t => t.MpesaReceiptNumber == receipt && t.Amount == 150m);

        _db.SignInTo(_db.Alvet);
        var alvet = await _db.RunAsync(sp => sp.GetRequiredService<UltimatePos.Application.Payments.MpesaService>().GetUnmatchedTillTransactionsAsync());
        Assert.DoesNotContain(alvet, t => t.MpesaReceiptNumber == receipt);
    }

    // ---------------------------------------------------------------- Sign-in

    [PostgresFact]
    public async Task Sign_in_is_to_a_business_refresh_tokens_rotate_and_non_members_are_turned_away()
    {
        _db.SignInTo(_db.Afya);
        var email = $"pharmacist-{Guid.NewGuid():N}@afya.test";
        var pharmacist = await _db.RunAsync(sp => sp.GetRequiredService<AuthService>().RegisterAsync(
            new RegisterRequest(email, "Dawa2026!", "Grace Pharmacist", "0700000001", "Admin")));

        _db.Caller.UserId = null;
        _db.Caller.BusinessId = null;
        var login = await _db.RunAsync(sp => sp.GetRequiredService<AuthService>().LoginAsync(new LoginRequest(email, "Dawa2026!")));
        Assert.Equal(_db.Afya, login.ActiveBusinessId);
        Assert.Single(login.Businesses);
        Assert.Contains("Prescriptions.Dispense", login.User.Permissions);
        Assert.DoesNotContain("Businesses.Register", login.User.Permissions);

        await Assert.ThrowsAsync<ForbiddenException>(() => _db.RunAsync(sp => sp.GetRequiredService<AuthService>().LoginAsync(new LoginRequest(email, "Dawa2026!", _db.Alvet))));

        var refreshed = await _db.RunAsync(sp => sp.GetRequiredService<AuthService>().RefreshAsync(new RefreshTokenRequest(login.RefreshToken)));
        Assert.Equal(_db.Afya, refreshed.ActiveBusinessId);
        await Assert.ThrowsAsync<InvalidTokenException>(() => _db.RunAsync(sp => sp.GetRequiredService<AuthService>().RefreshAsync(new RefreshTokenRequest(login.RefreshToken))));

        // Deactivating the member in Afya ends their access there.
        _db.SignInTo(_db.Afya);
        await _db.RunAsync(sp => sp.GetRequiredService<AuthService>().SetUserActiveStatusAsync(pharmacist.Id, false));
        _db.Caller.UserId = null;
        _db.Caller.BusinessId = null;
        await Assert.ThrowsAsync<InvalidTokenException>(() => _db.RunAsync(sp => sp.GetRequiredService<AuthService>().RefreshAsync(new RefreshTokenRequest(refreshed.RefreshToken))));
        await Assert.ThrowsAsync<ForbiddenException>(() => _db.RunAsync(sp => sp.GetRequiredService<AuthService>().LoginAsync(new LoginRequest(email, "Dawa2026!"))));

        // The owner belongs to both businesses and can switch between them, but has no platform powers.
        var admin = await _db.RunAsync(sp => sp.GetRequiredService<AuthService>().LoginAsync(new LoginRequest(DbSeeder.DevAdminEmail, DbSeeder.DevAdminPassword, _db.Afya)));
        Assert.Equal(2, admin.Businesses.Count());
        Assert.DoesNotContain("Businesses.Register", admin.User.Permissions);

        // The super user signs in to no business and holds only platform permissions.
        var super = await _db.RunAsync(sp => sp.GetRequiredService<AuthService>().LoginAsync(new LoginRequest(DbSeeder.DevSuperUserEmail, DbSeeder.DevSuperUserPassword)));
        Assert.Null(super.ActiveBusinessId);
        Assert.Empty(super.Businesses);
        Assert.Contains("Businesses.Register", super.User.Permissions);
        Assert.Contains("Licenses.Manage", super.User.Permissions);
        Assert.DoesNotContain("Sales.Create", super.User.Permissions);
    }
}
