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

    public async Task<bool> CategoryCodeExistsAsync(string code) =>
        await _context.Categories.AnyAsync(c => c.Code.ToUpper() == code.ToUpper());

    public async Task<Category> UpdateCategoryAsync(Guid categoryId, string name, Guid? parentCategoryId, Guid? updatedBy)
    {
        var category = await _context.Categories.FirstOrDefaultAsync(c => c.CategoryId == categoryId)
            ?? throw new NotFoundException($"Category '{categoryId}' not found.");
        category.Name = name;
        category.ParentCategoryId = parentCategoryId;
        category.LastUpdatedBy = updatedBy;
        category.LastUpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return category;
    }

    public async Task<Category> SetCategoryActiveStatusAsync(Guid categoryId, bool isActive, Guid? updatedBy)
    {
        var category = await _context.Categories.FirstOrDefaultAsync(c => c.CategoryId == categoryId)
            ?? throw new NotFoundException($"Category '{categoryId}' not found.");
        category.IsActive = isActive;
        category.LastUpdatedBy = updatedBy;
        category.LastUpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return category;
    }

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
        await _context.UnitsOfMeasure.AsNoTracking().OrderBy(u => u.Name).ToListAsync();

    public async Task<bool> UnitSymbolExistsAsync(string symbol, Guid? exceptUnitId = null) =>
        await _context.UnitsOfMeasure.AnyAsync(u => u.Symbol.ToLower() == symbol.ToLower() && (exceptUnitId == null || u.UnitOfMeasureId != exceptUnitId));

    public async Task<UnitOfMeasure> UpdateUnitOfMeasureAsync(Guid unitOfMeasureId, string name, string symbol, Guid? updatedBy)
    {
        var unit = await _context.UnitsOfMeasure.FirstOrDefaultAsync(u => u.UnitOfMeasureId == unitOfMeasureId)
            ?? throw new NotFoundException($"Unit of measure '{unitOfMeasureId}' not found.");
        unit.Name = name;
        unit.Symbol = symbol;
        unit.LastUpdatedBy = updatedBy;
        unit.LastUpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return unit;
    }

    public async Task<UnitOfMeasure> SetUnitOfMeasureActiveStatusAsync(Guid unitOfMeasureId, bool isActive, Guid? updatedBy)
    {
        var unit = await _context.UnitsOfMeasure.FirstOrDefaultAsync(u => u.UnitOfMeasureId == unitOfMeasureId)
            ?? throw new NotFoundException($"Unit of measure '{unitOfMeasureId}' not found.");
        unit.IsActive = isActive;
        unit.LastUpdatedBy = updatedBy;
        unit.LastUpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return unit;
    }

    public async Task<Dictionary<string, Guid>> GetUnitSymbolMapAsync() =>
        await _context.UnitsOfMeasure.AsNoTracking().ToDictionaryAsync(u => u.Symbol, u => u.UnitOfMeasureId, StringComparer.OrdinalIgnoreCase);

    public async Task<int> GetNextSkuNumberAsync(string prefix)
    {
        return await _context.NextSequenceNumberAsync(prefix);
    }

    public async Task<Product> CreateProductAsync(Product product)
    {
        _context.Products.Add(product);
        await _context.SaveChangesAsync();
        return product;
    }

    public async Task<(IEnumerable<Product> Items, int TotalCount)> GetProductsAsync(int page, int pageSize, ProductQuery filter)
    {
        var query = _context.Products.AsNoTracking()
            .Include(p => p.Category)
            .Include(p => p.BaseUnitOfMeasure)
            .AsQueryable();

        if (filter.CategoryId.HasValue)
            query = query.Where(p => p.CategoryId == filter.CategoryId.Value);
        if (filter.ItemType.HasValue)
            query = query.Where(p => p.ItemType == filter.ItemType.Value);
        if (filter.IsActive.HasValue)
            query = query.Where(p => p.IsActive == filter.IsActive.Value);
        if (filter.IsBatchTracked.HasValue)
            query = query.Where(p => p.IsBatchTracked == filter.IsBatchTracked.Value);
        if (filter.RequiresPrescription.HasValue)
            query = query.Where(p => p.RequiresPrescription == filter.RequiresPrescription.Value);
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var pattern = $"%{filter.Search.Trim()}%";
            query = query.Where(p => EF.Functions.ILike(p.Name, pattern) || EF.Functions.ILike(p.Sku, pattern)
                                     || (p.Barcode != null && EF.Functions.ILike(p.Barcode, pattern))
                                     || (p.GenericName != null && EF.Functions.ILike(p.GenericName, pattern)));
        }

        var descending = string.Equals(filter.SortDirection, "desc", StringComparison.OrdinalIgnoreCase);
        query = (filter.SortBy ?? "name").ToLowerInvariant() switch
        {
            "sku" => descending ? query.OrderByDescending(p => p.Sku) : query.OrderBy(p => p.Sku),
            "createdat" => descending ? query.OrderByDescending(p => p.CreatedAt) : query.OrderBy(p => p.CreatedAt),
            "category" => descending ? query.OrderByDescending(p => p.Category.Name).ThenBy(p => p.Name) : query.OrderBy(p => p.Category.Name).ThenBy(p => p.Name),
            _ => descending ? query.OrderByDescending(p => p.Name) : query.OrderBy(p => p.Name),
        };

        var totalCount = await query.CountAsync();
        var items = await ((IOrderedQueryable<Product>)query).ThenBy(p => p.ProductId)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        return (items, totalCount);
    }

    public async Task<Product?> GetProductByIdAsync(Guid productId) =>
        await _context.Products.AsNoTracking()
            .Include(p => p.Category)
            .Include(p => p.BaseUnitOfMeasure)
            .Include(p => p.UnitConversions).ThenInclude(c => c.PackUnitOfMeasure)
            .Include(p => p.UnitConversions).ThenInclude(c => c.ContainerProduct).ThenInclude(cp => cp!.BaseUnitOfMeasure)
            .Include(p => p.PriceTiers)
            .AsSplitQuery()
            .FirstOrDefaultAsync(p => p.ProductId == productId);

    public async Task<Product?> GetProductByCodeAsync(string code)
    {
        var upper = code.ToUpperInvariant();
        var productId = await _context.Products.AsNoTracking()
            .Where(p => p.Barcode == code || p.Sku.ToUpper() == upper)
            .OrderByDescending(p => p.Barcode == code) // a barcode match beats a SKU that happens to look the same
            .Select(p => (Guid?)p.ProductId)
            .FirstOrDefaultAsync();
        return productId is null ? null : await GetProductByIdAsync(productId.Value);
    }

    public async Task<bool> BarcodeExistsAsync(string barcode, Guid? exceptProductId = null) =>
        await _context.Products.AnyAsync(p => p.Barcode == barcode && (exceptProductId == null || p.ProductId != exceptProductId));

    public async Task<Product> SetProductActiveStatusAsync(Guid productId, bool isActive, Guid? updatedBy)
    {
        var product = await _context.Products.FirstOrDefaultAsync(p => p.ProductId == productId)
            ?? throw new NotFoundException($"Product '{productId}' not found.");
        product.IsActive = isActive;
        product.LastUpdatedBy = updatedBy;
        product.LastUpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return (await GetProductByIdAsync(productId))!;
    }

    /// <summary>
    /// Stock held at ANY branch, including ones the caller can't see (the product, already checked to be this business's, is shared by every branch). Absolute values, so
    /// a negative balance at one branch can't hide stock at another.
    /// </summary>
    public async Task<decimal> GetQuantityOnHandAsync(Guid productId) =>
        await _context.StockLevels.IgnoreQueryFilters()
            .Where(s => s.ProductId == productId)
            .SumAsync(s => (decimal?)Math.Abs(s.QuantityOnHand)) ?? 0m;

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
        product.Barcode = request.Barcode;
        product.IsBatchTracked = request.IsBatchTracked;
        product.RequiresPrescription = request.RequiresPrescription;
        product.GenericName = request.GenericName;
        product.Strength = request.Strength;
        product.DosageForm = request.DosageForm;
        product.Manufacturer = request.Manufacturer;
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
        return await WithDetails().AsNoTracking().FirstAsync(c => c.ProductUnitConversionId == conversion.ProductUnitConversionId);
    }

    /// <summary>A conversion with its pack unit and its container (and the container's base unit), for the DTO.</summary>
    private IQueryable<ProductUnitConversion> WithDetails() =>
        _context.ProductUnitConversions.Include(c => c.PackUnitOfMeasure).Include(c => c.ContainerProduct).ThenInclude(cp => cp!.BaseUnitOfMeasure);

    public async Task<IEnumerable<ProductUnitConversion>> GetUnitConversionsAsync(Guid productId) =>
        await WithDetails().AsNoTracking().Where(c => c.ProductId == productId).ToListAsync();

    public async Task<ProductUnitConversion> SetUnitConversionContainerAsync(Guid productId, Guid conversionId, Guid? containerProductId, Guid? updatedBy)
    {
        var conversion = await _context.ProductUnitConversions
            .FirstOrDefaultAsync(c => c.ProductId == productId && c.ProductUnitConversionId == conversionId)
            ?? throw new NotFoundException($"Unit conversion '{conversionId}' not found on this product.");
        conversion.ContainerProductId = containerProductId;
        conversion.LastUpdatedBy = updatedBy;
        conversion.LastUpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return await WithDetails().AsNoTracking().FirstAsync(c => c.ProductUnitConversionId == conversionId);
    }

    public async Task<ProductUnitConversion> UpdateUnitConversionAsync(Guid productId, Guid conversionId, decimal? conversionFactor, bool? isActive, Guid? updatedBy)
    {
        var conversion = await WithDetails()
            .FirstOrDefaultAsync(c => c.ProductId == productId && c.ProductUnitConversionId == conversionId)
            ?? throw new NotFoundException($"Unit conversion '{conversionId}' not found on this product.");
        if (conversionFactor.HasValue)
            conversion.ConversionFactor = conversionFactor.Value;
        if (isActive.HasValue)
            conversion.IsActive = isActive.Value;
        conversion.LastUpdatedBy = updatedBy;
        conversion.LastUpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return conversion;
    }

    public async Task<decimal?> GetConversionFactorAsync(Guid productId, Guid unitOfMeasureId)
    {
        var product = await _context.Products.AsNoTracking().FirstOrDefaultAsync(p => p.ProductId == productId);
        if (product is null) return null;
        if (product.BaseUnitOfMeasureId == unitOfMeasureId) return 1m;

        var conversion = await _context.ProductUnitConversions.AsNoTracking()
            .FirstOrDefaultAsync(c => c.ProductId == productId && c.PackUnitOfMeasureId == unitOfMeasureId && c.IsActive);
        return conversion?.ConversionFactor;
    }

    public async Task<IEnumerable<Product>> GetProductsByIdsAsync(IEnumerable<Guid> productIds)
    {
        var ids = productIds.Distinct().ToList();
        return await _context.Products.AsNoTracking().Where(p => ids.Contains(p.ProductId)).ToListAsync();
    }

    public async Task<decimal?> GetActivePriceAsync(Guid productId, Guid unitOfMeasureId, PriceType priceType) =>
    await _context.ProductPriceTiers.AsNoTracking()
        .Where(t => t.ProductId == productId && t.UnitOfMeasureId == unitOfMeasureId
                    && t.PriceType == priceType && t.IsActive && t.EffectiveFrom <= DateTime.UtcNow)
        .OrderByDescending(t => t.EffectiveFrom)
        .Select(t => (decimal?)t.Price)
        .FirstOrDefaultAsync();

    public async Task<bool> UnitOfMeasureExistsAsync(Guid unitOfMeasureId) =>
        await _context.UnitsOfMeasure.AnyAsync(u => u.UnitOfMeasureId == unitOfMeasureId);

    public async Task<bool> ConversionExistsAsync(Guid productId, Guid packUnitOfMeasureId) =>
        await _context.ProductUnitConversions.AnyAsync(c => c.ProductId == productId && c.PackUnitOfMeasureId == packUnitOfMeasureId);
}