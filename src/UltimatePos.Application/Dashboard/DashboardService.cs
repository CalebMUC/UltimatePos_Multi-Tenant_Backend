using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UltimatePos.Application.Costing;
using UltimatePos.Application.Costing.Dtos;
using UltimatePos.Application.Dashboard.Dtos;
using UltimatePos.Application.Reports;
using static UltimatePos.Application.Reports.ReportMath;
using static UltimatePos.Domain.Enums.PosEnums;

namespace UltimatePos.Application.Dashboard
{
    /// <summary>
    /// The at-a-glance numbers. This service never decides who may see what: callers say which sections they are allowed
    /// (<see cref="DashboardSections"/>) and the summary leaves out, without even querying, any section they are not. The
    /// controller works that out from the user's permissions, so the rule lives in one place.
    /// </summary>
    public class DashboardService
    {
        private readonly IReportsRepository _repository;
        private readonly CostingService _costingService;
        private readonly Customers.CustomerInsightsService _customers;

        public DashboardService(IReportsRepository repository, CostingService costingService, Customers.CustomerInsightsService customers)
        {
            _repository = repository;
            _costingService = costingService;
            _customers = customers;
        }

        // =====================================================================
        // Summary
        // =====================================================================

        /// <summary>Leave both dates out for today; give one for a single day. Sections not in <paramref name="allowed"/> are skipped.</summary>
        public async Task<DashboardSummaryDto> GetSummaryAsync(DateOnly? from, DateOnly? to, int utcOffsetMinutes, DashboardSections allowed)
        {
            var period = ResolvePeriod(from, to, utcOffsetMinutes);

            // One query after another: they share a single DbContext, which is not safe to use concurrently.
            var sales = allowed.HasFlag(DashboardSections.Sales) ? await LoadSalesAsync(period) : null;
            var profit = allowed.HasFlag(DashboardSections.Profit) ? await LoadProfitAsync(period) : null;
            var receivables = allowed.HasFlag(DashboardSections.Receivables) ? await LoadReceivablesAsync() : null;
            var stock = allowed.HasFlag(DashboardSections.Stock) ? await LoadStockAsync(allowed.HasFlag(DashboardSections.StockValue)) : null;
            var production = allowed.HasFlag(DashboardSections.Production) ? await LoadProductionAsync(period) : null;
            var customers = allowed.HasFlag(DashboardSections.Customers) ? await LoadCustomersAsync(period) : null;

            return new DashboardSummaryDto(period.From, period.To,
                DashboardPermissions.NamesIn(allowed), DashboardPermissions.NamesNotIn(allowed),
                sales, profit, receivables, stock, production, customers);
        }

        // =====================================================================
        // One section at a time. Each is the same figure the summary carries; the controller has already checked the permission.
        // =====================================================================

        public Task<DashboardSalesDto> GetSalesAsync(DateOnly? from, DateOnly? to, int utcOffsetMinutes) =>
            LoadSalesAsync(ResolvePeriod(from, to, utcOffsetMinutes));

        public Task<DashboardProfitDto> GetProfitAsync(DateOnly? from, DateOnly? to, int utcOffsetMinutes) =>
            LoadProfitAsync(ResolvePeriod(from, to, utcOffsetMinutes));

        public Task<DashboardReceivablesDto> GetReceivablesAsync() => LoadReceivablesAsync();

        /// <param name="includeValue">True only when the caller may see costs; otherwise the value fields come back null.</param>
        public Task<DashboardStockDto> GetStockAsync(bool includeValue) => LoadStockAsync(includeValue);

        public Task<DashboardProductionDto> GetProductionAsync(DateOnly? from, DateOnly? to, int utcOffsetMinutes) =>
            LoadProductionAsync(ResolvePeriod(from, to, utcOffsetMinutes));

        /// <summary>Always the 90 days up to the period's last day: buying habits need more than one day to show.</summary>
        public Task<Customers.DashboardCustomersDto> GetCustomersAsync(DateOnly? from, DateOnly? to, int utcOffsetMinutes) =>
            LoadCustomersAsync(ResolvePeriod(from, to, utcOffsetMinutes));

        // =====================================================================
        // Loading
        // =====================================================================

        private async Task<DashboardSalesDto> LoadSalesAsync(ReportPeriod period) =>
            BuildSales(period, await _repository.GetSalesAsync(period.FromUtc, period.ToUtcExclusive));

        private async Task<DashboardProfitDto> LoadProfitAsync(ReportPeriod period) =>
            BuildProfit(period, await _costingService.GetProfitAndLossAsync(period.From, period.To, (int)period.Offset.TotalMinutes));

        private async Task<DashboardReceivablesDto> LoadReceivablesAsync() =>
            BuildReceivables(await _repository.GetCustomerBalancesAsync());

        private async Task<DashboardStockDto> LoadStockAsync(bool includeValue)
        {
            var alerts = await _repository.GetStockAlertCountsAsync();
            var valuation = includeValue ? await _costingService.GetStockValuationAsync(null) : null;
            return BuildStock(alerts, valuation);
        }

        private Task<Customers.DashboardCustomersDto> LoadCustomersAsync(ReportPeriod period) =>
            _customers.GetDashboardAsync(period.To, (int)period.Offset.TotalMinutes);

        private async Task<DashboardProductionDto> LoadProductionAsync(ReportPeriod period) =>
            BuildProduction(period, await _repository.GetProductionRunsAsync(period.FromUtc, period.ToUtcExclusive, null));

        // =====================================================================
        // Pure builders (unit-tested directly)
        // =====================================================================

        public static ReportPeriod ResolvePeriod(DateOnly? from, DateOnly? to, int utcOffsetMinutes)
        {
            var today = ReportPeriod.TodayAt(utcOffsetMinutes);
            return ReportPeriod.Create(from ?? to ?? today, to ?? from ?? today, utcOffsetMinutes);
        }

        public static DashboardSalesDto BuildSales(ReportPeriod period, IReadOnlyList<SaleReportRow> sales)
        {
            var completed = sales.Where(s => s.Status == SaleStatus.Completed).ToList();
            var revenue = completed.Sum(s => s.TotalAmount);

            return new DashboardSalesDto(period.From, period.To, completed.Count, revenue, Average(revenue, completed.Count),
                completed.Where(s => s.PaymentMethod == PaymentMethod.Credit).Sum(s => s.TotalAmount),
                sales.Count(s => s.Status == SaleStatus.Voided), sales.Count(s => s.Status == SaleStatus.Pending));
        }

        public static DashboardProfitDto BuildProfit(ReportPeriod period, ProfitAndLossDto pnl) =>
            new(period.From, period.To, pnl.Revenue, pnl.CostOfGoodsSold, pnl.GrossProfit, pnl.GrossMarginPercent,
                pnl.TotalOperatingExpenses, pnl.NetProfit, pnl.NetMarginPercent, pnl.UncostedRevenue, pnl.IsComplete);

        public static DashboardReceivablesDto BuildReceivables(IReadOnlyList<CustomerBalanceRow> balances)
        {
            var owing = balances.Where(b => b.OutstandingBalance > 0).ToList();
            var inCredit = balances.Where(b => b.OutstandingBalance < 0).ToList();
            return new DashboardReceivablesDto(owing.Sum(b => b.OutstandingBalance), owing.Count,
                -inCredit.Sum(b => b.OutstandingBalance), inCredit.Count);
        }

        /// <param name="valuation">Null when the caller may not see costs: the value fields are then null, not zero.</param>
        public static DashboardStockDto BuildStock(StockAlertCounts alerts, StockValuationDto? valuation) =>
            new(alerts.LowStock, alerts.OutOfStock, valuation?.TotalStockValue, valuation?.ProductsMissingCost);

        public static DashboardProductionDto BuildProduction(ReportPeriod period, IReadOnlyList<ProductionRunReportRow> runs) =>
            new(period.From, period.To, runs.Count, runs.Sum(r => r.TotalCost));
    }
}
