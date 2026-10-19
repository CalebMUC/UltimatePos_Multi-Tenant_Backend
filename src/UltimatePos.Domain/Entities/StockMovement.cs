using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static UltimatePos.Domain.Enums.PosEnums;

namespace UltimatePos.Domain.Entities
{
    // Append-only ledger — no IsActive/LastUpdated*, a correction is a new row, not an edit.
    public class StockMovement : IBranchEntity
    {
        public Guid StockMovementId { get; set; }
        public Guid ProductId { get; set; }
        public Guid BranchId { get; set; }
        public StockMovementType MovementType { get; set; }
        public decimal QuantityChange { get; set; }

        /// <summary>Cost per BASE unit this movement was valued at. For decreases it is the average cost at that moment (set by
        /// StockLedger). Null on movements recorded before costing existed.</summary>
        public decimal? UnitCost { get; set; }
        public string ReferenceType { get; set; } = string.Empty;
        public Guid ReferenceId { get; set; }
        public Guid BatchId { get; set; }
        /// <summary>The stock batch moved, for batch-tracked products. Null for products that are not batch-tracked.</summary>
        public Guid? StockBatchId { get; set; }
        public string? Notes { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public Guid? CreatedBy { get; set; }

        public Product Product { get; set; } = null!;
    }
}
