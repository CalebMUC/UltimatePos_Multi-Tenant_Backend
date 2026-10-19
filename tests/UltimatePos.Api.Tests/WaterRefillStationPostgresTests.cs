using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UltimatePos.Application.Accounting;
using UltimatePos.Application.Business;
using UltimatePos.Application.Business.Dtos;
using UltimatePos.Application.Catalog;
using UltimatePos.Application.Catalog.Dtos;
using UltimatePos.Application.Expenses;
using UltimatePos.Application.Expenses.Dtos;
using UltimatePos.Application.Inventory;
using UltimatePos.Application.Inventory.Dtos;
using UltimatePos.Application.Payments;
using UltimatePos.Application.Payments.Dtos;
using UltimatePos.Application.Platform;
using UltimatePos.Application.Purchasing;
using UltimatePos.Application.Purchasing.Dtos;
using UltimatePos.Application.Reports;
using UltimatePos.Application.Sales;
using UltimatePos.Application.Sales.Dtos;
using UltimatePos.Domain.Entities;
using UltimatePos.Domain.Exceptions;
using UltimatePos.Infrastructure.Persistence;
using static UltimatePos.Domain.Enums.PosEnums;

namespace Ultimate.Api.Tests;

/// <summary>
/// A water refilling station, onboarded the way docs/WATER_REFILL_STATION_ONBOARDING.md describes: a Trader that does not allow
/// overselling, selling one litre-based water product in 1L, 10L and 20L refills, so every size draws on one balance in litres.
/// Also covers the concurrency guards that keep a delivery, a payment confirmation, a void or a till payment from counting twice.
/// Prices and quantities here are test data, not the station's.
/// </summary>
public class WaterRefillStationPostgresTests : IClassFixture<MultiBusinessDatabase>
{
    private const string StationKraPin = "P051000003C";
    private static readonly DateOnly Today = ReportPeriod.TodayAt(ReportPeriod.DefaultUtcOffsetMinutes);

    private readonly MultiBusinessDatabase _db;
    public WaterRefillStationPostgresTests(MultiBusinessDatabase db) => _db = db;

    private Task<T> Run<T>(Func<IServiceProvider, Task<T>> call) => _db.RunAsync(call);
    private Task Run(Func<IServiceProvider, Task> call) => _db.RunAsync(call);

    /// <summary>The water product of one test, with its three selling units. Each test gets its own, so its stock starts at zero.</summary>
    private sealed record Water(Guid ProductId, Guid Litre, Guid TenLitre, Guid TwentyLitre);

    // ---- Onboarding, as the super user and then the owner would do it ----

    /// <summary>Registers the station once per database (the super user onboards it), then signs in to it as its owner.</summary>
    private async Task<Guid> SignInToStationAsync(params string[] permissions)
    {
        var stationId = await Run(sp => sp.GetRequiredService<UltimatePosDbContext>().Businesses.IgnoreQueryFilters()
            .Where(b => b.KraPin == StationKraPin).Select(b => (Guid?)b.BusinessId).FirstOrDefaultAsync());

        if (stationId is null)
        {
            _db.Caller.UserId = _db.SuperUserId;
            _db.Caller.BusinessId = null;
            _db.Caller.Permissions = null;
            var registered = await Run(sp => sp.GetRequiredService<PlatformService>().RegisterBusinessAsync(new RegisterBusinessRequestDto(
                "Test Refill Station", null, BusinessType.Retailer, BusinessKind.Trader, null, StationKraPin, null, "Kiambu", null, null, null, null,
                Owner: new BusinessOwnerRequestDto("admin", DbSeeder.DevAdminEmail, null), AllowNegativeStock: false)));
            stationId = registered.Business.BusinessId;
            Assert.False(registered.Business.AllowNegativeStock);
        }

        if (!_db.HeadOffice.ContainsKey(stationId.Value))
            _db.HeadOffice[stationId.Value] = await Run(sp => sp.GetRequiredService<UltimatePosDbContext>().Branches.IgnoreQueryFilters()
                .Where(b => b.BusinessId == stationId && b.BranchNumber == 0).Select(b => b.BranchId).SingleAsync());

        _db.SignInTo(stationId.Value, permissions);
        return stationId.Value;
    }

    private Task<Guid> UnitAsync(string name, string symbol) => Run(async sp =>
    {
        var catalog = sp.GetRequiredService<CatalogService>();
        var existing = (await catalog.GetUnitsOfMeasureAsync()).FirstOrDefault(u => u.Symbol == symbol);
        return existing?.UnitOfMeasureId ?? (await catalog.CreateUnitOfMeasureAsync(new CreateUnitOfMeasureRequestDto(name, symbol))).UnitOfMeasureId;
    });

    /// <summary>Water held in litres, sold as a 1L refill (the base unit), a 10L refill (x10) and a 20L refill (x20).</summary>
    private async Task<Water> WaterAsync(decimal pricePerLitre = 5m, decimal priceTen = 40m, decimal priceTwenty = 70m)
    {
        var litre = await UnitAsync("Litre", "l");
        var ten = await UnitAsync("10 Litre Refill", "10L");
        var twenty = await UnitAsync("20 Litre Refill", "20L");
        var code = $"W{Guid.NewGuid():N}"[..8].ToUpperInvariant();

        return await Run(async sp =>
        {
            var catalog = sp.GetRequiredService<CatalogService>();
            var category = await catalog.CreateCategoryAsync(new CreateCategoryRequestDto("Water " + code, code, null));
            var product = await catalog.CreateProductAsync(new CreateProductRequestDto(
                "Water refill " + code, null, category.CategoryId, ItemType.FinishedGood, TaxClassification.Exempt, litre, 100));
            await catalog.AddUnitConversionAsync(product.ProductId, new CreateProductUnitConversionRequestDto(ten, 10));
            await catalog.AddUnitConversionAsync(product.ProductId, new CreateProductUnitConversionRequestDto(twenty, 20));
            var from = DateTime.UtcNow.AddMinutes(-1);
            await catalog.AddPriceTierAsync(product.ProductId, new AddPriceTierRequestDto(litre, PriceType.Retail, pricePerLitre, from));
            await catalog.AddPriceTierAsync(product.ProductId, new AddPriceTierRequestDto(ten, PriceType.Retail, priceTen, from));
            await catalog.AddPriceTierAsync(product.ProductId, new AddPriceTierRequestDto(twenty, PriceType.Retail, priceTwenty, from));
            return new Water(product.ProductId, litre, ten, twenty);
        });
    }

    private Task AdjustAsync(Water water, decimal litres, string reason, decimal? costPerLitre = 1.5m) =>
        Run(sp => sp.GetRequiredService<InventoryService>().AdjustStockAsync(
            new CreateStockAdjustmentRequestDto(water.ProductId, water.Litre, litres, reason, litres > 0 ? costPerLitre : null)));

    private Task<decimal> LitresAsync(Water water) =>
        Run(async sp => (await sp.GetRequiredService<InventoryService>().GetStockLevelAsync(water.ProductId)).QuantityOnHand);

    private static CreateSaleLineRequestDto Line(Guid productId, Guid unitOfMeasureId, decimal quantity) => new(productId, unitOfMeasureId, quantity);

    private static CreateSaleRequestDto Cash(params CreateSaleLineRequestDto[] lines) =>
        new(null, PaymentMethod.Cash, null, lines, 100_000m, null, null);

    private Task<SaleDto> SellAsync(CreateSaleRequestDto request) => Run(sp => sp.GetRequiredService<SalesService>().RecordSaleAsync(request));

    private async Task<(SupplierDto Supplier, PurchaseOrderDto Order)> OrderAsync(Water water, decimal litres, decimal costPerLitre)
    {
        var supplier = await Run(sp => sp.GetRequiredService<PurchasingService>().RegisterSupplierAsync(
            new CreateSupplierRequestDto(null, "Water vendor " + Guid.NewGuid().ToString("N")[..6], null, null, "0712000000", null, null, 7)));
        var order = await Run(sp => sp.GetRequiredService<PurchasingService>().CreatePurchaseOrderAsync(new CreatePurchaseOrderRequestDto(
            supplier.SupplierId, null, null, new[] { new CreatePurchaseOrderLineRequestDto(water.ProductId, water.Litre, litres, costPerLitre) })));
        return (supplier, order);
    }

    /// <summary>Runs <paramref name="call"/> <paramref name="times"/> times at once, each as its own request, and returns how many succeeded.</summary>
    private async Task<(int Succeeded, List<Exception> Failures)> AtOnceAsync(int times, Func<IServiceProvider, Task> call)
    {
        var results = await Task.WhenAll(Enumerable.Range(0, times).Select(_ => Task.Run(async () =>
        {
            try { await Run(call); return (Exception?)null; }
            catch (Exception e) { return e; }
        })));
        return (results.Count(r => r is null), results.OfType<Exception>().ToList());
    }

    // ---------------------------------------------------------------- One balance in litres

    [PostgresFact]
    public async Task Each_refill_size_takes_its_own_volume_from_one_litre_balance()
    {
        await SignInToStationAsync();
        var water = await WaterAsync();
        await AdjustAsync(water, 500, "Opening stock");

        await SellAsync(Cash(Line(water.ProductId, water.Litre, 1)));
        Assert.Equal(499, await LitresAsync(water));
        await SellAsync(Cash(Line(water.ProductId, water.TenLitre, 1)));
        Assert.Equal(489, await LitresAsync(water));
        await SellAsync(Cash(Line(water.ProductId, water.TwentyLitre, 1)));
        Assert.Equal(469, await LitresAsync(water));

        // Two 20L, one 10L and three 1L refills: (2 x 20) + (1 x 10) + (3 x 1) = 53 litres, priced per size.
        var mixed = await SellAsync(Cash(Line(water.ProductId, water.TwentyLitre, 2), Line(water.ProductId, water.TenLitre, 1),
            Line(water.ProductId, water.Litre, 3)));
        Assert.Equal(469 - 53, await LitresAsync(water));
        Assert.Equal(2 * 70m + 40m + 3 * 5m, mixed.TotalAmount);
    }

    // ---------------------------------------------------------------- No overselling

    [PostgresFact]
    public async Task A_sale_needing_more_water_than_is_held_is_refused_and_takes_nothing()
    {
        await SignInToStationAsync();
        var water = await WaterAsync();
        await AdjustAsync(water, 50, "Opening stock");

        // 53 litres across three lines of the same water, against 50 held: refused as one shortfall.
        var refused = await Assert.ThrowsAsync<InsufficientStockException>(() => SellAsync(Cash(Line(water.ProductId, water.TwentyLitre, 2),
            Line(water.ProductId, water.TenLitre, 1), Line(water.ProductId, water.Litre, 3))));
        Assert.Contains("50 available, 53 needed", refused.Message);
        Assert.Equal(50, await LitresAsync(water));

        // Exactly what is held can be sold, down to zero, and not a litre more.
        await SellAsync(Cash(Line(water.ProductId, water.TwentyLitre, 2), Line(water.ProductId, water.TenLitre, 1)));
        Assert.Equal(0, await LitresAsync(water));
        await Assert.ThrowsAsync<InsufficientStockException>(() => SellAsync(Cash(Line(water.ProductId, water.Litre, 1))));
        Assert.Equal(0, await LitresAsync(water));
    }

    [PostgresFact]
    public async Task Water_never_held_cannot_be_sold()
    {
        await SignInToStationAsync();
        var water = await WaterAsync();

        await Assert.ThrowsAsync<InsufficientStockException>(() => SellAsync(Cash(Line(water.ProductId, water.Litre, 1))));
    }

    [PostgresFact]
    public async Task Sales_at_the_same_moment_cannot_oversell_between_them()
    {
        await SignInToStationAsync();
        var water = await WaterAsync();
        await AdjustAsync(water, 40, "Opening stock");

        // Five counters each sell a 20L refill against 40 litres: two can be filled, three are refused.
        var (succeeded, failures) = await AtOnceAsync(5, sp => sp.GetRequiredService<SalesService>().RecordSaleAsync(
            Cash(Line(water.ProductId, water.TwentyLitre, 1))));

        Assert.Equal(2, succeeded);
        Assert.All(failures, f => Assert.IsType<InsufficientStockException>(f));
        Assert.Equal(0, await LitresAsync(water));
    }

    [PostgresFact]
    public async Task A_business_that_allows_negative_stock_still_may_and_the_setting_survives_an_older_client()
    {
        // Afya was registered without the setting: like every existing business, it keeps selling into negative stock.
        _db.SignInTo(_db.Afya);
        var afya = await Run(sp => sp.GetRequiredService<BusinessService>().GetCurrentBusinessAsync());
        Assert.True(afya.AllowNegativeStock);

        var stationId = await SignInToStationAsync();
        var station = await Run(sp => sp.GetRequiredService<BusinessService>().GetCurrentBusinessAsync());
        Assert.False(station.AllowNegativeStock);

        // A profile save from a client that does not know the field (null) leaves it alone.
        UpdateBusinessRequestDto Update(bool? allowNegativeStock) => new(station.BusinessName, station.TradingName, station.BusinessType,
            station.RegistrationNumber, station.KraPin, station.PhysicalAddress, station.County, station.PhoneNumber, station.Email, station.LogoUrl,
            station.BackgroundImageUrl, station.IsVatRegistered, station.MpesaShortCode, station.MaxDiscountPercent, allowNegativeStock);
        Assert.False((await Run(sp => sp.GetRequiredService<BusinessService>().UpdateBusinessAsync(stationId, Update(null)))).AllowNegativeStock);

        // Turned on, an oversell goes through and leaves the balance negative for a count to correct; turned off again, it is refused.
        var water = await WaterAsync();
        Assert.True((await Run(sp => sp.GetRequiredService<BusinessService>().UpdateBusinessAsync(stationId, Update(true)))).AllowNegativeStock);
        await SellAsync(Cash(Line(water.ProductId, water.Litre, 3)));
        Assert.Equal(-3, await LitresAsync(water));

        Assert.False((await Run(sp => sp.GetRequiredService<BusinessService>().UpdateBusinessAsync(stationId, Update(false)))).AllowNegativeStock);
        await Assert.ThrowsAsync<InsufficientStockException>(() => SellAsync(Cash(Line(water.ProductId, water.Litre, 1))));
    }

    // ---------------------------------------------------------------- Water in, exactly once

    [PostgresFact]
    public async Task A_delivery_adds_its_litres_once_and_owes_the_vendor_once()
    {
        await SignInToStationAsync();
        var water = await WaterAsync();
        var (supplier, order) = await OrderAsync(water, 1000, 1.2m);
        var line = order.Lines.Single();

        // Part of the order arrives, then the rest.
        await Run(sp => sp.GetRequiredService<PurchasingService>().ReceivePurchaseOrderAsync(order.PurchaseOrderId,
            new ReceivePurchaseOrderRequestDto(new[] { new ReceiveLineRequestDto(line.PurchaseOrderLineId, 400) }, null, "DN-1")));
        Assert.Equal(400, await LitresAsync(water));
        var done = await Run(sp => sp.GetRequiredService<PurchasingService>().ReceivePurchaseOrderAsync(order.PurchaseOrderId,
            new ReceivePurchaseOrderRequestDto(new[] { new ReceiveLineRequestDto(line.PurchaseOrderLineId, 600) }, null, "DN-2")));
        Assert.Equal(PurchaseOrderStatus.Received, done.Status);
        Assert.Equal(1000, await LitresAsync(water));

        // Receiving the finished order again (a retry, a reopened screen) is refused and changes nothing.
        await Assert.ThrowsAsync<InvalidAssignmentException>(() => Run(sp => sp.GetRequiredService<PurchasingService>().ReceivePurchaseOrderAsync(
            order.PurchaseOrderId, new ReceivePurchaseOrderRequestDto(new[] { new ReceiveLineRequestDto(line.PurchaseOrderLineId, 600) }, null, "DN-2"))));
        Assert.Equal(1000, await LitresAsync(water));
        var owed = await Run(sp => sp.GetRequiredService<AccountingService>().GetSupplierBalanceAsync(supplier.SupplierId));
        Assert.Equal(1200m, owed.OutstandingBalance);
    }

    [PostgresFact]
    public async Task The_same_delivery_submitted_several_times_at_once_is_received_once()
    {
        await SignInToStationAsync();
        var water = await WaterAsync();
        var (supplier, order) = await OrderAsync(water, 300, 1.5m);
        var line = order.Lines.Single();

        var (succeeded, failures) = await AtOnceAsync(5, sp => sp.GetRequiredService<PurchasingService>().ReceivePurchaseOrderAsync(order.PurchaseOrderId,
            new ReceivePurchaseOrderRequestDto(new[] { new ReceiveLineRequestDto(line.PurchaseOrderLineId, 300) }, null, "DN-77")));

        Assert.Equal(1, succeeded);
        Assert.All(failures, f => Assert.IsType<InvalidAssignmentException>(f));
        Assert.Equal(300, await LitresAsync(water));
        var owed = await Run(sp => sp.GetRequiredService<AccountingService>().GetSupplierBalanceAsync(supplier.SupplierId));
        Assert.Equal(450m, owed.OutstandingBalance);
    }

    // ---------------------------------------------------------------- Reconciliation

    [PostgresFact]
    public async Task Closing_stock_is_opening_plus_received_less_sold_less_losses()
    {
        await SignInToStationAsync();
        var water = await WaterAsync();
        await AdjustAsync(water, 500, "Opening stock");
        var (_, order) = await OrderAsync(water, 300, 1.5m);
        await Run(sp => sp.GetRequiredService<PurchasingService>().ReceivePurchaseOrderAsync(order.PurchaseOrderId,
            new ReceivePurchaseOrderRequestDto(new[] { new ReceiveLineRequestDto(order.Lines.Single().PurchaseOrderLineId, 300) }, null, null)));
        await SellAsync(Cash(Line(water.ProductId, water.TwentyLitre, 21))); // 420 litres
        await AdjustAsync(water, -5, "Spillage at the tap");

        // 500 + 300 - 420 - 5 = 375.
        Assert.Equal(375, await LitresAsync(water));
        var report = await Run(sp => sp.GetRequiredService<ReportsService>().GetMovementReportAsync(Today, Today,
            ReportPeriod.DefaultUtcOffsetMinutes, water.ProductId, includeCosts: true));
        var row = report.Items.Single();
        Assert.Equal((0m, 300m, 420m, 500m, 5m, 375m), (row.Opening, row.Received, row.Sold, row.AdjustedIn, row.AdjustedOut, row.Closing));
    }

    [PostgresFact]
    public async Task A_stock_adjustment_needs_a_reason()
    {
        await SignInToStationAsync();
        var water = await WaterAsync();
        await AdjustAsync(water, 100, "Opening stock");

        await Assert.ThrowsAsync<ValidationException>(() => AdjustAsync(water, -5, " "));
        Assert.Equal(100, await LitresAsync(water));
    }

    [PostgresFact]
    public async Task An_expense_does_not_touch_water_stock()
    {
        await SignInToStationAsync();
        var water = await WaterAsync();
        await AdjustAsync(water, 200, "Opening stock");

        await Run(async sp =>
        {
            var expenses = sp.GetRequiredService<ExpenseService>();
            var transport = (await expenses.GetCategoriesAsync(activeOnly: true)).Single(c => c.Code == "TRN");
            await expenses.RecordExpenseAsync(new CreateExpenseRequestDto(Today, transport.ExpenseCategoryId, "Delivery of water from vendor", 800m,
                ExpensePaymentMethod.Cash, null, null, "Lorry driver", null, null, null, null));
        });

        Assert.Equal(200, await LitresAsync(water));
    }

    // ---------------------------------------------------------------- Customers

    [PostgresFact]
    public async Task A_walk_in_buys_with_no_customer_and_gets_change()
    {
        await SignInToStationAsync();
        var water = await WaterAsync();
        await AdjustAsync(water, 100, "Opening stock");

        var sale = await SellAsync(new CreateSaleRequestDto(null, PaymentMethod.Cash, null,
            new[] { new CreateSaleLineRequestDto(water.ProductId, water.TwentyLitre, 1) }, 100m, null, null));

        Assert.Null(sale.CustomerId);
        Assert.Equal((SaleStatus.Completed, 70m, 30m), (sale.Status, sale.TotalAmount, sale.ChangeDue!.Value));
    }

    [PostgresFact]
    public async Task A_new_customer_registered_at_the_counter_is_on_the_sale_and_on_credit_within_their_limit()
    {
        await SignInToStationAsync();
        var water = await WaterAsync();
        await AdjustAsync(water, 500, "Opening stock");

        var customer = await Run(sp => sp.GetRequiredService<BusinessService>().RegisterCustomerAsync(
            new RegisterCustomerRequestDto(null, "Mama Njeri", null, null, "0722000111", null, null, CreditLimit: 150m)));
        var sale = await SellAsync(new CreateSaleRequestDto(customer.CustomerId, PaymentMethod.Credit, null,
            new[] { new CreateSaleLineRequestDto(water.ProductId, water.TwentyLitre, 2) }, null, null, null));
        Assert.Equal((customer.CustomerId, "Mama Njeri"), (sale.CustomerId!.Value, sale.CustomerName));

        // Found again by the number as the cashier types it, whichever way (it is stored as 254722000111).
        foreach (var typed in new[] { "0722000111", "+254 722 000 111", "722000" })
            Assert.Contains(await Run(sp => sp.GetRequiredService<BusinessService>().GetCustomersAsync(null, typed)), c => c.CustomerId == customer.CustomerId);
        var balance = await Run(sp => sp.GetRequiredService<AccountingService>().GetCustomerBalanceAsync(customer.CustomerId));
        Assert.Equal(140m, balance.OutstandingBalance);

        // Another 20L takes her to 210, past the 150 limit: refused, and only a user allowed to override may push it through.
        var more = new CreateSaleRequestDto(customer.CustomerId, PaymentMethod.Credit, null,
            new[] { new CreateSaleLineRequestDto(water.ProductId, water.TwentyLitre, 1) }, null, null, null);
        await Assert.ThrowsAsync<CreditLimitExceededException>(() => SellAsync(more));
        await SignInToStationAsync("Sales.Create", "Sales.View");
        await Assert.ThrowsAsync<ForbiddenException>(() => SellAsync(more with { OverrideCreditLimit = true }));
        await SignInToStationAsync();
        await SellAsync(more with { OverrideCreditLimit = true });
        Assert.Equal(210m, (await Run(sp => sp.GetRequiredService<AccountingService>().GetCustomerBalanceAsync(customer.CustomerId))).OutstandingBalance);
    }

    [PostgresFact]
    public async Task A_walk_in_cannot_buy_on_credit()
    {
        await SignInToStationAsync();
        var water = await WaterAsync();
        await AdjustAsync(water, 100, "Opening stock");

        await Assert.ThrowsAsync<ValidationException>(() => SellAsync(new CreateSaleRequestDto(null, PaymentMethod.Credit, null,
            new[] { new CreateSaleLineRequestDto(water.ProductId, water.Litre, 1) }, null, null, null)));
    }

    // ---------------------------------------------------------------- Payments and voids, exactly once

    [PostgresFact]
    public async Task Repeated_and_simultaneous_mpesa_confirmations_take_the_water_once()
    {
        var stationId = await SignInToStationAsync();
        var water = await WaterAsync();
        await AdjustAsync(water, 100, "Opening stock");

        // An STK sale waiting for the customer to pay (the prompt itself goes to Safaricom, which these tests do not call).
        var saleId = Guid.NewGuid();
        var checkoutRequestId = "ws_CO_" + Guid.NewGuid().ToString("N");
        await Run(async sp =>
        {
            await sp.GetRequiredService<ISalesRepository>().RecordPendingSaleAsync(new Sale
            {
                SaleId = saleId, SaleNumber = "INV-T" + Guid.NewGuid().ToString("N")[..10], PaymentMethod = PaymentMethod.MpesaStkPush,
                Status = SaleStatus.Pending, Subtotal = 140m, TotalAmount = 140m, CreatedBy = _db.AdminUserId,
                Lines = new List<SaleLine> { new() { SaleLineId = Guid.NewGuid(), ProductId = water.ProductId, UnitOfMeasureId = water.TwentyLitre,
                    Quantity = 2, QuantityInBaseUnits = 40, UnitPrice = 70m, LineTotal = 140m } }
            });
            await sp.GetRequiredService<IMpesaRepository>().CreateAsync(new MpesaTransaction
            {
                MpesaTransactionId = Guid.NewGuid(), BusinessId = stationId, TransactionType = MpesaTransactionType.StkPush,
                Status = MpesaTransactionStatus.Pending, PhoneNumber = "254722000111", Amount = 140m, CheckoutRequestId = checkoutRequestId, SaleId = saleId
            });
        });
        Assert.Equal(100, await LitresAsync(water)); // a pending sale takes nothing

        // Safaricom retries, and a cashier presses reconcile, all at the same moment.
        var callback = new StkCallback("mr-1", checkoutRequestId, 0, "The service request is processed successfully.", null);
        var (succeeded, failures) = await AtOnceAsync(5, sp => sp.GetRequiredService<MpesaService>().HandleStkCallbackAsync(callback));
        Assert.Equal(5, succeeded); // every duplicate is accepted quietly, as Safaricom expects...
        Assert.Empty(failures);
        Assert.Equal(60, await LitresAsync(water)); // ...but the 40 litres leave once

        // And a late duplicate after it settled changes nothing either.
        await Run(sp => sp.GetRequiredService<MpesaService>().HandleStkCallbackAsync(callback));
        Assert.Equal(60, await LitresAsync(water));
        var sale = await Run(sp => sp.GetRequiredService<SalesService>().GetSaleByIdAsync(saleId));
        Assert.Equal(SaleStatus.Completed, sale.Status);
    }

    [PostgresFact]
    public async Task One_till_payment_can_pay_for_only_one_sale_even_when_two_cashiers_try_at_once()
    {
        var stationId = await SignInToStationAsync();
        var water = await WaterAsync();
        await AdjustAsync(water, 100, "Opening stock");

        var transactionId = Guid.NewGuid();
        await Run(sp => sp.GetRequiredService<IMpesaRepository>().CreateAsync(new MpesaTransaction
        {
            MpesaTransactionId = transactionId, BusinessId = stationId, TransactionType = MpesaTransactionType.C2B,
            Status = MpesaTransactionStatus.Received, MpesaReceiptNumber = "T" + Guid.NewGuid().ToString("N")[..9].ToUpperInvariant(),
            PhoneNumber = "254722000222", Amount = 70m, TransactionDate = DateTime.UtcNow
        }));

        var (succeeded, failures) = await AtOnceAsync(4, sp => sp.GetRequiredService<SalesService>().RecordSaleAsync(new CreateSaleRequestDto(
            null, PaymentMethod.MpesaTill, null, new[] { new CreateSaleLineRequestDto(water.ProductId, water.TwentyLitre, 1) }, null, null, transactionId)));

        Assert.Equal(1, succeeded);
        Assert.All(failures, f => Assert.IsType<InvalidAssignmentException>(f));
        Assert.Equal(80, await LitresAsync(water)); // the refused sales were rolled back with their stock
    }

    [PostgresFact]
    public async Task A_sale_voided_several_times_at_once_puts_its_water_back_once()
    {
        await SignInToStationAsync();
        var water = await WaterAsync();
        await AdjustAsync(water, 100, "Opening stock");
        var sale = await SellAsync(Cash(Line(water.ProductId, water.TwentyLitre, 2)));
        Assert.Equal(60, await LitresAsync(water));

        var (succeeded, failures) = await AtOnceAsync(5, sp => sp.GetRequiredService<SalesService>().VoidSaleAsync(sale.SaleId));

        Assert.Equal(1, succeeded);
        Assert.All(failures, f => Assert.IsType<InvalidAssignmentException>(f));
        Assert.Equal(100, await LitresAsync(water));
    }

    // ---------------------------------------------------------------- Reports and tenancy

    [PostgresFact]
    public async Task The_sales_report_shows_litres_by_refill_size_and_who_sold_them()
    {
        await SignInToStationAsync();
        var water = await WaterAsync();
        await AdjustAsync(water, 500, "Opening stock");
        await SellAsync(Cash(Line(water.ProductId, water.TwentyLitre, 3), Line(water.ProductId, water.Litre, 4)));
        await SellAsync(Cash(Line(water.ProductId, water.TenLitre, 2)));

        var report = await Run(sp => sp.GetRequiredService<ReportsService>().GetSalesReportAsync(Today, Today, ReportPeriod.DefaultUtcOffsetMinutes, 100));

        var sizes = report.TopProductUnits!.Where(u => u.ProductId == water.ProductId).ToDictionary(u => u.UnitSymbol);
        Assert.Equal((3m, 60m, 210m), (sizes["20L"].QuantitySold, sizes["20L"].QuantitySoldInBaseUnits, sizes["20L"].Revenue));
        Assert.Equal((2m, 20m, 80m), (sizes["10L"].QuantitySold, sizes["10L"].QuantitySoldInBaseUnits, sizes["10L"].Revenue));
        Assert.Equal((4m, 4m, 20m), (sizes["l"].QuantitySold, sizes["l"].QuantitySoldInBaseUnits, sizes["l"].Revenue));
        Assert.Equal(84m, report.TopProducts.Single(p => p.ProductId == water.ProductId).QuantitySoldInBaseUnits); // litres sold

        var cashier = Assert.Single(report.ByCashier!, c => c.UserId == _db.AdminUserId);
        Assert.Equal("admin", cashier.CashierName);
        Assert.Equal(report.Revenue, report.ByCashier!.Sum(c => c.Total));
    }

    [PostgresFact]
    public async Task Another_business_cannot_see_or_sell_the_stations_water()
    {
        await SignInToStationAsync();
        var water = await WaterAsync();
        await AdjustAsync(water, 100, "Opening stock");

        _db.SignInTo(_db.Afya);
        await Assert.ThrowsAsync<NotFoundException>(() => Run(sp => sp.GetRequiredService<InventoryService>().GetStockLevelAsync(water.ProductId)));
        await Assert.ThrowsAsync<NotFoundException>(() => SellAsync(Cash(Line(water.ProductId, water.Litre, 1))));

        await SignInToStationAsync();
        Assert.Equal(100, await LitresAsync(water));
    }
}
