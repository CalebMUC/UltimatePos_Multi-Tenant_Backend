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
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.Name))
            errors["name"] = new[] { "Name is required." };
        if (string.IsNullOrWhiteSpace(request.Code))
            errors["code"] = new[] { "Code is required." };
        if (errors.Count > 0)
            throw new ValidationException(errors);

        var code = request.Code.Trim().ToUpperInvariant();
        if (await _repository.CategoryCodeExistsAsync(code))
            throw new ValidationException("code", $"Category code '{code}' already exists.");
        if (request.ParentCategoryId is { } parentId && await _repository.GetCategoryByIdAsync(parentId) is null)
            throw new ValidationException("parentCategoryId", "Parent category not found.");

        var category = new Category
        {
            Name = request.Name.Trim(),
            Code = code,
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

    public async Task<CategoryDto> UpdateCategoryAsync(Guid categoryId, UpdateCategoryRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new ValidationException("name", "Name is required.");

        var all = (await _repository.GetCategoriesAsync()).ToList();
        if (all.All(c => c.CategoryId != categoryId))
            throw new NotFoundException($"Category '{categoryId}' not found.");

        if (request.ParentCategoryId is { } parentId)
        {
            if (all.All(c => c.CategoryId != parentId))
                throw new ValidationException("parentCategoryId", "Parent category not found.");
            // Walk up from the new parent: reaching this category means the move would create a loop.
            for (Guid? cursor = parentId; cursor is { } id; cursor = all.First(c => c.CategoryId == id).ParentCategoryId)
                if (id == categoryId)
                    throw new ValidationException("parentCategoryId", "A category cannot be moved under itself or one of its subcategories.");
        }

        var updated = await _repository.UpdateCategoryAsync(categoryId, request.Name.Trim(), request.ParentCategoryId, _currentUser.UserId);
        return new CategoryDto(updated.CategoryId, updated.Name, updated.Code, updated.ParentCategoryId, updated.IsActive, Enumerable.Empty<CategoryDto>());
    }

    public async Task<CategoryDto> SetCategoryActiveStatusAsync(Guid categoryId, bool isActive)
    {
        var updated = await _repository.SetCategoryActiveStatusAsync(categoryId, isActive, _currentUser.UserId);
        return new CategoryDto(updated.CategoryId, updated.Name, updated.Code, updated.ParentCategoryId, updated.IsActive, Enumerable.Empty<CategoryDto>());
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
        await ValidateUnitAsync(request.Name, request.Symbol, null);
        var unit = new UnitOfMeasure { Name = request.Name.Trim(), Symbol = request.Symbol.Trim(), CreatedBy = _currentUser.UserId };
        return ToDto(await _repository.CreateUnitOfMeasureAsync(unit));
    }

    public async Task<UnitOfMeasureDto> UpdateUnitOfMeasureAsync(Guid unitOfMeasureId, UpdateUnitOfMeasureRequestDto request)
    {
        await ValidateUnitAsync(request.Name, request.Symbol, unitOfMeasureId);
        return ToDto(await _repository.UpdateUnitOfMeasureAsync(unitOfMeasureId, request.Name.Trim(), request.Symbol.Trim(), _currentUser.UserId));
    }

    public async Task<UnitOfMeasureDto> SetUnitOfMeasureActiveStatusAsync(Guid unitOfMeasureId, bool isActive) =>
        ToDto(await _repository.SetUnitOfMeasureActiveStatusAsync(unitOfMeasureId, isActive, _currentUser.UserId));

    private async Task ValidateUnitAsync(string name, string symbol, Guid? unitOfMeasureId)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(name))
            errors["name"] = new[] { "Name is required." };
        if (string.IsNullOrWhiteSpace(symbol))
            errors["symbol"] = new[] { "Symbol is required." };
        else if (await _repository.UnitSymbolExistsAsync(symbol.Trim(), unitOfMeasureId))
            errors["symbol"] = new[] { $"A unit with symbol '{symbol.Trim()}' already exists." };
        if (errors.Count > 0)
            throw new ValidationException(errors);
    }

    public async Task<IEnumerable<UnitOfMeasureDto>> GetUnitsOfMeasureAsync() =>
        (await _repository.GetUnitsOfMeasureAsync()).Select(ToDto);

    // ---- Products ----

    public async Task<ProductDto> CreateProductAsync(CreateProductRequestDto request)
    {
        var barcode = await ValidateProductAsync(request.Name, request.BaseUnitOfMeasureId, request.ReorderLevel, request.Barcode, null);
        var category = await _repository.GetCategoryByIdAsync(request.CategoryId)
            ?? throw new ValidationException("categoryId", $"Category '{request.CategoryId}' not found.");

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
            Barcode = barcode,
            IsBatchTracked = request.IsBatchTracked,
            RequiresPrescription = request.RequiresPrescription,
            GenericName = Clean(request.GenericName),
            Strength = Clean(request.Strength),
            DosageForm = Clean(request.DosageForm),
            Manufacturer = Clean(request.Manufacturer),
            CreatedBy = _currentUser.UserId
        };

        var created = await _repository.CreateProductAsync(product);
        return ToDto((await _repository.GetProductByIdAsync(created.ProductId))!);
    }

    /// <summary>Checks shared by create and update. Returns the cleaned barcode (null when none).</summary>
    private async Task<string?> ValidateProductAsync(string name, Guid baseUnitOfMeasureId, decimal reorderLevel, string? barcode, Guid? productId)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(name))
            errors["name"] = new[] { "Name is required." };
        if (reorderLevel < 0)
            errors["reorderLevel"] = new[] { "Reorder level cannot be negative." };
        if (!await _repository.UnitOfMeasureExistsAsync(baseUnitOfMeasureId))
            errors["baseUnitOfMeasureId"] = new[] { "Base unit of measure not found." };

        var cleaned = Clean(barcode);
        if (cleaned is not null)
        {
            if (cleaned.Length > 64 || cleaned.Any(char.IsWhiteSpace))
                errors["barcode"] = new[] { "A barcode is at most 64 characters with no spaces." };
            else if (await _repository.BarcodeExistsAsync(cleaned, productId))
                errors["barcode"] = new[] { $"Barcode '{cleaned}' is already on another product." };
        }

        if (errors.Count > 0)
            throw new ValidationException(errors);
        return cleaned;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string ItemTypeCode(ItemType itemType) => itemType switch
    {
        ItemType.RawMaterial => "RM",
        ItemType.Packaging => "PKG",
        ItemType.FinishedGood => "FG",
        _ => "GEN"
    };

    public async Task<PagedResult<ProductDto>> GetProductsAsync(int page, int pageSize, ProductQuery query)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        if (query.Search is { Length: > 100 })
            throw new ValidationException("search", "Search text cannot exceed 100 characters.");
        if (query.SortBy is { } sortBy && !ProductSortFields.Contains(sortBy, StringComparer.OrdinalIgnoreCase))
            throw new ValidationException("sortBy", $"sortBy must be one of: {string.Join(", ", ProductSortFields)}.");
        if (query.SortDirection is { } dir && !dir.Equals("asc", StringComparison.OrdinalIgnoreCase) && !dir.Equals("desc", StringComparison.OrdinalIgnoreCase))
            throw new ValidationException("sortDirection", "sortDirection must be asc or desc.");

        var (items, totalCount) = await _repository.GetProductsAsync(page, pageSize, query);
        return new PagedResult<ProductDto>(items.Select(ToDto), page, pageSize, totalCount);
    }

    public static readonly string[] ProductSortFields = { "name", "sku", "createdAt", "category" };

    /// <summary>Scanner lookup: an exact barcode or SKU. Inactive products are found too, so the till can say why it won't sell.</summary>
    public async Task<ProductDetailDto> GetProductByCodeAsync(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ValidationException("code", "A barcode or SKU is required.");
        var product = await _repository.GetProductByCodeAsync(code.Trim())
            ?? throw new NotFoundException($"No product has barcode or SKU '{code.Trim()}'.");
        return ToDetailDto(product);
    }

    public async Task<ProductDto> SetProductActiveStatusAsync(Guid productId, bool isActive) =>
        ToDto(await _repository.SetProductActiveStatusAsync(productId, isActive, _currentUser.UserId));

    public async Task<ProductDetailDto> GetProductByIdAsync(Guid productId)
    {
        var product = await _repository.GetProductByIdAsync(productId)
            ?? throw new NotFoundException($"Product '{productId}' not found.");
        return ToDetailDto(product);
    }

    public async Task<ProductDto> UpdateProductAsync(Guid productId, UpdateProductRequestDto request)
    {
        var existing = await _repository.GetProductByIdAsync(productId)
            ?? throw new NotFoundException($"Product '{productId}' not found.");
        var barcode = await ValidateProductAsync(request.Name, request.BaseUnitOfMeasureId, request.ReorderLevel, request.Barcode, productId);
        if (await _repository.GetCategoryByIdAsync(request.CategoryId) is null)
            throw new ValidationException("categoryId", $"Category '{request.CategoryId}' not found.");

        // Batch tracking can only change on an empty shelf: stock already held is either in batches or not, and switching
        // mid-stream would leave the batch totals and the product total disagreeing.
        if (request.IsBatchTracked != existing.IsBatchTracked && await _repository.GetQuantityOnHandAsync(productId) != 0)
            throw new InvalidAssignmentException(
                "Batch tracking can only be switched on or off while the product has no stock. Adjust stock to zero, change the setting, " +
                "then enter the stock again" + (request.IsBatchTracked ? " batch by batch." : "."));

        await _repository.UpdateProductAsync(productId, request with
        {
            Name = request.Name.Trim(),
            Barcode = barcode,
            GenericName = Clean(request.GenericName),
            Strength = Clean(request.Strength),
            DosageForm = Clean(request.DosageForm),
            Manufacturer = Clean(request.Manufacturer)
        }, _currentUser.UserId);
        return ToDto((await _repository.GetProductByIdAsync(productId))!);
    }

    public async Task<IEnumerable<ProductPriceTierDto>> GetPriceTiersAsync(Guid productId) =>
        (await _repository.GetPriceTiersAsync(productId)).Select(ToDto);

    public async Task<ProductPriceTierDto> AddPriceTierAsync(Guid productId, AddPriceTierRequestDto request)
    {
        if (await _repository.GetProductByIdAsync(productId) is null)
            throw new NotFoundException($"Product '{productId}' not found.");
        var errors = new Dictionary<string, string[]>();
        if (request.Price <= 0)
            errors["price"] = new[] { "Price must be greater than zero." };
        if (!Enum.IsDefined(request.PriceType))
            errors["priceType"] = new[] { "Price type must be Wholesale or Retail." };
        if (await _repository.GetConversionFactorAsync(productId, request.UnitOfMeasureId) is null)
            errors["unitOfMeasureId"] = new[] { "The unit must be the product's base unit or one of its active conversions." };
        if (errors.Count > 0)
            throw new ValidationException(errors);

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

        if (request.ContainerProductId is { } containerId)
            await RequireContainerAsync(productId, containerId);

        var conversion = new ProductUnitConversion
        {
            ProductId = productId,
            PackUnitOfMeasureId = request.PackUnitOfMeasureId,
            ConversionFactor = request.ConversionFactor,
            ContainerProductId = request.ContainerProductId,
            CreatedBy = _currentUser.UserId
        };
        return ToDto(await _repository.CreateUnitConversionAsync(conversion));
    }

    public async Task<IEnumerable<ProductUnitConversionDto>> GetUnitConversionsAsync(Guid productId) =>
        (await _repository.GetUnitConversionsAsync(productId)).Select(ToDto);

    public async Task<ProductUnitConversionDto> UpdateUnitConversionAsync(Guid productId, Guid conversionId, UpdateProductUnitConversionRequestDto request)
    {
        if (request.ConversionFactor <= 0)
            throw new ValidationException("conversionFactor", "Conversion factor must be greater than zero.");
        return ToDto(await _repository.UpdateUnitConversionAsync(productId, conversionId, request.ConversionFactor, null, _currentUser.UserId));
    }

    public async Task<ProductUnitConversionDto> SetUnitConversionActiveStatusAsync(Guid productId, Guid conversionId, bool isActive) =>
        ToDto(await _repository.UpdateUnitConversionAsync(productId, conversionId, null, isActive, _currentUser.UserId));

    /// <summary>The container sold with one of this pack when the customer has none of their own. Null clears it.</summary>
    public async Task<ProductUnitConversionDto> SetUnitConversionContainerAsync(Guid productId, Guid conversionId, SetConversionContainerRequestDto request)
    {
        if (request.ContainerProductId is { } containerId)
            await RequireContainerAsync(productId, containerId);
        return ToDto(await _repository.SetUnitConversionContainerAsync(productId, conversionId, request.ContainerProductId, _currentUser.UserId));
    }

    /// <summary>
    /// A container is another active finished good of this business (the query filter keeps other businesses' products out), sold
    /// in its own base unit. It is never the product itself, and never batch-tracked or prescription-only.
    /// </summary>
    private async Task RequireContainerAsync(Guid productId, Guid containerProductId)
    {
        if (containerProductId == productId)
            throw new ValidationException("containerProductId", "A product cannot be its own container.");
        var container = await _repository.GetProductByIdAsync(containerProductId)
            ?? throw new ValidationException("containerProductId", "That container product was not found.");
        if (!container.IsActive)
            throw new ValidationException("containerProductId", $"'{container.Name}' is inactive.");
        if (container.ItemType != ItemType.FinishedGood)
            throw new ValidationException("containerProductId", $"'{container.Name}' is not a finished good, so it cannot be sold.");
        if (container.IsBatchTracked || container.RequiresPrescription)
            throw new ValidationException("containerProductId", $"'{container.Name}' is batch-tracked or prescription-only: it cannot be added to a sale automatically.");
    }

    // ---- Mapping ----

    private static UnitOfMeasureDto ToDto(UnitOfMeasure u) => new(u.UnitOfMeasureId, u.Name, u.Symbol, u.IsActive);

    private static ProductDto ToDto(Product p) =>
        new(p.ProductId, p.Sku, p.Name, p.Description, p.CategoryId, p.ItemType, p.TaxClassification, p.BaseUnitOfMeasureId, p.ReorderLevel, p.IsActive,
            p.Barcode, p.IsBatchTracked, p.RequiresPrescription, p.GenericName, p.Strength, p.DosageForm, p.Manufacturer,
            p.Category?.Name, p.BaseUnitOfMeasure?.Symbol);

    private static ProductPriceTierDto ToDto(ProductPriceTier t) =>
        new(t.ProductPriceTierId, t.UnitOfMeasureId, t.PriceType, t.Price, t.EffectiveFrom);

    private static ProductUnitConversionDto ToDto(ProductUnitConversion c) =>
        new(c.ProductUnitConversionId, c.PackUnitOfMeasureId, c.ConversionFactor, c.IsActive, c.PackUnitOfMeasure?.Symbol,
            c.ContainerProductId, c.ContainerProduct?.Name, c.ContainerProduct?.Sku, c.ContainerProduct?.BaseUnitOfMeasureId,
            c.ContainerProduct?.BaseUnitOfMeasure?.Symbol, c.ContainerProduct?.IsActive);

    private static ProductDetailDto ToDetailDto(Product p) =>
        new(p.ProductId, p.Sku, p.Name, p.Description, p.CategoryId, p.ItemType, p.TaxClassification, p.BaseUnitOfMeasureId, p.ReorderLevel, p.IsActive,
            p.UnitConversions.Select(ToDto), p.PriceTiers.Select(ToDto),
            p.Barcode, p.IsBatchTracked, p.RequiresPrescription, p.GenericName, p.Strength, p.DosageForm, p.Manufacturer,
            p.Category?.Name, p.BaseUnitOfMeasure?.Symbol);
}