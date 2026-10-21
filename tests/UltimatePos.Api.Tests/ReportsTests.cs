using UltimatePos.Application.Costing;
using UltimatePos.Application.Reports;
using UltimatePos.Domain.Exceptions;
using static UltimatePos.Domain.Enums.PosEnums;

namespace Ultimate.Api.Tests;

public class ReportPeriodTests
{
    private static readonly DateOnly October1 = new(2026, 10, 1);
    private static readonly DateOnly October31 = new(2026, 10, 31);

    [Fact]
    public void A_Nairobi_month_runs_from_9pm_UTC_the_evening_before()
    {
        var period = ReportPeriod.Create(October1, October31, 180);

        Assert.Equal(new DateTime(2026, 9, 30, 21, 0, 0), period.FromUtc);
        Assert.Equal(new DateTime(2026, 10, 31, 21, 0, 0), period.ToUtcExclusive); // exclusive: the start of Nov 1 in Nairobi
        Assert.Equal(DateTimeKind.Utc, period.FromUtc.Kind);                       // Npgsql only accepts UTC timestamps
        Assert.Equal(31, period.DayCount);
    }

    [Fact]
    public void A_late_evening_UTC_sale_belongs_to_the_next_local_day()
    {
        var period = ReportPeriod.Create(October1, October31, 180);

        Assert.Equal(new DateOnly(2026, 10, 1), period.LocalDateOf(new DateTime(2026, 10, 1, 20, 59, 59, DateTimeKind.Utc)));
        Assert.Equal(new DateOnly(2026, 10, 2), period.LocalDateOf(new DateTime(2026, 10, 1, 21, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void An_offset_outside_the_real_world_is_rejected()
    {
        Assert.Throws<InvalidAssignmentException>(() => ReportPeriod.Create(October1, October31, 841));
        Assert.Throws<InvalidAssignmentException>(() => ReportPeriod.Create(October1, October31, -841));
    }

    [Fact]
    public void The_range_must_run_forwards_and_stay_within_a_year()
    {
        Assert.Throws<InvalidAssignmentException>(() => ReportPeriod.Create(October31, October1, 180));
        Assert.Throws<InvalidAssignmentException>(() => ReportPeriod.Create(new DateOnly(2025, 1, 1), new DateOnly(2026, 12, 31), 180));
    }

    [Fact]
    public void A_date_left_out_of_the_request_says_so_instead_of_blaming_the_year()
    {
        // An omitted query-string date arrives as DateOnly.MinValue.
        var missingFrom = Assert.Throws<InvalidAssignmentException>(() => ReportPeriod.Create(default, October31, 180));
        var missingTo = Assert.Throws<InvalidAssignmentException>(() => ReportPeriod.Create(October1, default, 180));

        Assert.Contains("required", missingFrom.Message);
        Assert.Contains("required", missingTo.Message);
    }
}

public class AgingCalculatorTests
{
    private static readonly TimeSpan Nairobi = TimeSpan.FromHours(3);
    private static readonly DateOnly AsOf = new(2026, 10, 31);
    private static readonly Guid Customer = Guid.NewGuid();

    // 09:00 UTC is midday in Nairobi, so these never sit near a day boundary.
    private static DateTime DaysAgo(int days) => AsOf.AddDays(-days).ToDateTime(new TimeOnly(9, 0), DateTimeKind.Utc);

    private static CustomerLedgerRow Entry(CustomerLedgerEntryType type, decimal amount, DateTime at, Guid? referenceId = null) =>
        new(Customer, "Kamau Agrovet", "0712345678", type, amount, type == CustomerLedgerEntryType.Payment ? "CustomerPayment" : "Sale",
            referenceId ?? Guid.NewGuid(), null, at);

    private static CustomerLedgerRow Invoice(decimal amount, int daysAgo, Guid? saleId = null) =>
        Entry(CustomerLedgerEntryType.Invoice, amount, DaysAgo(daysAgo), saleId);

    private static CustomerLedgerRow Payment(decimal amount, int daysAgo) =>
        Entry(CustomerLedgerEntryType.Payment, amount, DaysAgo(daysAgo));

    private static CustomerLedgerRow Reversal(decimal amount, int daysAgo, Guid saleId) =>
        Entry(CustomerLedgerEntryType.Reversal, amount, DaysAgo(daysAgo), saleId);

    [Theory]
    [InlineData(0, "0-30")]
    [InlineData(30, "0-30")]
    [InlineData(31, "31-60")]
    [InlineData(60, "31-60")]
    [InlineData(61, "61-90")]
    [InlineData(90, "61-90")]
    [InlineData(91, "90+")]
    [InlineData(400, "90+")]
    public void Invoices_fall_in_the_bucket_for_their_age(int ageDays, string expectedBucket)
    {
        var result = AgingCalculator.Calculate(new[] { Invoice(1000m, ageDays) }, AsOf, Nairobi);

        var invoice = Assert.Single(result.OpenInvoices);
        Assert.Equal(ageDays, invoice.AgeDays);
        Assert.Equal(expectedBucket, invoice.Bucket);
        Assert.Equal(expectedBucket, AgingCalculator.BucketFor(ageDays));
    }

    [Fact]
    public void Buckets_add_up_to_the_outstanding_total()
    {
        var result = AgingCalculator.Calculate(new[]
        {
            Invoice(1000m, 10), Invoice(2000m, 45), Invoice(4000m, 75), Invoice(700m, 120)
        }, AsOf, Nairobi);

        Assert.Equal(1000m, result.Days0To30);
        Assert.Equal(2000m, result.Days31To60);
        Assert.Equal(4000m, result.Days61To90);
        Assert.Equal(700m, result.Over90);
        Assert.Equal(7700m, result.Outstanding);
        Assert.Equal(0m, result.UnappliedCredit);
    }

    [Fact]
    public void A_payment_settles_the_oldest_invoice_first()
    {
        var result = AgingCalculator.Calculate(new[]
        {
            Invoice(1000m, 50), Invoice(500m, 10), Payment(1200m, 5)
        }, AsOf, Nairobi);

        var remaining = Assert.Single(result.OpenInvoices);
        Assert.Equal(500m, remaining.OriginalAmount);
        Assert.Equal(300m, remaining.OutstandingAmount); // the 50-day-old invoice is gone, 200 of the newer one is paid
        Assert.Equal(300m, result.Outstanding);
        Assert.Equal(300m, result.Days0To30);
        Assert.Equal(0m, result.Days31To60);
    }

    [Fact]
    public void A_voided_sale_reversal_cancels_exactly_the_invoice_it_points_at()
    {
        var voided = Guid.NewGuid();
        var result = AgingCalculator.Calculate(new[]
        {
            Invoice(800m, 40, voided), Invoice(200m, 5), Reversal(800m, 39, voided)
        }, AsOf, Nairobi);

        var remaining = Assert.Single(result.OpenInvoices);
        Assert.Equal(200m, remaining.OutstandingAmount);
        Assert.Equal(200m, result.Outstanding);
        Assert.Equal(0m, result.UnappliedCredit);
    }

    [Fact]
    public void Paying_more_than_is_owed_is_reported_as_unapplied_credit()
    {
        var result = AgingCalculator.Calculate(new[] { Invoice(500m, 20), Payment(800m, 3) }, AsOf, Nairobi);

        Assert.Empty(result.OpenInvoices);
        Assert.Equal(0m, result.Outstanding);
        Assert.Equal(300m, result.UnappliedCredit);
    }

    [Fact]
    public void A_payment_made_before_an_invoice_is_still_credit_against_it()
    {
        var result = AgingCalculator.Calculate(new[] { Payment(300m, 30), Invoice(1000m, 10) }, AsOf, Nairobi);

        Assert.Equal(700m, result.Outstanding);
        Assert.Equal(0m, result.UnappliedCredit);
    }

    [Fact]
    public void A_reversal_that_matches_no_invoice_is_treated_as_credit()
    {
        var result = AgingCalculator.Calculate(new[] { Invoice(400m, 10), Reversal(100m, 5, Guid.NewGuid()) }, AsOf, Nairobi);

        Assert.Equal(300m, result.Outstanding);
    }

    [Fact]
    public void A_reversal_larger_than_its_invoice_leaves_the_surplus_as_credit()
    {
        var sale = Guid.NewGuid();
        var result = AgingCalculator.Calculate(new[] { Invoice(100m, 10, sale), Reversal(150m, 9, sale) }, AsOf, Nairobi);

        Assert.Empty(result.OpenInvoices);
        Assert.Equal(50m, result.UnappliedCredit);
    }

    [Fact]
    public void Outstanding_less_credit_always_equals_the_ledger_balance()
    {
        var voided = Guid.NewGuid();
        var ledger = new[]
        {
            Invoice(1000m, 80), Invoice(2000m, 60, voided), Invoice(1500m, 20),
            Payment(700m, 50), Reversal(2000m, 55, voided), Payment(300m, 10)
        };

        var result = AgingCalculator.Calculate(ledger, AsOf, Nairobi);

        // invoices - payments - reversals, exactly as CustomerLedger signs them
        var ledgerBalance = 1000m + 2000m + 1500m - 700m - 2000m - 300m;
        Assert.Equal(1500m, ledgerBalance);
        Assert.Equal(ledgerBalance, result.Outstanding - result.UnappliedCredit);
    }

    [Fact]
    public void An_invoice_is_aged_from_its_Nairobi_date_not_its_UTC_date()
    {
        // 22:00 UTC on Oct 30 is 01:00 on Oct 31 in Nairobi — the same day as the as-of date, so age 0, not 1.
        var lateEvening = new DateTime(2026, 10, 30, 22, 0, 0, DateTimeKind.Utc);

        var result = AgingCalculator.Calculate(new[] { Entry(CustomerLedgerEntryType.Invoice, 100m, lateEvening) }, AsOf, Nairobi);

        var invoice = Assert.Single(result.OpenInvoices);
        Assert.Equal(new DateOnly(2026, 10, 31), invoice.InvoiceDate);
        Assert.Equal(0, invoice.AgeDays);
    }

    [Fact]
    public void A_customer_with_no_entries_owes_nothing()
    {
        var result = AgingCalculator.Calculate(Array.Empty<CustomerLedgerRow>(), AsOf, Nairobi);

        Assert.Empty(result.OpenInvoices);
        Assert.Equal(0m, result.Outstanding);
        Assert.Equal(0m, result.UnappliedCredit);
    }
}

public class SalesReportTests
{
    private static readonly ReportPeriod Period = ReportPeriod.Create(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 3), 180);
    private static readonly Guid Acme = Guid.NewGuid();
    private static readonly Guid LayersMash = Guid.NewGuid();
    private static readonly Guid DairyPremix = Guid.NewGuid();

    private static SaleReportRow Sale(string number, DateTime createdAtUtc, SaleStatus status, PaymentMethod method, decimal total, Guid? customerId = null) =>
        new(Guid.NewGuid(), number, createdAtUtc, status, method, customerId, customerId.HasValue ? "Acme Agrovet" : null, total);

    private static DateTime Utc(int day, int hour) => new(2026, 10, day, hour, 0, 0, DateTimeKind.Utc);

    private static readonly SaleReportRow[] Sales =
    {
        Sale("S1", Utc(1, 8),  SaleStatus.Completed, PaymentMethod.Cash, 1000m),
        Sale("S2", Utc(1, 22), SaleStatus.Completed, PaymentMethod.Credit, 3000m, Acme),         // 01:00 on Oct 2 in Nairobi
        Sale("S3", Utc(2, 9),  SaleStatus.Completed, PaymentMethod.MpesaStkPush, 2000m, Acme),
        Sale("S4", Utc(2, 10), SaleStatus.Voided,    PaymentMethod.Cash, 500m),
        Sale("S5", Utc(3, 10), SaleStatus.Pending,   PaymentMethod.MpesaStkPush, 700m),
    };

    private static readonly SaleCostRow[] Lines =
    {
        new(LayersMash, "Layers Mash", "LM-1", 60m, 3000m, 40m),
        new(LayersMash, "Layers Mash", "LM-1", 30m, 1500m, 40m),
        new(DairyPremix, "Dairy Premix", "DP-1", 10m, 1500m, 100m),
    };

    [Fact]
    public void Only_completed_sales_count_as_revenue_and_voided_and_pending_are_reported_apart()
    {
        var report = ReportsService.BuildSalesReport(Period, Sales, Lines, top: 10);

        Assert.Equal(3, report.SaleCount);
        Assert.Equal(6000m, report.Revenue);
        Assert.Equal(2000m, report.AverageSaleValue);
        Assert.Equal(1, report.VoidedCount);
        Assert.Equal(500m, report.VoidedAmount);
        Assert.Equal(1, report.PendingCount);
        Assert.Equal(700m, report.PendingAmount);
    }

    [Fact]
    public void Sales_are_placed_on_their_Nairobi_day_and_every_day_has_a_row()
    {
        var byDay = ReportsService.BuildSalesReport(Period, Sales, Lines, 10).ByDay.ToList();

        Assert.Equal(3, byDay.Count);
        Assert.Equal((new DateOnly(2026, 10, 1), 1, 1000m), (byDay[0].Date, byDay[0].SaleCount, byDay[0].Revenue));
        Assert.Equal((new DateOnly(2026, 10, 2), 2, 5000m), (byDay[1].Date, byDay[1].SaleCount, byDay[1].Revenue)); // S2 (late UTC) + S3
        Assert.Equal((new DateOnly(2026, 10, 3), 0, 0m), (byDay[2].Date, byDay[2].SaleCount, byDay[2].Revenue));    // only a pending sale that day
    }

    [Fact]
    public void Payment_methods_are_ranked_with_their_share_of_revenue()
    {
        var methods = ReportsService.BuildSalesReport(Period, Sales, Lines, 10).ByPaymentMethod.ToList();

        Assert.Equal(new[] { "Credit", "MpesaStkPush", "Cash" }, methods.Select(m => m.PaymentMethod).ToArray());
        Assert.Equal(new[] { 50.00m, 33.33m, 16.67m }, methods.Select(m => m.PercentOfRevenue).ToArray());
    }

    [Fact]
    public void A_typed_mpesa_code_is_its_own_payment_method_in_the_report_and_on_the_dashboard()
    {
        var sales = Sales.Append(Sale("S6", Utc(3, 11), SaleStatus.Completed, PaymentMethod.MpesaReference, 4000m)).ToList();

        var methods = ReportsService.BuildSalesReport(Period, sales, Lines, 10).ByPaymentMethod.ToList();
        Assert.Equal(("MpesaReference", 1, 4000m), (methods[0].PaymentMethod, methods[0].SaleCount, methods[0].Total));
        Assert.Equal(10_000m, UltimatePos.Application.Dashboard.DashboardService.BuildSales(Period, sales).Revenue);
    }

    [Fact]
    public void Walk_in_sales_are_their_own_customer_bucket()
    {
        var customers = ReportsService.BuildSalesReport(Period, Sales, Lines, 10).TopCustomers.ToList();

        Assert.Equal(2, customers.Count);
        Assert.Equal((Acme, "Acme Agrovet", 2, 5000m), (customers[0].CustomerId, customers[0].CustomerName, customers[0].SaleCount, customers[0].Total));
        Assert.Null(customers[1].CustomerId);
        Assert.Equal("Walk-in", customers[1].CustomerName);
        Assert.Equal(1000m, customers[1].Total);
    }

    [Fact]
    public void Top_products_are_ranked_by_revenue_and_top_limits_both_lists()
    {
        var full = ReportsService.BuildSalesReport(Period, Sales, Lines, 10).TopProducts.ToList();
        Assert.Equal(new[] { "Layers Mash", "Dairy Premix" }, full.Select(p => p.ProductName).ToArray());
        Assert.Equal(4500m, full[0].Revenue);
        Assert.Equal(90m, full[0].QuantitySoldInBaseUnits); // two lines of the same product are added together
        Assert.Equal(75.00m, full[0].PercentOfRevenue);

        var limited = ReportsService.BuildSalesReport(Period, Sales, Lines, 1);
        Assert.Single(limited.TopProducts);
        Assert.Single(limited.TopCustomers);
    }

    [Fact]
    public void Sales_are_broken_down_by_the_unit_sold_in_so_pack_sizes_of_one_product_stay_apart()
    {
        // One water product sold as 20L and 1L refills, with one 20L refill brought back.
        var water = Guid.NewGuid();
        var twenty = Guid.NewGuid();
        var one = Guid.NewGuid();
        var lines = new SaleCostRow[]
        {
            new(water, "Water", "W-1", 40m, 140m, 1m, twenty, "20L", 2m),
            new(water, "Water", "W-1", 20m, 70m, 1m, twenty, "20L", 1m),
            new(water, "Water", "W-1", 3m, 15m, 1m, one, "l", 3m),
            new(water, "Water", "W-1", -20m, -70m, 1m, twenty, "20L", -1m), // a return
        };

        var report = ReportsService.BuildSalesReport(Period, Sales, lines, 10);

        var units = report.TopProductUnits!.ToList();
        Assert.Equal(new[] { "20L", "l" }, units.Select(u => u.UnitSymbol).ToArray());
        Assert.Equal((2m, 40m, 140m), (units[0].QuantitySold, units[0].QuantitySoldInBaseUnits, units[0].Revenue));
        Assert.Equal((3m, 3m, 15m), (units[1].QuantitySold, units[1].QuantitySoldInBaseUnits, units[1].Revenue));
        Assert.Equal(90.32m, units[0].PercentOfRevenue);
        // The product-level row still adds every unit together.
        Assert.Equal(43m, report.TopProducts.Single().QuantitySoldInBaseUnits);
    }

    [Fact]
    public void Completed_sales_are_totalled_per_cashier()
    {
        var wanjiru = Guid.NewGuid();
        var otieno = Guid.NewGuid();
        var sales = new[]
        {
            new SaleReportRow(Guid.NewGuid(), "S1", Utc(1, 8), SaleStatus.Completed, PaymentMethod.Cash, null, null, 300m, wanjiru, "wanjiru"),
            new SaleReportRow(Guid.NewGuid(), "S2", Utc(1, 9), SaleStatus.Completed, PaymentMethod.MpesaTill, null, null, 500m, otieno, "otieno"),
            new SaleReportRow(Guid.NewGuid(), "S3", Utc(1, 10), SaleStatus.Completed, PaymentMethod.Cash, null, null, 300m, wanjiru, "wanjiru"),
            new SaleReportRow(Guid.NewGuid(), "S4", Utc(1, 11), SaleStatus.Voided, PaymentMethod.Cash, null, null, 900m, otieno, "otieno"),
        };

        var cashiers = ReportsService.BuildSalesReport(Period, sales, Array.Empty<SaleCostRow>(), 10).ByCashier!.ToList();

        Assert.Equal(2, cashiers.Count);
        Assert.Equal((wanjiru, "wanjiru", 2, 600m, 54.55m),
            (cashiers[0].UserId, cashiers[0].CashierName, cashiers[0].SaleCount, cashiers[0].Total, cashiers[0].PercentOfRevenue));
        Assert.Equal((otieno, 1, 500m), (cashiers[1].UserId, cashiers[1].SaleCount, cashiers[1].Total)); // the voided sale is not counted
    }

    [Fact]
    public void A_period_with_no_sales_is_an_empty_report_not_an_error()
    {
        var report = ReportsService.BuildSalesReport(Period, Array.Empty<SaleReportRow>(), Array.Empty<SaleCostRow>(), 10);

        Assert.Equal(0, report.SaleCount);
        Assert.Equal(0m, report.Revenue);
        Assert.Equal(0m, report.AverageSaleValue);
        Assert.Equal(3, report.ByDay.Count());
        Assert.Empty(report.ByPaymentMethod);
        Assert.Empty(report.TopCustomers);
        Assert.Empty(report.TopProducts);
    }
}

public class ProductionCostReportTests
{
    private static readonly ReportPeriod Period = ReportPeriod.Create(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), 180);

    private static readonly Guid Layers = Guid.NewGuid();
    private static readonly Guid Dairy = Guid.NewGuid();
    private static readonly Guid Bag = Guid.NewGuid();
    private static readonly Guid Kg = Guid.NewGuid();

    private static readonly Guid R1 = Guid.NewGuid();
    private static readonly Guid R2 = Guid.NewGuid();
    private static readonly Guid R3 = Guid.NewGuid();
    private static readonly Guid R4 = Guid.NewGuid();

    private static DateTime Utc(int day) => new(2026, 10, day, 9, 0, 0, DateTimeKind.Utc);

    private static readonly ProductionRunReportRow[] Runs =
    {
        new(R1, "PR-000001", Utc(5),  Layers, "Layers Mash", "LM-1", Bag, "bag", 100m, 90m,  8000m, 1000m, 9000m),
        new(R2, "PR-000002", Utc(12), Layers, "Layers Mash", "LM-1", Bag, "bag", 100m, 100m, 8500m, 500m,  9000m),
        new(R3, "PR-000003", Utc(15), Layers, "Layers Mash", "LM-1", Kg,  "kg",  50m,  50m,  2000m, 500m,  2500m),
        new(R4, "PR-000004", Utc(20), Dairy,  "Dairy Premix", "DP-1", Bag, "bag", 10m,  10m,  800m,  200m,  1000m),
    };

    private static readonly ProductionCostLineRow[] CostLines =
    {
        new(R1, ProductionCostType.Labour, 600m), new(R1, ProductionCostType.Transport, 400m),
        new(R2, ProductionCostType.Labour, 300m), new(R2, ProductionCostType.Utilities, 200m),
        new(R3, ProductionCostType.Labour, 500m),
        new(R4, ProductionCostType.Overhead, 200m),
    };

    private static readonly Guid Maize = Guid.NewGuid();
    private static readonly Guid Soya = Guid.NewGuid();
    private static readonly Guid Premix = Guid.NewGuid();

    private static readonly ProductionInputRow[] Inputs =
    {
        new(Maize, "Maize", "RM-1", "kg", 700m, 7000m), new(Maize, "Maize", "RM-1", "kg", 500m, 5000m),
        new(Soya, "Soya", "RM-2", "kg", 200m, 4000m),   new(Soya, "Soya", "RM-2", "kg", 100m, 2500m),
        new(Premix, "Premix", "RM-3", "kg", 10m, 800m),
    };

    [Fact]
    public void Totals_cover_every_run_and_material_plus_additional_equals_total()
    {
        var report = ReportsService.BuildProductionCostReport(Period, Runs, CostLines, Inputs, 10);

        Assert.Equal(4, report.RunCount);
        Assert.Equal(19300m, report.MaterialCost);
        Assert.Equal(2200m, report.AdditionalCost);
        Assert.Equal(21500m, report.TotalCost);
        Assert.Equal(report.TotalCost, report.MaterialCost + report.AdditionalCost);
    }

    [Fact]
    public void Runs_of_one_product_in_different_units_are_never_added_together()
    {
        var rows = ReportsService.BuildProductionCostReport(Period, Runs, CostLines, Inputs, 10).ByProduct.ToList();

        Assert.Equal(3, rows.Count);
        Assert.Equal(new[] { "bag", "kg", "bag" }, rows.Select(r => r.UnitSymbol).ToArray());              // ranked by total cost
        Assert.Equal(new[] { 18000m, 2500m, 1000m }, rows.Select(r => r.TotalCost).ToArray());
    }

    [Fact]
    public void A_product_row_shows_yield_and_how_unit_cost_moved_between_runs()
    {
        var layersBags = ReportsService.BuildProductionCostReport(Period, Runs, CostLines, Inputs, 10).ByProduct.First();

        Assert.Equal(2, layersBags.RunCount);
        Assert.Equal(200m, layersBags.QuantityPlanned);
        Assert.Equal(190m, layersBags.QuantityProduced);
        Assert.Equal(95.00m, layersBags.YieldPercent);
        Assert.Equal(16500m, layersBags.MaterialCost);
        Assert.Equal(1500m, layersBags.AdditionalCost);
        Assert.Equal(94.74m, layersBags.AverageCostPerUnit);  // 18,000 / 190
        Assert.Equal(90.00m, layersBags.LowestCostPerUnit);   // run 2: 9,000 / 100
        Assert.Equal(100.00m, layersBags.HighestCostPerUnit); // run 1: 9,000 / 90 — the poorer yield cost more per bag
        Assert.Equal(90.00m, layersBags.LatestCostPerUnit);   // the most recent run
        Assert.Equal(Utc(12), layersBags.LatestRunAt);
    }

    [Fact]
    public void Additional_costs_are_totalled_by_type_with_their_share()
    {
        var types = ReportsService.BuildProductionCostReport(Period, Runs, CostLines, Inputs, 10).AdditionalCostsByType.ToList();

        Assert.Equal(new[] { "Labour", "Transport", "Overhead", "Utilities" }, types.Select(t => t.CostType).ToArray());
        Assert.Equal(new[] { 1400m, 400m, 200m, 200m }, types.Select(t => t.Amount).ToArray());
        Assert.Equal(new[] { 63.64m, 18.18m, 9.09m, 9.09m }, types.Select(t => t.PercentOfAdditionalCost).ToArray());
    }

    [Fact]
    public void Ingredients_are_added_up_across_runs_ranked_by_cost_and_limited_by_top()
    {
        var ingredients = ReportsService.BuildProductionCostReport(Period, Runs, CostLines, Inputs, 2).TopIngredients.ToList();

        Assert.Equal(2, ingredients.Count);
        Assert.Equal((Maize, 1200m, 12000m, 62.18m), (ingredients[0].IngredientProductId, ingredients[0].QuantityInBaseUnits, ingredients[0].TotalCost, ingredients[0].PercentOfMaterialCost));
        Assert.Equal((Soya, 300m, 6500m, 33.68m), (ingredients[1].IngredientProductId, ingredients[1].QuantityInBaseUnits, ingredients[1].TotalCost, ingredients[1].PercentOfMaterialCost));
    }

    [Fact]
    public void A_run_that_produced_nothing_has_a_zero_unit_cost_instead_of_dividing_by_zero()
    {
        var scrapped = new ProductionRunReportRow(R1, "PR-000009", Utc(7), Layers, "Layers Mash", "LM-1", Bag, "bag", 100m, 0m, 500m, 0m, 500m);

        var row = Assert.Single(ReportsService.BuildProductionCostReport(Period, new[] { scrapped }, Array.Empty<ProductionCostLineRow>(), Array.Empty<ProductionInputRow>(), 10).ByProduct);

        Assert.Equal(0m, row.AverageCostPerUnit);
        Assert.Equal(0m, row.YieldPercent);
        Assert.Equal(500m, row.TotalCost);
    }

    [Fact]
    public void A_period_with_no_production_is_an_empty_report_not_an_error()
    {
        var report = ReportsService.BuildProductionCostReport(Period, Array.Empty<ProductionRunReportRow>(),
            Array.Empty<ProductionCostLineRow>(), Array.Empty<ProductionInputRow>(), 10);

        Assert.Equal(0, report.RunCount);
        Assert.Equal(0m, report.TotalCost);
        Assert.Empty(report.ByProduct);
        Assert.Empty(report.AdditionalCostsByType);
        Assert.Empty(report.TopIngredients);
    }
}

/// <summary>
/// For tests that a bad request is rejected BEFORE anything is queried: every query throws, so a test only passes if
/// validation stopped the request first.
/// </summary>
internal sealed class UnreachableRepository : IReportsRepository, ICostingRepository
{
    public static ReportsService Service()
    {
        var repository = new UnreachableRepository();
        return new ReportsService(repository, repository);
    }

    private static Task<T> Fail<T>() => throw new InvalidOperationException("The request should have been rejected before any query ran.");

    public Task<IReadOnlyList<SaleReportRow>> GetSalesAsync(DateTime fromUtc, DateTime toUtcExclusive) => Fail<IReadOnlyList<SaleReportRow>>();
    public Task<IReadOnlyList<CustomerBalanceRow>> GetCustomerBalancesAsync() => Fail<IReadOnlyList<CustomerBalanceRow>>();
    public Task<IReadOnlyList<CustomerLedgerRow>> GetCustomerLedgerAsync(DateTime toUtcExclusive, Guid? customerId) => Fail<IReadOnlyList<CustomerLedgerRow>>();
    public Task<Dictionary<Guid, (string SaleNumber, DateOnly? PaymentDueDate)>> GetInvoiceReferencesAsync(IEnumerable<Guid> saleIds) =>
        Fail<Dictionary<Guid, (string SaleNumber, DateOnly? PaymentDueDate)>>();
    public Task<IReadOnlyList<ProductionRunReportRow>> GetProductionRunsAsync(DateTime fromUtc, DateTime toUtcExclusive, Guid? productId) => Fail<IReadOnlyList<ProductionRunReportRow>>();
    public Task<IReadOnlyList<ProductionCostLineRow>> GetProductionCostLinesAsync(DateTime fromUtc, DateTime toUtcExclusive, Guid? productId) => Fail<IReadOnlyList<ProductionCostLineRow>>();
    public Task<IReadOnlyList<ProductionInputRow>> GetProductionInputsAsync(DateTime fromUtc, DateTime toUtcExclusive, Guid? productId) => Fail<IReadOnlyList<ProductionInputRow>>();
    public Task<StockAlertCounts> GetStockAlertCountsAsync() => Fail<StockAlertCounts>();
    public Task<IReadOnlyList<StockStatusRow>> GetStockStatusRowsAsync(ItemType? itemType, Guid? categoryId, bool includeInactive) => Fail<IReadOnlyList<StockStatusRow>>();
    public Task<IReadOnlyList<ProductUsageRow>> GetStockUsageAsync(DateTime fromUtc, DateTime toUtcExclusive) => Fail<IReadOnlyList<ProductUsageRow>>();
    public Task<IReadOnlyList<StockMovementTotalRow>> GetStockMovementTotalsAsync(DateTime fromUtc, DateTime toUtcExclusive, Guid? productId) => Fail<IReadOnlyList<StockMovementTotalRow>>();
    public Task<Dictionary<Guid, decimal>> GetOpeningStockBalancesAsync(DateTime beforeUtc, Guid? productId) => Fail<Dictionary<Guid, decimal>>();
    public Task<IReadOnlyList<StockAdjustmentRow>> GetStockAdjustmentsAsync(DateTime fromUtc, DateTime toUtcExclusive, Guid? productId) => Fail<IReadOnlyList<StockAdjustmentRow>>();
    public Task<Dictionary<Guid, string>> GetUserNamesAsync(IEnumerable<Guid> userIds) => Fail<Dictionary<Guid, string>>();
    public Task<IReadOnlyList<PurchaseLineRow>> GetPurchaseLinesAsync(DateTime fromUtc, DateTime toUtcExclusive, Guid? supplierId, Guid? productId) => Fail<IReadOnlyList<PurchaseLineRow>>();
    public Task<IReadOnlyList<PurchaseLineRow>> GetOpenPurchaseLinesAsync(Guid? supplierId) => Fail<IReadOnlyList<PurchaseLineRow>>();

    public Task<IReadOnlyList<SaleCostRow>> GetSaleCostRowsAsync(DateTime fromUtc, DateTime toUtcExclusive, Guid? productId) => Fail<IReadOnlyList<SaleCostRow>>();
    public Task<IReadOnlyList<UltimatePos.Domain.Entities.ProductPriceTier>> GetCurrentPriceTiersAsync(PriceType? priceType) => Fail<IReadOnlyList<UltimatePos.Domain.Entities.ProductPriceTier>>();
    public Task<Dictionary<(Guid ProductId, Guid UnitOfMeasureId), decimal>> GetConversionFactorsAsync(IEnumerable<Guid> productIds) => Fail<Dictionary<(Guid ProductId, Guid UnitOfMeasureId), decimal>>();
}
