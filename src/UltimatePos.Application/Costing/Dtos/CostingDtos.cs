using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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

    // ---- Revaluation ----
    public record RevalueStockRequestDto(Guid ProductId, Guid UnitOfMeasureId, decimal UnitCost, string Reason);
}
