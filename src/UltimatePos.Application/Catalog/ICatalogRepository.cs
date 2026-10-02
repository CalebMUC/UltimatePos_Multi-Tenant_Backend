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
        Task<Dictionary<string, Guid>> GetCategoryCodeMapAsync();
        Task CreateCategoriesAsync(IEnumerable<Category> categories);

        Task<UnitOfMeasure> CreateUnitOfMeasureAsync(UnitOfMeasure unit);
        Task<IEnumerable<UnitOfMeasure>> GetUnitsOfMeasureAsync();
        Task<Dictionary<string, Guid>> GetUnitSymbolMapAsync();

        Task<int> GetNextSkuNumberAsync(string prefix);
        Task<Product> CreateProductAsync(Product product);
        Task<(IEnumerable<Product> Items, int TotalCount)> GetProductsAsync(int page, int pageSize, Guid? categoryId, ItemType? itemType);
        Task<Product?> GetProductByIdAsync(Guid productId);
        Task<Product> UpdateProductAsync(Guid productId, UpdateProductRequestDto request, Guid? updatedBy);
        Task<HashSet<(string Name, Guid CategoryId)>> GetProductNameCategoryPairsAsync();
        Task CreateProductsAsync(IEnumerable<Product> products, IEnumerable<ProductPriceTier> priceTiers);

        Task<IEnumerable<ProductPriceTier>> GetPriceTiersAsync(Guid productId);
        Task<ProductPriceTier> AddPriceTierAsync(ProductPriceTier tier);

        Task<ProductUnitConversion> CreateUnitConversionAsync(ProductUnitConversion conversion);
        Task<IEnumerable<ProductUnitConversion>> GetUnitConversionsAsync(Guid productId);
        Task<decimal?> GetConversionFactorAsync(Guid productId, Guid unitOfMeasureId);

        Task<IEnumerable<Product>> GetProductsByIdsAsync(IEnumerable<Guid> productIds);
        Task<bool> UnitOfMeasureExistsAsync(Guid unitOfMeasureId);
        Task<bool> ConversionExistsAsync(Guid productId, Guid packUnitOfMeasureId);
    }
}
