using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UltimatePos.Domain.Entities
{
    // A completed production fact — immutable, no IsActive/LastUpdated*. Corrections go through stock adjustments.
    public class ProductionRun
    {
        public Guid ProductionRunId { get; set; }
        public string RunNumber { get; set; } = string.Empty;
        public Guid FormulaId { get; set; }
        public Guid ProductId { get; set; }
        public Guid UnitOfMeasureId { get; set; }
        public decimal QuantityPlanned { get; set; }
        public decimal QuantityProduced { get; set; }
        public string? Notes { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public Guid? CreatedBy { get; set; }

        public Formula Formula { get; set; } = null!;
        public Product Product { get; set; } = null!;
        public UnitOfMeasure UnitOfMeasure { get; set; } = null!;
        public ICollection<ProductionRunInput> Inputs { get; set; } = new List<ProductionRunInput>();
    }
}
