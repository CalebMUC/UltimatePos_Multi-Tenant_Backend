using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UltimatePos.Domain.Entities;
using static UltimatePos.Domain.Enums.PosEnums;

namespace UltimatePos.Application.Costing
{
    /// <summary>One sold line, reduced to what profit reporting needs. UnitOfMeasureId / UnitSymbol / Quantity are the unit it was sold
    /// in and how many of that unit (negative on a return row), so sales can be broken down by pack size (a 20L refill vs a 1L).</summary>
    public record SaleCostRow(Guid ProductId, string ProductName, string Sku, decimal QuantityInBaseUnits, decimal LineTotal, decimal? UnitCost,
        Guid UnitOfMeasureId = default, string? UnitSymbol = null, decimal Quantity = 0m);

    public interface ICostingRepository
    {
        /// <summary>Lines of Completed sales created in [fromUtc, toUtcExclusive).</summary>
        Task<IReadOnlyList<SaleCostRow>> GetSaleCostRowsAsync(DateTime fromUtc, DateTime toUtcExclusive, Guid? productId);

        /// <summary>Active price tiers already in effect, with Product and UnitOfMeasure loaded. May contain history; the caller picks the latest per product/unit/type.</summary>
        Task<IReadOnlyList<ProductPriceTier>> GetCurrentPriceTiersAsync(PriceType? priceType);

        /// <summary>Active pack-unit conversion factors keyed by (product, pack unit). The base unit (factor 1) is not included.</summary>
        Task<Dictionary<(Guid ProductId, Guid UnitOfMeasureId), decimal>> GetConversionFactorsAsync(IEnumerable<Guid> productIds);
    }
}
