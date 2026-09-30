using UltimatePos.Application.Catalog.Dtos;
using UltimatePos.Application.Common.Dtos;
using UltimatePos.Application.Common.Interfaces;
using UltimatePos.Domain.Entities;
using UltimatePos.Domain.Enums;
using UltimatePos.Domain.Exceptions;
using static UltimatePos.Domain.Enums.PosEnums;

namespace UltimatePos.Application.Catalog;

public class CatalogService
{
    private readonly ICatalogRepository _repository;
    private readonly ICurrentUser _currentUser;
    private readonly ICategoryImportParser _categoryImportParser;
    private readonly IProductImportParser _productImportParser;

    public CatalogService(
        ICatalogRepository repository, ICurrentUser currentUser,
        ICategoryImportParser categoryImportParser, IProductImportParser productImportParser)
    {
        _repository = repository;
        _currentUser = currentUser;
        _categoryImportParser = categoryImportParser;
        _productImportParser = productImportParser;
    }

    // ---- Categories ----

    public async Task<CategoryDto> CreateCategoryAsync(CreateCategoryRequestDto request)
    {
        var category = new Category
        {
            Name = request.Name,
            Code = request.Code.Trim().ToUpperInvariant(),
            ParentCategoryId = request.ParentCategoryId,
            CreatedBy = _currentUser.UserId
        };
        var created = await _repository.CreateCategoryAsync(category);
        return new CategoryDto(created.CategoryId, created.Name, created.Code, created.ParentCategoryId, created.IsActive, Enumerable.Empty<CategoryDto>());
    }

    public async Task<IEnumerable<CategoryDto>> GetCategoriesAsync()
    {
        var all = (await _repository.GetCategoriesAsync()).ToList();
        return BuildCategoryTree(all, null);
    }

    private static IEnumerable<CategoryDto> BuildCategoryTree(List<Category> all, Guid? parentId) =>
        all.Where(c => c.ParentCategoryId == parentId)
           .Select(c => new CategoryDto(c.CategoryId, c.Name, c.Code, c.ParentCategoryId, c.IsActive, BuildCategoryTree(all, c.CategoryId)));

    public byte[] GenerateCategoryImportTemplate() => _categoryImportParser.GenerateTemplate();

    public async Task<CategoryBulkImportResultDto> BulkImportCategoriesAsync(Stream fileStream, bool dryRun)
    {
        var rows = _categoryImportParser.Parse(fileStream).ToList();

        if (rows.Count > 5000)
            throw new InvalidAssignmentException("File has more than 5000 rows — split it into smaller batches.");

        var existingCodes = await _repository.GetCategoryCodeMapAsync();
        var batchCodes = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        var results = new List<CategoryImportRowResult>();
        var toCreate = new List<Category>();

        foreach (var (rowNumber, name, code, parentCode) in rows)
        {
            string? error = null;
            if (string.IsNullOrWhiteSpace(name))
                error = "Name is required.";
            else if (string.IsNullOrWhiteSpace(code))
                error = "Code is required.";
            else if (existingCodes.ContainsKey(code) || batchCodes.ContainsKey(code))
                error = $"Code '{code}' already exists.";
            else if (parentCode is not null && !existingCodes.ContainsKey(parentCode) && !batchCodes.ContainsKey(parentCode))
                error = $"Parent code '{parentCode}' not found — it must already exist or appear on an earlier row in this file.";

            var isValid = error is null;
            results.Add(new CategoryImportRowResult(rowNumber, name, code, parentCode, isValid, error));

            if (isValid)
            {
                var categoryId = Guid.NewGuid();
                Guid? parentId = parentCode is null ? null
                    : batchCodes.TryGetValue(parentCode, out var batchId) ? batchId
                    : existingCodes[parentCode];

                batchCodes[code] = categoryId;
                toCreate.Add(new Category { CategoryId = categoryId, Name = name, Code = code, ParentCategoryId = parentId, CreatedBy = _currentUser.UserId });
            }
        }

        var invalidCount = results.Count(r => !r.IsValid);
        var committed = false;

        if (!dryRun && invalidCount == 0 && toCreate.Count > 0)
        {
            await _repository.CreateCategoriesAsync(toCreate);
            committed = true;
        }

        return new CategoryBulkImportResultDto(committed, results.Count, results.Count - invalidCount, invalidCount, results);
    }

    // ---- Units of measure ----

    public async Task<UnitOfMeasureDto> CreateUnitOfMeasureAsync(CreateUnitOfMeasureRequestDto request)
    {
        var unit = new UnitOfMeasure { Name = request.Name, Symbol = request.Symbol.Trim(), CreatedBy = _currentUser.UserId };
        return ToDto(await _repository.CreateUnitOfMeasureAsync(unit));
    }

    public async Task<IEnumerable<UnitOfMeasureDto>> GetUnitsOfMeasureAsync() =>
        (await _repository.GetUnitsOfMeasureAsync()).Select(ToDto);

    // ---- Products ----

    public async Task<ProductDto> CreateProductAsync(CreateProductRequestDto request)
    {
        var category = await _repository.GetCategoryByIdAsync(request.CategoryId)
            ?? throw new NotFoundException($"Category '{request.CategoryId}' not found.");

        var prefix = $"{category.Code}-{ItemTypeCode(request.ItemType)}";
        var number = await _repository.GetNextSkuNumberAsync(prefix);
        var sku = $"{prefix}-{number:D6}";

        var product = new Product
        {
            Sku = sku,
            Name = request.Name,
            Description = request.Description,
            CategoryId = request.CategoryId,
            ItemType = request.ItemType,
            TaxClassification = request.TaxClassification,
            BaseUnitOfMeasureId = request.BaseUnitOfMeasureId,
            ReorderLevel = request.ReorderLevel,
            CreatedBy = _currentUser.UserId
        };

        return ToDto(await _repository.CreateProductAsync(product));
    }

    private static string ItemTypeCode(ItemType itemType) => itemType switch
    {
        ItemType.RawMaterial => "RM",
        ItemType.Packaging => "PKG",
        ItemType.FinishedGood => "FG",
        _ => "GEN"
    };

    public async Task<PagedResult<ProductDto>> GetProductsAsync(int page, int pageSize, Guid? categoryId, ItemType? itemType)
    {
        var (items, totalCount) = await _repository.GetProductsAsync(page, pageSize, categoryId, itemType);
        return new PagedResult<ProductDto>(items.Select(ToDto), page, pageSize, totalCount);
    }

    public async Task<ProductDetailDto> GetProductByIdAsync(Guid productId)
    {
        var product = await _repository.GetProductByIdAsync(productId)
            ?? throw new NotFoundException($"Product '{productId}' not found.");
        return ToDetailDto(product);
    }

    public async Task<ProductDto> UpdateProductAsync(Guid productId, UpdateProductRequestDto request) =>
        ToDto(await _repository.UpdateProductAsync(productId, request, _currentUser.UserId));

    public async Task<IEnumerable<ProductPriceTierDto>> GetPriceTiersAsync(Guid productId) =>
        (await _repository.GetPriceTiersAsync(productId)).Select(ToDto);

    public async Task<ProductPriceTierDto> AddPriceTierAsync(Guid productId, AddPriceTierRequestDto request)
    {
        var tier = new ProductPriceTier
        {
            ProductId = productId,
            UnitOfMeasureId = request.UnitOfMeasureId,
            PriceType = request.PriceType,
            Price = request.Price,
            EffectiveFrom = request.EffectiveFrom,
            CreatedBy = _currentUser.UserId
        };
        return ToDto(await _repository.AddPriceTierAsync(tier));
    }

    public byte[] GenerateProductImportTemplate() => _productImportParser.GenerateTemplate();

    public async Task<ProductBulkImportResultDto> BulkImportProductsAsync(Stream fileStream, bool dryRun)
    {
        var rows = _productImportParser.Parse(fileStream).ToList();

        if (rows.Count > 5000)
            throw new InvalidAssignmentException("File has more than 5000 rows — split it into smaller batches.");

        var categoryCodes = await _repository.GetCategoryCodeMapAsync();
        var unitSymbols = await _repository.GetUnitSymbolMapAsync();
        var existingPairs = (await _repository.GetProductNameCategoryPairsAsync())
            .Select(p => (Name: p.Name.Trim().ToUpperInvariant(), p.CategoryId))
            .ToHashSet();

        var batchPairs = new HashSet<(string Name, Guid CategoryId)>();
        var results = new Dictionary<int, ProductImportRowResult>();
        var validRows = new List<(int RowNumber, string Name, Guid CategoryId, string CategoryCode, ItemType ItemType,
            TaxClassification TaxClassification, Guid UnitId, decimal ReorderLevel, decimal? WholesalePrice, decimal? RetailPrice, string? Description)>();

        foreach (var (rowNumber, rawName, categoryCode, itemTypeRaw, taxRaw, unitSymbol, reorderLevel, wholesalePrice, retailPrice, description) in rows)
        {
            var name = rawName ?? string.Empty;
            string? error = null;
            var categoryFound = categoryCodes.TryGetValue(categoryCode ?? "", out var categoryId);
            var unitFound = unitSymbols.TryGetValue(unitSymbol ?? "", out var unitId);
            var itemTypeParsed = Enum.TryParse<ItemType>(itemTypeRaw, ignoreCase: true, out var itemType);
            var taxParsed = Enum.TryParse<TaxClassification>(taxRaw, ignoreCase: true, out var taxClassification);
            var nameKey = name.Trim().ToUpperInvariant();

            if (string.IsNullOrWhiteSpace(name))
                error = "Name is required.";
            else if (string.IsNullOrWhiteSpace(categoryCode) || !categoryFound)
                error = $"Category code '{categoryCode}' not found.";
            else if (string.IsNullOrWhiteSpace(itemTypeRaw) || !itemTypeParsed)
                error = $"Item type '{itemTypeRaw}' is not valid (RawMaterial, Packaging, FinishedGood).";
            else if (string.IsNullOrWhiteSpace(taxRaw) || !taxParsed)
                error = $"Tax classification '{taxRaw}' is not valid (Standard, ZeroRated, Exempt).";
            else if (string.IsNullOrWhiteSpace(unitSymbol) || !unitFound)
                error = $"Unit symbol '{unitSymbol}' not found.";
            else if (reorderLevel is null || reorderLevel < 0)
                error = "Reorder level is required and must be zero or greater.";
            else if (existingPairs.Contains((nameKey, categoryId)) || batchPairs.Contains((nameKey, categoryId)))
                error = $"Product '{name}' already exists in this category.";

            var isValid = error is null;
            results[rowNumber] = new ProductImportRowResult(rowNumber, name, isValid, error, null);

            if (isValid)
            {
                batchPairs.Add((nameKey, categoryId));
                validRows.Add((rowNumber, name, categoryId, categoryCode!, itemType, taxClassification, unitId, reorderLevel!.Value, wholesalePrice, retailPrice, description));
            }
        }

        var invalidCount = results.Values.Count(r => !r.IsValid);
        var committed = false;

        if (!dryRun && invalidCount == 0 && validRows.Count > 0)
        {
            var products = new List<Product>();
            var priceTiers = new List<ProductPriceTier>();

            foreach (var r in validRows)
            {
                var prefix = $"{r.CategoryCode}-{ItemTypeCode(r.ItemType)}";
                var number = await _repository.GetNextSkuNumberAsync(prefix);
                var sku = $"{prefix}-{number:D6}";
                var productId = Guid.NewGuid();

                products.Add(new Product
                {
                    ProductId = productId,
                    Sku = sku,
                    Name = r.Name,
                    Description = r.Description,
                    CategoryId = r.CategoryId,
                    ItemType = r.ItemType,
                    TaxClassification = r.TaxClassification,
                    BaseUnitOfMeasureId = r.UnitId,
                    ReorderLevel = r.ReorderLevel,
                    CreatedBy = _currentUser.UserId
                });

                if (r.WholesalePrice.HasValue)
                    priceTiers.Add(new ProductPriceTier { ProductId = productId, UnitOfMeasureId = r.UnitId, PriceType = PriceType.Wholesale, Price = r.WholesalePrice.Value, EffectiveFrom = DateTime.UtcNow, CreatedBy = _currentUser.UserId });
                if (r.RetailPrice.HasValue)
                    priceTiers.Add(new ProductPriceTier { ProductId = productId, UnitOfMeasureId = r.UnitId, PriceType = PriceType.Retail, Price = r.RetailPrice.Value, EffectiveFrom = DateTime.UtcNow, CreatedBy = _currentUser.UserId });

                results[r.RowNumber] = results[r.RowNumber] with { Sku = sku };
            }

            await _repository.CreateProductsAsync(products, priceTiers);
            committed = true;
        }

        var ordered = results.Values.OrderBy(r => r.RowNumber).ToList();
        return new ProductBulkImportResultDto(committed, ordered.Count, ordered.Count(r => r.IsValid), invalidCount, ordered);
    }

    public async Task<ProductUnitConversionDto> AddUnitConversionAsync(Guid productId, CreateProductUnitConversionRequestDto request)
    {
        var product = await _repository.GetProductByIdAsync(productId)
            ?? throw new NotFoundException($"Product '{productId}' not found.");

        if (request.ConversionFactor <= 0)
            throw new InvalidAssignmentException("Conversion factor must be greater than zero.");

        if (request.PackUnitOfMeasureId == product.BaseUnitOfMeasureId)
            throw new InvalidAssignmentException("The product's base unit always converts at 1 — a conversion to it is meaningless.");

        if (!await _repository.UnitOfMeasureExistsAsync(request.PackUnitOfMeasureId))
            throw new NotFoundException($"Unit of measure '{request.PackUnitOfMeasureId}' not found.");

        if (await _repository.ConversionExistsAsync(productId, request.PackUnitOfMeasureId))
            throw new InvalidAssignmentException("A conversion for this unit already exists on this product.");

        var conversion = new ProductUnitConversion
        {
            ProductId = productId,
            PackUnitOfMeasureId = request.PackUnitOfMeasureId,
            ConversionFactor = request.ConversionFactor,
            CreatedBy = _currentUser.UserId
        };
        return ToDto(await _repository.CreateUnitConversionAsync(conversion));
    }

    public async Task<IEnumerable<ProductUnitConversionDto>> GetUnitConversionsAsync(Guid productId) =>
        (await _repository.GetUnitConversionsAsync(productId)).Select(ToDto);

    // ---- Mapping ----

    private static UnitOfMeasureDto ToDto(UnitOfMeasure u) => new(u.UnitOfMeasureId, u.Name, u.Symbol, u.IsActive);

    private static ProductDto ToDto(Product p) =>
        new(p.ProductId, p.Sku, p.Name, p.Description, p.CategoryId, p.ItemType, p.TaxClassification, p.BaseUnitOfMeasureId, p.ReorderLevel, p.IsActive);

    private static ProductPriceTierDto ToDto(ProductPriceTier t) =>
        new(t.ProductPriceTierId, t.UnitOfMeasureId, t.PriceType, t.Price, t.EffectiveFrom);

    private static ProductUnitConversionDto ToDto(ProductUnitConversion c) =>
        new(c.ProductUnitConversionId, c.PackUnitOfMeasureId, c.ConversionFactor);

    private static ProductDetailDto ToDetailDto(Product p) =>
        new(p.ProductId, p.Sku, p.Name, p.Description, p.CategoryId, p.ItemType, p.TaxClassification, p.BaseUnitOfMeasureId, p.ReorderLevel, p.IsActive,
            p.UnitConversions.Select(ToDto), p.PriceTiers.Select(ToDto));
}