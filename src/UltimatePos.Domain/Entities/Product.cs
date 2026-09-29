using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static UltimatePos.Domain.Enums.PosEnums;

namespace UltimatePos.Domain.Entities
{
    
    public class Product
    {
        public Guid ProductId { get; set; }
        public string Sku { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public Guid CategoryId { get; set; }
        public ItemType ItemType { get; set; }
        public TaxClassification TaxClassification { get; set; }
        public Guid BaseUnitOfMeasureId { get; set; }
        public decimal ReorderLevel { get; set; }
        public string? Description { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public Guid? CreatedBy { get; set; }
        public DateTime? LastUpdatedAt { get; set; }
        public Guid? LastUpdatedBy { get; set; }

        public Category Category { get; set; } = null!;
        public UnitOfMeasure BaseUnitOfMeasure { get; set; } = null!;
        public ICollection<ProductUnitConversion> UnitConversions { get; set; } = new List<ProductUnitConversion>();
        public ICollection<ProductPriceTier> PriceTiers { get; set; } = new List<ProductPriceTier>();
    }
}
