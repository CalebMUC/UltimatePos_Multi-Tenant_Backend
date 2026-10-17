using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UltimatePos.Application.Inventory.Dtos
{
    /// <param name="SellableQuantity">For batch-tracked products, only what is in unexpired batches; otherwise the quantity on hand.</param>
    public record StockLevelDto(Guid ProductId, string ProductName, string Sku, decimal QuantityOnHand, decimal ReorderLevel, bool BelowReorderLevel,
        bool IsBatchTracked = false, decimal? SellableQuantity = null, string? BaseUnitSymbol = null);
    public record StockMovementDto(Guid StockMovementId, Guid ProductId, string MovementType, decimal QuantityChange,
        string ReferenceType, Guid ReferenceId, Guid BatchId, string? Notes, DateTime CreatedAt,
        Guid? StockBatchId = null, string? BatchNumber = null);

    /// <param name="QuantityChange">Signed, in UnitOfMeasureId. Positive adds stock, negative removes it.</param>
    /// <param name="StockBatchId">Batch-tracked products: the lot being adjusted. Required to remove stock (e.g. writing off an
    /// expired lot); to add stock, give this or a BatchNumber.</param>
    /// <param name="BatchNumber">Batch-tracked products: the lot to add stock into, created if new (with ExpiryDate).</param>
    public record CreateStockAdjustmentRequestDto(Guid ProductId, Guid UnitOfMeasureId, decimal QuantityChange, string Reason, decimal? UnitCost = null,
        Guid? StockBatchId = null, string? BatchNumber = null, DateOnly? ExpiryDate = null, DateOnly? ManufactureDate = null);

    /// <param name="ExpiryStatus">Expired, NearExpiry (within the window asked for), Ok, or NoExpiry.</param>
    public record StockBatchDto(Guid StockBatchId, Guid ProductId, string ProductName, string Sku, string BatchNumber,
        DateOnly? ExpiryDate, DateOnly? ManufactureDate, decimal QuantityOnHand, string? BaseUnitSymbol, decimal? UnitCost,
        int? DaysToExpiry, string ExpiryStatus, Guid? SupplierId, Guid? PurchaseOrderId, DateTime CreatedAt);

    public record ExpiryReportItemDto(Guid StockBatchId, Guid ProductId, string ProductName, string Sku, string BatchNumber, DateOnly ExpiryDate,
        int DaysToExpiry, string ExpiryStatus, decimal QuantityOnHand, string? BaseUnitSymbol, decimal? Value);
    /// <param name="ExpiredValue">At the products' average cost. Null without Costing.View.</param>
    public record ExpiryReportDto(DateOnly AsOf, int WithinDays, int ExpiredBatchCount, decimal? ExpiredValue, int NearExpiryBatchCount,
        decimal? NearExpiryValue, IEnumerable<ExpiryReportItemDto> Items);
}
