using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UltimatePos.Application.CashSessions;
using UltimatePos.Application.Catalog;
using UltimatePos.Application.Catalog.Dtos;
using UltimatePos.Application.Inventory;
using UltimatePos.Application.Inventory.Dtos;
using UltimatePos.Application.Notifications;
using UltimatePos.Application.Platform;
using UltimatePos.Application.Business.Dtos;
using UltimatePos.Application.Sales;
using UltimatePos.Application.Sales.Dtos;
using UltimatePos.Domain.Entities;
using UltimatePos.Domain.Exceptions;
using UltimatePos.Infrastructure.Persistence;
using static UltimatePos.Domain.Enums.PosEnums;

namespace Ultimate.Api.Tests;

/// <summary>
/// The power went off for a day and the shop wrote its sales in a paper book. Once it is back, a manager enters them with the time they
/// were made (up to 3 days back): each sale is held to the stock there was at its time, not to what is on the shelf now, and is dated
/// then on the sales list, the stock card and the cash-up.
/// </summary>
public class BackdatedSalesPostgresTests : IClassFixture<MultiBusinessDatabase>
{
    private const string ShopKraPin = "P051000031K";
    private const string Reason = "Power failure, entered from the paper book";
    private readonly MultiBusinessDatabase _db;
    public BackdatedSalesPostgresTests(MultiBusinessDatabase db) => _db = db;

    private Task<T> Run<T>(Func<IServiceProvider, Task<T>> call) => _db.RunAsync(call);

    private sealed record Item(Guid ProductId, Guid Unit);

    private async Task SignInAsync(params string[] permissions)
    {
        var shopId = await Run(sp => sp.GetRequiredService<UltimatePosDbContext>().Businesses.IgnoreQueryFilters()
            .Where(b => b.KraPin == ShopKraPin).Select(b => (Guid?)b.BusinessId).FirstOrDefaultAsync());
        if (shopId is null)
        {
            _db.Caller.UserId = _db.SuperUserId;
            _db.Caller.BusinessId = null;
            _db.Caller.Permissions = null;
            var registered = await Run(sp => sp.GetRequiredService<PlatformService>().RegisterBusinessAsync(new RegisterBusinessRequestDto(
                "Mwangaza Minimart", null, BusinessType.Retailer, null, null, ShopKraPin, null, "Nakuru", null, null, null, null,
                Owner: new BusinessOwnerRequestDto("admin", DbSeeder.DevAdminEmail, null), AllowNegativeStock: false,
                Sector: BusinessSector.Minimart)));
            shopId = registered.Business.BusinessId;
        }
        if (!_db.HeadOffice.ContainsKey(shopId.Value))
            _db.HeadOffice[shopId.Value] = await Run(sp => sp.GetRequiredService<UltimatePosDbContext>().Branches.IgnoreQueryFilters()
                .Where(b => b.BusinessId == shopId && b.BranchNumber == 0).Select(b => b.BranchId).SingleAsync());
        _db.SignInTo(shopId.Value, permissions);
    }

    /// <summary>A product at 100 a piece, with <paramref name="openingStock"/> received at <paramref name="stockedAt"/> (now when null).</summary>
    private async Task<Item> ProductAsync(decimal openingStock, DateTime? stockedAt = null)
    {
        var code = $"B{Guid.NewGuid():N}"[..8].ToUpperInvariant();
        var item = await Run(async sp =>
        {
            var catalog = sp.GetRequiredService<CatalogService>();
            var piece = (await catalog.GetUnitsOfMeasureAsync()).FirstOrDefault(u => u.Symbol == "pcs")?.UnitOfMeasureId
                ?? (await catalog.CreateUnitOfMeasureAsync(new CreateUnitOfMeasureRequestDto("Piece", "pcs"))).UnitOfMeasureId;
            var category = await catalog.CreateCategoryAsync(new CreateCategoryRequestDto("Groceries " + code, code, null));
            var product = await catalog.CreateProductAsync(new CreateProductRequestDto("Sugar 1kg " + code, null, category.CategoryId,
                ItemType.FinishedGood, TaxClassification.Standard, piece, 0));
            // In force well before any backdated sale here: the price lookup compares with the clock.
            await catalog.AddPriceTierAsync(product.ProductId, new AddPriceTierRequestDto(piece, PriceType.Retail, 100m, DateTime.UtcNow.AddDays(-10)));
            if (openingStock > 0)
                await sp.GetRequiredService<InventoryService>().AdjustStockAsync(
                    new CreateStockAdjustmentRequestDto(product.ProductId, piece, openingStock, "Opening stock", 60m));
            return new Item(product.ProductId, piece);
        });
        if (stockedAt is { } at)
            await MoveStockToAsync(item.ProductId, at);
        return item;
    }

    /// <summary>Dates every stock movement of the product so far at <paramref name="at"/>, as if it had been received then.</summary>
    private Task<int> MoveStockToAsync(Guid productId, DateTime at) =>
        Run(sp => sp.GetRequiredService<UltimatePosDbContext>().Database.ExecuteSqlInterpolatedAsync(
            $"""UPDATE "StockMovements" SET "CreatedAt" = {at} WHERE "ProductId" = {productId}"""));

    private static string NewCode() => ("S" + Guid.NewGuid().ToString("N")).ToUpperInvariant()[..10];

    private static CreateSaleRequestDto Sale(Item item, decimal quantity, DateTime? soldAt, PaymentMethod method = PaymentMethod.MpesaReference,
        string? reason = Reason) =>
        new(null, method, null, new[] { new CreateSaleLineRequestDto(item.ProductId, item.Unit, quantity) },
            method == PaymentMethod.Cash ? quantity * 100m : null, method == PaymentMethod.MpesaStkPush ? "0712345678" : null, null,
            MpesaReference: method == PaymentMethod.MpesaReference ? NewCode() : null,
            SoldAt: soldAt is { } at ? new DateTimeOffset(at, TimeSpan.Zero) : null, BackdateReason: reason);

    private Task<SaleDto> SellAsync(CreateSaleRequestDto request) => Run(sp => sp.GetRequiredService<SalesService>().RecordSaleAsync(request));

    private Task<decimal> OnHandAsync(Guid productId) =>
        Run(async sp => (await sp.GetRequiredService<InventoryService>().GetStockLevelAsync(productId)).QuantityOnHand);

    private static void AssertSameMoment(DateTime expected, DateTime actual) =>
        Assert.True(Math.Abs((expected - actual).TotalMilliseconds) < 1, $"Expected {expected:O}, got {actual:O}.");

    // ---------------------------------------------------------------- Dated when it happened

    [PostgresFact]
    public async Task A_sale_from_the_paper_book_is_dated_when_it_happened_with_its_stock()
    {
        await SignInAsync();
        var soldAt = DateTime.UtcNow.AddDays(-1);
        var sugar = await ProductAsync(10, stockedAt: DateTime.UtcNow.AddDays(-2));

        var sale = await SellAsync(Sale(sugar, 4, soldAt));

        AssertSameMoment(soldAt, sale.CreatedAt);
        Assert.NotNull(sale.RecordedAt);
        Assert.True(sale.RecordedAt > DateTime.UtcNow.AddMinutes(-1));
        Assert.Equal((SaleStatus.Completed, 400m, Reason), (sale.Status, sale.TotalAmount, sale.BackdateReason));
        Assert.Equal(6m, await OnHandAsync(sugar.ProductId));

        // The stock card shows it on the day it happened.
        var movement = (await Run(sp => sp.GetRequiredService<InventoryService>().GetStockMovementsAsync(sugar.ProductId)))
            .Single(m => m.ReferenceId == sale.SaleId);
        Assert.Equal(-4m, movement.QuantityChange);
        AssertSameMoment(soldAt, movement.CreatedAt);

        // It is on the audit log, and the sales list can show only the late entries.
        var logged = await Run(sp => sp.GetRequiredService<UltimatePosDbContext>().SystemEvents.IgnoreQueryFilters()
            .SingleAsync(e => e.EntityId == sale.SaleId && e.EventType == SystemEventTypes.SaleBackdated));
        Assert.Contains(Reason, logged.Summary);
        var late = await Run(sp => sp.GetRequiredService<SalesService>().GetSalesAsync(1, 100, new SaleQuery(Backdated: true)));
        Assert.Contains(late.Items, s => s.SaleId == sale.SaleId);
        var onTime = await Run(sp => sp.GetRequiredService<SalesService>().GetSalesAsync(1, 100, new SaleQuery(Backdated: false)));
        Assert.DoesNotContain(onTime.Items, s => s.SaleId == sale.SaleId);

        // A sale entered as it happens is not backdated, even when the till sends its own clock.
        var now = await SellAsync(Sale(sugar, 1, DateTime.UtcNow.AddSeconds(-5)));
        Assert.Null(now.RecordedAt);
        Assert.Null(now.BackdateReason);
    }

    // ---------------------------------------------------------------- The stock at the time

    [PostgresFact]
    public async Task Stock_received_after_the_sale_cannot_be_sold_by_it()
    {
        await SignInAsync();
        var soldAt = DateTime.UtcNow.AddDays(-1);
        var sugar = await ProductAsync(20); // delivered today, after the power failure

        var preview = await Run(sp => sp.GetRequiredService<SalesService>().PreviewSaleAsync(Sale(sugar, 5, soldAt)));
        var line = Assert.Single(preview.Lines);
        Assert.Equal((0m, false, false), (line.AvailableInBaseUnits, line.StockSufficient, preview.AllStockSufficient));
        Assert.NotNull(preview.SoldAt);

        var refused = await Assert.ThrowsAsync<InsufficientStockException>(() => SellAsync(Sale(sugar, 5, soldAt)));
        Assert.Contains("only the stock there was then", refused.Message);
        Assert.Equal(20m, await OnHandAsync(sugar.ProductId));
    }

    [PostgresFact]
    public async Task Slips_entered_out_of_order_cannot_together_sell_more_than_the_shelf_held()
    {
        await SignInAsync();
        var sugar = await ProductAsync(6, stockedAt: DateTime.UtcNow.AddDays(-2));
        var tenOClock = DateTime.UtcNow.AddDays(-1);

        await SellAsync(Sale(sugar, 5, tenOClock));
        // Earlier in the day there were still 6, but 3 more then would have left the 10 o'clock sale short.
        await Assert.ThrowsAsync<InsufficientStockException>(() => SellAsync(Sale(sugar, 3, tenOClock.AddHours(-2))));
        await SellAsync(Sale(sugar, 1, tenOClock.AddHours(-2)));

        Assert.Equal(0m, await OnHandAsync(sugar.ProductId));
    }

    [PostgresFact]
    public async Task Stock_sold_since_is_not_there_for_a_backdated_sale()
    {
        await SignInAsync();
        var sugar = await ProductAsync(10, stockedAt: DateTime.UtcNow.AddDays(-2));
        await SellAsync(Sale(sugar, 8, soldAt: null)); // sold today, after the power came back

        // There were 10 yesterday, but 8 have been sold since: only 2 can have gone in the paper book.
        await Assert.ThrowsAsync<InsufficientStockException>(() => SellAsync(Sale(sugar, 5, DateTime.UtcNow.AddDays(-1))));
        await SellAsync(Sale(sugar, 2, DateTime.UtcNow.AddDays(-1)));
        Assert.Equal(0m, await OnHandAsync(sugar.ProductId));
    }

    // ---------------------------------------------------------------- Limits

    [PostgresFact]
    public async Task A_sale_can_be_backdated_up_to_three_days_by_someone_allowed_to_with_a_reason()
    {
        await SignInAsync();
        var sugar = await ProductAsync(50, stockedAt: DateTime.UtcNow.AddDays(-10));

        await Assert.ThrowsAsync<ValidationException>(() => SellAsync(Sale(sugar, 1, DateTime.UtcNow.AddDays(-4))));
        await Assert.ThrowsAsync<ValidationException>(() => SellAsync(Sale(sugar, 1, DateTime.UtcNow.AddHours(1))));
        await Assert.ThrowsAsync<ValidationException>(() => SellAsync(Sale(sugar, 1, DateTime.UtcNow.AddDays(-1), reason: " ")));
        await Assert.ThrowsAsync<ValidationException>(() => SellAsync(Sale(sugar, 1, DateTime.UtcNow.AddDays(-1), PaymentMethod.MpesaStkPush)));
        // Noon (East Africa Time), three calendar days ago: the earliest day allowed.
        var threeDaysAgoAtNoon = DateTime.UtcNow.AddHours(3).Date.AddDays(-3).AddHours(12 - 3);
        await SellAsync(Sale(sugar, 1, threeDaysAgoAtNoon));

        // A cashier can sell, but not backdate.
        await SignInAsync("Sales.Create", "Sales.View");
        await Assert.ThrowsAsync<ForbiddenException>(() => SellAsync(Sale(sugar, 1, DateTime.UtcNow.AddDays(-1))));
        await SellAsync(Sale(sugar, 1, soldAt: null));
    }

    // ---------------------------------------------------------------- The cash-up

    [PostgresFact]
    public async Task A_backdated_cash_sale_goes_into_the_open_drawer_of_its_time_but_never_into_a_counted_one()
    {
        await SignInAsync();
        var sugar = await ProductAsync(50, stockedAt: DateTime.UtcNow.AddDays(-3));
        var sessions = (Func<IServiceProvider, CashSessionService>)(sp => sp.GetRequiredService<CashSessionService>());
        var openedAt = DateTime.UtcNow.AddDays(-2);
        Task<int> OpenedAt(Guid id, DateTime at) => Run(sp => sp.GetRequiredService<UltimatePosDbContext>().Database.ExecuteSqlInterpolatedAsync(
            $"""UPDATE "CashSessions" SET "OpenedAt" = {at} WHERE "CashSessionId" = {id}"""));

        // The drawer opened before the power failed is still open: the paper book's cash is in it.
        var drawer = await Run(sp => sessions(sp).OpenAsync(new OpenCashSessionRequestDto(1000m, null)));
        await OpenedAt(drawer.CashSessionId, openedAt);
        await SellAsync(Sale(sugar, 3, DateTime.UtcNow.AddDays(-1), PaymentMethod.Cash));
        var counted = await Run(sp => sessions(sp).GetAsync(drawer.CashSessionId));
        Assert.Equal((300m, 1300m), (counted.Breakdown.CashSales, counted.Breakdown.ExpectedCash));

        // Once it has been counted, its figures are fixed.
        await Run(sp => sessions(sp).CloseAsync(drawer.CashSessionId, new CloseCashSessionRequestDto(1300m, null)));
        var refused = await Assert.ThrowsAsync<InvalidAssignmentException>(() =>
            SellAsync(Sale(sugar, 2, DateTime.UtcNow.AddDays(-1).AddHours(1), PaymentMethod.Cash)));
        Assert.Contains(drawer.SessionNumber, refused.Message);
        // Paid another way, it does not touch the drawer.
        await SellAsync(Sale(sugar, 2, DateTime.UtcNow.AddDays(-1).AddHours(1)));
        Assert.Equal(1300m, (await Run(sp => sessions(sp).GetAsync(drawer.CashSessionId))).Breakdown.ExpectedCash);
    }
}
