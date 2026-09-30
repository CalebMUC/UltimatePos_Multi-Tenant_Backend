using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UltimatePos.Application.Inventory.Dtos
{
    public record StockLevelDto(Guid ProductId, string ProductName, string Sku, decimal QuantityOnHand, decimal ReorderLevel, bool BelowReorderLevel);

    public record StockMovementDto(Guid StockMovementId, Guid ProductId, string MovementType, decimal QuantityChange,
        string ReferenceType, Guid ReferenceId, Guid BatchId, string? Notes, DateTime CreatedAt);

    public record CreateStockAdjustmentRequestDto(Guid ProductId, Guid UnitOfMeasureId, decimal QuantityChange, string Reason); 
}
