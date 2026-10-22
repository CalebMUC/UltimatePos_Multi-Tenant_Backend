using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UltimatePos.Application.Business;
using UltimatePos.Application.Business.Dtos;
using UltimatePos.Application.Catalog;
using UltimatePos.Application.Catalog.Dtos;
using UltimatePos.Application.Customers;
using UltimatePos.Application.Dashboard;
using UltimatePos.Application.Deliveries;
using UltimatePos.Application.Inventory;
using UltimatePos.Application.Inventory.Dtos;
using UltimatePos.Application.Platform;
using UltimatePos.Application.Reports;
using UltimatePos.Application.Sales;
using UltimatePos.Application.Sales.Dtos;
using UltimatePos.Domain.Entities;
using UltimatePos.Domain.Exceptions;
using UltimatePos.Infrastructure.Persistence;
using static UltimatePos.Domain.Enums.PosEnums;

namespace Ultimate.Api.Tests;

/// <summary>
/// How customers have been buying (insights, segments, the dashboard panel) and the standing customer discount the owner can give on
/// the strength of it. Prices and quantities here are test data, not the station's.
/// </summary>
public class CustomerInsightsPostgresTests : IClassFixture<MultiBusinessDatabase>
{
    private const string StationKraPin = "P051000011F";
    private const decimal Refill20 = 200m;
    private static readonly DateOnly Today = ReportPeriod.TodayAt(ReportPeriod.DefaultUtcOffsetMinutes);

    private readonly MultiBusinessDatabase _db;
    public CustomerInsightsPostgresTests(MultiBusinessDatabase db) => _db = db;

    private Task<T> Run<T>(Func<IServiceProvider, Task<T>> call) => _db.RunAsync(call);

    private sealed record Water(Guid ProductId, Guid Litre, Guid TwentyLitre);

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
                "Test Refill Station 3", null, BusinessType.Retailer, BusinessKind.Trader, null, StationKraPin, null, "Kiambu", null, null, null, null,
                Owner: new BusinessOwnerRequestDto("admin", DbSeeder.DevAdminEmail, null), AllowNegativeStock: false)));
            stationId = registered.Business.BusinessId;
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

    private async Task<Water> WaterAsync(decimal litres = 1000)
    {
        var litre = await UnitAsync("Litre", "l");
        var twenty = await UnitAsync("20 Litre Refill", "20L");
        var code = $"W{Guid.NewGuid():N}"[..8].ToUpperInvariant();
        return await Run(async sp =>
        {
            var catalog = sp.GetRequiredService<CatalogService>();
            var category = await catalog.CreateCategoryAsync(new CreateCategoryRequestDto("Water " + code, code, null));
            var water = await catalog.CreateProductAsync(new CreateProductRequestDto(
                "Water refill " + code, null, category.CategoryId, ItemType.FinishedGood, TaxClassification.Exempt, litre, 100));
            await catalog.AddUnitConversionAsync(water.ProductId, new CreateProductUnitConversionRequestDto(twenty, 20));
            var from = DateTime.UtcNow.AddMinutes(-1);
            await catalog.AddPriceTierAsync(water.ProductId, new AddPriceTierRequestDto(litre, PriceType.Retail, 10m, from));
            await catalog.AddPriceTierAsync(water.ProductId, new AddPriceTierRequestDto(twenty, PriceType.Retail, Refill20, from));
            await sp.GetRequiredService<InventoryService>().AdjustStockAsync(new CreateStockAdjustmentRequestDto(water.ProductId, litre, litres, "Opening stock", 1.5m));
            return new Water(water.ProductId, litre, twenty);
        });
    }

    private Task<CustomerDto> CustomerAsync(string name = "Customer") =>
        Run(sp => sp.GetRequiredService<BusinessService>().RegisterCustomerAsync(new RegisterCustomerRequestDto(
            null, name + " " + Guid.NewGuid().ToString("N")[..6], null, null, "0755" + Random.Shared.Next(100000, 999999), null, "Juja")));

    private Task<SaleDto> SellAsync(Guid? customerId, Water water, decimal refills, decimal? discountAmount = null) =>
        Run(sp => sp.GetRequiredService<SalesService>().RecordSaleAsync(new CreateSaleRequestDto(customerId, PaymentMethod.Cash, null,
            new[] { new CreateSaleLineRequestDto(water.ProductId, water.TwentyLitre, refills) }, 100_000m, null, null, DiscountAmount: discountAmount)));

    private Task<CustomerDto> DiscountAsync(Guid customerId, decimal? percent, string? note = "Loyal customer") =>
        Run(sp => sp.GetRequiredService<CustomerInsightsService>().SetDiscountAsync(customerId, new SetCustomerDiscountRequestDto(percent, note)));

    /// <summary>Moves a sale back in time, as if it had been rung up <paramref name="daysAgo"/> days ago.</summary>
    private Task BackdateAsync(Guid saleId, int daysAgo)
    {
        var at = DateTime.UtcNow.AddDays(-daysAgo);
        return Run(sp => sp.GetRequiredService<UltimatePosDbContext>().Sales.IgnoreQueryFilters()
            .Where(s => s.SaleId == saleId).ExecuteUpdateAsync(u => u.SetProperty(s => s.CreatedAt, at)));
    }

    [PostgresFact]
    public async Task A_standing_discount_is_applied_to_every_sale_to_the_customer_without_the_discount_permission()
    {
        var stationId = await SignInToStationAsync();
        var water = await WaterAsync();
        var customer = await CustomerAsync("Loyal");

        // A note is required, the rate must be sensible, and it may not pass the business's cap.
        await Assert.ThrowsAsync<ValidationException>(() => DiscountAsync(customer.CustomerId, 10m, null));
        await Assert.ThrowsAsync<ValidationException>(() => DiscountAsync(customer.CustomerId, 100m));
        await Assert.ThrowsAsync<ValidationException>(() => DiscountAsync(customer.CustomerId, 10.555m));
        await Run(sp => sp.GetRequiredService<UltimatePosDbContext>().Businesses.IgnoreQueryFilters().Where(b => b.BusinessId == stationId)
            .ExecuteUpdateAsync(u => u.SetProperty(b => b.MaxDiscountPercent, 15m)));
        await Assert.ThrowsAsync<ValidationException>(() => DiscountAsync(customer.CustomerId, 20m));

        var given = await DiscountAsync(customer.CustomerId, 10m, "Buys 20L three times a week");
        Assert.Equal((10m, "Buys 20L three times a week"), (given.DiscountPercent!.Value, given.DiscountNote));
        Assert.NotNull(given.DiscountSetAt);
        Assert.Equal(10m, (await Run(sp => sp.GetRequiredService<BusinessService>().GetCustomerByIdAsync(customer.CustomerId))).DiscountPercent);

        // A cashier without Sales.ApplyDiscount sells to them: the discount applies on its own, and the preview shows it.
        await SignInToStationAsync("Sales.Create", "Sales.View");
        var preview = await Run(sp => sp.GetRequiredService<SalesService>().PreviewSaleAsync(new CreateSaleRequestDto(customer.CustomerId,
            PaymentMethod.Cash, null, new[] { new CreateSaleLineRequestDto(water.ProductId, water.TwentyLitre, 2) }, null, null, null)));
        Assert.Equal((360m, 10m, 40m), (preview.TotalAmount, preview.CustomerDiscountPercent!.Value, preview.CustomerDiscountTotal));
        var sale = await SellAsync(customer.CustomerId, water, 2);
        Assert.Equal((400m, 40m, 360m), (sale.Subtotal, sale.DiscountTotal, sale.TotalAmount));
        Assert.Equal((10m, 40m), (sale.CustomerDiscountPercent!.Value, sale.CustomerDiscountAmount));
        // A discount the cashier adds on top still needs the permission; a walk-in pays full price.
        await Assert.ThrowsAsync<ForbiddenException>(() => SellAsync(customer.CustomerId, water, 1, discountAmount: 10m));
        var walkIn = await SellAsync(null, water, 1);
        Assert.Equal((200m, 0m, (decimal?)null), (walkIn.TotalAmount, walkIn.CustomerDiscountAmount, walkIn.CustomerDiscountPercent));

        // Deliveries are priced the same way.
        await SignInToStationAsync();
        var order = await Run(sp => sp.GetRequiredService<DeliveryOrderService>().CreateAsync(new CreateDeliveryOrderRequestDto(customer.CustomerId, "Juja",
            null, null, new[] { new DeliveryOrderLineRequestDto(water.ProductId, water.TwentyLitre, 1) })));
        Assert.Equal(180m, order.EstimatedTotal);

        // Removed: full price again, and sales already made keep what they were charged.
        var removed = await DiscountAsync(customer.CustomerId, null, null);
        Assert.Null(removed.DiscountPercent);
        Assert.Equal(200m, (await SellAsync(customer.CustomerId, water, 1)).TotalAmount);
        Assert.Equal(360m, (await Run(sp => sp.GetRequiredService<SalesService>().GetSaleByIdAsync(sale.SaleId))).TotalAmount);
        Assert.Equal(2, await Run(sp => sp.GetRequiredService<UltimatePosDbContext>().SystemEvents
            .CountAsync(e => e.EventType == "Customer.DiscountChanged" && e.EntityId == customer.CustomerId)));
    }

    [PostgresFact]
    public async Task Insights_show_how_each_customer_buys_and_who_to_consider_for_a_discount()
    {
        await SignInToStationAsync();
        var water = await WaterAsync();
        var regular = await CustomerAsync("Regular");
        var slipping = await CustomerAsync("Slipping");
        var newcomer = await CustomerAsync("New");

        // Regular: a 20L refill every 3 days for the last 18 days. Slipping: every 3 days, but the last was 30 days ago.
        foreach (var daysAgo in new[] { 16, 13, 10, 7, 4, 1 })
            await BackdateAsync((await SellAsync(regular.CustomerId, water, 2)).SaleId, daysAgo);
        foreach (var daysAgo in new[] { 36, 33, 30 })
            await BackdateAsync((await SellAsync(slipping.CustomerId, water, 1)).SaleId, daysAgo);
        await SellAsync(newcomer.CustomerId, water, 1);
        await SellAsync(null, water, 1); // a walk-in

        var service = (Func<IServiceProvider, CustomerInsightsService>)(sp => sp.GetRequiredService<CustomerInsightsService>());
        var list = (await Run(sp => service(sp).GetCustomersAsync(1, 100, new CustomerInsightsQuery()))).Items.ToList();
        var r = list.Single(x => x.CustomerId == regular.CustomerId);
        Assert.Equal((6, 2400m, 400m, 3.0m), (r.PurchaseCount, r.TotalSpent, r.AverageSpend, r.AverageDaysBetweenPurchases!.Value));
        Assert.Contains(r.Segment, new[] { CustomerSegment.Top, CustomerSegment.Regular });
        Assert.Equal(CustomerSuggestedAction.ConsiderDiscount, r.SuggestedAction);
        Assert.True(r.SpendRank < list.Single(x => x.CustomerId == slipping.CustomerId).SpendRank);
        var s = list.Single(x => x.CustomerId == slipping.CustomerId);
        Assert.Equal((CustomerSegment.Slipping, CustomerSuggestedAction.WinBack), (s.Segment, s.SuggestedAction));
        Assert.InRange(s.DaysSinceLastPurchase, 29, 30);
        var n = list.Single(x => x.CustomerId == newcomer.CustomerId);
        Assert.Equal((CustomerSegment.New, 1), (n.Segment, n.PurchaseCount));
        Assert.Null(n.AverageDaysBetweenPurchases);

        // Filters: by suggestion, by segment, by name; sorted by most recent.
        var candidates = await Run(sp => service(sp).GetCustomersAsync(1, 100, new CustomerInsightsQuery(SuggestedAction: CustomerSuggestedAction.ConsiderDiscount)));
        Assert.Contains(candidates.Items, x => x.CustomerId == regular.CustomerId);
        Assert.DoesNotContain(candidates.Items, x => x.CustomerId == slipping.CustomerId);
        var bySegment = await Run(sp => service(sp).GetCustomersAsync(1, 100, new CustomerInsightsQuery(Segment: CustomerSegment.Slipping)));
        Assert.All(bySegment.Items, x => Assert.Equal(CustomerSegment.Slipping, x.Segment));
        var searched = await Run(sp => service(sp).GetCustomersAsync(1, 100, new CustomerInsightsQuery(Search: regular.CustomerName)));
        Assert.Equal(regular.CustomerId, searched.Items.Single().CustomerId);
        var recent = (await Run(sp => service(sp).GetCustomersAsync(1, 100, new CustomerInsightsQuery(Sort: CustomerInsightsSort.Recent)))).Items.ToList();
        Assert.True(recent.FindIndex(x => x.CustomerId == newcomer.CustomerId) < recent.FindIndex(x => x.CustomerId == slipping.CustomerId));
        // A window that ends before they started buying leaves them out.
        var earlier = await Run(sp => service(sp).GetCustomersAsync(1, 100, new CustomerInsightsQuery(Today.AddDays(-60), Today.AddDays(-40))));
        Assert.DoesNotContain(earlier.Items, x => x.CustomerId == regular.CustomerId);

        // One customer: months, products in litres, payment mix and the reasons.
        var detail = await Run(sp => service(sp).GetCustomerAsync(regular.CustomerId, null, null, ReportPeriod.DefaultUtcOffsetMinutes));
        Assert.Equal(6, detail.Summary!.PurchaseCount);
        Assert.Equal(12 + (detail.From.Day == 1 ? 0 : 1), detail.ByMonth.Count());
        Assert.Equal(2400m, detail.ByMonth.Sum(m => m.Total));
        var product = detail.TopProducts.Single();
        Assert.Equal((water.ProductId, 240m, "l", 2400m), (product.ProductId, product.QuantityInBaseUnits, product.BaseUnitSymbol, product.Total));
        Assert.Equal((PaymentMethod.Cash, 6), (detail.ByPaymentMethod.Single().PaymentMethod, detail.ByPaymentMethod.Single().PurchaseCount));
        Assert.Equal((0, 6), (detail.DeliveredByRider, detail.BoughtAtCounter));
        Assert.Contains(detail.Reasons, x => x.Contains("every 3 day"));
        Assert.NotNull(detail.TypicalDaysBetweenPurchases);
        var stranger = await CustomerAsync();
        var nothing = await Run(sp => service(sp).GetCustomerAsync(stranger.CustomerId, null, null, ReportPeriod.DefaultUtcOffsetMinutes));
        Assert.Null(nothing.Summary);

        // The owner gives the regular a discount and the slipping customer one too: the suggestions follow.
        await DiscountAsync(regular.CustomerId, 5m);
        await DiscountAsync(slipping.CustomerId, 5m);
        list = (await Run(sp => service(sp).GetCustomersAsync(1, 100, new CustomerInsightsQuery()))).Items.ToList();
        Assert.Equal((CustomerSuggestedAction.None, 5m), (list.Single(x => x.CustomerId == regular.CustomerId).SuggestedAction,
            list.Single(x => x.CustomerId == regular.CustomerId).DiscountPercent!.Value));
        Assert.Equal(CustomerSuggestedAction.ReviewDiscount, list.Single(x => x.CustomerId == slipping.CustomerId).SuggestedAction);

        // The dashboard panel: last 90 days.
        var panel = await Run(sp => sp.GetRequiredService<DashboardService>().GetCustomersAsync(null, null, ReportPeriod.DefaultUtcOffsetMinutes));
        Assert.Equal((Today.AddDays(-89), Today), (panel.From, panel.To));
        Assert.True(panel.ActiveCustomers >= 3);
        Assert.True(panel.SlippingCustomers >= 1);
        Assert.True(panel.CustomersWithDiscount >= 2);
        Assert.True(panel.WalkInRevenue >= 200m);
        Assert.Contains(panel.Slipping, x => x.CustomerId == slipping.CustomerId);
        Assert.Contains(panel.TopCustomers, x => x.CustomerId == regular.CustomerId);
        var summary = await Run(sp => sp.GetRequiredService<DashboardService>().GetSummaryAsync(null, null, ReportPeriod.DefaultUtcOffsetMinutes,
            DashboardSections.Customers));
        Assert.NotNull(summary.Customers);
        Assert.Null((await Run(sp => sp.GetRequiredService<DashboardService>().GetSummaryAsync(null, null, ReportPeriod.DefaultUtcOffsetMinutes,
            DashboardSections.Sales))).Customers);
    }
}

/// <summary>The segment rules on their own.</summary>
public class CustomerSegmentTests
{
    private static readonly DateTime Now = new(2026, 10, 22, 12, 0, 0, DateTimeKind.Utc);

    private static CustomerInsightsService.BuyingStats Stats(decimal spent, DateTime? firstEver, params int[] daysAgo) =>
        new(Guid.NewGuid(), daysAgo.Select(d => Now.AddDays(-d)).ToList(), spent, 0m, firstEver);

    [Fact]
    public void Customers_are_placed_relative_to_each_other()
    {
        var top = Stats(5000m, Now.AddDays(-200), 20, 10, 1);
        var regular = Stats(900m, Now.AddDays(-200), 6, 4, 2);
        var slipping = Stats(800m, Now.AddDays(-200), 40, 38, 36);
        var newcomer = Stats(100m, Now.AddDays(-3), 3);
        var occasional = Stats(150m, Now.AddDays(-300), 50);
        var oneBigOrder = Stats(9000m, Now.AddDays(-300), 5);

        var (results, typical) = CustomerInsightsService.Classify(new[] { top, regular, slipping, newcomer, occasional, oneBigOrder }, Now);
        CustomerSegment SegmentOf(CustomerInsightsService.BuyingStats s) => results.Single(r => r.Stats == s).Segment;

        Assert.Equal(2m, typical); // repeat customers buy every 9.5, 2 and 2 days
        Assert.Equal(CustomerSegment.Top, SegmentOf(top));
        Assert.Equal(CustomerSegment.Regular, SegmentOf(regular));
        Assert.Equal(CustomerSegment.Slipping, SegmentOf(slipping));
        Assert.Equal(CustomerSegment.New, SegmentOf(newcomer));
        Assert.Equal(CustomerSegment.Occasional, SegmentOf(occasional));
        // Ranked first by spend, but one order does not make a loyal customer.
        Assert.Equal((1, CustomerSegment.Occasional), (results.Single(r => r.Stats == oneBigOrder).Rank, SegmentOf(oneBigOrder)));
    }

    [Fact]
    public void A_daily_buyer_away_for_a_few_days_is_not_yet_slipping()
    {
        var daily = Stats(1000m, Now.AddDays(-100), 12, 11, 10, 9, 8, 7, 6, 5);
        var (results, _) = CustomerInsightsService.Classify(new[] { daily }, Now);
        Assert.NotEqual(CustomerSegment.Slipping, results.Single().Segment); // 5 days away, but under the 7-day floor
    }

    [Theory]
    [InlineData(CustomerSegment.Top, null, CustomerSuggestedAction.ConsiderDiscount)]
    [InlineData(CustomerSegment.Regular, null, CustomerSuggestedAction.ConsiderDiscount)]
    [InlineData(CustomerSegment.Regular, 5.0, CustomerSuggestedAction.None)]
    [InlineData(CustomerSegment.Slipping, null, CustomerSuggestedAction.WinBack)]
    [InlineData(CustomerSegment.Slipping, 5.0, CustomerSuggestedAction.ReviewDiscount)]
    [InlineData(CustomerSegment.Occasional, 5.0, CustomerSuggestedAction.ReviewDiscount)]
    [InlineData(CustomerSegment.New, null, CustomerSuggestedAction.None)]
    public void The_suggestion_follows_the_segment_and_any_discount_already_given(CustomerSegment segment, double? discount, CustomerSuggestedAction expected) =>
        Assert.Equal(expected, CustomerInsightsService.SuggestFor(segment, discount is null ? null : (decimal)discount));
}
