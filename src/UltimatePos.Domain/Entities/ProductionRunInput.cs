using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UltimatePos.Domain.Entities
{
    public class ProductionRunInput
    {
        public Guid ProductionRunInputId { get; set; }
        public Guid ProductionRunId { get; set; }
        public Guid IngredientProductId { get; set; }
        public Guid UnitOfMeasureId { get; set; }
        public decimal QuantityConsumed { get; set; }
        public decimal QuantityConsumedInBaseUnits { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public Guid? CreatedBy { get; set; }

        public ProductionRun ProductionRun { get; set; } = null!;
        public Product IngredientProduct { get; set; } = null!;
        public UnitOfMeasure UnitOfMeasure { get; set; } = null!;
    }
}
