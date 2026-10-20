using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UltimatePos.Application.Catalog.Dtos;
using UltimatePos.Domain.Entities;
using static UltimatePos.Domain.Enums.PosEnums;

namespace UltimatePos.Application.Catalog
{
    public interface ICatalogRepository
    {
        Task<Category> CreateCategoryAsync(Category category);
        Task<IEnumerable<Category>> GetCategoriesAsync();
        Task<Category?> GetCategoryByIdAsync(Guid categoryId);
        Task<bool> CategoryCodeExistsAsync(string code);
        Task<Category> UpdateCategoryAsync(Guid categoryId, string name, Guid? parentCategoryId, Guid? updatedBy);
        Task<Category> SetCategoryActiveStatusAsync(Guid categoryId, bool isActive, Guid? updatedBy);
        Task<Dictionary<string, Guid>> GetCategoryCodeMapAsync();
        Task CreateCategoriesAsync(IEnumerable<Category> categories);

        Task<UnitOfMeasure> CreateUnitOfMeasureAsync(UnitOfMeasure unit);
        Task<IEnumerable<UnitOfMeasure>> GetUnitsOfMeasureAsync();
        Task<bool> UnitSymbolExistsAsync(string symbol, Guid? exceptUnitId = null);
        Task<UnitOfMeasure> UpdateUnitOfMeasureAsync(Guid unitOfMeasureId, string name, string symbol, Guid? updatedBy);
        Task<UnitOfMeasure> SetUnitOfMeasureActiveStatusAsync(Guid unitOfMeasureId, bool isActive, Guid? updatedBy);
        Task<Dictionary<string, Guid>> GetUnitSymbolMapAsync();

        Task<int> GetNextSkuNumberAsync(string prefix);
        Task<Product> CreateProductAsync(Product product);
        Task<(IEnumerable<Product> Items, int TotalCount)> GetProductsAsync(int page, int pageSize, ProductQuery query);
        Task<Product?> GetProductByIdAsync(Guid productId);
        /// <summary>Exact barcode or SKU match (case-insensitive), for scanners.</summary>
        Task<Product?> GetProductByCodeAsync(string code);
        Task<bool> BarcodeExistsAsync(string barcode, Guid? exceptProductId = null);
        Task<Product> UpdateProductAsync(Guid productId, UpdateProductRequestDto request, Guid? updatedBy);
        Task<Product> SetProductActiveStatusAsync(Guid productId, bool isActive, Guid? updatedBy);
        Task<decimal> GetQuantityOnHandAsync(Guid productId);
        Task<HashSet<(string Name, Guid CategoryId)>> GetProductNameCategoryPairsAsync();
        Task CreateProductsAsync(IEnumerable<Product> products, IEnumerable<ProductPriceTier> priceTiers);

        Task<IEnumerable<ProductPriceTier>> GetPriceTiersAsync(Guid productId);
        Task<ProductPriceTier> AddPriceTierAsync(ProductPriceTier tier);

        Task<ProductUnitConversion> CreateUnitConversionAsync(ProductUnitConversion conversion);
        Task<IEnumerable<ProductUnitConversion>> GetUnitConversionsAsync(Guid productId);
        Task<ProductUnitConversion> UpdateUnitConversionAsync(Guid productId, Guid conversionId, decimal? conversionFactor, bool? isActive, Guid? updatedBy);
        /// <summary>Null clears it. Returns the conversion with its pack unit and container loaded.</summary>
        Task<ProductUnitConversion> SetUnitConversionContainerAsync(Guid productId, Guid conversionId, Guid? containerProductId, Guid? updatedBy);
        Task<decimal?> GetConversionFactorAsync(Guid productId, Guid unitOfMeasureId);

        Task<decimal?> GetActivePriceAsync(Guid productId, Guid unitOfMeasureId, PriceType priceType);

        Task<IEnumerable<Product>> GetProductsByIdsAsync(IEnumerable<Guid> productIds);
        Task<bool> UnitOfMeasureExistsAsync(Guid unitOfMeasureId);
        Task<bool> ConversionExistsAsync(Guid productId, Guid packUnitOfMeasureId);
    }
}
