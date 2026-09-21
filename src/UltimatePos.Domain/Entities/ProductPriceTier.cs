using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static UltimatePos.Domain.Enums.PosEnums;

namespace UltimatePos.Domain.Entities
{
    public class ProductPriceTier
    {
        public Guid ProductPriceTierId { get; set; }
        public Guid ProductId { get; set; }
        public Guid UnitOfMeasureId { get; set; }
        public PriceType PriceType { get; set; }
        public decimal Price { get; set; }
        public DateTime EffectiveFrom { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public Guid? CreatedBy { get; set; }
        public DateTime? LastUpdatedAt { get; set; }
        public Guid? LastUpdatedBy { get; set; }

        public Product Product { get; set; } = null!;
        public UnitOfMeasure UnitOfMeasure { get; set; } = null!;
    }
}
