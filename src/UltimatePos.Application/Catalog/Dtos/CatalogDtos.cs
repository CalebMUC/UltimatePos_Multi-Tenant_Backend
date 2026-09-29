using UltimatePos.Domain.Enums;
using static UltimatePos.Domain.Enums.PosEnums;

namespace UltimatePos.Application.Catalog.Dtos;

public record CreateCategoryRequestDto(string Name, string Code, Guid? ParentCategoryId);
public record CategoryDto(Guid CategoryId, string Name, string Code, Guid? ParentCategoryId, bool IsActive, IEnumerable<CategoryDto> Subcategories);
public record CategoryImportRowResult(int RowNumber, string Name, string Code, string? ParentCode, bool IsValid, string? Error);
public record CategoryBulkImportResultDto(bool Committed, int TotalRows, int ValidRows, int InvalidRows, IEnumerable<CategoryImportRowResult> Rows);

public record CreateUnitOfMeasureRequestDto(string Name, string Symbol);
public record UnitOfMeasureDto(Guid UnitOfMeasureId, string Name, string Symbol, bool IsActive);

public record CreateProductRequestDto(
    string Name, string? Description, Guid CategoryId, ItemType ItemType,
    TaxClassification TaxClassification, Guid BaseUnitOfMeasureId, decimal ReorderLevel);

public record UpdateProductRequestDto(
    string Name, string? Description, Guid CategoryId, ItemType ItemType,
    TaxClassification TaxClassification, Guid BaseUnitOfMeasureId, decimal ReorderLevel);

public record ProductDto(
    Guid ProductId, string Sku, string Name, string? Description, Guid CategoryId, ItemType ItemType,
    TaxClassification TaxClassification, Guid BaseUnitOfMeasureId, decimal ReorderLevel, bool IsActive);

public record ProductUnitConversionDto(Guid ProductUnitConversionId, Guid PackUnitOfMeasureId, decimal ConversionFactor);
public record ProductPriceTierDto(Guid ProductPriceTierId, Guid UnitOfMeasureId, PriceType PriceType, decimal Price, DateTime EffectiveFrom);
public record AddPriceTierRequestDto(Guid UnitOfMeasureId, PriceType PriceType, decimal Price, DateTime EffectiveFrom);

public record ProductDetailDto(
    Guid ProductId, string Sku, string Name, string? Description, Guid CategoryId, ItemType ItemType,
    TaxClassification TaxClassification, Guid BaseUnitOfMeasureId, decimal ReorderLevel, bool IsActive,
    IEnumerable<ProductUnitConversionDto> UnitConversions, IEnumerable<ProductPriceTierDto> PriceTiers);

public record ProductImportRowResult(int RowNumber, string Name, bool IsValid, string? Error, string? Sku);
public record ProductBulkImportResultDto(bool Committed, int TotalRows, int ValidRows, int InvalidRows, IEnumerable<ProductImportRowResult> Rows);

public record CreateProductUnitConversionRequestDto(Guid PackUnitOfMeasureId, decimal ConversionFactor);