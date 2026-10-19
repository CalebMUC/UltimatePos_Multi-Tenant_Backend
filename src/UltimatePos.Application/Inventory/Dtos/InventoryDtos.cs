using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UltimatePos.Application.Inventory.Dtos
{
    /// <summary>The stock of one product at one branch.</summary>
    /// <param name="SellableQuantity">What can be sold now: for batch-tracked products only what is in unexpired batches, otherwise the
    /// quantity on hand; less what is reserved for open delivery orders.</param>
    /// <param name="ReservedQuantity">Held back for open delivery orders (pending or out for delivery). Still in QuantityOnHand until delivered.</param>
    public record StockLevelDto(Guid ProductId, string ProductName, string Sku, decimal QuantityOnHand, decimal ReorderLevel, bool BelowReorderLevel,
        bool IsBatchTracked = false, decimal? SellableQuantity = null, string? BaseUnitSymbol = null,
        Guid BranchId = default, string? KraBranchId = null, string? BranchName = null, decimal ReservedQuantity = 0);
    public record StockMovementDto(Guid StockMovementId, Guid ProductId, string MovementType, decimal QuantityChange,
        string ReferenceType, Guid ReferenceId, Guid BatchId, string? Notes, DateTime CreatedAt,
        Guid? StockBatchId = null, string? BatchNumber = null, Guid BranchId = default);

    /// <param name="QuantityChange">Signed, in UnitOfMeasureId. Positive adds stock, negative removes it.</param>
    /// <param name="StockBatchId">Batch-tracked products: the lot being adjusted. Required to remove stock (e.g. writing off an
    /// expired lot); to add stock, give this or a BatchNumber.</param>
    /// <param name="BatchNumber">Batch-tracked products: the lot to add stock into, created if new (with ExpiryDate).</param>
    public record CreateStockAdjustmentRequestDto(Guid ProductId, Guid UnitOfMeasureId, decimal QuantityChange, string Reason, decimal? UnitCost = null,
        Guid? StockBatchId = null, string? BatchNumber = null, DateOnly? ExpiryDate = null, DateOnly? ManufactureDate = null);

    /// <param name="ExpiryStatus">Expired, NearExpiry (within the window asked for), Ok, or NoExpiry.</param>
    public record StockBatchDto(Guid StockBatchId, Guid ProductId, string ProductName, string Sku, string BatchNumber,
        DateOnly? ExpiryDate, DateOnly? ManufactureDate, decimal QuantityOnHand, string? BaseUnitSymbol, decimal? UnitCost,
        int? DaysToExpiry, string ExpiryStatus, Guid? SupplierId, Guid? PurchaseOrderId, DateTime CreatedAt,
        Guid BranchId = default, string? KraBranchId = null, string? BranchName = null);

    public record ExpiryReportItemDto(Guid StockBatchId, Guid ProductId, string ProductName, string Sku, string BatchNumber, DateOnly ExpiryDate,
        int DaysToExpiry, string ExpiryStatus, decimal QuantityOnHand, string? BaseUnitSymbol, decimal? Value,
        Guid BranchId = default, string? KraBranchId = null, string? BranchName = null);
    /// <param name="ExpiredValue">At the products' average cost. Null without Costing.View.</param>
    public record ExpiryReportDto(DateOnly AsOf, int WithinDays, int ExpiredBatchCount, decimal? ExpiredValue, int NearExpiryBatchCount,
        decimal? NearExpiryValue, IEnumerable<ExpiryReportItemDto> Items);
}
