using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UltimatePos.Application.Business.Dtos;
using UltimatePos.Application.Catalog;
using UltimatePos.Application.Catalog.Dtos;
using UltimatePos.Application.Inventory;
using UltimatePos.Application.Inventory.Dtos;
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

/// <summary>How a bundle's price is split over its parts. No database.</summary>
public class BundlePricingTests
{
    [Fact]
    public void Parts_with_their_own_price_keep_it_and_the_bottle_takes_the_rest()
    {
        // 20L Heavy-duty + water at 1500: the water counts at 200 (20 x 10), the bottle is 1300.
        Assert.Equal(new[] { 200m, 1300m }, BundlePricing.Split(1500m, new decimal?[] { 200m, null }));
        // 500ml bottle + water at 20: half a litre at 10 a litre is 5, the bottle 15.
        Assert.Equal(new[] { 5m, 15m }, BundlePricing.Split(20m, new decimal?[] { 5m, null }));
    }

    [Fact]
    public void Several_unpriced_parts_share_the_rest_equally_to_the_cent()
    {
        var shares = BundlePricing.Split(100m, new decimal?[] { 50m, null, null, null });
        Assert.Equal(100m, shares.Sum());
        Assert.Equal(50m, shares[0]);
        Assert.All(shares.Skip(1), s => Assert.InRange(s, 16.66m, 16.68m));
    }

    [Fact]
    public void A_bundle_cheaper_than_its_priced_parts_shares_its_price_in_proportion_to_them()
    {
        // Every part priced: 300 + 100 sold for 360 gives each 10% off.
        Assert.Equal(new[] { 270m, 90m }, BundlePricing.Split(360m, new decimal?[] { 300m, 100m }));
        // Priced parts worth more than the bundle: the unpriced part gets nothing, never a negative price.
        Assert.Equal(new[] { 100m, 0m }, BundlePricing.Split(100m, new decimal?[] { 200m, null }));
    }
}

/// <summary>
/// A refill station that sells water with or without a bottle, the way AQUA HUB (Gavland) does: water bought at 2 and sold at 10 a litre;
/// a customer with a bottle pays for the water only; one without buys a bundle of water and bottle at one price; 1L and 500ml bottles
/// also go out in packs of 12 and 24. Bottles are bought on purchase orders and leave stock when sold. Prices are the owner's as given;
/// bottle costs and pack prices are test data.
/// </summary>
public class WaterBundlesPostgresTests : IClassFixture<MultiBusinessDatabase>
{
    private const string StationKraPin = "P051000004D";

    private readonly MultiBusinessDatabase _db;
    public WaterBundlesPostgresTests(MultiBusinessDatabase db) => _db = db;

    private Task<T> Run<T>(Func<IServiceProvider, Task<T>> call) => _db.RunAsync(call);

    /// <summary>One test's own stock: water in litres (1L, 10L, 20L refills) and four bottles in pieces, none priced on their own.</summary>
    private sealed record Station(Guid Water, Guid Litre, Guid Ten, Guid Twenty, Guid Pieces,
        Guid HeavyDuty20, Guid Retail20, Guid Retail10, Guid Bottle1L, Guid Bottle500);

    private async Task SignInAsync(params string[] permissions)
    {
        var stationId = await Run(sp => sp.GetRequiredService<UltimatePosDbContext>().Businesses.IgnoreQueryFilters()
            .Where(b => b.KraPin == StationKraPin).Select(b => (Guid?)b.BusinessId).FirstOrDefaultAsync());
        if (stationId is null)
        {
            _db.Caller.UserId = _db.SuperUserId;
            _db.Caller.BusinessId = null;
            _db.Caller.Permissions = null;
            var registered = await Run(sp => sp.GetRequiredService<PlatformService>().RegisterBusinessAsync(new RegisterBusinessRequestDto(
                "Test Bottled Water Station", null, BusinessType.Retailer, null, null, StationKraPin, null, "Kiambu", null, null, null, null,
                Owner: new BusinessOwnerRequestDto("admin", DbSeeder.DevAdminEmail, null), Sector: BusinessSector.WaterRefill)));
            stationId = registered.Business.BusinessId;
        }
        if (!_db.HeadOffice.ContainsKey(stationId.Value))
            _db.HeadOffice[stationId.Value] = await Run(sp => sp.GetRequiredService<UltimatePosDbContext>().Branches.IgnoreQueryFilters()
                .Where(b => b.BusinessId == stationId && b.BranchNumber == 0).Select(b => b.BranchId).SingleAsync());
        _db.SignInTo(stationId.Value, permissions);
    }

    private Task<Guid> UnitAsync(string name, string symbol) => Run(async sp =>
    {
        var catalog = sp.GetRequiredService<CatalogService>();
        var existing = (await catalog.GetUnitsOfMeasureAsync()).FirstOrDefault(u => u.Symbol == symbol);
        return existing?.UnitOfMeasureId ?? (await catalog.CreateUnitOfMeasureAsync(new CreateUnitOfMeasureRequestDto(name, symbol))).UnitOfMeasureId;
    });

    private async Task<Station> StationAsync()
    {
        var litre = await UnitAsync("Litre", "l");
        var ten = await UnitAsync("10 Litre Refill", "10L");
        var twenty = await UnitAsync("20 Litre Refill", "20L");
        var pieces = await UnitAsync("Piece", "pcs");
        var code = $"B{Guid.NewGuid():N}"[..8].ToUpperInvariant();

        return await Run(async sp =>
        {
            var catalog = sp.GetRequiredService<CatalogService>();
            var category = await catalog.CreateCategoryAsync(new CreateCategoryRequestDto("Water " + code, code, null));
            async Task<Guid> ProductAsync(string name, Guid unit) => (await catalog.CreateProductAsync(new CreateProductRequestDto(
                $"{name} {code}", null, category.CategoryId, ItemType.FinishedGood, TaxClassification.Exempt, unit, 0))).ProductId;

            var water = await ProductAsync("Purified Water", litre);
            await catalog.AddUnitConversionAsync(water, new CreateProductUnitConversionRequestDto(ten, 10));
            await catalog.AddUnitConversionAsync(water, new CreateProductUnitConversionRequestDto(twenty, 20));
            var from = DateTime.UtcNow.AddMinutes(-1);
            await catalog.AddPriceTierAsync(water, new AddPriceTierRequestDto(litre, PriceType.Retail, 10m, from));
            await catalog.AddPriceTierAsync(water, new AddPriceTierRequestDto(ten, PriceType.Retail, 100m, from));
            await catalog.AddPriceTierAsync(water, new AddPriceTierRequestDto(twenty, PriceType.Retail, 200m, from));

            return new Station(water, litre, ten, twenty, pieces,
                await ProductAsync("20L Heavy-duty bottle", pieces), await ProductAsync("20L Semi-flexible bottle", pieces),
                await ProductAsync("10L Semi-flexible bottle", pieces), await ProductAsync("1L bottle", pieces), await ProductAsync("500ml bottle", pieces));
        });
    }

    private Task AdjustAsync(Guid productId, Guid unit, decimal quantity, decimal cost) =>
        Run(sp => sp.GetRequiredService<InventoryService>().AdjustStockAsync(new CreateStockAdjustmentRequestDto(productId, unit, quantity, "Opening stock", cost)));

    private Task<decimal> OnHandAsync(Guid productId) =>
        Run(async sp => (await sp.GetRequiredService<InventoryService>().GetStockLevelAsync(productId)).QuantityOnHand);

    private Task<ProductBundleDto> BundleAsync(string name, decimal price, params ProductBundleComponentRequestDto[] parts) =>
        Run(sp => sp.GetRequiredService<ProductBundleService>().CreateAsync(new CreateProductBundleRequestDto(name, null, null, price, null, parts)));

    private static CreateSaleRequestDto Cash(IEnumerable<CreateSaleLineRequestDto> lines, params CreateSaleBundleRequestDto[] bundles) =>
        new(null, PaymentMethod.Cash, null, lines, 100_000m, null, null, Bundles: bundles);

    private static CreateSaleRequestDto Cash(params CreateSaleBundleRequestDto[] bundles) => Cash(Array.Empty<CreateSaleLineRequestDto>(), bundles);

    private Task<SaleDto> SellAsync(CreateSaleRequestDto request) => Run(sp => sp.GetRequiredService<SalesService>().RecordSaleAsync(request));

    /// <summary>The owner's price list: bottle + water bundles and the two packs.</summary>
    private async Task<(ProductBundleDto Hd20, ProductBundleDto Retail20, ProductBundleDto Retail10, ProductBundleDto One, ProductBundleDto Half,
        ProductBundleDto Pack12, ProductBundleDto Pack24)> PriceListAsync(Station s)
    {
        ProductBundleComponentRequestDto Water(decimal litres) => new(s.Water, s.Litre, litres);
        ProductBundleComponentRequestDto Bottles(Guid bottle, decimal count) => new(bottle, s.Pieces, count);
        return (
            await BundleAsync("20L Heavy-duty bottle + water", 1500m, new(s.Water, s.Twenty, 1), Bottles(s.HeavyDuty20, 1)),
            await BundleAsync("20L Semi-flexible bottle + water", 1300m, Water(20), Bottles(s.Retail20, 1)),
            await BundleAsync("10L Semi-flexible bottle + water", 250m, new(s.Water, s.Ten, 1), Bottles(s.Retail10, 1)),
            await BundleAsync("1L bottle + water", 30m, Water(1), Bottles(s.Bottle1L, 1)),
            await BundleAsync("500ml bottle + water", 20m, Water(0.5m), Bottles(s.Bottle500, 1)),
            await BundleAsync("Pack of 12 x 1L", 330m, Water(12), Bottles(s.Bottle1L, 12)),
            await BundleAsync("Pack of 24 x 500ml", 450m, Water(12), Bottles(s.Bottle500, 24)));
    }

    private async Task<Station> StockedStationAsync()
    {
        var s = await StationAsync();
        await AdjustAsync(s.Water, s.Litre, 5000, 2m);
        foreach (var bottle in new[] { s.HeavyDuty20, s.Retail20, s.Retail10 })
            await AdjustAsync(bottle, s.Pieces, 10, 800m);
        await AdjustAsync(s.Bottle1L, s.Pieces, 100, 12m);
        await AdjustAsync(s.Bottle500, s.Pieces, 100, 8m);
        return s;
    }

    // ---------------------------------------------------------------- Selling a bundle

    [PostgresFact]
    public async Task A_bottle_and_water_bundle_takes_the_water_and_the_bottle_at_one_price()
    {
        await SignInAsync();
        var s = await StockedStationAsync();
        var list = await PriceListAsync(s);

        var sale = await SellAsync(Cash(new CreateSaleBundleRequestDto(list.Hd20.ProductBundleId, 1)));

        Assert.Equal(1500m, sale.TotalAmount);
        Assert.Equal(4980m, await OnHandAsync(s.Water));
        Assert.Equal(9m, await OnHandAsync(s.HeavyDuty20));
        // One line per part: the water at its own price, the bottle the rest. The receipt shows the bundle once.
        var water = Assert.Single(sale.Lines, l => l.ProductId == s.Water);
        var bottle = Assert.Single(sale.Lines, l => l.ProductId == s.HeavyDuty20);
        Assert.Equal((200m, 1300m), (water.LineTotal, bottle.LineTotal));
        Assert.All(sale.Lines, l => Assert.Equal(list.Hd20.ProductBundleId, l.ProductBundleId));
        var onReceipt = Assert.Single(sale.Bundles!);
        Assert.Equal(("20L Heavy-duty bottle + water", 1m, 1500m), (onReceipt.Name, onReceipt.Quantity, onReceipt.Total));
    }

    [PostgresFact]
    public async Task Every_size_on_the_owners_price_list_takes_the_right_litres_and_bottles()
    {
        await SignInAsync();
        var s = await StockedStationAsync();
        var list = await PriceListAsync(s);

        // A counter sale of everything at once: own-bottle 20L refill (200), plus one of each bundle and pack.
        var sale = await SellAsync(Cash(new[] { new CreateSaleLineRequestDto(s.Water, s.Twenty, 1) },
            new(list.Hd20.ProductBundleId, 1), new(list.Retail20.ProductBundleId, 1), new(list.Retail10.ProductBundleId, 1),
            new(list.One.ProductBundleId, 1), new(list.Half.ProductBundleId, 1), new(list.Pack12.ProductBundleId, 1), new(list.Pack24.ProductBundleId, 1)));

        Assert.Equal(200m + 1500m + 1300m + 250m + 30m + 20m + 330m + 450m, sale.TotalAmount);
        // Litres: 20 own bottle + 20 + 20 + 10 + 1 + 0.5 + 12 + 12 = 95.5, all from one balance.
        Assert.Equal(5000m - 95.5m, await OnHandAsync(s.Water));
        Assert.Equal((9m, 9m, 9m), (await OnHandAsync(s.HeavyDuty20), await OnHandAsync(s.Retail20), await OnHandAsync(s.Retail10)));
        Assert.Equal(100m - 1 - 12, await OnHandAsync(s.Bottle1L));
        Assert.Equal(100m - 1 - 24, await OnHandAsync(s.Bottle500));
        Assert.Equal(7, sale.Bundles!.Count());
    }

    [PostgresFact]
    public async Task Several_of_a_bundle_with_a_discount_split_the_money_and_the_stock()
    {
        await SignInAsync();
        var s = await StockedStationAsync();
        var list = await PriceListAsync(s);

        var sale = await SellAsync(Cash(new CreateSaleBundleRequestDto(list.Retail20.ProductBundleId, 3, DiscountAmount: 300m)));

        Assert.Equal(3 * 1300m - 300m, sale.TotalAmount);
        Assert.Equal(5000m - 60m, await OnHandAsync(s.Water));
        Assert.Equal(7m, await OnHandAsync(s.Retail20));
        Assert.Equal(300m, sale.DiscountTotal);
        Assert.Equal(3m, Assert.Single(sale.Bundles!).Quantity);
    }

    // ---------------------------------------------------------------- Stock rules

    [PostgresFact]
    public async Task No_bottles_left_refuses_the_bundle_and_takes_no_water()
    {
        await SignInAsync();
        var s = await StationAsync();
        await AdjustAsync(s.Water, s.Litre, 100, 2m);
        await AdjustAsync(s.HeavyDuty20, s.Pieces, 1, 800m);
        var list = await PriceListAsync(s);

        var refused = await Assert.ThrowsAsync<InsufficientStockException>(() =>
            SellAsync(Cash(new CreateSaleBundleRequestDto(list.Hd20.ProductBundleId, 2))));
        Assert.Contains("1 available, 2 needed", refused.Message);
        Assert.Equal((100m, 1m), (await OnHandAsync(s.Water), await OnHandAsync(s.HeavyDuty20)));

        // A bundle and a plain refill draw on the same water: 100 litres cover one 20L bundle and four own-bottle 20L refills, no more.
        await Assert.ThrowsAsync<InsufficientStockException>(() => SellAsync(Cash(
            new[] { new CreateSaleLineRequestDto(s.Water, s.Twenty, 5) }, new CreateSaleBundleRequestDto(list.Hd20.ProductBundleId, 1))));
        await SellAsync(Cash(new[] { new CreateSaleLineRequestDto(s.Water, s.Twenty, 4) }, new CreateSaleBundleRequestDto(list.Hd20.ProductBundleId, 1)));
        Assert.Equal((0m, 0m), (await OnHandAsync(s.Water), await OnHandAsync(s.HeavyDuty20)));
    }

    [PostgresFact]
    public async Task The_till_is_told_how_many_of_each_bundle_it_can_sell()
    {
        await SignInAsync();
        var s = await StationAsync();
        await AdjustAsync(s.Water, s.Litre, 50, 2m);
        await AdjustAsync(s.Bottle1L, s.Pieces, 30, 12m);
        var list = await PriceListAsync(s);

        var bundles = await Run(sp => sp.GetRequiredService<ProductBundleService>().ListAsync(true, null));
        int? Available(ProductBundleDto b) => bundles.Single(x => x.ProductBundleId == b.ProductBundleId).AvailableQuantity;
        Assert.Equal(30, Available(list.One));       // bottles run out first
        Assert.Equal(2, Available(list.Pack12));     // 30 bottles make two packs of 12
        Assert.Equal(0, Available(list.Hd20));       // no heavy-duty bottles at all
    }

    [PostgresFact]
    public async Task Bottles_bought_on_a_purchase_order_are_what_bundles_sell()
    {
        await SignInAsync();
        var s = await StationAsync();
        await AdjustAsync(s.Water, s.Litre, 100, 2m);
        var list = await PriceListAsync(s);

        var purchasing = (Func<IServiceProvider, PurchasingService>)(sp => sp.GetRequiredService<PurchasingService>());
        var supplier = await Run(sp => purchasing(sp).RegisterSupplierAsync(new CreateSupplierRequestDto(null, "Bottle maker " + Guid.NewGuid().ToString("N")[..6],
            null, null, "0712000000", null, null, 7)));
        var order = await Run(sp => purchasing(sp).CreatePurchaseOrderAsync(new CreatePurchaseOrderRequestDto(supplier.SupplierId, null, null,
            new[] { new CreatePurchaseOrderLineRequestDto(s.Bottle500, s.Pieces, 48, 8m) })));
        await Run(sp => purchasing(sp).ReceivePurchaseOrderAsync(order.PurchaseOrderId,
            new ReceivePurchaseOrderRequestDto(new[] { new ReceiveLineRequestDto(order.Lines.Single().PurchaseOrderLineId, 48) }, null, "DN-B1")));
        Assert.Equal(48m, await OnHandAsync(s.Bottle500));

        await SellAsync(Cash(new CreateSaleBundleRequestDto(list.Pack24.ProductBundleId, 2)));
        Assert.Equal((0m, 76m), (await OnHandAsync(s.Bottle500), await OnHandAsync(s.Water)));
    }

    // ---------------------------------------------------------------- After the sale

    [PostgresFact]
    public async Task A_voided_bundle_puts_back_the_water_and_the_bottle_and_a_bottle_can_be_returned_alone()
    {
        await SignInAsync();
        var s = await StockedStationAsync();
        var list = await PriceListAsync(s);

        var voided = await SellAsync(Cash(new CreateSaleBundleRequestDto(list.Retail10.ProductBundleId, 2)));
        await Run(sp => sp.GetRequiredService<SalesService>().VoidSaleAsync(voided.SaleId));
        Assert.Equal((5000m, 10m), (await OnHandAsync(s.Water), await OnHandAsync(s.Retail10)));

        var sale = await SellAsync(Cash(new CreateSaleBundleRequestDto(list.Retail10.ProductBundleId, 1)));
        var bottle = sale.Lines.Single(l => l.ProductId == s.Retail10);
        var refund = await Run(sp => sp.GetRequiredService<SalesService>().CreateReturnAsync(sale.SaleId, new CreateSaleReturnRequestDto(
            "Bottle cracked", RefundMethod.Cash, null, new[] { new CreateSaleReturnLineRequestDto(bottle.SaleLineId, 1) })));
        Assert.Equal(150m, refund.TotalAmount); // 250 less the water's 100
        Assert.Equal((4990m, 10m), (await OnHandAsync(s.Water), await OnHandAsync(s.Retail10)));
    }

    [PostgresFact]
    public async Task Litres_sold_in_bundles_count_in_the_sales_report()
    {
        await SignInAsync();
        var s = await StockedStationAsync();
        var list = await PriceListAsync(s);

        await SellAsync(Cash(new[] { new CreateSaleLineRequestDto(s.Water, s.Twenty, 1) }, new CreateSaleBundleRequestDto(list.Pack12.ProductBundleId, 1)));

        var today = ReportPeriod.TodayAt(ReportPeriod.DefaultUtcOffsetMinutes);
        var report = await Run(sp => sp.GetRequiredService<ReportsService>().GetSalesReportAsync(today, today, ReportPeriod.DefaultUtcOffsetMinutes, 100));
        Assert.Equal(32m, report.TopProducts.Single(p => p.ProductId == s.Water).QuantitySoldInBaseUnits);
    }

    // ---------------------------------------------------------------- Setting bundles up

    [PostgresFact]
    public async Task A_bundle_must_be_made_of_sellable_products_in_units_they_have()
    {
        await SignInAsync();
        var s = await StationAsync();
        var bundles = (Func<IServiceProvider, ProductBundleService>)(sp => sp.GetRequiredService<ProductBundleService>());

        // A bottle is counted in pieces; it has no 20L unit.
        await Assert.ThrowsAsync<InvalidAssignmentException>(() => BundleAsync("Wrong unit", 100m, new ProductBundleComponentRequestDto(s.HeavyDuty20, s.Twenty, 1)));
        await Assert.ThrowsAsync<ValidationException>(() => BundleAsync("Nothing in it", 100m));
        await Assert.ThrowsAsync<ValidationException>(() => BundleAsync("Free", 0m, new ProductBundleComponentRequestDto(s.Water, s.Litre, 1)));
        await Assert.ThrowsAsync<ValidationException>(() => BundleAsync("Twice", 100m, new(s.Water, s.Litre, 1), new(s.Water, s.Litre, 2)));

        var bundle = await BundleAsync("1L bottle + water", 30m, new(s.Water, s.Litre, 1), new(s.Bottle1L, s.Pieces, 1));
        await Assert.ThrowsAsync<ValidationException>(() => SellAsync(Cash(new CreateSaleBundleRequestDto(bundle.ProductBundleId, 1.5m))));

        await Run(sp => bundles(sp).SetActiveAsync(bundle.ProductBundleId, false));
        await Assert.ThrowsAsync<InvalidAssignmentException>(() => SellAsync(Cash(new CreateSaleBundleRequestDto(bundle.ProductBundleId, 1))));
    }

    [PostgresFact]
    public async Task A_new_price_applies_to_new_sales_and_past_sales_keep_theirs()
    {
        await SignInAsync();
        var s = await StockedStationAsync();
        var bundle = await BundleAsync("1L bottle + water", 30m, new(s.Water, s.Litre, 1), new(s.Bottle1L, s.Pieces, 1));
        var before = await SellAsync(Cash(new CreateSaleBundleRequestDto(bundle.ProductBundleId, 1)));

        await Run(sp => sp.GetRequiredService<ProductBundleService>().UpdateAsync(bundle.ProductBundleId, new UpdateProductBundleRequestDto(
            "1L bottle + water", null, null, 35m, null, new ProductBundleComponentRequestDto[] { new(s.Water, s.Litre, 1), new(s.Bottle1L, s.Pieces, 1) })));
        var after = await SellAsync(Cash(new CreateSaleBundleRequestDto(bundle.ProductBundleId, 1)));

        Assert.Equal(35m, after.TotalAmount);
        Assert.Equal(30m, (await Run(sp => sp.GetRequiredService<SalesService>().GetSaleByIdAsync(before.SaleId))).TotalAmount);
    }

    [PostgresFact]
    public async Task A_price_start_time_sent_with_a_time_zone_offset_is_stored_in_utc()
    {
        await SignInAsync();
        var s = await StationAsync();
        // "2026-…T14:00:00+03:00" from a browser in Nairobi arrives as local time; it used to fail with a 500.
        var local = DateTime.Now.AddMinutes(-2);
        var unspecified = DateTime.SpecifyKind(DateTime.UtcNow.AddMinutes(-1), DateTimeKind.Unspecified);

        var tier = await Run(sp => sp.GetRequiredService<CatalogService>().AddPriceTierAsync(s.Bottle1L,
            new AddPriceTierRequestDto(s.Pieces, PriceType.Retail, 20m, local)));
        await Run(sp => sp.GetRequiredService<CatalogService>().AddPriceTierAsync(s.Bottle500,
            new AddPriceTierRequestDto(s.Pieces, PriceType.Retail, 15m, unspecified)));

        Assert.Equal(local.ToUniversalTime(), tier.EffectiveFrom.ToUniversalTime(), TimeSpan.FromSeconds(1));
    }
}
