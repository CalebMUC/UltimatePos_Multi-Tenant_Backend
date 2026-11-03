using System;
using System.Collections.Generic;

namespace UltimatePos.Application.Dashboard.Dtos
{
    // Sales.Revenue counts Completed sales only. Profit comes from the same calculation as the profit-and-loss report,
    // so IsComplete / UncostedRevenue mean what they mean there: false means some revenue has no recorded cost and
    // profit is understated. Receivables are the net balance per customer; see the customer-ageing report for the age of it.
    public record DashboardSalesDto(DateOnly From, DateOnly To, int SaleCount, decimal Revenue, decimal AverageSaleValue,
        decimal CreditSalesAmount, int VoidedCount, int PendingCount);
    public record DashboardProfitDto(DateOnly From, DateOnly To, decimal Revenue, decimal CostOfGoodsSold, decimal GrossProfit,
        decimal? GrossMarginPercent, decimal OperatingExpenses, decimal NetProfit, decimal? NetMarginPercent,
        decimal UncostedRevenue, bool IsComplete);
    public record DashboardReceivablesDto(decimal TotalOutstanding, int CustomersOwing, decimal TotalCustomerCredit, int CustomersInCredit);

    // The customers behind DashboardSalesDto.CreditSalesAmount: TotalCreditSales always equals that figure for the same period and branch.
    // CreditSalesAmount is what was sold on credit in the period, before any payment or return. OutstandingBalance is what the customer
    // owes now, whatever the period and across every branch; negative means they are in credit, and it is null unless the caller may see
    // balances (Customers.ViewLedger). PhoneNumber is null unless the caller may see customer details (Customers.View).
    public record DashboardCreditCustomerDto(Guid CustomerId, string CustomerName, string? PhoneNumber, int SaleCount,
        decimal CreditSalesAmount, DateTime LastSaleAt, decimal? OutstandingBalance);
    public record DashboardCreditCustomersDto(DateOnly From, DateOnly To, int CustomerCount, decimal TotalCreditSales,
        IEnumerable<DashboardCreditCustomerDto> Customers);

    // Stock counts are as of now. TotalStockValue and ProductsMissingCost are null unless the caller may see costs (Costing.View).
    public record DashboardStockDto(int LowStockCount, int OutOfStockCount, decimal? TotalStockValue, int? ProductsMissingCost);
    public record DashboardProductionDto(DateOnly From, DateOnly To, int RunCount, decimal TotalCost);

    // A section the caller is not allowed to see is null, and its name is in HiddenSections. Sections lists what is included.
    public record DashboardSummaryDto(DateOnly From, DateOnly To, IEnumerable<string> Sections, IEnumerable<string> HiddenSections,
        DashboardSalesDto? Sales, DashboardProfitDto? Profit, DashboardReceivablesDto? Receivables,
        DashboardStockDto? Stock, DashboardProductionDto? Production,
        UltimatePos.Application.Customers.DashboardCustomersDto? Customers = null);
}
