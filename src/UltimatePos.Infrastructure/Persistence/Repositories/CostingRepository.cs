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

            // Revenue is what the business keeps: the charged amount less the VAT in it (owed to KRA).
            var sold = await query
                .Select(l => new SaleCostRow(l.ProductId, l.Product.Name, l.Product.Sku, l.QuantityInBaseUnits, l.LineTotal - l.TaxAmount, l.UnitCost))
                .ToListAsync();

            // Returns fall in the period they were made, as negative rows. Goods put back on the shelf also take their cost back out
            // of cost of goods sold; goods written off (damaged, expired) keep their cost — that stock is lost.
            var returnsQuery = _context.SaleReturnLines.AsNoTracking()
                .Where(l => l.SaleReturn.CreatedAt >= fromUtc && l.SaleReturn.CreatedAt < toUtcExclusive);
            if (productId.HasValue)
                returnsQuery = returnsQuery.Where(l => l.ProductId == productId.Value);

            var returned = await returnsQuery
                .Select(l => new SaleCostRow(l.ProductId, l.Product.Name, l.Product.Sku, l.Restock ? -l.QuantityInBaseUnits : 0m,
                    -(l.Amount - l.TaxAmount), l.UnitCost))
                .ToListAsync();

            return sold.Concat(returned).ToList();
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