using UltimatePos.Domain.Enums;
using static UltimatePos.Domain.Enums.PosEnums;

namespace UltimatePos.Application.Catalog.Dtos;

public record CreateCategoryRequestDto(string Name, string Code, Guid? ParentCategoryId);
/// <summary>The code is fixed once created: it is baked into the SKUs of the category's products.</summary>
public record UpdateCategoryRequestDto(string Name, Guid? ParentCategoryId);
public record CategoryDto(Guid CategoryId, string Name, string Code, Guid? ParentCategoryId, bool IsActive, IEnumerable<CategoryDto> Subcategories);
public record CategoryImportRowResult(int RowNumber, string Name, string Code, string? ParentCode, bool IsValid, string? Error);
public record CategoryBulkImportResultDto(bool Committed, int TotalRows, int ValidRows, int InvalidRows, IEnumerable<CategoryImportRowResult> Rows);

public record CreateUnitOfMeasureRequestDto(string Name, string Symbol);
public record UpdateUnitOfMeasureRequestDto(string Name, string Symbol);
public record UnitOfMeasureDto(Guid UnitOfMeasureId, string Name, string Symbol, bool IsActive);

public record CreateBrandRequestDto(string Name);
public record UpdateBrandRequestDto(string Name);
/// <param name="ProductCount">Products of this brand, active or not.</param>
public record BrandDto(Guid BrandId, string Name, bool IsActive, int ProductCount = 0);

/// <param name="Barcode">Manufacturer's barcode as printed on the pack. Optional; unique within the business.</param>
/// <param name="IsBatchTracked">Hold stock in batches with expiry dates and sell first-expiry-first-out. Use for medicines.</param>
/// <param name="RequiresPrescription">Prescription-only: a sale must carry prescription details.</param>
/// <param name="BrandId">The brand it is sold under (an active brand of this business). Optional.</param>
/// <param name="RetailPrice">Price of one base unit to walk-in and retail customers, in force from now. Needs Products.ManagePricing.</param>
/// <param name="WholesalePrice">Price of one base unit to wholesale customers, in force from now. Needs Products.ManagePricing.</param>
/// <param name="Packs">Packs it also comes in (a bale of 12), each with its own prices. Needs Products.ManageUnitConversions.</param>
/// <remarks>The product, its prices and its packs are saved together: if any of them is refused, nothing is created.</remarks>
public record CreateProductRequestDto(
    string Name, string? Description, Guid CategoryId, ItemType ItemType,
    TaxClassification TaxClassification, Guid BaseUnitOfMeasureId, decimal ReorderLevel,
    string? Barcode = null, bool IsBatchTracked = false, bool RequiresPrescription = false,
    string? GenericName = null, string? Strength = null, string? DosageForm = null, string? Manufacturer = null,
    Guid? BrandId = null, decimal? RetailPrice = null, decimal? WholesalePrice = null, IEnumerable<NewProductPackDto>? Packs = null);
/// <summary>A pack set up with a new product: how many base units it holds, and optionally what it sells for.</summary>
public record NewProductPackDto(Guid PackUnitOfMeasureId, decimal ConversionFactor, decimal? RetailPrice = null, decimal? WholesalePrice = null);
/// <summary>Full replace. IsBatchTracked cannot be switched off while the product still has stock in batches.</summary>
/// <param name="BrandId">Sets the brand. Null keeps the current brand, so a client that does not know brands never clears one.</param>
/// <param name="RemoveBrand">True clears the brand (BrandId must then be null).</param>
public record UpdateProductRequestDto(
    string Name, string? Description, Guid CategoryId, ItemType ItemType,
    TaxClassification TaxClassification, Guid BaseUnitOfMeasureId, decimal ReorderLevel,
    string? Barcode = null, bool IsBatchTracked = false, bool RequiresPrescription = false,
    string? GenericName = null, string? Strength = null, string? DosageForm = null, string? Manufacturer = null,
    Guid? BrandId = null, bool RemoveBrand = false);
public record ProductDto(
    Guid ProductId, string Sku, string Name, string? Description, Guid CategoryId, ItemType ItemType,
    TaxClassification TaxClassification, Guid BaseUnitOfMeasureId, decimal ReorderLevel, bool IsActive,
    string? Barcode = null, bool IsBatchTracked = false, bool RequiresPrescription = false,
    string? GenericName = null, string? Strength = null, string? DosageForm = null, string? Manufacturer = null,
    string? CategoryName = null, string? BaseUnitSymbol = null, Guid? BrandId = null, string? BrandName = null);
/// <param name="ContainerProductId">Sold with one of this pack when the customer has no container (see ProductUnitConversion).
/// The Container* fields describe it, in its base unit, so a till can add the line without another call.</param>
public record ProductUnitConversionDto(Guid ProductUnitConversionId, Guid PackUnitOfMeasureId, decimal ConversionFactor,
    bool IsActive = true, string? PackUnitSymbol = null,
    Guid? ContainerProductId = null, string? ContainerProductName = null, string? ContainerSku = null,
    Guid? ContainerUnitOfMeasureId = null, string? ContainerUnitSymbol = null, bool? ContainerIsActive = null);
public record ProductPriceTierDto(Guid ProductPriceTierId, Guid UnitOfMeasureId, PriceType PriceType, decimal Price, DateTime EffectiveFrom);
public record AddPriceTierRequestDto(Guid UnitOfMeasureId, PriceType PriceType, decimal Price, DateTime EffectiveFrom);
public record ProductDetailDto(
    Guid ProductId, string Sku, string Name, string? Description, Guid CategoryId, ItemType ItemType,
    TaxClassification TaxClassification, Guid BaseUnitOfMeasureId, decimal ReorderLevel, bool IsActive,
    IEnumerable<ProductUnitConversionDto> UnitConversions, IEnumerable<ProductPriceTierDto> PriceTiers,
    string? Barcode = null, bool IsBatchTracked = false, bool RequiresPrescription = false,
    string? GenericName = null, string? Strength = null, string? DosageForm = null, string? Manufacturer = null,
    string? CategoryName = null, string? BaseUnitSymbol = null, Guid? BrandId = null, string? BrandName = null);
public record ProductImportRowResult(int RowNumber, string Name, bool IsValid, string? Error, string? Sku);
public record ProductBulkImportResultDto(bool Committed, int TotalRows, int ValidRows, int InvalidRows, IEnumerable<ProductImportRowResult> Rows);
/// <param name="ContainerProductId">Optional: the container sold with this pack when the customer has none.</param>
public record CreateProductUnitConversionRequestDto(Guid PackUnitOfMeasureId, decimal ConversionFactor, Guid? ContainerProductId = null);
/// <summary>Sets (or, with null, clears) the container sold with one of this pack. A request of its own, so the factor update stays as it was.</summary>
public record SetConversionContainerRequestDto(Guid? ContainerProductId);
/// <summary>Changes how many base units the pack holds. Past documents keep the base quantities they were recorded with.</summary>
public record UpdateProductUnitConversionRequestDto(decimal ConversionFactor);

/// <summary>Filters and ordering for the product list. Search matches name, SKU, barcode, generic name and brand.</summary>
public record ProductQuery(Guid? CategoryId = null, ItemType? ItemType = null, string? Search = null, bool? IsActive = null,
    bool? IsBatchTracked = null, bool? RequiresPrescription = null, string? SortBy = null, string? SortDirection = null,
    Guid? BrandId = null);
