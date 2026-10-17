using UltimatePos.Application.Reports;
using UltimatePos.Application.Reports.Dtos;
using UltimatePos.Domain.Exceptions;
using static UltimatePos.Domain.Enums.PosEnums;

namespace Ultimate.Api.Tests;

public class StockStatusRulesTests
{
    [Theory]
    [InlineData(0, 10, "OutOfStock")]
    [InlineData(-5, 10, "OutOfStock")]      // oversold: still out of stock, not "low"
    [InlineData(-5, 0, "OutOfStock")]
    [InlineData(10, 10, "LowStock")]        // at the reorder level counts as low
    [InlineData(4, 10, "LowStock")]
    [InlineData(11, 10, "InStock")]
    [InlineData(3, 0, "InStock")]           // a reorder level of 0 was never set: never low
    public void Status_follows_quantity_and_reorder_level(int quantity, int reorderLevel, string expected)
    {
        Assert.Equal(expected, StockStatusRules.Classify(quantity, reorderLevel).ToString());
    }
}

public class StockStatusReportTests
{
    private static readonly DateOnly From = new(2026, 9, 14);
    private static readonly DateOnly To = new(2026, 10, 13);
    private static readonly Guid Mash = Guid.NewGuid();
    private static readonly Guid Premix = Guid.NewGuid();
    private static readonly Guid Salt = Guid.NewGuid();
    private static readonly Guid Bags = Guid.NewGuid();
    private static readonly Guid Idle = Guid.NewGuid();

    private static StockStatusRow Row(Guid id, string name, ItemType type, decimal qty, decimal reorder, decimal avgCost) =>
        new(id, name, name[..2].ToUpper(), type, "Feeds", "kg", qty, reorder, avgCost);

    private static readonly StockStatusRow[] Rows =
    {
        Row(Mash, "Layers Mash", ItemType.FinishedGood, 300m, 100m, 40m),    // in stock, uses 10/day -> 30 days
        Row(Premix, "Dairy Premix", ItemType.FinishedGood, 20m, 50m, 100m),  // low, uses 2/day -> 10 days
        Row(Salt, "Salt", ItemType.RawMaterial, 0m, 25m, 12m),                // out
        Row(Bags, "Bags", ItemType.Packaging, 500m, 0m, 0m),                  // in stock, cost never recorded
        Row(Idle, "Idle Item", ItemType.FinishedGood, 50m, 10m, 20m),         // in stock, nothing used
    };

    private static readonly ProductUsageRow[] Usage =
    {
        new(Mash, 300m), new(Premix, 60m), new(Salt, 30m),
    };

    private static StockStatusReportDto Build(StockStatus? filter = null, bool costs = true) =>
        ReportsService.BuildStockStatus(From, To, 30, Rows, Usage, filter, costs);

    [Fact]
    public void Counts_split_the_products_into_three_groups_that_never_overlap()
    {
        var report = Build();

        Assert.Equal(5, report.ProductCount);
        Assert.Equal(3, report.InStockCount);
        Assert.Equal(1, report.LowStockCount);
        Assert.Equal(1, report.OutOfStockCount);
        Assert.Equal(report.ProductCount, report.InStockCount + report.LowStockCount + report.OutOfStockCount);
    }

    [Fact]
    public void Most_urgent_first_out_of_stock_then_low_then_in_stock_by_days_of_cover()
    {
        var names = Build().Items.Select(i => i.ProductName).ToArray();

        Assert.Equal(new[] { "Salt", "Dairy Premix", "Layers Mash", "Bags", "Idle Item" }, names);
    }

    [Fact]
    public void Days_of_cover_is_stock_over_recent_daily_usage_and_null_when_nothing_was_used()
    {
        var items = Build().Items.ToDictionary(i => i.ProductName);

        Assert.Equal(10m, items["Layers Mash"].AverageDailyUsage);
        Assert.Equal(30m, items["Layers Mash"].DaysOfCover);
        Assert.Equal(10m, items["Dairy Premix"].DaysOfCover);
        Assert.Equal(0m, items["Salt"].DaysOfCover);          // out of stock with recent usage: no days left
        Assert.Null(items["Idle Item"].DaysOfCover);
        Assert.Equal(0m, items["Idle Item"].AverageDailyUsage);
    }

    [Fact]
    public void A_net_negative_usage_window_never_gives_negative_usage_or_cover()
    {
        var voidHeavy = new[] { new ProductUsageRow(Mash, -40m) }; // more sales voided than made in the window
        var report = ReportsService.BuildStockStatus(From, To, 30, Rows, voidHeavy, null, true);
        var item = report.Items.Single(i => i.ProductName == "Layers Mash");

        Assert.Equal(0m, item.AverageDailyUsage);
        Assert.Null(item.DaysOfCover);
    }

    [Fact]
    public void Shortfall_is_what_it_takes_to_get_back_to_the_reorder_level()
    {
        var items = Build().Items.ToDictionary(i => i.ProductName);

        Assert.Equal(30m, items["Dairy Premix"].Shortfall);
        Assert.Equal(25m, items["Salt"].Shortfall);
        Assert.Equal(0m, items["Layers Mash"].Shortfall);
    }

    [Fact]
    public void The_status_filter_trims_the_list_but_not_the_counts()
    {
        var report = Build(StockStatus.LowStock);

        Assert.Equal(new[] { "Dairy Premix" }, report.Items.Select(i => i.ProductName).ToArray());
        Assert.Equal(5, report.ProductCount);
        Assert.Equal(1, report.OutOfStockCount);
    }

    [Fact]
    public void Values_follow_the_same_rules_as_the_stock_valuation_report()
    {
        var report = Build();
        var items = report.Items.ToDictionary(i => i.ProductName);

        Assert.Equal(12000m, items["Layers Mash"].StockValue);   // 300 x 40
        Assert.Equal(2000m, items["Dairy Premix"].StockValue);
        Assert.Equal(0m, items["Salt"].StockValue);
        Assert.Equal(true, items["Bags"].CostMissing);           // stock but no cost
        Assert.Equal(false, items["Salt"].CostMissing);          // no stock, so nothing to cost
        Assert.Equal(1, report.ProductsMissingCost);
        Assert.Equal(12000m + 2000m + 0m + 0m + 1000m, report.TotalStockValue);
    }

    [Fact]
    public void Without_cost_permission_every_cost_field_is_null_but_quantities_are_intact()
    {
        var report = Build(costs: false);

        Assert.Null(report.TotalStockValue);
        Assert.Null(report.ProductsMissingCost);
        Assert.True(report.ByItemType.All(t => t.StockValue == null));
        foreach (var item in report.Items)
        {
            Assert.Null(item.AverageUnitCost);
            Assert.Null(item.StockValue);
            Assert.Null(item.CostMissing);
        }

        Assert.Equal(300m, report.Items.Single(i => i.ProductName == "Layers Mash").QuantityOnHand);
        Assert.Equal(1, report.LowStockCount);
    }

    [Fact]
    public void Totals_by_item_type_count_the_alerts_in_each_type()
    {
        var byType = Build().ByItemType.ToDictionary(t => t.ItemType);

        Assert.Equal(3, byType["FinishedGood"].ProductCount);
        Assert.Equal(1, byType["FinishedGood"].LowStockCount);
        Assert.Equal(1, byType["RawMaterial"].OutOfStockCount);
        Assert.Equal(500m * 0m, byType["Packaging"].StockValue);
    }

    [Fact]
    public void An_empty_shop_gives_an_empty_report()
    {
        var report = ReportsService.BuildStockStatus(From, To, 30, Array.Empty<StockStatusRow>(), Array.Empty<ProductUsageRow>(), null, true);

        Assert.Equal(0, report.ProductCount);
        Assert.Equal(0m, report.TotalStockValue);
        Assert.Empty(report.Items);
    }
}

public class StockMovementReportTests
{
    private static readonly ReportPeriod Period = ReportPeriod.Create(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), 180);
    private static readonly Guid Mash = Guid.NewGuid();
    private static readonly Guid Salt = Guid.NewGuid();

    private static StockMovementTotalRow T(Guid id, string name, StockMovementType type, bool increase, decimal qty, decimal value, int count = 1) =>
        new(id, name, name[..2].ToUpper(), "kg", type, increase, qty, value, count);

    private static readonly StockMovementTotalRow[] Totals =
    {
        T(Mash, "Layers Mash", StockMovementType.PurchaseReceipt, true, 500m, 20000m, 2),
        T(Mash, "Layers Mash", StockMovementType.ProductionOutput, true, 200m, 0m),
        T(Mash, "Layers Mash", StockMovementType.Sale, false, -350m, -14000m, 7),
        T(Mash, "Layers Mash", StockMovementType.SaleVoid, true, 50m, 2000m),
        T(Mash, "Layers Mash", StockMovementType.ProductionConsumption, false, -80m, -3200m),
        T(Mash, "Layers Mash", StockMovementType.Adjustment, true, 10m, 400m),
        T(Mash, "Layers Mash", StockMovementType.Adjustment, false, -25m, -1000m),
        T(Salt, "Salt", StockMovementType.ProductionConsumption, false, -40m, -480m, 2),
    };

    private static readonly Dictionary<Guid, decimal> Opening = new() { [Mash] = 100m, [Salt] = 60m };

    [Fact]
    public void Each_row_adds_up_opening_plus_in_minus_out_equals_closing()
    {
        var report = ReportsService.BuildMovementReport(Period, Opening, Totals, true);
        var mash = report.Items.Single(i => i.ProductName == "Layers Mash");

        Assert.Equal(100m, mash.Opening);
        Assert.Equal(500m, mash.Received);
        Assert.Equal(200m, mash.Produced);
        Assert.Equal(300m, mash.Sold);        // 350 sold, 50 of it voided again
        Assert.Equal(80m, mash.Consumed);
        Assert.Equal(10m, mash.AdjustedIn);
        Assert.Equal(25m, mash.AdjustedOut);
        Assert.Equal(100m + 500m + 200m - 300m - 80m + 10m - 25m, mash.Closing);
        Assert.Equal(405m, mash.Closing);
    }

    [Fact]
    public void Closing_is_built_from_every_movement_so_it_matches_the_real_balance()
    {
        var report = ReportsService.BuildMovementReport(Period, Opening, Totals, true);

        foreach (var row in report.Items)
        {
            var net = Totals.Where(t => t.ProductId == row.ProductId).Sum(t => t.Quantity);
            Assert.Equal(Opening[row.ProductId] + net, row.Closing);
            Assert.Equal(row.Opening + row.Received + row.Produced - row.Sold - row.Consumed + row.AdjustedIn - row.AdjustedOut, row.Closing);
        }
    }

    [Fact]
    public void Values_are_the_cost_of_what_moved_and_adjustments_net_to_a_loss()
    {
        var mash = ReportsService.BuildMovementReport(Period, Opening, Totals, true).Items.Single(i => i.ProductName == "Layers Mash");

        Assert.Equal(20000m, mash.ReceivedValue);
        Assert.Equal(12000m, mash.SoldCost);            // 14,000 sold less 2,000 voided
        Assert.Equal(3200m, mash.ConsumedCost);
        Assert.Equal(-600m, mash.AdjustmentNetValue);   // +400 - 1,000
    }

    [Fact]
    public void Without_cost_permission_values_are_null_and_quantities_unchanged()
    {
        var report = ReportsService.BuildMovementReport(Period, Opening, Totals, false);

        Assert.Null(report.TotalReceivedValue);
        Assert.Null(report.TotalSoldCost);
        Assert.Null(report.TotalConsumedCost);
        Assert.Null(report.TotalAdjustmentNetValue);
        foreach (var row in report.Items)
        {
            Assert.Null(row.ReceivedValue);
            Assert.Null(row.SoldCost);
            Assert.Null(row.ConsumedCost);
            Assert.Null(row.AdjustmentNetValue);
        }

        Assert.Equal(405m, report.Items.Single(i => i.ProductName == "Layers Mash").Closing);
    }

    [Fact]
    public void A_product_first_seen_in_the_period_opens_at_zero()
    {
        var newProduct = Guid.NewGuid();
        var totals = new[] { T(newProduct, "Newbie", StockMovementType.PurchaseReceipt, true, 40m, 800m) };

        var row = ReportsService.BuildMovementReport(Period, new Dictionary<Guid, decimal>(), totals, true).Items.Single();

        Assert.Equal(0m, row.Opening);
        Assert.Equal(40m, row.Closing);
    }

    [Fact]
    public void Rows_are_by_product_name_and_totals_sum_the_values()
    {
        var report = ReportsService.BuildMovementReport(Period, Opening, Totals, true);

        Assert.Equal(new[] { "Layers Mash", "Salt" }, report.Items.Select(i => i.ProductName).ToArray());
        Assert.Equal(2, report.ProductCount);
        Assert.Equal(12000m + 0m, report.TotalSoldCost);
        Assert.Equal(3200m + 480m, report.TotalConsumedCost);
    }

    [Fact]
    public void No_movements_means_no_rows_even_if_stock_exists()
    {
        var report = ReportsService.BuildMovementReport(Period, Opening, Array.Empty<StockMovementTotalRow>(), true);

        Assert.Empty(report.Items);
        Assert.Equal(0, report.ProductCount);
    }
}

public class StockAdjustmentReportTests
{
    private static readonly ReportPeriod Period = ReportPeriod.Create(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), 180);
    private static readonly Guid Mash = Guid.NewGuid();
    private static readonly Guid Wanjiku = Guid.NewGuid();
    private static readonly Guid Otieno = Guid.NewGuid();

    private static StockAdjustmentRow Adj(int day, int hour, decimal qty, decimal? unitCost, string reason, Guid? by) =>
        new(Guid.NewGuid(), new DateTime(2026, 10, day, hour, 0, 0, DateTimeKind.Utc), Mash, "Layers Mash", "LM", "kg", qty, unitCost, reason, by);

    private static readonly StockAdjustmentRow[] Rows =
    {
        Adj(3, 8,  -10m, 40m, "Spillage", Wanjiku),
        Adj(5, 8,  -5m,  40m, "Rats", Otieno),
        Adj(7, 8,   20m, 38m, "Stock count", Wanjiku),
        Adj(9, 8,  -2m,  null, "Old stock, no cost", null),
    };

    private static readonly Dictionary<Guid, string> Names = new() { [Wanjiku] = "wanjiku", [Otieno] = "otieno" };

    [Fact]
    public void Counts_and_values_split_increases_from_decreases_with_the_loss_as_a_positive_amount()
    {
        var report = ReportsService.BuildAdjustmentReport(Period, Rows, Names, 100, true);

        Assert.Equal(4, report.AdjustmentCount);
        Assert.Equal(1, report.IncreaseCount);
        Assert.Equal(3, report.DecreaseCount);
        Assert.Equal(760m, report.IncreaseValue);    // 20 x 38
        Assert.Equal(600m, report.DecreaseValue);    // 10 x 40 + 5 x 40, the uncosted 2 kg counts as 0
        Assert.Equal(160m, report.NetValue);
        Assert.Equal(1, report.UncostedCount);
    }

    [Fact]
    public void Newest_first_with_the_person_and_reason_and_the_local_day()
    {
        var items = ReportsService.BuildAdjustmentReport(Period, Rows, Names, 100, true).Items.ToList();

        Assert.Equal(new[] { 9, 7, 5, 3 }, items.Select(i => i.Date.Day).ToArray());
        Assert.Equal("wanjiku", items[1].AdjustedBy);
        Assert.Equal("Stock count", items[1].Reason);
        Assert.Equal("Increase", items[1].Direction);
        Assert.Null(items[0].AdjustedBy);             // no user recorded
        Assert.Equal("Decrease", items[0].Direction);
    }

    [Fact]
    public void An_adjustment_late_in_the_evening_UTC_sits_on_the_next_Nairobi_day()
    {
        var rows = new[] { Adj(4, 22, -1m, 40m, "Late", Wanjiku) };

        var item = ReportsService.BuildAdjustmentReport(Period, rows, Names, 100, true).Items.Single();

        Assert.Equal(5, item.Date.Day);
    }

    [Fact]
    public void The_row_limit_cuts_the_list_but_never_the_totals()
    {
        var report = ReportsService.BuildAdjustmentReport(Period, Rows, Names, 2, true);

        Assert.Equal(2, report.Items.Count());
        Assert.True(report.Truncated);
        Assert.Equal(4, report.AdjustmentCount);
        Assert.Equal(600m, report.DecreaseValue);
    }

    [Fact]
    public void Without_cost_permission_values_and_unit_costs_are_hidden_but_the_audit_trail_is_not()
    {
        var report = ReportsService.BuildAdjustmentReport(Period, Rows, Names, 100, false);

        Assert.Null(report.IncreaseValue);
        Assert.Null(report.DecreaseValue);
        Assert.Null(report.NetValue);
        Assert.Null(report.UncostedCount);
        Assert.Equal(4, report.AdjustmentCount);
        foreach (var item in report.Items)
        {
            Assert.Null(item.UnitCost);
            Assert.Null(item.Value);
            Assert.True(item.QuantityChange != 0m);
        }

        Assert.Equal("Spillage", report.Items.Last().Reason);
        Assert.Equal("wanjiku", report.Items.Last().AdjustedBy);
    }
}

public class ReportLimitsTests
{
    [Theory]
    [InlineData(0, 100)]
    [InlineData(-5, 100)]
    [InlineData(7, 7)]
    [InlineData(500, 500)]
    [InlineData(9999, 500)]
    public void Row_limits_default_and_cap(int requested, int expected)
    {
        Assert.Equal(expected, ReportLimits.ClampRows(requested));
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(-1, 10)]
    [InlineData(25, 25)]
    [InlineData(50, 50)]
    [InlineData(51, 50)]
    public void Top_limits_default_and_cap(int requested, int expected)
    {
        Assert.Equal(expected, ReportLimits.ClampTop(requested));
    }
}

public class InventoryReportsInputTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    [InlineData(367)]
    public async Task Usage_days_outside_one_to_366_are_rejected(int usageDays)
    {
        var service = UnreachableRepository.Service();

        await Assert.ThrowsAsync<InvalidAssignmentException>(() =>
            service.GetStockStatusAsync(null, null, null, false, usageDays, 180, true));
    }

    [Fact]
    public async Task A_bad_offset_is_rejected_before_any_query_runs()
    {
        var service = UnreachableRepository.Service();

        await Assert.ThrowsAsync<InvalidAssignmentException>(() =>
            service.GetStockStatusAsync(null, null, null, false, 30, 9999, true));
    }

    [Fact]
    public async Task A_range_longer_than_a_year_is_rejected()
    {
        var service = UnreachableRepository.Service();

        await Assert.ThrowsAsync<InvalidAssignmentException>(() =>
            service.GetMovementReportAsync(new DateOnly(2025, 1, 1), new DateOnly(2026, 12, 31), 180, null, true));
    }
}
