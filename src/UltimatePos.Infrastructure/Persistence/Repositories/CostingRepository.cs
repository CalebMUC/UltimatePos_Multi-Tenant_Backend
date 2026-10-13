using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UltimatePos.Application.Costing;
using UltimatePos.Domain.Entities;
using static UltimatePos.Domain.Enums.PosEnums;

namespace UltimatePos.Infrastructure.Persistence.Repositories
{
    // Read-only queries for the costing reports. All arithmetic lives in CostingService so it stays unit-testable.
    public class CostingRepository : ICostingRepository
    {
        private readonly UltimatePosDbContext _context;
        public CostingRepository(UltimatePosDbContext context) => _context = context;

        public async Task<IReadOnlyList<SaleCostRow>> GetSaleCostRowsAsync(DateTime fromUtc, DateTime toUtcExclusive, Guid? productId)
        {
            var query = _context.SaleLines.AsNoTracking()
                .Where(l => l.Sale.Status == SaleStatus.Completed
                            && l.Sale.CreatedAt >= fromUtc && l.Sale.CreatedAt < toUtcExclusive);

            if (productId.HasValue)
                query = query.Where(l => l.ProductId == productId.Value);

            return await query
                .Select(l => new SaleCostRow(l.ProductId, l.Product.Name, l.Product.Sku, l.QuantityInBaseUnits, l.LineTotal, l.UnitCost))
                .ToListAsync();
        }

        public async Task<IReadOnlyList<ProductPriceTier>> GetCurrentPriceTiersAsync(PriceType? priceType)
        {
            var now = DateTime.UtcNow;
            var query = _context.ProductPriceTiers.AsNoTracking()
                .Include(t => t.Product)
                .Include(t => t.UnitOfMeasure)
                .Where(t => t.IsActive && t.EffectiveFrom <= now);

            if (priceType.HasValue)
                query = query.Where(t => t.PriceType == priceType.Value);

            return await query.ToListAsync();
        }

        public async Task<Dictionary<(Guid ProductId, Guid UnitOfMeasureId), decimal>> GetConversionFactorsAsync(IEnumerable<Guid> productIds)
        {
            var ids = productIds.Distinct().ToList();
            return await _context.ProductUnitConversions.AsNoTracking()
                .Where(c => c.IsActive && ids.Contains(c.ProductId))
                .ToDictionaryAsync(c => (c.ProductId, c.PackUnitOfMeasureId), c => c.ConversionFactor);
        }
    }
}