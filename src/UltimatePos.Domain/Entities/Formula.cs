using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UltimatePos.Domain.Entities
{
    // Versions are immutable: editing a recipe creates a new version. Only IsActive/LastUpdated* ever change.
    public class Formula
    {
        public Guid FormulaId { get; set; }
        public Guid ProductId { get; set; }
        public int VersionNumber { get; set; }
        public decimal OutputQuantity { get; set; }
        public Guid OutputUnitOfMeasureId { get; set; }
        public string? Notes { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public Guid? CreatedBy { get; set; }
        public DateTime? LastUpdatedAt { get; set; }
        public Guid? LastUpdatedBy { get; set; }

        public Product Product { get; set; } = null!;
        public UnitOfMeasure OutputUnitOfMeasure { get; set; } = null!;
        public ICollection<FormulaLine> Lines { get; set; } = new List<FormulaLine>();
    }
}
