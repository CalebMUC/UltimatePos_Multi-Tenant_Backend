using System;
using System.Collections.Generic;

namespace UltimatePos.Application.Reports.Dtos
{
    // ---- Sales ----
    // Revenue and the breakdowns count Completed sales only. Voided and Pending sales are reported separately and, like every
    // sale, sit on the day they were CREATED (a sale voided next week still shows on the day it was rung up).
    public record SalesByDayDto(DateOnly Date, int SaleCount, decimal Revenue);
    public record SalesByPaymentMethodDto(string PaymentMethod, int SaleCount, decimal Total, decimal PercentOfRevenue);
    public record SalesByCustomerDto(Guid? CustomerId, string CustomerName, int SaleCount, decimal Total);
    public record SalesByProductDto(Guid ProductId, string ProductName, string Sku, decimal QuantitySoldInBaseUnits, decimal Revenue, decimal PercentOfRevenue);
    // One row per product AND the unit it was sold in (a 20L refill, a 10L refill, a 1L refill of the same water), measured like
    // TopProducts: Revenue is net of VAT and of returns. QuantitySold is in that unit, net of returns.
    public record SalesByProductUnitDto(Guid ProductId, string ProductName, string Sku, Guid UnitOfMeasureId, string UnitSymbol,
        decimal QuantitySold, decimal QuantitySoldInBaseUnits, decimal Revenue, decimal PercentOfRevenue);
    // Who rang the sales up, measured like ByPaymentMethod (sale totals, VAT included). CashierName is null for sales with no user.
    public record SalesByCashierDto(Guid? UserId, string? CashierName, int SaleCount, decimal Total, decimal PercentOfRevenue);
    public record SalesReportDto(DateOnly From, DateOnly To, int SaleCount, decimal Revenue, decimal AverageSaleValue,
        int VoidedCount, decimal VoidedAmount, int PendingCount, decimal PendingAmount,
        IEnumerable<SalesByDayDto> ByDay, IEnumerable<SalesByPaymentMethodDto> ByPaymentMethod,
        IEnumerable<SalesByCustomerDto> TopCustomers, IEnumerable<SalesByProductDto> TopProducts,
        IEnumerable<SalesByProductUnitDto>? TopProductUnits = null, IEnumerable<SalesByCashierDto>? ByCashier = null);

    // ---- Production costs ----
    // One row per product AND unit: runs of the same product in different units (a box and a packet) are never added together.
    // Cost figures are per unit of that row's unit of measure.
    public record ProductionCostByProductDto(Guid ProductId, string ProductName, string Sku, Guid UnitOfMeasureId, string UnitSymbol,
        int RunCount, decimal QuantityPlanned, decimal QuantityProduced, decimal YieldPercent,
        decimal MaterialCost, decimal AdditionalCost, decimal TotalCost,
        decimal AverageCostPerUnit, decimal LowestCostPerUnit, decimal HighestCostPerUnit, decimal LatestCostPerUnit, DateTime LatestRunAt);
    public record ProductionCostByTypeDto(string CostType, decimal Amount, decimal PercentOfAdditionalCost);
    public record ProductionIngredientCostDto(Guid IngredientProductId, string IngredientName, string IngredientSku, string BaseUnitSymbol,
        decimal QuantityInBaseUnits, decimal TotalCost, decimal PercentOfMaterialCost);
    public record ProductionCostReportDto(DateOnly From, DateOnly To, int RunCount, decimal MaterialCost, decimal AdditionalCost, decimal TotalCost,
        IEnumerable<ProductionCostByProductDto> ByProduct, IEnumerable<ProductionCostByTypeDto> AdditionalCostsByType,
        IEnumerable<ProductionIngredientCostDto> TopIngredients);

    // ---- Customer ageing ----
    // Outstanding is what is still owed on open invoices; UnappliedCredit is what the customer has paid beyond that.
    // NetBalance = Outstanding - UnappliedCredit, the same figure as the customer's running balance on that day.
    public record AgingBucketsDto(decimal Days0To30, decimal Days31To60, decimal Days61To90, decimal Over90);
    public record OpenInvoiceDto(Guid InvoiceId, string? InvoiceNumber, DateOnly InvoiceDate, decimal OriginalAmount,
        decimal OutstandingAmount, int AgeDays, string Bucket);
    public record CustomerAgingRowDto(Guid CustomerId, string CustomerName, string? PhoneNumber, decimal Outstanding, decimal UnappliedCredit,
        decimal NetBalance, AgingBucketsDto Buckets, int OpenInvoiceCount, DateOnly? OldestOpenInvoiceDate, int? OldestOpenInvoiceAgeDays,
        IEnumerable<OpenInvoiceDto>? OpenInvoices);
    public record CustomerAgingReportDto(DateOnly AsOf, int CustomerCount, decimal TotalOutstanding, decimal TotalUnappliedCredit,
        AgingBucketsDto Totals, IEnumerable<CustomerAgingRowDto> Customers);

    // ---- Stock status ----
    // Quantities are in the product's BASE unit. Cost fields (AverageUnitCost, StockValue, CostMissing, and the totals built from
    // them) are null unless the caller may see costs (Costing.View). AverageDailyUsage is what left stock for sales and production
    // over the usage window, per day; DaysOfCover is QuantityOnHand / AverageDailyUsage (null when nothing was used).
    // Only products that have a stock record are listed: one never received, produced or adjusted has none, same as GET /stock.
    public record StockStatusItemDto(Guid ProductId, string ProductName, string Sku, string ItemType, string Category, string BaseUnitSymbol,
        decimal QuantityOnHand, decimal ReorderLevel, string Status, decimal Shortfall, decimal AverageDailyUsage, decimal? DaysOfCover,
        decimal? AverageUnitCost, decimal? StockValue, bool? CostMissing);
    public record StockStatusByItemTypeDto(string ItemType, int ProductCount, int LowStockCount, int OutOfStockCount, decimal? StockValue);
    public record StockStatusReportDto(int UsageDays, DateOnly UsageFrom, DateOnly UsageTo, int ProductCount, int InStockCount,
        int LowStockCount, int OutOfStockCount, decimal? TotalStockValue, int? ProductsMissingCost,
        IEnumerable<StockStatusByItemTypeDto> ByItemType, IEnumerable<StockStatusItemDto> Items);

    // ---- Movements ----
    // One row per product that moved in the period. Quantities are in the BASE unit and tie together:
    //   Closing = Opening + Received + Produced - Sold - Consumed + AdjustedIn - AdjustedOut
    // Sold is net of voids. Value fields are null unless the caller may see costs; movements with no recorded cost count as 0.
    public record StockMovementReportRowDto(Guid ProductId, string ProductName, string Sku, string BaseUnitSymbol,
        decimal Opening, decimal Received, decimal Produced, decimal Sold, decimal Consumed, decimal AdjustedIn, decimal AdjustedOut,
        decimal Closing, int MovementCount,
        decimal? ReceivedValue, decimal? SoldCost, decimal? ConsumedCost, decimal? AdjustmentNetValue);
    public record StockMovementReportDto(DateOnly From, DateOnly To, int ProductCount,
        decimal? TotalReceivedValue, decimal? TotalSoldCost, decimal? TotalConsumedCost, decimal? TotalAdjustmentNetValue,
        IEnumerable<StockMovementReportRowDto> Items);

    // ---- Adjustments ----
    // Manual corrections to stock: the audit trail for shrinkage and counts. DecreaseValue is a positive amount lost;
    // NetValue = IncreaseValue - DecreaseValue. Value fields are null unless the caller may see costs.
    // Items is limited to the most recent rows; the counts and values always cover every adjustment in the period (Truncated says if rows were cut).
    public record StockAdjustmentItemDto(Guid StockMovementId, DateTime CreatedAt, DateOnly Date, Guid ProductId, string ProductName,
        string Sku, string BaseUnitSymbol, decimal QuantityChange, string Direction, decimal? UnitCost, decimal? Value,
        string? Reason, Guid? AdjustedById, string? AdjustedBy);
    public record StockAdjustmentReportDto(DateOnly From, DateOnly To, int AdjustmentCount, int IncreaseCount, int DecreaseCount,
        decimal? IncreaseValue, decimal? DecreaseValue, decimal? NetValue, int? UncostedCount, bool Truncated,
        IEnumerable<StockAdjustmentItemDto> Items);

    // ---- Purchase summary ----
    // Orders are dated by the day they were raised. OrderedValue is what was committed to suppliers (orders that are Ordered,
    // PartiallyReceived or Received, quantity x price); ReceivedValue is the part of it that has actually arrived, which is the
    // real spend; OutstandingValue is what is still to arrive. Cancelled orders are reported on their own and never counted in
    // those; Draft orders are not commitments yet and are only counted in ByStatus. A line with no price (0) adds nothing to
    // the values and is counted in UnpricedLineCount, so a high count means the spend is understated.
    // Per-product rows are per product AND unit of the order line, never added across units (a 25kg bag and a 50kg bag).
    public record PurchaseByStatusDto(string Status, int OrderCount, decimal OrderedValue);
    public record PurchaseBySupplierDto(Guid SupplierId, string SupplierName, int OrderCount, decimal OrderedValue, decimal ReceivedValue,
        decimal OutstandingValue, decimal PercentOfOrdered);
    public record PurchaseByProductDto(Guid ProductId, string ProductName, string Sku, Guid UnitOfMeasureId, string UnitSymbol,
        decimal QuantityOrdered, decimal QuantityReceived, decimal OrderedValue, decimal AverageUnitCost, decimal PercentOfOrdered);
    public record PurchaseSummaryDto(DateOnly From, DateOnly To, int OrderCount, int CancelledCount, decimal CancelledValue,
        decimal OrderedValue, decimal ReceivedValue, decimal OutstandingValue, int UnpricedLineCount,
        IEnumerable<PurchaseByStatusDto> ByStatus, IEnumerable<PurchaseBySupplierDto> BySupplier, IEnumerable<PurchaseByProductDto> TopProducts);

    // ---- Open orders ----
    // Orders still waiting on stock, as of today. An order is overdue when its expected delivery day has passed.
    // Only lines with something still to arrive are listed. Values are quantity x price of what is outstanding.
    public record OpenOrderLineDto(Guid ProductId, string ProductName, string Sku, string UnitSymbol, decimal QuantityOrdered,
        decimal QuantityReceived, decimal QuantityOutstanding, decimal UnitCost, decimal OutstandingValue);
    public record OpenOrderDto(Guid PurchaseOrderId, string OrderNumber, Guid SupplierId, string SupplierName, string Status,
        DateOnly OrderDate, int AgeDays, DateOnly? ExpectedDeliveryDate, bool IsOverdue, int DaysOverdue,
        decimal OutstandingValue, IEnumerable<OpenOrderLineDto> Lines);
    public record OpenOrdersReportDto(DateOnly AsOf, int OrderCount, int OverdueCount, decimal OutstandingValue, decimal OverdueValue,
        IEnumerable<OpenOrderDto> Orders);

    // ---- Price trends ----
    // What we paid per unit over the period, per product and unit, from orders raised in it (Cancelled and Draft orders and
    // unpriced lines left out). ChangePercent is first price to latest price; null with a single purchase. AverageUnitCost is
    // weighted by quantity. Points (the individual purchases, oldest first) are listed only when a single product is asked for.
    public record PricePointDto(DateOnly Date, string OrderNumber, Guid SupplierId, string SupplierName, decimal Quantity, decimal UnitCost);
    public record PriceTrendDto(Guid ProductId, string ProductName, string Sku, Guid UnitOfMeasureId, string UnitSymbol,
        int PurchaseCount, int SupplierCount, decimal QuantityOrdered, decimal AverageUnitCost,
        decimal FirstUnitCost, decimal LatestUnitCost, DateOnly LatestDate, string LatestSupplier,
        decimal LowestUnitCost, string LowestSupplier, decimal HighestUnitCost, string HighestSupplier,
        decimal? ChangePercent, IEnumerable<PricePointDto>? Points);
    public record PriceTrendsReportDto(DateOnly From, DateOnly To, int ProductCount, IEnumerable<PriceTrendDto> Items);
}
