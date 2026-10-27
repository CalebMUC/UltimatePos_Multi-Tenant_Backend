using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static UltimatePos.Domain.Enums.PosEnums;

namespace UltimatePos.Domain.Entities
{
    
    public class Product : ITenantEntity
    {
        public Guid ProductId { get; set; }
        public Guid BusinessId { get; set; }
        public string Sku { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public Guid CategoryId { get; set; }
        /// <summary>The brand it is sold under (Soko, Pembe). Optional.</summary>
        public Guid? BrandId { get; set; }
        public ItemType ItemType { get; set; }
        public TaxClassification TaxClassification { get; set; }
        public Guid BaseUnitOfMeasureId { get; set; }
        public decimal ReorderLevel { get; set; }
        public string? Description { get; set; }
        /// <summary>Manufacturer's barcode (EAN/UPC) as printed on the pack. Optional; unique within the business when set.</summary>
        public string? Barcode { get; set; }
        /// <summary>Stock is held in batches with an expiry date and issued first-expiry-first-out. Required for medicines.</summary>
        public bool IsBatchTracked { get; set; }
        /// <summary>May only be sold with prescription details captured on the sale (prescription-only medicine).</summary>
        public bool RequiresPrescription { get; set; }
        // Descriptive medicine attributes. Stored and shown, never interpreted: the system applies no clinical rules.
        public string? GenericName { get; set; }
        public string? Strength { get; set; }
        public string? DosageForm { get; set; }
        public string? Manufacturer { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public Guid? CreatedBy { get; set; }
        public DateTime? LastUpdatedAt { get; set; }
        public Guid? LastUpdatedBy { get; set; }

        public Category Category { get; set; } = null!;
        public Brand? Brand { get; set; }
        public UnitOfMeasure BaseUnitOfMeasure { get; set; } = null!;
        public ICollection<ProductUnitConversion> UnitConversions { get; set; } = new List<ProductUnitConversion>();
        public ICollection<ProductPriceTier> PriceTiers { get; set; } = new List<ProductPriceTier>();
    }
}
