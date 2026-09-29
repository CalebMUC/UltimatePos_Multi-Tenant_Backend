using Microsoft.EntityFrameworkCore;
using UltimatePos.Application.Catalog;
using UltimatePos.Application.Catalog.Dtos;
using UltimatePos.Domain.Entities;
using UltimatePos.Domain.Enums;
using UltimatePos.Domain.Exceptions;
using static UltimatePos.Domain.Enums.PosEnums;

namespace UltimatePos.Infrastructure.Persistence.Repositories;

public class CatalogRepository : ICatalogRepository
{
    private readonly UltimatePosDbContext _context;
    public CatalogRepository(UltimatePosDbContext context) => _context = context;

    public async Task<Category> CreateCategoryAsync(Category category)
    {
        _context.Categories.Add(category);
        await _context.SaveChangesAsync();
        return category;
    }

    public async Task<IEnumerable<Category>> GetCategoriesAsync() =>
        await _context.Categories.AsNoTracking().ToListAsync();

    public async Task<Category?> GetCategoryByIdAsync(Guid categoryId) =>
        await _context.Categories.AsNoTracking().FirstOrDefaultAsync(c => c.CategoryId == categoryId);

    public async Task<Dictionary<string, Guid>> GetCategoryCodeMapAsync() =>
        await _context.Categories.AsNoTracking().ToDictionaryAsync(c => c.Code, c => c.CategoryId, StringComparer.OrdinalIgnoreCase);

    public async Task CreateCategoriesAsync(IEnumerable<Category> categories)
    {
        _context.Categories.AddRange(categories);
        await _context.SaveChangesAsync();
    }

    public async Task<UnitOfMeasure> CreateUnitOfMeasureAsync(UnitOfMeasure unit)
    {
        _context.UnitsOfMeasure.Add(unit);
        await _context.SaveChangesAsync();
        return unit;
    }

    public async Task<IEnumerable<UnitOfMeasure>> GetUnitsOfMeasureAsync() =>
        await _context.UnitsOfMeasure.AsNoTracking().ToListAsync();

    public async Task<Dictionary<string, Guid>> GetUnitSymbolMapAsync() =>
        await _context.UnitsOfMeasure.AsNoTracking().ToDictionaryAsync(u => u.Symbol, u => u.UnitOfMeasureId, StringComparer.OrdinalIgnoreCase);

    public async Task<int> GetNextSkuNumberAsync(string prefix)
    {
        var results = await _context.Database
            .SqlQuery<int>($"""
                INSERT INTO "SkuSequences" ("Prefix", "LastNumber") VALUES ({prefix}, 1)
                ON CONFLICT ("Prefix") DO UPDATE SET "LastNumber" = "SkuSequences"."LastNumber" + 1
                RETURNING "LastNumber"
                """)
            .ToListAsync();
        return results.Single();
    }

    public async Task<Product> CreateProductAsync(Product product)
    {
        _context.Products.Add(product);
        await _context.SaveChangesAsync();
        return product;
    }

    public async Task<(IEnumerable<Product> Items, int TotalCount)> GetProductsAsync(int page, int pageSize, Guid? categoryId, ItemType? itemType)
    {
        var query = _context.Products.AsNoTracking().AsQueryable();

        if (categoryId.HasValue)
            query = query.Where(p => p.CategoryId == categoryId.Value);
        if (itemType.HasValue)
            query = query.Where(p => p.ItemType == itemType.Value);

        var totalCount = await query.CountAsync();
        var items = await query.OrderBy(p => p.Name).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        return (items, totalCount);
    }

    public async Task<Product?> GetProductByIdAsync(Guid productId) =>
        await _context.Products.AsNoTracking()
            .Include(p => p.UnitConversions)
            .Include(p => p.PriceTiers)
            .FirstOrDefaultAsync(p => p.ProductId == productId);

    public async Task<Product> UpdateProductAsync(Guid productId, UpdateProductRequestDto request, Guid? updatedBy)
    {
        var product = await _context.Products.FirstOrDefaultAsync(p => p.ProductId == productId)
            ?? throw new NotFoundException($"Product '{productId}' not found.");

        product.Name = request.Name;
        product.CategoryId = request.CategoryId;
        product.ItemType = request.ItemType;
        product.TaxClassification = request.TaxClassification;
        product.BaseUnitOfMeasureId = request.BaseUnitOfMeasureId;
        product.ReorderLevel = request.ReorderLevel;
        product.Description = request.Description;
        product.LastUpdatedBy = updatedBy;
        product.LastUpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return product;
    }

    public async Task<HashSet<(string Name, Guid CategoryId)>> GetProductNameCategoryPairsAsync()
    {
        var pairs = await _context.Products.AsNoTracking().Select(p => new { p.Name, p.CategoryId }).ToListAsync();
        return pairs.Select(p => (p.Name, p.CategoryId)).ToHashSet();
    }

    public async Task CreateProductsAsync(IEnumerable<Product> products, IEnumerable<ProductPriceTier> priceTiers)
    {
        _context.Products.AddRange(products);
        _context.ProductPriceTiers.AddRange(priceTiers);
        await _context.SaveChangesAsync();
    }

    public async Task<IEnumerable<ProductPriceTier>> GetPriceTiersAsync(Guid productId) =>
        await _context.ProductPriceTiers.AsNoTracking()
            .Where(t => t.ProductId == productId)
            .OrderByDescending(t => t.EffectiveFrom)
            .ToListAsync();

    public async Task<ProductPriceTier> AddPriceTierAsync(ProductPriceTier tier)
    {
        _context.ProductPriceTiers.Add(tier);
        await _context.SaveChangesAsync();
        return tier;
    }

    public async Task<ProductUnitConversion> CreateUnitConversionAsync(ProductUnitConversion conversion)
    {
        _context.ProductUnitConversions.Add(conversion);
        await _context.SaveChangesAsync();
        return conversion;
    }

    public async Task<IEnumerable<ProductUnitConversion>> GetUnitConversionsAsync(Guid productId) =>
        await _context.ProductUnitConversions.AsNoTracking().Where(c => c.ProductId == productId).ToListAsync();

    public async Task<decimal?> GetConversionFactorAsync(Guid productId, Guid unitOfMeasureId)
    {
        var product = await _context.Products.AsNoTracking().FirstOrDefaultAsync(p => p.ProductId == productId);
        if (product is null) return null;
        if (product.BaseUnitOfMeasureId == unitOfMeasureId) return 1m;

        var conversion = await _context.ProductUnitConversions.AsNoTracking()
            .FirstOrDefaultAsync(c => c.ProductId == productId && c.PackUnitOfMeasureId == unitOfMeasureId && c.IsActive);
        return conversion?.ConversionFactor;
    }
}