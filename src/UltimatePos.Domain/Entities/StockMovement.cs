using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static UltimatePos.Domain.Enums.PosEnums;

namespace UltimatePos.Domain.Entities
{
    // Append-only ledger — no IsActive/LastUpdated*, a correction is a new row, not an edit.
    public class StockMovement
    {
        public Guid StockMovementId { get; set; }
        public Guid ProductId { get; set; }
        public StockMovementType MovementType { get; set; }
        public decimal QuantityChange { get; set; }
        public string ReferenceType { get; set; } = string.Empty;
        public Guid ReferenceId { get; set; }
        public Guid BatchId { get; set; }
        public string? Notes { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public Guid? CreatedBy { get; set; }

        public Product Product { get; set; } = null!;
    }
}
