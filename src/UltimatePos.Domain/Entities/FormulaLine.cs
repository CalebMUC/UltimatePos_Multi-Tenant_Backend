using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UltimatePos.Domain.Entities
{
    // Immutable, like its parent version — CreatedAt/CreatedBy only, same as StockMovement.
    public class FormulaLine
    {
        public Guid FormulaLineId { get; set; }
        public Guid FormulaId { get; set; }
        public Guid IngredientProductId { get; set; }
        public Guid UnitOfMeasureId { get; set; }
        public decimal Quantity { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public Guid? CreatedBy { get; set; }

        public Formula Formula { get; set; } = null!;
        public Product IngredientProduct { get; set; } = null!;
        public UnitOfMeasure UnitOfMeasure { get; set; } = null!;
    }
}
