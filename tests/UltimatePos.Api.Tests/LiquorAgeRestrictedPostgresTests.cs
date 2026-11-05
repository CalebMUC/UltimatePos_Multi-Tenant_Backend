using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UltimatePos.Application.Business;
using UltimatePos.Application.Business.Dtos;
using UltimatePos.Application.Catalog;
using UltimatePos.Application.Catalog.Dtos;
using UltimatePos.Application.Deliveries;
using UltimatePos.Application.Inventory;
using UltimatePos.Application.Inventory.Dtos;
using UltimatePos.Application.Platform;
using UltimatePos.Application.Sales;
using UltimatePos.Application.Sales.Dtos;
using UltimatePos.Domain.Entities;
using UltimatePos.Domain.Exceptions;
using UltimatePos.Infrastructure.Persistence;
using static UltimatePos.Domain.Enums.PosEnums;

namespace Ultimate.Api.Tests;

/// <summary>
/// A wines and spirits shop: beer and spirits are sold only to a customer whose age the cashier checked (and the sale says how), only
/// within the hours the liquor licence gives, and the owner sees every check in the age-check register. Soda is sold as before.
/// </summary>
public class LiquorAgeRestrictedPostgresTests : IClassFixture<MultiBusinessDatabase>
{
    private const string ShopKraPin = "P051000007L";
    private readonly MultiBusinessDatabase _db;
    public LiquorAgeRestrictedPostgresTests(MultiBusinessDatabase db) => _db = db;

    private Task<T> Run<T>(Func<IServiceProvider, Task<T>> call) => _db.RunAsync(call);

    private sealed record Shelf(Guid Beer, Guid Soda, Guid Bottle, string BeerName);

    private async Task<Guid> SignInToShopAsync()
    {
        var shopId = await Run(sp => sp.GetRequiredService<UltimatePosDbContext>().Businesses.IgnoreQueryFilters()
            .Where(b => b.KraPin == ShopKraPin).Select(b => (Guid?)b.BusinessId).FirstOrDefaultAsync());
        if (shopId is null)
        {
            _db.Caller.UserId = _db.SuperUserId;
            _db.Caller.BusinessId = null;
            _db.Caller.Permissions = null;
            var registered = await Run(sp => sp.GetRequiredService<PlatformService>().RegisterBusinessAsync(new RegisterBusinessRequestDto(
                "Baridi Wines & Spirits", null, BusinessType.Retailer, null, null, ShopKraPin, null, "Nairobi", "0722000777", null, null, null,
                Owner: new BusinessOwnerRequestDto("admin", DbSeeder.DevAdminEmail, null), Sector: BusinessSector.Liquor)));
            shopId = registered.Business.BusinessId;
        }
        if (!_db.HeadOffice.ContainsKey(shopId.Value))
            _db.HeadOffice[shopId.Value] = await Run(sp => sp.GetRequiredService<UltimatePosDbContext>().Branches.IgnoreQueryFilters()
                .Where(b => b.BusinessId == shopId && b.BranchNumber == 0).Select(b => b.BranchId).SingleAsync());
        _db.SignInTo(shopId.Value);
        return shopId.Value;
    }

    /// <summary>A lager (age-restricted, 250 a bottle), a soda (80) and their unit, 24 of each on the shelf.</summary>
    private async Task<Shelf> ShelfAsync()
    {
        var code = $"L{Guid.NewGuid():N}"[..8].ToUpperInvariant();
        return await Run(async sp =>
        {
            var catalog = sp.GetRequiredService<CatalogService>();
            var inventory = sp.GetRequiredService<InventoryService>();
            var bottle = (await catalog.GetUnitsOfMeasureAsync()).Single(u => u.Symbol == "btl").UnitOfMeasureId;
            var category = await catalog.CreateCategoryAsync(new CreateCategoryRequestDto("Drinks " + code, code, null));
            var beer = await catalog.CreateProductAsync(new CreateProductRequestDto("Lager 500ml " + code, null, category.CategoryId,
                ItemType.FinishedGood, TaxClassification.Standard, bottle, 0, IsAgeRestricted: true));
            var soda = await catalog.CreateProductAsync(new CreateProductRequestDto("Soda 500ml " + code, null, category.CategoryId,
                ItemType.FinishedGood, TaxClassification.Standard, bottle, 0));
            // In force a minute ago: the price lookup compares with the database's clock.
            foreach (var (id, price) in new[] { (beer.ProductId, 250m), (soda.ProductId, 80m) })
            {
                await catalog.AddPriceTierAsync(id, new AddPriceTierRequestDto(bottle, PriceType.Retail, price, DateTime.UtcNow.AddMinutes(-1)));
                await inventory.AdjustStockAsync(new CreateStockAdjustmentRequestDto(id, bottle, 24, "Opening stock", 50m));
            }
            return new Shelf(beer.ProductId, soda.ProductId, bottle, beer.Name);
        });
    }

    private static string NewCode() => ("S" + Guid.NewGuid().ToString("N")).ToUpperInvariant()[..10];

    private static CreateSaleRequestDto Sale(Shelf shelf, decimal beers, decimal sodas, AgeCheckMethod? check)
    {
        var lines = new List<CreateSaleLineRequestDto>();
        if (beers > 0)
            lines.Add(new CreateSaleLineRequestDto(shelf.Beer, shelf.Bottle, beers));
        if (sodas > 0)
            lines.Add(new CreateSaleLineRequestDto(shelf.Soda, shelf.Bottle, sodas));
        return new CreateSaleRequestDto(null, PaymentMethod.MpesaReference, null, lines, null, null, null, MpesaReference: NewCode(), AgeCheck: check);
    }

    private Task<SaleDto> SellAsync(CreateSaleRequestDto request) => Run(sp => sp.GetRequiredService<SalesService>().RecordSaleAsync(request));
    private Task<SalePreviewDto> PreviewAsync(CreateSaleRequestDto request) => Run(sp => sp.GetRequiredService<SalesService>().PreviewSaleAsync(request));

    private Task<decimal> OnHandAsync(Guid productId) =>
        Run(async sp => (await sp.GetRequiredService<InventoryService>().GetStockLevelAsync(productId)).QuantityOnHand);

    private Task<BusinessDto> HoursAsync(AgeRestrictedSalesSettingsDto settings) =>
        Run(sp => sp.GetRequiredService<BusinessService>().UpdateAgeRestrictedSalesAsync(settings));

    private static TimeOnly NowEat => TimeOnly.FromDateTime(DateTime.UtcNow.AddHours(3));
    private static TimeOnly Minute(TimeOnly t) => new(t.Hour, t.Minute);
    /// <summary>Selling hours that started an hour ago and end in an hour, every day (across midnight when need be).</summary>
    private static AgeRestrictedSalesSettingsDto OpenNow => new(18, Minute(NowEat.AddHours(-1)), Minute(NowEat.AddHours(1)));
    /// <summary>Selling hours that start in two hours: closed now, every day.</summary>
    private static AgeRestrictedSalesSettingsDto ClosedNow => new(18, Minute(NowEat.AddHours(2)), Minute(NowEat.AddHours(3)));
    private static readonly AgeRestrictedSalesSettingsDto AnyTime = new();

    // ---------------------------------------------------------------- The sector

    [PostgresFact]
    public async Task A_liquor_store_starts_as_a_trader_that_counts_every_bottle_with_crates_and_the_age_check()
    {
        var shopId = await SignInToShopAsync();
        var business = await Run(sp => sp.GetRequiredService<BusinessService>().GetCurrentBusinessAsync());

        Assert.Equal((BusinessSector.Liquor, BusinessKind.Trader, false), (business.Sector, business.Kind, business.AllowNegativeStock));
        Assert.Contains(BusinessModules.AgeRestricted, business.Modules!);
        Assert.Contains(BusinessModules.Containers, business.Modules!);
        Assert.Contains(BusinessModules.Deliveries, business.Modules!);
        Assert.DoesNotContain(BusinessModules.Prescriptions, business.Modules!);
        Assert.Equal(18, business.AgeRestrictedSales!.MinimumAge);

        var units = await Run(async sp => (await sp.GetRequiredService<CatalogService>().GetUnitsOfMeasureAsync()).Select(u => u.Symbol).ToList());
        Assert.Contains("crate", units);
        Assert.Contains("case", units);
        Assert.Contains("btl", units);
        Assert.NotEqual(Guid.Empty, shopId);
    }

    // ---------------------------------------------------------------- The age check at the till

    [PostgresFact]
    public async Task Beer_is_not_sold_until_the_cashier_records_how_the_customers_age_was_checked()
    {
        await SignInToShopAsync();
        await HoursAsync(AnyTime);
        var shelf = await ShelfAsync();

        var preview = await PreviewAsync(Sale(shelf, 2, 1, null));
        Assert.Equal((true, 18, true), (preview.RequiresAgeCheck, preview.MinimumAge, preview.WithinSellingHours));
        Assert.True(preview.Lines.Single(l => l.ProductId == shelf.Beer).IsAgeRestricted);
        Assert.False(preview.Lines.Single(l => l.ProductId == shelf.Soda).IsAgeRestricted);

        var refused = await Assert.ThrowsAsync<AgeCheckRequiredException>(() => SellAsync(Sale(shelf, 2, 1, null)));
        Assert.Contains(shelf.BeerName, refused.Message);
        Assert.Contains("18 or over", refused.Message);
        Assert.Equal(24m, await OnHandAsync(shelf.Beer));

        var sale = await SellAsync(Sale(shelf, 2, 1, AgeCheckMethod.NationalId));
        Assert.Equal((SaleStatus.Completed, AgeCheckMethod.NationalId, 580m), (sale.Status, sale.AgeCheck, sale.TotalAmount));
        Assert.Equal(22m, await OnHandAsync(shelf.Beer));
    }

    [PostgresFact]
    public async Task Soda_alone_needs_no_age_check_and_records_none_even_when_one_is_sent()
    {
        await SignInToShopAsync();
        await HoursAsync(ClosedNow);
        var shelf = await ShelfAsync();

        var plain = await SellAsync(Sale(shelf, 0, 2, null));
        var sent = await SellAsync(Sale(shelf, 0, 1, AgeCheckMethod.Passport));

        Assert.Null(plain.AgeCheck);
        Assert.Null(sent.AgeCheck);
        Assert.False((await PreviewAsync(Sale(shelf, 0, 1, null))).RequiresAgeCheck);
        await HoursAsync(AnyTime);
    }

    [PostgresFact]
    public async Task The_age_check_register_lists_each_sale_of_alcohol_with_how_and_by_whom_the_age_was_checked()
    {
        await SignInToShopAsync();
        await HoursAsync(AnyTime);
        var shelf = await ShelfAsync();
        var byId = await SellAsync(Sale(shelf, 1, 1, AgeCheckMethod.DrivingLicence));
        var regular = await SellAsync(Sale(shelf, 3, 0, AgeCheckMethod.KnownCustomer));
        var sodaOnly = await SellAsync(Sale(shelf, 0, 1, null));

        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3));
        var register = (await Run(sp => sp.GetRequiredService<SalesService>().GetAgeCheckRegisterAsync(today, today, 180))).ToList();

        Assert.DoesNotContain(register, e => e.SaleId == sodaOnly.SaleId);
        var first = register.Single(e => e.SaleId == byId.SaleId);
        Assert.Equal(AgeCheckMethod.DrivingLicence, first.AgeCheck);
        Assert.Equal(shelf.Beer, Assert.Single(first.Lines).ProductId);   // the soda on the same sale is not listed
        var second = register.Single(e => e.SaleId == regular.SaleId);
        Assert.Equal((AgeCheckMethod.KnownCustomer, 3m, 750m), (second.AgeCheck, second.Lines.Single().Quantity, second.Lines.Single().LineTotal));
        Assert.True(register.IndexOf(second) < register.IndexOf(first));  // newest first
    }

    // ---------------------------------------------------------------- Selling hours

    [PostgresFact]
    public async Task Outside_the_licence_hours_beer_is_refused_but_soda_still_sells()
    {
        await SignInToShopAsync();
        var shelf = await ShelfAsync();
        var hours = await HoursAsync(ClosedNow);

        var preview = await PreviewAsync(Sale(shelf, 1, 1, AgeCheckMethod.NationalId));
        Assert.False(preview.WithinSellingHours);
        Assert.Equal(SellingHours.Describe(hours.AgeRestrictedSales!), preview.SellingHours);
        var refused = await Assert.ThrowsAsync<OutsideSellingHoursException>(() => SellAsync(Sale(shelf, 1, 1, AgeCheckMethod.NationalId)));
        Assert.Contains(shelf.BeerName, refused.Message);
        Assert.Equal(24m, await OnHandAsync(shelf.Beer));
        Assert.Equal(SaleStatus.Completed, (await SellAsync(Sale(shelf, 0, 1, null))).Status);

        await HoursAsync(OpenNow);
        Assert.Equal(SaleStatus.Completed, (await SellAsync(Sale(shelf, 1, 0, AgeCheckMethod.NationalId))).Status);
        await HoursAsync(AnyTime);
    }

    [PostgresFact]
    public async Task The_hours_and_age_are_checked_and_saved_to_the_minute()
    {
        await SignInToShopAsync();

        var error = await Assert.ThrowsAsync<ValidationException>(() => HoursAsync(new AgeRestrictedSalesSettingsDto(17,
            new TimeOnly(17, 0), null, new TimeOnly(14, 0), new TimeOnly(14, 0))));
        Assert.Equal(new[] { "minimumAge", "weekdayTo", "weekendTo" }, error.Errors.Keys.Order());
        var weekendOnly = await Assert.ThrowsAsync<ValidationException>(() => HoursAsync(new AgeRestrictedSalesSettingsDto(18,
            null, null, new TimeOnly(14, 0), new TimeOnly(23, 0))));
        Assert.Contains("weekdayFrom", weekendOnly.Errors.Keys);

        var saved = await HoursAsync(new AgeRestrictedSalesSettingsDto(21, new TimeOnly(17, 0, 45), new TimeOnly(2, 0), new TimeOnly(14, 0), new TimeOnly(2, 0)));
        Assert.Equal(new AgeRestrictedSalesSettingsDto(21, new TimeOnly(17, 0), new TimeOnly(2, 0), new TimeOnly(14, 0), new TimeOnly(2, 0)),
            saved.AgeRestrictedSales);
        Assert.Equal(saved.AgeRestrictedSales, (await Run(sp => sp.GetRequiredService<BusinessService>().GetCurrentBusinessAsync())).AgeRestrictedSales);
        await HoursAsync(AnyTime);
    }

    [PostgresFact]
    public async Task Switched_off_beer_sells_like_soda_and_switched_back_on_the_check_returns()
    {
        await SignInToShopAsync();
        var shelf = await ShelfAsync();

        var off = await HoursAsync(ClosedNow with { Enabled = false });
        Assert.False(off.AgeRestrictedSales!.Enabled);
        Assert.DoesNotContain(BusinessModules.AgeRestricted, off.Modules!);
        var preview = await PreviewAsync(Sale(shelf, 1, 0, null));
        Assert.Equal((false, true), (preview.RequiresAgeCheck, preview.WithinSellingHours));
        Assert.False(preview.Lines.Single().IsAgeRestricted);
        // Outside the hours that were set, and with no age check: off means off.
        var sale = await SellAsync(Sale(shelf, 1, 0, null));
        Assert.Equal((SaleStatus.Completed, (AgeCheckMethod?)null), (sale.Status, sale.AgeCheck));

        // The product keeps its mark, so switching back on brings the checks back.
        var on = await HoursAsync(AnyTime);
        Assert.True(on.AgeRestrictedSales!.Enabled);
        Assert.Contains(BusinessModules.AgeRestricted, on.Modules!);
        Assert.True((await PreviewAsync(Sale(shelf, 1, 0, null))).RequiresAgeCheck);
        await Assert.ThrowsAsync<AgeCheckRequiredException>(() => SellAsync(Sale(shelf, 1, 0, null)));
    }

    // ---------------------------------------------------------------- The product flag

    [PostgresFact]
    public async Task Editing_a_product_without_the_flag_keeps_it_age_restricted()
    {
        await SignInToShopAsync();
        var shelf = await ShelfAsync();

        var updated = await Run(async sp =>
        {
            var catalog = sp.GetRequiredService<CatalogService>();
            var beer = await catalog.GetProductByIdAsync(shelf.Beer);
            await catalog.UpdateProductAsync(shelf.Beer, new UpdateProductRequestDto(beer.Name + " (cold)", null, beer.CategoryId, beer.ItemType,
                beer.TaxClassification, beer.BaseUnitOfMeasureId, 6));
            return await catalog.GetProductByIdAsync(shelf.Beer);
        });
        Assert.True(updated.IsAgeRestricted);

        var cleared = await Run(async sp =>
        {
            var catalog = sp.GetRequiredService<CatalogService>();
            await catalog.UpdateProductAsync(shelf.Beer, new UpdateProductRequestDto(updated.Name, null, updated.CategoryId, updated.ItemType,
                updated.TaxClassification, updated.BaseUnitOfMeasureId, 6, IsAgeRestricted: false));
            return await catalog.GetProductsAsync(1, 50, new ProductQuery(IsAgeRestricted: true));
        });
        Assert.DoesNotContain(cleared.Items, p => p.ProductId == shelf.Beer);
    }

    // ---------------------------------------------------------------- Deliveries

    [PostgresFact]
    public async Task A_night_delivery_of_beer_leaves_only_in_hours_and_is_completed_only_with_the_riders_age_check()
    {
        await SignInToShopAsync();
        var shelf = await ShelfAsync();
        var customer = await Run(sp => sp.GetRequiredService<BusinessService>().RegisterCustomerAsync(new RegisterCustomerRequestDto(
            null, "Apartments " + Guid.NewGuid().ToString("N")[..6], null, null, "0744" + Random.Shared.Next(100000, 999999), null, "Zimmerman")));
        var deliveries = (Func<IServiceProvider, DeliveryOrderService>)(sp => sp.GetRequiredService<DeliveryOrderService>());

        // Taken after hours, it waits; it cannot go out with a rider until the hours open.
        await HoursAsync(ClosedNow);
        var order = await Run(sp => deliveries(sp).CreateAsync(new CreateDeliveryOrderRequestDto(customer.CustomerId, "Block C, House 4", null, null,
            new[] { new DeliveryOrderLineRequestDto(shelf.Beer, shelf.Bottle, 6), new DeliveryOrderLineRequestDto(shelf.Soda, shelf.Bottle, 2) })));
        await Assert.ThrowsAsync<OutsideSellingHoursException>(() => Run(sp => deliveries(sp).DispatchAsync(order.DeliveryOrderId,
            new DispatchDeliveryOrderRequestDto("Kevin"))));
        await Assert.ThrowsAsync<OutsideSellingHoursException>(() => Run(sp => deliveries(sp).CreateAsync(new CreateDeliveryOrderRequestDto(
            customer.CustomerId, "Block C, House 4", null, null, new[] { new DeliveryOrderLineRequestDto(shelf.Beer, shelf.Bottle, 1) },
            RiderName: "Kevin"))));

        await HoursAsync(OpenNow);
        await Run(sp => deliveries(sp).DispatchAsync(order.DeliveryOrderId, new DispatchDeliveryOrderRequestDto("Kevin")));

        // The rider is back after closing: the goods left in time, so only the age check is asked for.
        await HoursAsync(ClosedNow);
        await Assert.ThrowsAsync<AgeCheckRequiredException>(() => Run(sp => deliveries(sp).DeliverAsync(order.DeliveryOrderId,
            new DeliverDeliveryOrderRequestDto(PaymentMethod.MpesaReference, MpesaReference: NewCode()))));
        var delivered = await Run(sp => deliveries(sp).DeliverAsync(order.DeliveryOrderId,
            new DeliverDeliveryOrderRequestDto(PaymentMethod.MpesaReference, MpesaReference: NewCode(), AgeCheck: AgeCheckMethod.NationalId)));

        Assert.Equal(DeliveryOrderStatus.Delivered, delivered.Status);
        var sale = await Run(sp => sp.GetRequiredService<UltimatePosDbContext>().Sales.AsNoTracking()
            .SingleAsync(s => s.Lines.Any(l => l.ProductId == shelf.Beer)));
        Assert.Equal((AgeCheckMethod.NationalId, DeliveryMode.Rider), (sale.AgeCheck, sale.DeliveryMode));
        await HoursAsync(AnyTime);
    }
}
