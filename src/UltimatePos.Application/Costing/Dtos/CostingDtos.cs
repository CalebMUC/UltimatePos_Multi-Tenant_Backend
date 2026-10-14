using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UltimatePos.Application.Expenses.Dtos;

namespace UltimatePos.Application.Costing.Dtos
{
    // ---- Stock valuation ----
    public record StockValuationRowDto(Guid ProductId, string ProductName, string Sku, string ItemType,
        decimal QuantityOnHand, decimal AverageUnitCost, decimal StockValue, bool CostMissing);
    public record StockValuationTotalDto(string ItemType, int ProductCount, decimal StockValue);
    public record StockValuationDto(IEnumerable<StockValuationRowDto> Items, IEnumerable<StockValuationTotalDto> TotalsByItemType,
        decimal TotalStockValue, int ProductsMissingCost);

    // ---- Price list vs cost ----
    public record MarginRowDto(Guid ProductId, string ProductName, string Sku, Guid UnitOfMeasureId, string UnitSymbol,
        string PriceType, decimal Price, decimal UnitCost, decimal Margin, decimal? MarginPercent, bool CostMissing);

    // ---- Sales profit ----
    // Revenue is every line sold. GrossProfit and MarginPercent are measured ONLY over lines whose cost was known
    // (UncostedRevenue is reported separately), so sales from before costing never inflate the margin.
    public record ProfitRowDto(Guid ProductId, string ProductName, string Sku, decimal QuantitySoldInBaseUnits,
        decimal Revenue, decimal Cost, decimal GrossProfit, decimal? MarginPercent, decimal UncostedRevenue);
    public record ProfitReportDto(DateTimeOffset From, DateTimeOffset To, IEnumerable<ProfitRowDto> Items,
        decimal TotalRevenue, decimal TotalCost, decimal GrossProfit, decimal? MarginPercent, decimal UncostedRevenue);

    // ---- Profit and loss ----
    // Revenue is every sale. CostOfGoodsSold and GrossProfit are measured ONLY over sales whose cost was known, so
    // sales from before costing (UncostedRevenue) are excluded from profit rather than counted as 100% margin.
    // NetProfit = GrossProfit - TotalOperatingExpenses. IsComplete is false while any revenue is uncosted,
    // which means NetProfit is understated by the margin on that revenue.
    public record ProfitAndLossDto(DateOnly From, DateOnly To, decimal Revenue, decimal CostOfGoodsSold, decimal GrossProfit,
        decimal? GrossMarginPercent, decimal UncostedRevenue,
        IEnumerable<ExpenseCategoryTotalDto> OperatingExpenses, decimal TotalOperatingExpenses,
        decimal NetProfit, decimal? NetMarginPercent, bool IsComplete);

    // ---- Revaluation ----
    public record RevalueStockRequestDto(Guid ProductId, Guid UnitOfMeasureId, decimal UnitCost, string Reason);
}
