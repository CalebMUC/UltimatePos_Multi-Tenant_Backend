using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using static UltimatePos.Domain.Enums.PosEnums;

namespace UltimatePos.Application.Reports
{
    /// <summary>One sale reduced to what the sales report and dashboard need. CustomerName is null for walk-in sales.
    /// CreatedBy / CashierName are the user who rang the sale up (null on sales recorded without a user).</summary>
    public record SaleReportRow(Guid SaleId, string SaleNumber, DateTime CreatedAt, SaleStatus Status, PaymentMethod PaymentMethod,
        Guid? CustomerId, string? CustomerName, decimal TotalAmount, Guid? CreatedBy = null, string? CashierName = null);

    public record CustomerBalanceRow(Guid CustomerId, string CustomerName, decimal OutstandingBalance);

    /// <summary>One customer ledger entry. Amount is always positive; EntryType decides whether it adds to or reduces what the customer owes.</summary>
    public record CustomerLedgerRow(Guid CustomerId, string CustomerName, string? PhoneNumber, CustomerLedgerEntryType EntryType,
        decimal Amount, string ReferenceType, Guid ReferenceId, string? Notes, DateTime CreatedAt);

    public record ProductionRunReportRow(Guid ProductionRunId, string RunNumber, DateTime CreatedAt, Guid ProductId, string ProductName,
        string Sku, Guid UnitOfMeasureId, string UnitSymbol, decimal QuantityPlanned, decimal QuantityProduced,
        decimal MaterialCost, decimal AdditionalCost, decimal TotalCost);

    public record ProductionCostLineRow(Guid ProductionRunId, ProductionCostType CostType, decimal Amount);

    public record ProductionInputRow(Guid IngredientProductId, string IngredientName, string IngredientSku, string BaseUnitSymbol,
        decimal QuantityInBaseUnits, decimal TotalCost);

    /// <summary>LowStock and OutOfStock never overlap: a product at zero is out of stock, not also low. Same rules as the stock-status report.</summary>
    public record StockAlertCounts(int LowStock, int OutOfStock);

    /// <summary>One product with its stock record. Quantities and cost are per BASE unit.</summary>
    public record StockStatusRow(Guid ProductId, string ProductName, string Sku, ItemType ItemType, string CategoryName,
        string BaseUnitSymbol, decimal QuantityOnHand, decimal ReorderLevel, decimal AverageUnitCost);

    /// <summary>Net quantity of a product that left stock for sales or production in a window, in base units (a void cancels the sale).</summary>
    public record ProductUsageRow(Guid ProductId, decimal QuantityUsed);

    /// <summary>
    /// The movements of one product and type in a window, split by direction. Quantity is the signed sum of QuantityChange;
    /// Value is the signed sum of QuantityChange x UnitCost, with movements that have no recorded cost counting as 0.
    /// </summary>
    public record StockMovementTotalRow(Guid ProductId, string ProductName, string Sku, string BaseUnitSymbol, StockMovementType MovementType,
        bool IsIncrease, decimal Quantity, decimal Value, int MovementCount);

    public record StockAdjustmentRow(Guid StockMovementId, DateTime CreatedAt, Guid ProductId, string ProductName, string Sku,
        string BaseUnitSymbol, decimal QuantityChange, decimal? UnitCost, string? Reason, Guid? CreatedBy);

    /// <summary>
    /// One line of one purchase order, with its order and supplier. Quantities are in the unit the line was ordered in
    /// (UnitSymbol), and UnitCost is the price of one of THAT unit, so the three always belong together.
    /// </summary>
    public record PurchaseLineRow(Guid PurchaseOrderId, string OrderNumber, DateTime OrderDate, DateTime? ExpectedDeliveryDate,
        PurchaseOrderStatus Status, Guid SupplierId, string SupplierName, Guid ProductId, string ProductName, string Sku,
        Guid UnitOfMeasureId, string UnitSymbol, decimal QuantityOrdered, decimal QuantityReceived, decimal UnitCost);

    /// <summary>Read-only queries behind the reports. All arithmetic and grouping lives in ReportsService so it stays unit-testable.</summary>
    public interface IReportsRepository
    {
        /// <summary>Completed, Voided and Pending sales created in [fromUtc, toUtcExclusive). Failed sales are left out.</summary>
        Task<IReadOnlyList<SaleReportRow>> GetSalesAsync(DateTime fromUtc, DateTime toUtcExclusive);

        /// <summary>Customers whose running balance is not zero.</summary>
        Task<IReadOnlyList<CustomerBalanceRow>> GetCustomerBalancesAsync();

        /// <summary>Every ledger entry created before the bound, oldest first within each customer.</summary>
        Task<IReadOnlyList<CustomerLedgerRow>> GetCustomerLedgerAsync(DateTime toUtcExclusive, Guid? customerId);

        /// <summary>Each sale's number, and the day the customer promised to pay by (null = no promise).</summary>
        Task<Dictionary<Guid, (string SaleNumber, DateOnly? PaymentDueDate)>> GetInvoiceReferencesAsync(IEnumerable<Guid> saleIds);

        /// <summary>Production runs created in [fromUtc, toUtcExclusive).</summary>
        Task<IReadOnlyList<ProductionRunReportRow>> GetProductionRunsAsync(DateTime fromUtc, DateTime toUtcExclusive, Guid? productId);

        /// <summary>The non-stock cost lines (labour, utilities, ...) of those same runs.</summary>
        Task<IReadOnlyList<ProductionCostLineRow>> GetProductionCostLinesAsync(DateTime fromUtc, DateTime toUtcExclusive, Guid? productId);

        /// <summary>The ingredients consumed by those same runs, each with the cost it was consumed at.</summary>
        Task<IReadOnlyList<ProductionInputRow>> GetProductionInputsAsync(DateTime fromUtc, DateTime toUtcExclusive, Guid? productId);

        /// <summary>Active products with a stock record: those above zero but at or below a positive reorder level, and those at or below zero.</summary>
        Task<StockAlertCounts> GetStockAlertCountsAsync();

        // ---------------------------------------------------------------- Inventory

        /// <summary>Products that have a stock record (active ones only unless includeInactive), optionally for one item type and/or category.</summary>
        Task<IReadOnlyList<StockStatusRow>> GetStockStatusRowsAsync(ItemType? itemType, Guid? categoryId, bool includeInactive);

        /// <summary>Sale, SaleVoid and ProductionConsumption movements created in [fromUtc, toUtcExclusive), netted per product.</summary>
        Task<IReadOnlyList<ProductUsageRow>> GetStockUsageAsync(DateTime fromUtc, DateTime toUtcExclusive);

        /// <summary>Movements created in [fromUtc, toUtcExclusive), grouped by product, type and direction. Revaluations move no quantity and are left out.</summary>
        Task<IReadOnlyList<StockMovementTotalRow>> GetStockMovementTotalsAsync(DateTime fromUtc, DateTime toUtcExclusive, Guid? productId);

        /// <summary>Quantity on hand just before the bound, rebuilt from the ledger. Products with no movement before it are absent.</summary>
        Task<Dictionary<Guid, decimal>> GetOpeningStockBalancesAsync(DateTime beforeUtc, Guid? productId);

        /// <summary>Manual stock adjustments created in [fromUtc, toUtcExclusive), newest first.</summary>
        Task<IReadOnlyList<StockAdjustmentRow>> GetStockAdjustmentsAsync(DateTime fromUtc, DateTime toUtcExclusive, Guid? productId);

        Task<Dictionary<Guid, string>> GetUserNamesAsync(IEnumerable<Guid> userIds);

        // ---------------------------------------------------------------- Purchasing

        /// <summary>Lines of active orders dated (OrderDate) in [fromUtc, toUtcExclusive), whatever their status.</summary>
        Task<IReadOnlyList<PurchaseLineRow>> GetPurchaseLinesAsync(DateTime fromUtc, DateTime toUtcExclusive, Guid? supplierId, Guid? productId);

        /// <summary>Lines of active orders that are Ordered or PartiallyReceived, however old.</summary>
        Task<IReadOnlyList<PurchaseLineRow>> GetOpenPurchaseLinesAsync(Guid? supplierId);
    }
}
