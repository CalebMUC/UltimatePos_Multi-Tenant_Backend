using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UltimatePos.Application.Accounting;
using UltimatePos.Application.Accounting.Dtos;
using UltimatePos.Application.Business;
using UltimatePos.Application.Business.Dtos;
using UltimatePos.Application.CashSessions;
using UltimatePos.Application.Catalog;
using UltimatePos.Application.Catalog.Dtos;
using UltimatePos.Application.Deliveries;
using UltimatePos.Application.Expenses;
using UltimatePos.Application.Expenses.Dtos;
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
/// The station's second round of requirements (docs/WATER_REFILL_STATION_ONBOARDING.md §6.2–§8.1): choosing delivery by rider at the
/// till, a delivery that is not a sale until the rider is back with the cash or the M-Pesa code, deliveries that come back, trips weighed
/// against fuel, and the day's float and cash-up. Prices and quantities here are test data, not the station's.
/// </summary>
public class WaterDeliveryAndCashUpPostgresTests : IClassFixture<MultiBusinessDatabase>
{
    private const string StationKraPin = "P051000007E";
    private const decimal Refill20 = 200m;
    private static readonly DateOnly Today = ReportPeriod.TodayAt(ReportPeriod.DefaultUtcOffsetMinutes);

    private readonly MultiBusinessDatabase _db;
    public WaterDeliveryAndCashUpPostgresTests(MultiBusinessDatabase db) => _db = db;

    private Task<T> Run<T>(Func<IServiceProvider, Task<T>> call) => _db.RunAsync(call);

    private sealed record Water(Guid ProductId, Guid Litre, Guid TwentyLitre, Guid Piece, Guid Bottle);

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
                "Test Refill Station 2", null, BusinessType.Retailer, BusinessKind.Trader, null, StationKraPin, null, "Kiambu", null, null, null, null,
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

    /// <summary>Water in litres with a 20L refill, 100 litres in the tank, and a 20L bottle (10 on the shelf) sold only in a bundle.</summary>
    private async Task<Water> WaterAsync()
    {
        var litre = await UnitAsync("Litre", "l");
        var twenty = await UnitAsync("20 Litre Refill", "20L");
        var piece = await UnitAsync("Piece", "pcs");
        var code = $"W{Guid.NewGuid():N}"[..8].ToUpperInvariant();
        return await Run(async sp =>
        {
            var catalog = sp.GetRequiredService<CatalogService>();
            var inventory = sp.GetRequiredService<InventoryService>();
            var category = await catalog.CreateCategoryAsync(new CreateCategoryRequestDto("Water " + code, code, null));
            var water = await catalog.CreateProductAsync(new CreateProductRequestDto(
                "Water refill " + code, null, category.CategoryId, ItemType.FinishedGood, TaxClassification.Exempt, litre, 100));
            await catalog.AddUnitConversionAsync(water.ProductId, new CreateProductUnitConversionRequestDto(twenty, 20));
            var from = DateTime.UtcNow.AddMinutes(-1);
            await catalog.AddPriceTierAsync(water.ProductId, new AddPriceTierRequestDto(litre, PriceType.Retail, 10m, from));
            await catalog.AddPriceTierAsync(water.ProductId, new AddPriceTierRequestDto(twenty, PriceType.Retail, Refill20, from));
            await inventory.AdjustStockAsync(new CreateStockAdjustmentRequestDto(water.ProductId, litre, 100, "Opening stock", 1.5m));
            var bottle = await catalog.CreateProductAsync(new CreateProductRequestDto(
                "20L bottle " + code, null, category.CategoryId, ItemType.FinishedGood, TaxClassification.Exempt, piece, 2));
            await inventory.AdjustStockAsync(new CreateStockAdjustmentRequestDto(bottle.ProductId, piece, 10, "Opening stock", 300m));
            return new Water(water.ProductId, litre, twenty, piece, bottle.ProductId);
        });
    }

    private Task<CustomerDto> CustomerAsync() =>
        Run(sp => sp.GetRequiredService<BusinessService>().RegisterCustomerAsync(new RegisterCustomerRequestDto(
            null, "Customer " + Guid.NewGuid().ToString("N")[..6], null, null, "0744" + Random.Shared.Next(100000, 999999), null, "Ruiru")));

    private static DeliveryOrderLineRequestDto Item(Guid productId, Guid unit, decimal quantity) => new(productId, unit, quantity);

    /// <summary>Rung up at the till with delivery by rider: straight out with <paramref name="rider"/>.</summary>
    private Task<DeliveryOrderDto> CheckoutByRiderAsync(Guid customerId, string? rider, PaymentMethod expected, params DeliveryOrderLineRequestDto[] lines) =>
        Run(sp => sp.GetRequiredService<DeliveryOrderService>().CreateAsync(new CreateDeliveryOrderRequestDto(
            customerId, "Ruiru, Kimbo stage, green gate", null, null, lines, ExpectedPaymentMethod: expected, RiderName: rider)));

    private Task<DeliveryOrderDto> OrderAsync(Guid customerId, params DeliveryOrderLineRequestDto[] lines) =>
        Run(sp => sp.GetRequiredService<DeliveryOrderService>().CreateAsync(new CreateDeliveryOrderRequestDto(
            customerId, "Ruiru, Kimbo stage, green gate", null, null, lines)));

    private Task<DeliveryOrderDto> DeliverAsync(DeliveryOrderDto order, DeliverDeliveryOrderRequestDto request) =>
        Run(sp => sp.GetRequiredService<DeliveryOrderService>().DeliverAsync(order.DeliveryOrderId, request));

    private Task<DeliveryOrderDto> ReturnAsync(DeliveryOrderDto order, string reason = "Customer not home") =>
        Run(sp => sp.GetRequiredService<DeliveryOrderService>().ReturnAsync(order.DeliveryOrderId, new ReturnDeliveryOrderRequestDto(reason)));

    private Task<StockLevelDto> StockAsync(Guid productId) => Run(sp => sp.GetRequiredService<InventoryService>().GetStockLevelAsync(productId));

    private static string Rider() => "Rider " + Guid.NewGuid().ToString("N")[..6];

    [PostgresFact]
    public async Task A_sale_by_rider_is_not_completed_until_the_rider_brings_back_the_cash()
    {
        await SignInToStationAsync();
        var water = await WaterAsync();
        var customer = await CustomerAsync();
        var rider = Rider();

        // At the till: two 20L refills, delivery by rider, the customer will pay cash. It goes out at once on a trip of its own.
        var order = await CheckoutByRiderAsync(customer.CustomerId, rider, PaymentMethod.Cash, Item(water.ProductId, water.TwentyLitre, 2));
        Assert.Equal((DeliveryOrderStatus.OutForDelivery, rider, PaymentMethod.Cash), (order.Status, order.RiderName, order.ExpectedPaymentMethod));
        Assert.StartsWith("TRP-", order.TripNumber);
        Assert.Null(order.SaleId); // not a sale yet: nothing has been paid

        // The water is on the bike, held back from the counter, but has not left stock; no sale has been recorded.
        var stock = await StockAsync(water.ProductId);
        Assert.Equal((100m, 40m), (stock.QuantityOnHand, stock.ReservedQuantity));
        Assert.Empty((await Run(sp => sp.GetRequiredService<SalesService>().GetSalesAsync(1, 100, new SaleQuery(CustomerId: customer.CustomerId)))).Items);

        // The rider summary shows the cash the rider is out with.
        var summary = await Run(sp => sp.GetRequiredService<DeliveryOrderService>().GetSummaryAsync(Today, Today, ReportPeriod.DefaultUtcOffsetMinutes));
        var theirs = summary.ByRider.Single(r => r.RiderName == rider);
        Assert.Equal((1, 400m, 1), (theirs.OutForDeliveryNow, theirs.CashExpectedNow, theirs.TripCount));

        // The rider is back with the cash: now it is a completed sale, delivered by rider, and the stock leaves.
        order = await DeliverAsync(order, new DeliverDeliveryOrderRequestDto(PaymentMethod.Cash, AmountTendered: 400m));
        Assert.Equal(DeliveryOrderStatus.Delivered, order.Status);
        var sale = await Run(sp => sp.GetRequiredService<SalesService>().GetSaleByIdAsync(order.SaleId!.Value));
        Assert.Equal((SaleStatus.Completed, DeliveryMode.Rider, 400m), (sale.Status, sale.DeliveryMode, sale.TotalAmount));
        Assert.Equal((60m, 0m), ((await StockAsync(water.ProductId)).QuantityOnHand, (await StockAsync(water.ProductId)).ReservedQuantity));
        var byRider = await Run(sp => sp.GetRequiredService<SalesService>().GetSalesAsync(1, 100, new SaleQuery(DeliveryMode: DeliveryMode.Rider)));
        Assert.Contains(byRider.Items, s => s.SaleId == sale.SaleId);
        var atCounter = await Run(sp => sp.GetRequiredService<SalesService>().GetSalesAsync(1, 100, new SaleQuery(DeliveryMode: DeliveryMode.Counter)));
        Assert.DoesNotContain(atCounter.Items, s => s.SaleId == sale.SaleId);
    }

    [PostgresFact]
    public async Task A_delivery_paid_by_mpesa_is_completed_only_with_the_code_the_rider_brings_back()
    {
        await SignInToStationAsync();
        var water = await WaterAsync();
        var customer = await CustomerAsync();
        var order = await CheckoutByRiderAsync(customer.CustomerId, Rider(), PaymentMethod.MpesaReference, Item(water.ProductId, water.TwentyLitre, 1));
        Assert.Equal(PaymentMethod.MpesaReference, order.ExpectedPaymentMethod);

        var ex = await Assert.ThrowsAsync<ValidationException>(() => DeliverAsync(order, new DeliverDeliveryOrderRequestDto(PaymentMethod.MpesaReference)));
        Assert.Contains("mpesaReference", ex.Errors.Keys);
        Assert.Equal(DeliveryOrderStatus.OutForDelivery, (await Run(sp => sp.GetRequiredService<DeliveryOrderService>().GetAsync(order.DeliveryOrderId))).Status);

        var code = "S" + Guid.NewGuid().ToString("N")[..9].ToUpperInvariant();
        order = await DeliverAsync(order, new DeliverDeliveryOrderRequestDto(PaymentMethod.MpesaReference, MpesaReference: code));
        var sale = await Run(sp => sp.GetRequiredService<SalesService>().GetSaleByIdAsync(order.SaleId!.Value));
        Assert.Equal((SaleStatus.Completed, code, DeliveryMode.Rider), (sale.Status, sale.MpesaReference, sale.DeliveryMode));
    }

    [PostgresFact]
    public async Task A_delivery_the_rider_brings_back_is_returned_and_its_water_is_sellable_again()
    {
        await SignInToStationAsync();
        var water = await WaterAsync();
        var customer = await CustomerAsync();
        var rider = Rider();
        var order = await CheckoutByRiderAsync(customer.CustomerId, rider, PaymentMethod.Cash, Item(water.ProductId, water.TwentyLitre, 3));
        Assert.Equal(60m, (await StockAsync(water.ProductId)).ReservedQuantity);

        await Assert.ThrowsAsync<ValidationException>(() => ReturnAsync(order, " "));
        order = await ReturnAsync(order);
        Assert.Equal((DeliveryOrderStatus.Returned, "Customer not home"), (order.Status, order.ReturnReason));
        Assert.NotNull(order.ReturnedAt);
        Assert.Null(order.SaleId);
        var stock = await StockAsync(water.ProductId);
        Assert.Equal((100m, 0m), (stock.QuantityOnHand, stock.ReservedQuantity));

        // Back is back: it cannot be delivered, returned again or cancelled.
        await Assert.ThrowsAsync<InvalidAssignmentException>(() => DeliverAsync(order, new DeliverDeliveryOrderRequestDto(PaymentMethod.Cash, AmountTendered: 600m)));
        await Assert.ThrowsAsync<InvalidAssignmentException>(() => ReturnAsync(order));
        await Assert.ThrowsAsync<InvalidAssignmentException>(() => Run(sp => sp.GetRequiredService<DeliveryOrderService>()
            .CancelAsync(order.DeliveryOrderId, new CancelDeliveryOrderRequestDto("Changed mind"))));
        // An order that has not gone out is cancelled, not returned.
        var pending = await OrderAsync(customer.CustomerId, Item(water.ProductId, water.TwentyLitre, 1));
        await Assert.ThrowsAsync<InvalidAssignmentException>(() => ReturnAsync(pending));

        var summary = await Run(sp => sp.GetRequiredService<DeliveryOrderService>().GetSummaryAsync(Today, Today, ReportPeriod.DefaultUtcOffsetMinutes));
        Assert.Equal(1, summary.ByRider.Single(r => r.RiderName == rider).ReturnedCount);
        var returned = await Run(sp => sp.GetRequiredService<DeliveryOrderService>().GetOrdersAsync(1, 100, new DeliveryOrderQuery(Status: DeliveryOrderStatus.Returned)));
        Assert.Contains(returned.Items, o => o.DeliveryOrderId == order.DeliveryOrderId);
    }

    [PostgresFact]
    public async Task A_bundle_can_be_sent_by_rider_and_is_charged_at_the_bundle_price()
    {
        await SignInToStationAsync();
        var water = await WaterAsync();
        var customer = await CustomerAsync();
        var bundle = await Run(sp => sp.GetRequiredService<ProductBundleService>().CreateAsync(new CreateProductBundleRequestDto(
            "20L bottle + water " + Guid.NewGuid().ToString("N")[..4], null, null, 1500m, null,
            new ProductBundleComponentRequestDto[] { new(water.ProductId, water.TwentyLitre, 1), new(water.Bottle, water.Piece, 1) })));

        var order = await Run(sp => sp.GetRequiredService<DeliveryOrderService>().CreateAsync(new CreateDeliveryOrderRequestDto(
            customer.CustomerId, "Ruiru", null, null, null, new[] { new DeliveryOrderBundleRequestDto(bundle.ProductBundleId, 2) },
            PaymentMethod.Cash, Rider())));
        Assert.Equal(3000m, order.EstimatedTotal);
        Assert.All(order.Lines, l => Assert.Equal((bundle.ProductBundleId, 2m), (l.ProductBundleId!.Value, l.BundleQuantity!.Value)));
        Assert.Equal((40m, 2m), ((await StockAsync(water.ProductId)).ReservedQuantity, (await StockAsync(water.Bottle)).ReservedQuantity));

        order = await DeliverAsync(order, new DeliverDeliveryOrderRequestDto(PaymentMethod.Cash, AmountTendered: 3000m));
        var sale = await Run(sp => sp.GetRequiredService<SalesService>().GetSaleByIdAsync(order.SaleId!.Value));
        Assert.Equal(3000m, sale.TotalAmount);
        Assert.Equal((bundle.ProductBundleId, 2m), (sale.Bundles!.Single().ProductBundleId, sale.Bundles!.Single().Quantity));
        Assert.Equal((60m, 8m), ((await StockAsync(water.ProductId)).QuantityOnHand, (await StockAsync(water.Bottle)).QuantityOnHand));
    }

    [PostgresFact]
    public async Task Sending_an_order_out_at_the_till_needs_the_dispatch_permission()
    {
        await SignInToStationAsync();
        var water = await WaterAsync();
        var customer = await CustomerAsync();
        await SignInToStationAsync("DeliveryOrders.Create", "DeliveryOrders.View", "Sales.Create");
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            CheckoutByRiderAsync(customer.CustomerId, Rider(), PaymentMethod.Cash, Item(water.ProductId, water.TwentyLitre, 1)));
        // Without a rider it is an ordinary order waiting to be dispatched.
        var order = await OrderAsync(customer.CustomerId, Item(water.ProductId, water.TwentyLitre, 1));
        Assert.Equal(DeliveryOrderStatus.Pending, order.Status);
    }

    [PostgresFact]
    public async Task Orders_sent_out_together_are_one_trip_and_fuel_is_weighed_against_the_trips()
    {
        await SignInToStationAsync();
        var water = await WaterAsync();
        var customer = await CustomerAsync();
        var peter = Rider();
        var jane = Rider();
        var service = (Func<IServiceProvider, DeliveryOrderService>)(sp => sp.GetRequiredService<DeliveryOrderService>());

        // Peter: two pending orders out together, and a third added at the till before he leaves — one trip, all delivered.
        var a = await OrderAsync(customer.CustomerId, Item(water.ProductId, water.Litre, 5));
        var b = await OrderAsync(customer.CustomerId, Item(water.ProductId, water.Litre, 5));
        var trip = await Run(sp => service(sp).DispatchTripAsync(new DispatchTripRequestDto(peter.ToUpperInvariant() + " ", new[] { a.DeliveryOrderId, b.DeliveryOrderId })));
        Assert.Equal((2, 2), (trip.OrderCount, trip.OutCount));
        var c = await Run(sp => service(sp).CreateAsync(new CreateDeliveryOrderRequestDto(customer.CustomerId, "Ruiru", null, null,
            new[] { Item(water.ProductId, water.Litre, 5) }, DeliveryTripId: trip.DeliveryTripId)));
        Assert.Equal((trip.TripNumber, DeliveryOrderStatus.OutForDelivery), (c.TripNumber, c.Status));
        // Not on someone else's trip, and an order already out cannot go out again.
        await Assert.ThrowsAsync<ValidationException>(() => Run(sp => service(sp).CreateAsync(new CreateDeliveryOrderRequestDto(customer.CustomerId, "Ruiru",
            null, null, new[] { Item(water.ProductId, water.Litre, 1) }, RiderName: jane, DeliveryTripId: trip.DeliveryTripId))));
        await Assert.ThrowsAsync<InvalidAssignmentException>(() => Run(sp => service(sp).DispatchTripAsync(new DispatchTripRequestDto(jane, new[] { a.DeliveryOrderId }))));
        foreach (var order in new[] { a, b, c })
            await DeliverAsync(order, new DeliverDeliveryOrderRequestDto(PaymentMethod.Cash, AmountTendered: 50m));
        // Back: nothing more can join it.
        await Assert.ThrowsAsync<InvalidAssignmentException>(() => Run(sp => service(sp).CreateAsync(new CreateDeliveryOrderRequestDto(customer.CustomerId,
            "Ruiru", null, null, new[] { Item(water.ProductId, water.Litre, 1) }, DeliveryTripId: trip.DeliveryTripId))));

        // Jane: two trips of one order each; the second came back — a wasted trip.
        await DeliverAsync(await CheckoutByRiderAsync(customer.CustomerId, jane, PaymentMethod.Cash, Item(water.ProductId, water.Litre, 5)),
            new DeliverDeliveryOrderRequestDto(PaymentMethod.Cash, AmountTendered: 50m));
        await ReturnAsync(await CheckoutByRiderAsync(customer.CustomerId, jane, PaymentMethod.Cash, Item(water.ProductId, water.Litre, 5)));

        // Fuel: Peter 150 for his one trip; Jane 400 for two (one tagged in lower case), plus an untagged expense that is not anyone's fuel.
        var transport = (await Run(sp => sp.GetRequiredService<ExpenseService>().GetCategoriesAsync(true))).Single(x => x.Code == "TRN");
        Task<ExpenseDto> FuelAsync(decimal amount, string? rider) => Run(sp => sp.GetRequiredService<ExpenseService>().RecordExpenseAsync(
            new CreateExpenseRequestDto(Today, transport.ExpenseCategoryId, "Fuel", amount, ExpensePaymentMethod.Cash, null, null, null, null, null, null, null,
                RiderName: rider)));
        Assert.Equal(peter, (await FuelAsync(150m, peter)).RiderName);
        await FuelAsync(250m, jane);
        await FuelAsync(150m, jane.ToLowerInvariant());
        await FuelAsync(999m, null);

        var report = await Run(sp => service(sp).GetFuelReportAsync(Today, Today, transport.ExpenseCategoryId, 160m, ReportPeriod.DefaultUtcOffsetMinutes));
        var p = report.ByRider.Single(r => r.RiderName.Equals(peter, StringComparison.OrdinalIgnoreCase));
        Assert.Equal((1, 3, 3, 0, 0, 0), (p.TripCount, p.OrdersCarried, p.DeliveredCount, p.ReturnedCount, p.WastedTrips, p.SingleOrderTrips));
        Assert.Equal((150m, 150m, 160m, -10m, true), (p.FuelSpent, p.FuelPerTrip!.Value, p.ExpectedFuel!.Value, p.FuelVariance!.Value, p.FuelAccountedFor));
        var j = report.ByRider.Single(r => r.RiderName.Equals(jane, StringComparison.OrdinalIgnoreCase));
        Assert.Equal((2, 1, 1, 1, 2), (j.TripCount, j.DeliveredCount, j.ReturnedCount, j.WastedTrips, j.SingleOrderTrips));
        Assert.Equal((400m, 200m, 320m, 80m, false), (j.FuelSpent, j.FuelPerTrip!.Value, j.ExpectedFuel!.Value, j.FuelVariance!.Value, j.FuelAccountedFor));
        Assert.True(report.TripCount >= 3);

        // Without an expected cost per trip, riders are compared with the average.
        var plain = await Run(sp => service(sp).GetFuelReportAsync(Today, Today, transport.ExpenseCategoryId, null, ReportPeriod.DefaultUtcOffsetMinutes));
        Assert.Null(plain.ByRider.Single(r => r.RiderName.Equals(jane, StringComparison.OrdinalIgnoreCase)).ExpectedFuel);

        // The trip list shows each trip with its orders; the expense list filters by rider.
        var trips = await Run(sp => service(sp).GetTripsAsync(new DeliveryTripQuery(Today, Today, jane)));
        Assert.Equal(2, trips.Count());
        Assert.Single(trips, t => t.IsWasted);
        var janesFuel = await Run(sp => sp.GetRequiredService<ExpenseService>().GetExpensesAsync(1, 100,
            new ExpenseFilter(Today, Today, null, null, null, null, null, null, jane.ToUpperInvariant())));
        Assert.Equal(400m, janesFuel.Items.Sum(e => e.Amount));
    }

    [PostgresFact]
    public async Task The_day_opens_with_a_float_and_the_cash_up_accounts_for_it()
    {
        await SignInToStationAsync();
        var water = await WaterAsync();
        var customer = await CustomerAsync();
        var sessions = (Func<IServiceProvider, CashSessionService>)(sp => sp.GetRequiredService<CashSessionService>());
        Assert.Null(await Run(sp => sessions(sp).GetCurrentAsync()));

        await Assert.ThrowsAsync<ValidationException>(() => Run(sp => sessions(sp).OpenAsync(new OpenCashSessionRequestDto(-1m, null))));

        var session = await Run(sp => sessions(sp).OpenAsync(new OpenCashSessionRequestDto(1000m, "Float from the safe")));
        Assert.Equal((CashSessionStatus.Open, 1000m, 1000m), (session.Status, session.OpeningFloat, session.Breakdown.ExpectedCash));
        Assert.StartsWith("CSH-", session.SessionNumber);
        await Assert.ThrowsAsync<InvalidAssignmentException>(() => Run(sp => sessions(sp).OpenAsync(new OpenCashSessionRequestDto(500m, null))));

        // The day: a cash sale of 200 (paid 500, 300 change from the float), a rider's cash delivery of 400, a cash refund of 10
        // (1 litre), a customer paying 100 on account in cash, a cash expense of 50, 600 banked, and a delivery still out (200 in cash).
        var sale = await Run(sp => sp.GetRequiredService<SalesService>().RecordSaleAsync(new CreateSaleRequestDto(null, PaymentMethod.Cash, null,
            new[] { new CreateSaleLineRequestDto(water.ProductId, water.Litre, 20) }, 500m, null, null)));
        await DeliverAsync(await CheckoutByRiderAsync(customer.CustomerId, Rider(), PaymentMethod.Cash, Item(water.ProductId, water.TwentyLitre, 2)),
            new DeliverDeliveryOrderRequestDto(PaymentMethod.Cash, AmountTendered: 400m));
        await Run(sp => sp.GetRequiredService<SalesService>().CreateReturnAsync(sale.SaleId, new CreateSaleReturnRequestDto("Spilt", RefundMethod.Cash, null,
            new[] { new CreateSaleReturnLineRequestDto(sale.Lines.Single().SaleLineId, 1, Restock: false) })));
        await Run(sp => sp.GetRequiredService<AccountingService>().RecordCustomerPaymentAsync(customer.CustomerId,
            new RecordCustomerPaymentRequestDto(100m, null, SettlementMethod.Cash)));
        var other = (await Run(sp => sp.GetRequiredService<ExpenseService>().GetCategoriesAsync(true))).Single(x => x.Code == "MISC");
        await Run(sp => sp.GetRequiredService<ExpenseService>().RecordExpenseAsync(new CreateExpenseRequestDto(Today, other.ExpenseCategoryId,
            "Airtime", 50m, ExpensePaymentMethod.Cash, null, null, null, null, null, null, null)));
        await Run(sp => sp.GetRequiredService<ExpenseService>().RecordExpenseAsync(new CreateExpenseRequestDto(Today, other.ExpenseCategoryId,
            "Paid by M-Pesa: not from the drawer", 70m, ExpensePaymentMethod.Mpesa, "S" + Guid.NewGuid().ToString("N")[..9].ToUpperInvariant(),
            null, null, null, null, null, null)));
        await Assert.ThrowsAsync<ValidationException>(() => Run(sp => sessions(sp).RecordMovementAsync(session.CashSessionId,
            new RecordCashMovementRequestDto(CashMovementType.PayOut, 600m, " "))));
        await Run(sp => sessions(sp).RecordMovementAsync(session.CashSessionId, new RecordCashMovementRequestDto(CashMovementType.PayOut, 600m, "Banked")));
        await CheckoutByRiderAsync(customer.CustomerId, Rider(), PaymentMethod.Cash, Item(water.ProductId, water.TwentyLitre, 1));

        var live = (await Run(sp => sessions(sp).GetCurrentAsync()))!;
        var d = live.Breakdown;
        Assert.Equal((600m, 2, 100m, 0m, 10m, 50m, 0m, 600m), (d.CashSales, d.CashSaleCount, d.CustomerPayments, d.PayIns, d.CashRefunds, d.CashExpenses,
            d.SupplierPayments, d.PayOuts));
        // 1000 + 600 + 100 - 10 - 50 - 600
        Assert.Equal(1040m, d.ExpectedCash);
        Assert.Equal((200m, 1), (d.CashStillWithRiders, d.OrdersStillOut));
        Assert.Equal("Banked", live.Movements.Single().Reason);

        // Counted 20 short.
        var closed = await Run(sp => sessions(sp).CloseAsync(session.CashSessionId, new CloseCashSessionRequestDto(1020m, "Counted with the manager")));
        Assert.Equal((CashSessionStatus.Closed, 1040m, 1020m, -20m), (closed.Status, closed.Breakdown.ExpectedCash, closed.CountedCash!.Value, closed.Variance!.Value));
        Assert.Null(await Run(sp => sessions(sp).GetCurrentAsync()));
        await Assert.ThrowsAsync<InvalidAssignmentException>(() => Run(sp => sessions(sp).CloseAsync(session.CashSessionId, new CloseCashSessionRequestDto(1m, null))));
        await Assert.ThrowsAsync<InvalidAssignmentException>(() => Run(sp => sessions(sp).RecordMovementAsync(session.CashSessionId,
            new RecordCashMovementRequestDto(CashMovementType.PayIn, 5m, "Late"))));

        // A closed day keeps its figures; the short count raised an alert; the next day can open.
        await Run(sp => sp.GetRequiredService<SalesService>().RecordSaleAsync(new CreateSaleRequestDto(null, PaymentMethod.Cash, null,
            new[] { new CreateSaleLineRequestDto(water.ProductId, water.Litre, 1) }, 10m, null, null)));
        Assert.Equal(1040m, (await Run(sp => sessions(sp).GetAsync(session.CashSessionId))).Breakdown.ExpectedCash);
        Assert.True(await Run(sp => sp.GetRequiredService<UltimatePosDbContext>().SystemEvents
            .AnyAsync(e => e.EventType == "CashSession.Variance" && e.EntityId == session.CashSessionId)));
        var next = await Run(sp => sessions(sp).OpenAsync(new OpenCashSessionRequestDto(1020m, null)));
        Assert.Equal(CashSessionStatus.Open, next.Status);
        var list = await Run(sp => sessions(sp).GetSessionsAsync(1, 10, new CashSessionQuery(Today, Today)));
        Assert.Equal(2, list.TotalCount);
        await Run(sp => sessions(sp).CloseAsync(next.CashSessionId, new CloseCashSessionRequestDto(1030m, null)));
    }
}
