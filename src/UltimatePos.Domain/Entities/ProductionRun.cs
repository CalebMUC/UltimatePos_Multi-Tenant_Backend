using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UltimatePos.Domain.Entities
{
    // A completed production fact — immutable, no IsActive/LastUpdated*. Corrections go through stock adjustments.
    public class ProductionRun : ITenantEntity
    {
        public Guid ProductionRunId { get; set; }
        public Guid BusinessId { get; set; }
        public string RunNumber { get; set; } = string.Empty;
        public Guid FormulaId { get; set; }
        public Guid ProductId { get; set; }
        public Guid UnitOfMeasureId { get; set; }
        public decimal QuantityPlanned { get; set; }
        public decimal QuantityProduced { get; set; }

        /// <summary>Cost of all stock items consumed (ingredients and tracked packaging).</summary>
        public decimal MaterialCost { get; set; }
        /// <summary>Sum of the non-inventory cost lines (labour, utilities, ...).</summary>
        public decimal AdditionalCost { get; set; }
        public decimal TotalCost { get; set; }

        /// <summary>TotalCost divided by the output in BASE units — the cost the finished good enters stock at.</summary>
        public decimal UnitCost { get; set; }
        public string? Notes { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public Guid? CreatedBy { get; set; }

        public Formula Formula { get; set; } = null!;
        public Product Product { get; set; } = null!;
        public UnitOfMeasure UnitOfMeasure { get; set; } = null!;
        public ICollection<ProductionRunInput> Inputs { get; set; } = new List<ProductionRunInput>();
        public ICollection<ProductionRunCost> AdditionalCosts { get; set; } = new List<ProductionRunCost>();
    }
}
