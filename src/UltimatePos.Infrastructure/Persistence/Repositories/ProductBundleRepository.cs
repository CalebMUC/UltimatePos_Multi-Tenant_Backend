using Microsoft.EntityFrameworkCore;
using UltimatePos.Application.Catalog;
using UltimatePos.Domain.Entities;

namespace UltimatePos.Infrastructure.Persistence.Repositories
{
    public class ProductBundleRepository : IProductBundleRepository
    {
        private readonly UltimatePosDbContext _context;

        public ProductBundleRepository(UltimatePosDbContext context) => _context = context;

        public async Task CreateAsync(ProductBundle bundle)
        {
            _context.ProductBundles.Add(bundle);
            await _context.SaveChangesAsync();
        }

        private IQueryable<ProductBundle> WithDetails() =>
            _context.ProductBundles.AsNoTracking()
                .Include(b => b.Components).ThenInclude(c => c.Product).ThenInclude(p => p.BaseUnitOfMeasure)
                .Include(b => b.Components).ThenInclude(c => c.UnitOfMeasure)
                .AsSplitQuery();

        public async Task<ProductBundle?> GetAsync(Guid productBundleId) =>
            await WithDetails().FirstOrDefaultAsync(b => b.ProductBundleId == productBundleId);

        public async Task<IReadOnlyList<ProductBundle>> GetManyAsync(IEnumerable<Guid> productBundleIds)
        {
            var ids = productBundleIds.Distinct().ToList();
            return await WithDetails().Where(b => ids.Contains(b.ProductBundleId)).ToListAsync();
        }

        public async Task<IReadOnlyList<ProductBundle>> ListAsync(bool? isActive, string? search)
        {
            var query = WithDetails();
            if (isActive.HasValue)
                query = query.Where(b => b.IsActive == isActive.Value);
            if (search is not null)
                query = query.Where(b => EF.Functions.ILike(b.Name, $"%{search}%") || (b.Code != null && EF.Functions.ILike(b.Code, $"%{search}%")));
            return await query.OrderBy(b => b.Name).ToListAsync();
        }

        public async Task<bool> CodeExistsAsync(string code, Guid? exceptBundleId = null) =>
            await _context.ProductBundles.AnyAsync(b => b.Code == code && (exceptBundleId == null || b.ProductBundleId != exceptBundleId));

        public async Task UpdateAsync(Guid productBundleId, string name, string? code, string? description, decimal retailPrice,
            decimal? wholesalePrice, IReadOnlyList<ProductBundleComponent> components, Guid? userId)
        {
            var bundle = await _context.ProductBundles.Include(b => b.Components).FirstAsync(b => b.ProductBundleId == productBundleId);
            bundle.Name = name;
            bundle.Code = code;
            bundle.Description = description;
            bundle.RetailPrice = retailPrice;
            bundle.WholesalePrice = wholesalePrice;
            bundle.LastUpdatedAt = DateTime.UtcNow;
            bundle.LastUpdatedBy = userId;
            // Sale lines keep their own product, quantity and price, so replacing the parts never rewrites a past sale.
            _context.ProductBundleComponents.RemoveRange(bundle.Components);
            _context.ProductBundleComponents.AddRange(components);
            await _context.SaveChangesAsync();
        }

        public async Task SetActiveAsync(Guid productBundleId, bool isActive, Guid? userId) =>
            await _context.ProductBundles.Where(b => b.ProductBundleId == productBundleId)
                .ExecuteUpdateAsync(s => s.SetProperty(b => b.IsActive, isActive)
                    .SetProperty(b => b.LastUpdatedAt, DateTime.UtcNow)
                    .SetProperty(b => b.LastUpdatedBy, userId));
    }
}
