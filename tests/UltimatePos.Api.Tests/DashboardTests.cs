using UltimatePos.Application.Costing.Dtos;
using UltimatePos.Application.Dashboard;
using UltimatePos.Application.Reports;
using UltimatePos.Domain.Exceptions;
using static UltimatePos.Domain.Enums.PosEnums;

namespace Ultimate.Api.Tests;

public class DashboardPermissionTests
{
    [Fact]
    public void Each_section_is_unlocked_by_the_permission_that_already_protects_that_data()
    {
        Assert.Equal("Sales.View", DashboardPermissions.PermissionFor(DashboardSections.Sales));
        Assert.Equal("Costing.View", DashboardPermissions.PermissionFor(DashboardSections.Profit));
        Assert.Equal("Customers.ViewLedger", DashboardPermissions.PermissionFor(DashboardSections.Receivables));
        Assert.Equal("Stock.View", DashboardPermissions.PermissionFor(DashboardSections.Stock));
        Assert.Equal("Costing.View", DashboardPermissions.PermissionFor(DashboardSections.StockValue));
        Assert.Equal("Production.View", DashboardPermissions.PermissionFor(DashboardSections.Production));
    }

    [Fact]
    public void Every_section_has_exactly_one_permission_entry()
    {
        foreach (var section in Enum.GetValues<DashboardSections>().Where(s => s != DashboardSections.None))
            Assert.Equal(1, DashboardPermissions.Required.Count(r => r.Section == section));
    }

    [Fact]
    public void Visible_and_hidden_names_split_the_sections_and_StockValue_is_never_listed()
    {
        var allowed = DashboardSections.Sales | DashboardSections.Stock | DashboardSections.StockValue;

        Assert.Equal(new[] { "Sales", "Stock" }, DashboardPermissions.NamesIn(allowed));
        Assert.Equal(new[] { "Profit", "Receivables", "Production" }, DashboardPermissions.NamesNotIn(allowed));
    }

    [Fact]
    public void A_user_with_no_section_has_nothing_to_show_even_if_they_could_see_stock_value()
    {
        Assert.False(DashboardPermissions.HasAnySection(DashboardSections.None));
        Assert.False(DashboardPermissions.HasAnySection(DashboardSections.StockValue));
        Assert.True(DashboardPermissions.HasAnySection(DashboardSections.Receivables));
    }

    [Fact]
    public void All_lists_the_gate_permission_and_every_section_permission_once()
    {
        var all = DashboardPermissions.All.ToList();

        Assert.True(all.Contains("Dashboard.View"));
        Assert.Equal(all.Count, all.Distinct().Count());
        Assert.Equal(6, all.Count); // Dashboard.View, Sales.View, Costing.View, Customers.ViewLedger, Stock.View, Production.View
    }
}

public class DashboardBuilderTests
{
    private static readonly ReportPeriod Period = ReportPeriod.Create(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 1), 180);

    private static SaleReportRow Sale(SaleStatus status, PaymentMethod method, decimal total) =>
        new(Guid.NewGuid(), "S", new DateTime(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc), status, method, null, null, total);

    [Fact]
    public void Sales_count_completed_only_and_report_credit_voided_and_pending_apart()
    {
        var sales = new[]
        {
            Sale(SaleStatus.Completed, PaymentMethod.Cash, 1000m),
            Sale(SaleStatus.Completed, PaymentMethod.Credit, 3000m),
            Sale(SaleStatus.Voided, PaymentMethod.Cash, 400m),
            Sale(SaleStatus.Pending, PaymentMethod.MpesaStkPush, 250m),
        };

        var dto = DashboardService.BuildSales(Period, sales);

        Assert.Equal(2, dto.SaleCount);
        Assert.Equal(4000m, dto.Revenue);
        Assert.Equal(2000m, dto.AverageSaleValue);
        Assert.Equal(3000m, dto.CreditSalesAmount);
        Assert.Equal(1, dto.VoidedCount);
        Assert.Equal(1, dto.PendingCount);
    }

    [Fact]
    public void A_day_with_no_sales_is_all_zeros_not_an_error()
    {
        var dto = DashboardService.BuildSales(Period, Array.Empty<SaleReportRow>());

        Assert.Equal(0, dto.SaleCount);
        Assert.Equal(0m, dto.Revenue);
        Assert.Equal(0m, dto.AverageSaleValue);
    }

    [Fact]
    public void Receivables_separate_what_customers_owe_from_what_they_are_owed()
    {
        var balances = new CustomerBalanceRow[]
        {
            new(Guid.NewGuid(), "A", 1500m), new(Guid.NewGuid(), "B", 500m), new(Guid.NewGuid(), "C", -300m),
        };

        var dto = DashboardService.BuildReceivables(balances);

        Assert.Equal(2000m, dto.TotalOutstanding);
        Assert.Equal(2, dto.CustomersOwing);
        Assert.Equal(300m, dto.TotalCustomerCredit);
        Assert.Equal(1, dto.CustomersInCredit);
    }

    [Fact]
    public void Stock_value_is_null_not_zero_when_the_caller_may_not_see_costs()
    {
        var withoutCosts = DashboardService.BuildStock(new StockAlertCounts(3, 2), null);

        Assert.Equal(3, withoutCosts.LowStockCount);
        Assert.Equal(2, withoutCosts.OutOfStockCount);
        Assert.Null(withoutCosts.TotalStockValue);
        Assert.Null(withoutCosts.ProductsMissingCost);

        var valuation = new StockValuationDto(Array.Empty<StockValuationRowDto>(), Array.Empty<StockValuationTotalDto>(), 12500m, 4);
        var withCosts = DashboardService.BuildStock(new StockAlertCounts(3, 2), valuation);

        Assert.Equal(12500m, withCosts.TotalStockValue);
        Assert.Equal(4, withCosts.ProductsMissingCost);
    }

    [Fact]
    public void Production_totals_the_cost_of_the_runs_in_the_period()
    {
        var runs = new ProductionRunReportRow[]
        {
            new(Guid.NewGuid(), "PR-1", new DateTime(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc), Guid.NewGuid(), "Layers Mash", "LM", Guid.NewGuid(), "kg", 100m, 100m, 3000m, 500m, 3500m),
            new(Guid.NewGuid(), "PR-2", new DateTime(2026, 10, 1, 11, 0, 0, DateTimeKind.Utc), Guid.NewGuid(), "Dairy Premix", "DP", Guid.NewGuid(), "kg", 50m, 50m, 1000m, 200m, 1200m),
        };

        var dto = DashboardService.BuildProduction(Period, runs);

        Assert.Equal(2, dto.RunCount);
        Assert.Equal(4700m, dto.TotalCost);
    }

    [Fact]
    public void No_dates_means_today_and_one_date_means_that_single_day()
    {
        var today = ReportPeriod.TodayAt(180);

        var none = DashboardService.ResolvePeriod(null, null, 180);
        Assert.Equal(today, none.From);
        Assert.Equal(today, none.To);

        var single = new DateOnly(2026, 9, 15);
        var onlyFrom = DashboardService.ResolvePeriod(single, null, 180);
        Assert.Equal(single, onlyFrom.From);
        Assert.Equal(single, onlyFrom.To);

        var onlyTo = DashboardService.ResolvePeriod(null, single, 180);
        Assert.Equal(single, onlyTo.From);
        Assert.Equal(single, onlyTo.To);
    }

    [Fact]
    public void A_backwards_range_is_rejected()
    {
        Assert.Throws<InvalidAssignmentException>(() =>
            DashboardService.ResolvePeriod(new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 1), 180));
    }
}
