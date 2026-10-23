using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UltimatePos.Application.Business;
using UltimatePos.Application.Business.Dtos;
using UltimatePos.Application.Catalog;
using UltimatePos.Application.Catalog.Dtos;
using UltimatePos.Application.Customers;
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
/// The station's monthly-spend reward: once a customer has bought 4000 in a calendar month, 20% off their later purchases that month;
/// back to normal prices when the month changes. Its own station, so switching the reward on cannot change other tests' prices.
/// </summary>
public class MonthlySpendDiscountPostgresTests : IClassFixture<MultiBusinessDatabase>
{
    private const string StationKraPin = "P051000013G";
    private const decimal Refill20 = 200m;
    private static readonly DateOnly Today = ReportPeriod.TodayAt(ReportPeriod.DefaultUtcOffsetMinutes);

    private readonly MultiBusinessDatabase _db;
    public MonthlySpendDiscountPostgresTests(MultiBusinessDatabase db) => _db = db;

    private Task<T> Run<T>(Func<IServiceProvider, Task<T>> call) => _db.RunAsync(call);

    private sealed record Water(Guid ProductId, Guid TwentyLitre);

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
                "Test Refill Station 4", null, BusinessType.Retailer, BusinessKind.Trader, null, StationKraPin, null, "Kiambu", null, null, null, null,
                Owner: new BusinessOwnerRequestDto("admin", DbSeeder.DevAdminEmail, null), AllowNegativeStock: false)));
            stationId = registered.Business.BusinessId;
        }
        if (!_db.HeadOffice.ContainsKey(stationId.Value))
            _db.HeadOffice[stationId.Value] = await Run(sp => sp.GetRequiredService<UltimatePosDbContext>().Branches.IgnoreQueryFilters()
                .Where(b => b.BusinessId == stationId && b.BranchNumber == 0).Select(b => b.BranchId).SingleAsync());
        _db.SignInTo(stationId.Value, permissions);
        return stationId.Value;
    }

    private async Task<Water> WaterAsync()
    {
        var units = await Run(async sp =>
        {
            var catalog = sp.GetRequiredService<CatalogService>();
            var existing = (await catalog.GetUnitsOfMeasureAsync()).ToList();
            async Task<Guid> Unit(string name, string symbol) => existing.FirstOrDefault(u => u.Symbol == symbol)?.UnitOfMeasureId
                ?? (await catalog.CreateUnitOfMeasureAsync(new CreateUnitOfMeasureRequestDto(name, symbol))).UnitOfMeasureId;
            return (Litre: await Unit("Litre", "l"), Twenty: await Unit("20 Litre Refill", "20L"));
        });
        var code = $"W{Guid.NewGuid():N}"[..8].ToUpperInvariant();
        return await Run(async sp =>
        {
            var catalog = sp.GetRequiredService<CatalogService>();
            var category = await catalog.CreateCategoryAsync(new CreateCategoryRequestDto("Water " + code, code, null));
            var water = await catalog.CreateProductAsync(new CreateProductRequestDto(
                "Water refill " + code, null, category.CategoryId, ItemType.FinishedGood, TaxClassification.Exempt, units.Litre, 100));
            await catalog.AddUnitConversionAsync(water.ProductId, new CreateProductUnitConversionRequestDto(units.Twenty, 20));
            await catalog.AddPriceTierAsync(water.ProductId, new AddPriceTierRequestDto(units.Twenty, PriceType.Retail, Refill20, DateTime.UtcNow.AddMinutes(-1)));
            await sp.GetRequiredService<InventoryService>().AdjustStockAsync(new CreateStockAdjustmentRequestDto(water.ProductId, units.Litre, 5000, "Opening stock", 1.5m));
            return new Water(water.ProductId, units.Twenty);
        });
    }

    private Task<CustomerDto> CustomerAsync() =>
        Run(sp => sp.GetRequiredService<BusinessService>().RegisterCustomerAsync(new RegisterCustomerRequestDto(
            null, "Customer " + Guid.NewGuid().ToString("N")[..6], null, null, "0766" + Random.Shared.Next(100000, 999999), null, "Thika")));

    private static CreateSaleRequestDto Cash(Guid? customerId, Water water, decimal refills) =>
        new(customerId, PaymentMethod.Cash, null, new[] { new CreateSaleLineRequestDto(water.ProductId, water.TwentyLitre, refills) }, 100_000m, null, null);

    private Task<SaleDto> SellAsync(Guid? customerId, Water water, decimal refills) =>
        Run(sp => sp.GetRequiredService<SalesService>().RecordSaleAsync(Cash(customerId, water, refills)));

    private Task<SalePreviewDto> PreviewAsync(Guid? customerId, Water water, decimal refills) =>
        Run(sp => sp.GetRequiredService<SalesService>().PreviewSaleAsync(Cash(customerId, water, refills)));

    private Task<BusinessDto> RewardAsync(decimal? threshold, decimal? percent, MonthlyDiscountScope scope = MonthlyDiscountScope.RestOfMonth) =>
        Run(sp => sp.GetRequiredService<BusinessService>().UpdateMonthlyDiscountAsync(new UpdateMonthlyDiscountRequestDto(threshold, percent, scope)));

    /// <summary>Moves all of a customer's sales into last month, as if the month had changed since.</summary>
    private Task LastMonthAsync(Guid customerId)
    {
        var monthStart = new DateOnly(Today.Year, Today.Month, 1);
        var lastMonth = ReportPeriod.StartOfLocalDayUtc(monthStart, TimeSpan.FromMinutes(ReportPeriod.DefaultUtcOffsetMinutes)).AddDays(-1);
        return Run(sp => sp.GetRequiredService<UltimatePosDbContext>().Sales.IgnoreQueryFilters()
            .Where(s => s.CustomerId == customerId).ExecuteUpdateAsync(u => u.SetProperty(s => s.CreatedAt, lastMonth)));
    }

    [PostgresFact]
    public async Task After_4000_in_a_month_later_purchases_get_20_percent_off_until_the_month_changes()
    {
        var stationId = await SignInToStationAsync();
        var water = await WaterAsync();
        var customer = await CustomerAsync();

        // Settings: both or neither, sensible values.
        await Assert.ThrowsAsync<ValidationException>(() => RewardAsync(4000m, null));
        await Assert.ThrowsAsync<ValidationException>(() => RewardAsync(null, 20m));
        await Assert.ThrowsAsync<ValidationException>(() => RewardAsync(4000m, 100m));
        await Assert.ThrowsAsync<ValidationException>(() => RewardAsync(0m, 20m));
        var business = await RewardAsync(4000m, 20m);
        Assert.Equal((4000m, 20m, MonthlyDiscountScope.RestOfMonth),
            (business.MonthlyDiscountThreshold!.Value, business.MonthlyDiscountPercent!.Value, business.MonthlyDiscountScope));
        // The business's cashier discount cap does not stop the reward.
        await Run(sp => sp.GetRequiredService<UltimatePosDbContext>().Businesses.IgnoreQueryFilters().Where(b => b.BusinessId == stationId)
            .ExecuteUpdateAsync(u => u.SetProperty(b => b.MaxDiscountPercent, 10m)));

        // 2000 + 1800 = 3800: full price. Then 400 takes them to 4200: that purchase is still full price.
        Assert.Equal(2000m, (await SellAsync(customer.CustomerId, water, 10)).TotalAmount);
        Assert.Equal(1800m, (await SellAsync(customer.CustomerId, water, 9)).TotalAmount);
        var preview = await PreviewAsync(customer.CustomerId, water, 2);
        Assert.Equal((3800m, 200m, false, 400m), (preview.CustomerMonth!.Spent, preview.CustomerMonth.RemainingToThreshold, preview.CustomerMonth.Qualifies,
            preview.TotalAmount));
        var crossing = await SellAsync(customer.CustomerId, water, 2);
        Assert.Equal((400m, (CustomerDiscountSource?)null), (crossing.TotalAmount, crossing.CustomerDiscountSource));

        // From the next purchase: 20% off, and it says why.
        preview = await PreviewAsync(customer.CustomerId, water, 1);
        Assert.Equal((4200m, 0m, true), (preview.CustomerMonth!.Spent, preview.CustomerMonth.RemainingToThreshold, preview.CustomerMonth.Qualifies));
        Assert.Equal((160m, 20m, 40m, CustomerDiscountSource.MonthlySpend),
            (preview.TotalAmount, preview.CustomerDiscountPercent!.Value, preview.CustomerDiscountTotal, preview.CustomerDiscountSource!.Value));
        // A cashier with no discount permission charges it.
        await SignInToStationAsync("Sales.Create", "Sales.View");
        var rewarded = await SellAsync(customer.CustomerId, water, 1);
        Assert.Equal((160m, 40m, CustomerDiscountSource.MonthlySpend), (rewarded.TotalAmount, rewarded.CustomerDiscountAmount, rewarded.CustomerDiscountSource!.Value));
        // ...and every later purchase this month.
        Assert.Equal(320m, (await SellAsync(customer.CustomerId, water, 2)).TotalAmount);
        // Walk-ins are never rewarded.
        Assert.Equal(200m, (await SellAsync(null, water, 1)).TotalAmount);

        // The customer's Buying tab shows the month.
        await SignInToStationAsync();
        var insights = await Run(sp => sp.GetRequiredService<CustomerInsightsService>().GetCustomerAsync(customer.CustomerId, null, null, ReportPeriod.DefaultUtcOffsetMinutes));
        Assert.Equal((4680m, true), (insights.ThisMonth!.Spent, insights.ThisMonth.Qualifies));

        // A new month: normal prices again, counting from zero.
        await LastMonthAsync(customer.CustomerId);
        preview = await PreviewAsync(customer.CustomerId, water, 1);
        Assert.Equal((0m, false, 200m), (preview.CustomerMonth!.Spent, preview.CustomerMonth.Qualifies, preview.TotalAmount));
        Assert.Equal(200m, (await SellAsync(customer.CustomerId, water, 1)).TotalAmount);

        // Switched off: no reward, and the preview no longer reports the month.
        await RewardAsync(null, null);
        Assert.Null((await PreviewAsync(customer.CustomerId, water, 1)).CustomerMonth);
    }

    [PostgresFact]
    public async Task With_next_purchase_only_the_reward_is_given_once_a_month_and_the_larger_discount_wins()
    {
        await SignInToStationAsync();
        var water = await WaterAsync();
        var customer = await CustomerAsync();
        await RewardAsync(4000m, 20m, MonthlyDiscountScope.NextPurchaseOnly);

        await SellAsync(customer.CustomerId, water, 20); // 4000: reaches it
        Assert.Equal(160m, (await SellAsync(customer.CustomerId, water, 1)).TotalAmount);
        Assert.Equal(200m, (await SellAsync(customer.CustomerId, water, 1)).TotalAmount); // already used this month

        // A returned purchase does not count towards the month.
        var other = await CustomerAsync();
        var big = await SellAsync(other.CustomerId, water, 20);
        await Run(sp => sp.GetRequiredService<SalesService>().CreateReturnAsync(big.SaleId, new CreateSaleReturnRequestDto("Leaking bottle",
            RefundMethod.Cash, null, new[] { new CreateSaleReturnLineRequestDto(big.Lines.Single().SaleLineId, 1) })));
        var preview = await PreviewAsync(other.CustomerId, water, 1);
        Assert.Equal((3800m, false), (preview.CustomerMonth!.Spent, preview.CustomerMonth.Qualifies));

        // A standing discount and the reward do not add up: the larger applies.
        await Run(sp => sp.GetRequiredService<CustomerInsightsService>().SetDiscountAsync(other.CustomerId, new SetCustomerDiscountRequestDto(10m, "Neighbour")));
        preview = await PreviewAsync(other.CustomerId, water, 1);
        Assert.Equal((180m, CustomerDiscountSource.Standing), (preview.TotalAmount, preview.CustomerDiscountSource!.Value));
        await SellAsync(other.CustomerId, water, 2); // 3800 + 360 = 4160
        preview = await PreviewAsync(other.CustomerId, water, 1);
        Assert.Equal((160m, CustomerDiscountSource.MonthlySpend), (preview.TotalAmount, preview.CustomerDiscountSource!.Value));

        await RewardAsync(null, null);
    }
}
