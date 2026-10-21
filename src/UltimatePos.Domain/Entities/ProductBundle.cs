using System;
using System.Collections.Generic;

namespace UltimatePos.Domain.Entities
{
    /// <summary>
    /// Several stocked products sold together at one price: "20L Heavy-duty bottle + water" for 1500 is 20 litres of water and one
    /// heavy-duty bottle; "Pack of 12 x 1L" is 12 litres and twelve 1L bottles. A bundle holds no stock of its own. When it is sold,
    /// the sale gets one ordinary line per component, so each component leaves its own stock at its own cost, and returns, voids and
    /// reports keep working per product. The bundle's price is split over those lines (see SalesService) and each line remembers the
    /// bundle it came from, so a receipt can show the bundle as one item.
    /// </summary>
    public class ProductBundle : ITenantEntity
    {
        public Guid ProductBundleId { get; set; }
        public Guid BusinessId { get; set; }
        public string Name { get; set; } = string.Empty;
        /// <summary>Short code for the till and for scanning, unique in the business. Optional.</summary>
        public string? Code { get; set; }
        public string? Description { get; set; }
        /// <summary>What one bundle costs a walk-in or retail customer, VAT included.</summary>
        public decimal RetailPrice { get; set; }
        /// <summary>What it costs a wholesale customer. Null = the retail price.</summary>
        public decimal? WholesalePrice { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public Guid? CreatedBy { get; set; }
        public DateTime? LastUpdatedAt { get; set; }
        public Guid? LastUpdatedBy { get; set; }

        public ICollection<ProductBundleComponent> Components { get; set; } = new List<ProductBundleComponent>();
    }

    /// <summary>What goes into ONE bundle: (water, l, 20) or (heavy-duty bottle, pcs, 1).</summary>
    public class ProductBundleComponent
    {
        public Guid ProductBundleComponentId { get; set; }
        public Guid ProductBundleId { get; set; }
        public Guid ProductId { get; set; }
        /// <summary>The product's base unit or one of its pack units.</summary>
        public Guid UnitOfMeasureId { get; set; }
        /// <summary>In UnitOfMeasureId, per bundle. May be a fraction of the unit: half a litre is (water, l, 0.5).</summary>
        public decimal Quantity { get; set; }

        public ProductBundle ProductBundle { get; set; } = null!;
        public Product Product { get; set; } = null!;
        public UnitOfMeasure UnitOfMeasure { get; set; } = null!;
    }
}
