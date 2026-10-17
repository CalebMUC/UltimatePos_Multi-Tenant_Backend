using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UltimatePos.Domain.Entities
{
    public class SaleLine
    {
        public Guid SaleLineId { get; set; }
        public Guid SaleId { get; set; }
        public Guid ProductId { get; set; }
        public Guid UnitOfMeasureId { get; set; }
        public decimal Quantity { get; set; }
        public decimal QuantityInBaseUnits { get; set; }
        public decimal UnitPrice { get; set; }

        /// <summary>Cost per BASE unit at the moment of sale (snapshot, so later cost changes never rewrite history).
        /// Null for sales recorded before costing existed; 0 when the product had no cost recorded.</summary>
        public decimal? UnitCost { get; set; }
        /// <summary>Quantity x UnitPrice - DiscountAmount, VAT included.</summary>
        public decimal LineTotal { get; set; }
        public decimal DiscountAmount { get; set; }
        /// <summary>VAT rate applied, as a percentage (16 = 16%). Zero for exempt and zero-rated goods.</summary>
        public decimal TaxRate { get; set; }
        /// <summary>VAT contained in LineTotal.</summary>
        public decimal TaxAmount { get; set; }
        /// <summary>How much of Quantity (in the sale unit) has been returned.</summary>
        public decimal QuantityReturned { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public Guid? CreatedBy { get; set; }

        public Sale Sale { get; set; } = null!;
        public Product Product { get; set; } = null!;
        public UnitOfMeasure UnitOfMeasure { get; set; } = null!;
        public ICollection<SaleLineBatchAllocation> BatchAllocations { get; set; } = new List<SaleLineBatchAllocation>();
    }
}
