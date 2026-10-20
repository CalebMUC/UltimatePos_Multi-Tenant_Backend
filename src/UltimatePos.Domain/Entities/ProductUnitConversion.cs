using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UltimatePos.Domain.Entities
{
    public class ProductUnitConversion
    {
        public Guid ProductUnitConversionId { get; set; }
        public Guid ProductId { get; set; }
        public Guid PackUnitOfMeasureId { get; set; }
        public decimal ConversionFactor { get; set; }
        /// <summary>
        /// The product sold with one of this pack when the customer has none of their own: an empty 20L container with a 20L refill.
        /// The till adds it as a sale line of its own; it never changes how this pack is priced or stocked. Null = none.
        /// </summary>
        public Guid? ContainerProductId { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public Guid? CreatedBy { get; set; }
        public DateTime? LastUpdatedAt { get; set; }
        public Guid? LastUpdatedBy { get; set; }

        public Product Product { get; set; } = null!;
        public UnitOfMeasure PackUnitOfMeasure { get; set; } = null!;
        public Product? ContainerProduct { get; set; }
    }
}
