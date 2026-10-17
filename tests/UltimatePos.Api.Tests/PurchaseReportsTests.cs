using UltimatePos.Application.Reports;
using UltimatePos.Domain.Exceptions;
using static UltimatePos.Domain.Enums.PosEnums;

namespace Ultimate.Api.Tests;

internal static class PurchaseFixtures
{
    public static readonly Guid Kapa = Guid.NewGuid();
    public static readonly Guid Unga = Guid.NewGuid();
    public static readonly Guid Maize = Guid.NewGuid();
    public static readonly Guid Soya = Guid.NewGuid();
    public static readonly Guid Salt = Guid.NewGuid();
    public static readonly Guid Bag25 = Guid.NewGuid();
    public static readonly Guid Bag50 = Guid.NewGuid();

    public static DateTime Utc(int month, int day, int hour = 9) => new(2026, month, day, hour, 0, 0, DateTimeKind.Utc);

    public static PurchaseLineRow Line(string order, DateTime orderDate, PurchaseOrderStatus status, Guid supplier, string supplierName,
        Guid product, string productName, Guid unit, string unitSymbol, decimal ordered, decimal received, decimal unitCost,
        DateTime? expected = null) =>
        new(Guid.Parse(Convert.ToHexString(System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(order)))),
            order, orderDate, expected, status, supplier, supplierName, product, productName, productName[..2].ToUpper(), unit, unitSymbol,
            ordered, received, unitCost);
}

public class PurchaseSummaryTests
{
    private static readonly ReportPeriod Period = ReportPeriod.Create(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), 180);

    private static PurchaseLineRow L(string order, int day, PurchaseOrderStatus status, Guid supplier, string supplierName,
        Guid product, string productName, Guid unit, string unitSymbol, decimal ordered, decimal received, decimal cost) =>
        PurchaseFixtures.Line(order, PurchaseFixtures.Utc(10, day), status, supplier, supplierName, product, productName, unit, unitSymbol, ordered, received, cost);

    private static readonly PurchaseLineRow[] Lines =
    {
        // PO-1: Kapa, maize, fully received.   10 x 2,000 = 20,000
        L("PO-1", 2, PurchaseOrderStatus.Received, PurchaseFixtures.Kapa, "Kapa Millers", PurchaseFixtures.Maize, "Maize", PurchaseFixtures.Bag50, "50kg bag", 10m, 10m, 2000m),
        // PO-2: Unga, two lines, partly received.  maize 5 x 2,100 = 10,500 (2 in); soya 4 x 3,000 = 12,000 (0 in)
        L("PO-2", 5, PurchaseOrderStatus.PartiallyReceived, PurchaseFixtures.Unga, "Unga Ltd", PurchaseFixtures.Maize, "Maize", PurchaseFixtures.Bag50, "50kg bag", 5m, 2m, 2100m),
        L("PO-2", 5, PurchaseOrderStatus.PartiallyReceived, PurchaseFixtures.Unga, "Unga Ltd", PurchaseFixtures.Soya, "Soya", PurchaseFixtures.Bag25, "25kg bag", 4m, 0m, 3000m),
        // PO-3: Kapa, nothing received yet.  maize in a different bag size 8 x 1,100 = 8,800
        L("PO-3", 9, PurchaseOrderStatus.Ordered, PurchaseFixtures.Kapa, "Kapa Millers", PurchaseFixtures.Maize, "Maize", PurchaseFixtures.Bag25, "25kg bag", 8m, 0m, 1100m),
        // PO-4: cancelled, must never count as a commitment.
        L("PO-4", 12, PurchaseOrderStatus.Cancelled, PurchaseFixtures.Unga, "Unga Ltd", PurchaseFixtures.Soya, "Soya", PurchaseFixtures.Bag25, "25kg bag", 100m, 0m, 3000m),
        // PO-5: draft, not a commitment either.
        L("PO-5", 14, PurchaseOrderStatus.Draft, PurchaseFixtures.Kapa, "Kapa Millers", PurchaseFixtures.Soya, "Soya", PurchaseFixtures.Bag25, "25kg bag", 1m, 0m, 3000m),
    };

    [Fact]
    public void Commitments_exclude_cancelled_and_draft_orders_which_are_reported_apart()
    {
        var s = ReportsService.BuildPurchaseSummary(Period, Lines, 10);

        Assert.Equal(5, s.OrderCount);
        Assert.Equal(1, s.CancelledCount);
        Assert.Equal(300000m, s.CancelledValue);
        Assert.Equal(20000m + 10500m + 12000m + 8800m, s.OrderedValue);   // 51,300
    }

    [Fact]
    public void Ordered_equals_received_plus_outstanding()
    {
        var s = ReportsService.BuildPurchaseSummary(Period, Lines, 10);

        Assert.Equal(20000m + 4200m, s.ReceivedValue);        // PO-1 in full, 2 of 5 on PO-2's maize
        Assert.Equal(6300m + 12000m + 8800m, s.OutstandingValue);
        Assert.Equal(s.OrderedValue, s.ReceivedValue + s.OutstandingValue);
    }

    [Fact]
    public void Orders_are_counted_once_per_status_however_many_lines_they_have()
    {
        var byStatus = ReportsService.BuildPurchaseSummary(Period, Lines, 10).ByStatus.ToDictionary(x => x.Status);

        Assert.Equal(1, byStatus["PartiallyReceived"].OrderCount);   // PO-2 has two lines
        Assert.Equal(22500m, byStatus["PartiallyReceived"].OrderedValue);
        Assert.Equal(1, byStatus["Received"].OrderCount);
        Assert.Equal(1, byStatus["Ordered"].OrderCount);
        Assert.Equal(1, byStatus["Cancelled"].OrderCount);
        Assert.Equal(1, byStatus["Draft"].OrderCount);
    }

    [Fact]
    public void Suppliers_are_ranked_by_what_was_ordered_and_percentages_add_up()
    {
        var suppliers = ReportsService.BuildPurchaseSummary(Period, Lines, 10).BySupplier.ToList();

        Assert.Equal(new[] { "Kapa Millers", "Unga Ltd" }, suppliers.Select(s => s.SupplierName).ToArray());
        Assert.Equal(2, suppliers[0].OrderCount);            // PO-1 and PO-3; the draft is not committed
        Assert.Equal(28800m, suppliers[0].OrderedValue);
        Assert.Equal(22500m, suppliers[1].OrderedValue);
        Assert.Equal(100m, Math.Round(suppliers.Sum(s => s.PercentOfOrdered), 0));
    }

    [Fact]
    public void The_same_product_in_different_units_is_never_added_together()
    {
        var products = ReportsService.BuildPurchaseSummary(Period, Lines, 10).TopProducts.ToList();
        var maize = products.Where(p => p.ProductName == "Maize").ToList();

        Assert.Equal(2, maize.Count);
        Assert.Equal(15m, maize.Single(p => p.UnitSymbol == "50kg bag").QuantityOrdered);
        Assert.Equal(8m, maize.Single(p => p.UnitSymbol == "25kg bag").QuantityOrdered);
    }

    [Fact]
    public void The_average_price_is_weighted_by_quantity()
    {
        var maize50 = ReportsService.BuildPurchaseSummary(Period, Lines, 10).TopProducts.Single(p => p.ProductName == "Maize" && p.UnitSymbol == "50kg bag");

        // (10 x 2,000 + 5 x 2,100) / 15 = 2,033.3333, not the plain average 2,050
        Assert.Equal(2033.3333m, maize50.AverageUnitCost);
        Assert.Equal(15m, maize50.QuantityOrdered);
        Assert.Equal(12m, maize50.QuantityReceived);
    }

    [Fact]
    public void Lists_are_limited_by_top()
    {
        var s = ReportsService.BuildPurchaseSummary(Period, Lines, 1);

        Assert.Single(s.BySupplier);
        Assert.Single(s.TopProducts);
    }

    [Fact]
    public void A_line_with_no_price_is_counted_so_an_understated_spend_is_visible()
    {
        var withFree = Lines.Append(L("PO-6", 15, PurchaseOrderStatus.Ordered, PurchaseFixtures.Kapa, "Kapa Millers",
            PurchaseFixtures.Maize, "Maize", PurchaseFixtures.Bag50, "50kg bag", 3m, 0m, 0m)).ToList();

        var s = ReportsService.BuildPurchaseSummary(Period, withFree, 10);

        Assert.Equal(1, s.UnpricedLineCount);
        Assert.Equal(51300m, s.OrderedValue);   // the free line adds nothing
    }

    [Fact]
    public void Rounding_is_per_line_so_the_totals_still_add_up_to_the_cent()
    {
        // 0.5 x 33.33 = 16.665 -> 16.67; 0.25 received = 8.3325 -> 8.33; outstanding is the difference, not a second rounding
        var line = L("PO-7", 3, PurchaseOrderStatus.PartiallyReceived, PurchaseFixtures.Kapa, "Kapa Millers",
            PurchaseFixtures.Maize, "Maize", PurchaseFixtures.Bag50, "50kg bag", 0.5m, 0.25m, 33.33m);

        var s = ReportsService.BuildPurchaseSummary(Period, new[] { line }, 10);

        Assert.Equal(16.67m, s.OrderedValue);
        Assert.Equal(8.33m, s.ReceivedValue);
        Assert.Equal(8.34m, s.OutstandingValue);
        Assert.Equal(s.OrderedValue, s.ReceivedValue + s.OutstandingValue);
    }

    [Fact]
    public void No_orders_in_the_period_is_an_empty_summary()
    {
        var s = ReportsService.BuildPurchaseSummary(Period, Array.Empty<PurchaseLineRow>(), 10);

        Assert.Equal(0, s.OrderCount);
        Assert.Equal(0m, s.OrderedValue);
        Assert.Empty(s.BySupplier);
        Assert.Empty(s.TopProducts);
    }
}

public class OpenOrdersTests
{
    private static readonly TimeSpan Eat = TimeSpan.FromHours(3);
    private static readonly DateOnly Today = new(2026, 10, 15);

    private static PurchaseLineRow L(string order, int orderDay, PurchaseOrderStatus status, Guid product, string name, decimal ordered, decimal received,
        decimal cost, DateTime? expected = null, Guid? supplier = null, string supplierName = "Kapa Millers") =>
        PurchaseFixtures.Line(order, PurchaseFixtures.Utc(10, orderDay), status, supplier ?? PurchaseFixtures.Kapa, supplierName, product, name,
            PurchaseFixtures.Bag50, "50kg bag", ordered, received, cost, expected);

    [Fact]
    public void Overdue_orders_come_first_the_most_overdue_at_the_top_then_by_expected_date_then_undated()
    {
        var lines = new[]
        {
            L("PO-A", 1, PurchaseOrderStatus.Ordered, PurchaseFixtures.Maize, "Maize", 10m, 0m, 100m, PurchaseFixtures.Utc(10, 20)),   // due in 5 days
            L("PO-B", 2, PurchaseOrderStatus.Ordered, PurchaseFixtures.Maize, "Maize", 10m, 0m, 100m),                                 // no date
            L("PO-C", 3, PurchaseOrderStatus.Ordered, PurchaseFixtures.Maize, "Maize", 10m, 0m, 100m, PurchaseFixtures.Utc(10, 12)),   // 3 days late
            L("PO-D", 4, PurchaseOrderStatus.Ordered, PurchaseFixtures.Maize, "Maize", 10m, 0m, 100m, PurchaseFixtures.Utc(10, 8)),    // 7 days late
            L("PO-E", 5, PurchaseOrderStatus.Ordered, PurchaseFixtures.Maize, "Maize", 10m, 0m, 100m, PurchaseFixtures.Utc(10, 17)),   // due in 2 days
        };

        var report = ReportsService.BuildOpenOrders(Today, Eat, lines);

        Assert.Equal(new[] { "PO-D", "PO-C", "PO-E", "PO-A", "PO-B" }, report.Orders.Select(o => o.OrderNumber).ToArray());
        Assert.Equal(2, report.OverdueCount);
        Assert.Equal(7, report.Orders.First().DaysOverdue);
        Assert.Equal(0, report.Orders.Last().DaysOverdue);
    }

    [Fact]
    public void An_order_due_today_is_not_yet_overdue()
    {
        var due = new[] { L("PO-T", 1, PurchaseOrderStatus.Ordered, PurchaseFixtures.Maize, "Maize", 10m, 0m, 100m, PurchaseFixtures.Utc(10, 15, 6)) };

        var order = ReportsService.BuildOpenOrders(Today, Eat, due).Orders.Single();

        Assert.False(order.IsOverdue);
        Assert.Equal(0, order.DaysOverdue);
    }

    [Fact]
    public void A_delivery_day_is_judged_in_Nairobi_time()
    {
        // 21:30 UTC on the 14th is 00:30 on the 15th in Nairobi: due today, not 1 day overdue.
        var lines = new[] { L("PO-N", 1, PurchaseOrderStatus.Ordered, PurchaseFixtures.Maize, "Maize", 10m, 0m, 100m, PurchaseFixtures.Utc(10, 14, 21).AddMinutes(30)) };

        var order = ReportsService.BuildOpenOrders(Today, Eat, lines).Orders.Single();

        Assert.Equal(new DateOnly(2026, 10, 15), order.ExpectedDeliveryDate);
        Assert.False(order.IsOverdue);
    }

    [Fact]
    public void Only_lines_with_something_still_to_arrive_are_listed_with_what_is_outstanding()
    {
        var lines = new[]
        {
            L("PO-M", 1, PurchaseOrderStatus.PartiallyReceived, PurchaseFixtures.Maize, "Maize", 10m, 10m, 2000m),   // done
            L("PO-M", 1, PurchaseOrderStatus.PartiallyReceived, PurchaseFixtures.Soya, "Soya", 10m, 4m, 3000m),      // 6 to come
        };

        var order = ReportsService.BuildOpenOrders(Today, Eat, lines).Orders.Single();
        var line = order.Lines.Single();

        Assert.Equal("Soya", line.ProductName);
        Assert.Equal(6m, line.QuantityOutstanding);
        Assert.Equal(18000m, line.OutstandingValue);
        Assert.Equal(18000m, order.OutstandingValue);
    }

    [Fact]
    public void Received_cancelled_and_draft_orders_are_not_open_and_a_stale_status_with_nothing_left_is_dropped()
    {
        var lines = new[]
        {
            L("PO-R", 1, PurchaseOrderStatus.Received, PurchaseFixtures.Maize, "Maize", 10m, 10m, 100m),
            L("PO-X", 2, PurchaseOrderStatus.Cancelled, PurchaseFixtures.Maize, "Maize", 10m, 0m, 100m),
            L("PO-D", 3, PurchaseOrderStatus.Draft, PurchaseFixtures.Maize, "Maize", 10m, 0m, 100m),
            L("PO-S", 4, PurchaseOrderStatus.PartiallyReceived, PurchaseFixtures.Maize, "Maize", 10m, 10m, 100m), // status never caught up
        };

        var report = ReportsService.BuildOpenOrders(Today, Eat, lines);

        Assert.Equal(0, report.OrderCount);
        Assert.Equal(0m, report.OutstandingValue);
    }

    [Fact]
    public void Totals_split_overdue_from_the_rest_and_age_counts_from_the_order_day()
    {
        var lines = new[]
        {
            L("PO-1", 5, PurchaseOrderStatus.Ordered, PurchaseFixtures.Maize, "Maize", 10m, 0m, 100m, PurchaseFixtures.Utc(10, 10)),   // overdue, 1,000
            L("PO-2", 12, PurchaseOrderStatus.Ordered, PurchaseFixtures.Maize, "Maize", 4m, 0m, 100m, PurchaseFixtures.Utc(10, 25)),   // 400
        };

        var report = ReportsService.BuildOpenOrders(Today, Eat, lines);

        Assert.Equal(1400m, report.OutstandingValue);
        Assert.Equal(1000m, report.OverdueValue);
        Assert.Equal(10, report.Orders.First().AgeDays);   // raised on the 5th, today is the 15th
        Assert.Equal(Today, report.AsOf);
    }
}

public class PriceTrendsTests
{
    private static readonly ReportPeriod Period = ReportPeriod.Create(new DateOnly(2026, 8, 1), new DateOnly(2026, 10, 31), 180);

    private static PurchaseLineRow L(string order, int month, int day, Guid supplier, string supplierName, Guid product, string name,
        decimal ordered, decimal cost, PurchaseOrderStatus status = PurchaseOrderStatus.Received) =>
        PurchaseFixtures.Line(order, PurchaseFixtures.Utc(month, day), status, supplier, supplierName, product, name,
            PurchaseFixtures.Bag50, "50kg bag", ordered, ordered, cost);

    private static readonly PurchaseLineRow[] Lines =
    {
        L("PO-1", 8, 3,  PurchaseFixtures.Kapa, "Kapa Millers", PurchaseFixtures.Maize, "Maize", 10m, 2000m),
        L("PO-2", 9, 10, PurchaseFixtures.Unga, "Unga Ltd",     PurchaseFixtures.Maize, "Maize", 10m, 1900m),
        L("PO-3", 10, 5, PurchaseFixtures.Kapa, "Kapa Millers", PurchaseFixtures.Maize, "Maize", 20m, 2300m),
        L("PO-4", 8, 4,  PurchaseFixtures.Kapa, "Kapa Millers", PurchaseFixtures.Soya,  "Soya",  5m, 3000m),
        L("PO-5", 10, 6, PurchaseFixtures.Kapa, "Kapa Millers", PurchaseFixtures.Soya,  "Soya",  5m, 2850m),
        L("PO-6", 9, 1,  PurchaseFixtures.Kapa, "Kapa Millers", PurchaseFixtures.Salt,  "Salt",  2m, 500m),   // bought once
    };

    [Fact]
    public void First_latest_lowest_and_highest_price_with_who_charged_what()
    {
        var maize = ReportsService.BuildPriceTrends(Period, Lines, false, 100).Items.Single(i => i.ProductName == "Maize");

        Assert.Equal(3, maize.PurchaseCount);
        Assert.Equal(2, maize.SupplierCount);
        Assert.Equal(2000m, maize.FirstUnitCost);
        Assert.Equal(2300m, maize.LatestUnitCost);
        Assert.Equal("Kapa Millers", maize.LatestSupplier);
        Assert.Equal(1900m, maize.LowestUnitCost);
        Assert.Equal("Unga Ltd", maize.LowestSupplier);
        Assert.Equal(2300m, maize.HighestUnitCost);
        Assert.Equal(15m, maize.ChangePercent);          // 2,000 -> 2,300
    }

    [Fact]
    public void The_average_is_weighted_by_quantity()
    {
        var maize = ReportsService.BuildPriceTrends(Period, Lines, false, 100).Items.Single(i => i.ProductName == "Maize");

        // (10 x 2,000 + 10 x 1,900 + 20 x 2,300) / 40 = 2,125
        Assert.Equal(2125m, maize.AverageUnitCost);
    }

    [Fact]
    public void A_price_that_fell_has_a_negative_change_and_biggest_movers_come_first_either_way()
    {
        var items = ReportsService.BuildPriceTrends(Period, Lines, false, 100).Items.ToList();

        Assert.Equal(new[] { "Maize", "Soya", "Salt" }, items.Select(i => i.ProductName).ToArray());   // +15%, -5%, single purchase
        Assert.Equal(-5m, items.Single(i => i.ProductName == "Soya").ChangePercent);
    }

    [Fact]
    public void A_single_purchase_has_no_trend()
    {
        var salt = ReportsService.BuildPriceTrends(Period, Lines, false, 100).Items.Single(i => i.ProductName == "Salt");

        Assert.Null(salt.ChangePercent);
        Assert.Equal(salt.FirstUnitCost, salt.LatestUnitCost);
    }

    [Fact]
    public void Cancelled_draft_and_unpriced_lines_are_left_out()
    {
        var noisy = Lines.Concat(new[]
        {
            L("PO-X", 10, 7, PurchaseFixtures.Kapa, "Kapa Millers", PurchaseFixtures.Maize, "Maize", 5m, 9999m, PurchaseOrderStatus.Cancelled),
            L("PO-D", 10, 8, PurchaseFixtures.Kapa, "Kapa Millers", PurchaseFixtures.Maize, "Maize", 5m, 8888m, PurchaseOrderStatus.Draft),
            L("PO-Z", 10, 9, PurchaseFixtures.Kapa, "Kapa Millers", PurchaseFixtures.Maize, "Maize", 5m, 0m),
        }).ToList();

        var maize = ReportsService.BuildPriceTrends(Period, noisy, false, 100).Items.Single(i => i.ProductName == "Maize");

        Assert.Equal(3, maize.PurchaseCount);
        Assert.Equal(2300m, maize.HighestUnitCost);
    }

    [Fact]
    public void Individual_purchases_are_listed_oldest_first_only_when_a_single_product_is_asked_for()
    {
        var maizeOnly = Lines.Where(l => l.ProductId == PurchaseFixtures.Maize).ToList();

        var withPoints = ReportsService.BuildPriceTrends(Period, maizeOnly, true, 100).Items.Single();
        var without = ReportsService.BuildPriceTrends(Period, maizeOnly, false, 100).Items.Single();

        Assert.Equal(new[] { "PO-1", "PO-2", "PO-3" }, withPoints.Points!.Select(p => p.OrderNumber).ToArray());
        Assert.Null(without.Points);
    }

    [Fact]
    public void The_row_limit_keeps_the_latest_purchases_and_the_biggest_movers()
    {
        var maizeOnly = Lines.Where(l => l.ProductId == PurchaseFixtures.Maize).ToList();

        var points = ReportsService.BuildPriceTrends(Period, maizeOnly, true, 2).Items.Single().Points!;
        Assert.Equal(new[] { "PO-2", "PO-3" }, points.Select(p => p.OrderNumber).ToArray());

        var report = ReportsService.BuildPriceTrends(Period, Lines, false, 1);
        Assert.Single(report.Items);
        Assert.Equal(3, report.ProductCount);     // the count is of all products, not just the ones listed
        Assert.Equal("Maize", report.Items.Single().ProductName);
    }

    [Fact]
    public void Different_units_of_the_same_product_get_their_own_trend()
    {
        var mixed = Lines.Append(PurchaseFixtures.Line("PO-9", PurchaseFixtures.Utc(10, 9), PurchaseOrderStatus.Ordered, PurchaseFixtures.Kapa,
            "Kapa Millers", PurchaseFixtures.Maize, "Maize", PurchaseFixtures.Bag25, "25kg bag", 10m, 0m, 1100m)).ToList();

        var maize = ReportsService.BuildPriceTrends(Period, mixed, false, 100).Items.Where(i => i.ProductName == "Maize").ToList();

        Assert.Equal(2, maize.Count);
        Assert.Equal(1100m, maize.Single(m => m.UnitSymbol == "25kg bag").LatestUnitCost);
        Assert.Equal(2300m, maize.Single(m => m.UnitSymbol == "50kg bag").LatestUnitCost);
    }
}

public class PurchaseReportsInputTests
{
    [Fact]
    public async Task A_backwards_range_is_rejected()
    {
        var service = UnreachableRepository.Service();

        await Assert.ThrowsAsync<InvalidAssignmentException>(() =>
            service.GetPurchaseSummaryAsync(new DateOnly(2026, 10, 31), new DateOnly(2026, 10, 1), 180, null, 10));
    }

    [Fact]
    public async Task A_bad_offset_is_rejected_for_open_orders()
    {
        var service = UnreachableRepository.Service();

        await Assert.ThrowsAsync<InvalidAssignmentException>(() => service.GetOpenOrdersAsync(null, 5000));
    }
}
