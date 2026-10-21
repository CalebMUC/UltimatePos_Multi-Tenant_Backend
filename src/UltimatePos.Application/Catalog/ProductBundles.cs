using UltimatePos.Application.Common.Interfaces;
using UltimatePos.Application.Inventory;
using UltimatePos.Application.Reports;
using UltimatePos.Domain.Entities;
using UltimatePos.Domain.Exceptions;
using static UltimatePos.Domain.Enums.PosEnums;

namespace UltimatePos.Application.Catalog
{
    /// <summary>One part of a bundle: 20 litres of water is (water, l, 20); one heavy-duty bottle is (bottle, pcs, 1).</summary>
    public record ProductBundleComponentRequestDto(Guid ProductId, Guid UnitOfMeasureId, decimal Quantity);
    /// <param name="WholesalePrice">For customers on the wholesale price list. Left out = they pay the retail price.</param>
    public record CreateProductBundleRequestDto(string Name, string? Code, string? Description, decimal RetailPrice, decimal? WholesalePrice,
        IEnumerable<ProductBundleComponentRequestDto> Components);
    /// <summary>Replaces the bundle's name, prices and parts. Sales already made keep what they were sold at.</summary>
    public record UpdateProductBundleRequestDto(string Name, string? Code, string? Description, decimal RetailPrice, decimal? WholesalePrice,
        IEnumerable<ProductBundleComponentRequestDto> Components);

    /// <param name="QuantityInBaseUnits">Per bundle, in the product's base unit: 20 for a 20L refill held in litres.</param>
    /// <param name="AvailableInBaseUnits">What the branch signed in to can sell of this product now (stock not held for deliveries).</param>
    public record ProductBundleComponentDto(Guid ProductBundleComponentId, Guid ProductId, string ProductName, string Sku, bool ProductIsActive,
        Guid UnitOfMeasureId, string? UnitSymbol, decimal Quantity, decimal QuantityInBaseUnits, string? BaseUnitSymbol, decimal? AvailableInBaseUnits);
    /// <param name="AvailableQuantity">How many whole bundles the branch signed in to can sell now: the scarcest part decides. Only on lists
    /// and single reads made by a user who can see stock.</param>
    public record ProductBundleDto(Guid ProductBundleId, string Name, string? Code, string? Description, decimal RetailPrice, decimal? WholesalePrice,
        bool IsActive, int? AvailableQuantity, IEnumerable<ProductBundleComponentDto> Components, DateTime CreatedAt, DateTime? LastUpdatedAt);

    public interface IProductBundleRepository
    {
        Task CreateAsync(ProductBundle bundle);
        /// <summary>With its components, their products (base unit loaded) and units.</summary>
        Task<ProductBundle?> GetAsync(Guid productBundleId);
        Task<IReadOnlyList<ProductBundle>> GetManyAsync(IEnumerable<Guid> productBundleIds);
        Task<IReadOnlyList<ProductBundle>> ListAsync(bool? isActive, string? search);
        Task<bool> CodeExistsAsync(string code, Guid? exceptBundleId = null);
        /// <summary>Replaces the fields and the component rows in one save.</summary>
        Task UpdateAsync(Guid productBundleId, string name, string? code, string? description, decimal retailPrice, decimal? wholesalePrice,
            IReadOnlyList<ProductBundleComponent> components, Guid? userId);
        Task SetActiveAsync(Guid productBundleId, bool isActive, Guid? userId);
    }

    /// <summary>
    /// Bundles: several stocked products sold together at one price, set up once and rung up as one item. The sale takes each part
    /// from its own stock (SalesService expands a bundle into one line per part and splits the price over them with
    /// <see cref="BundlePricing"/>).
    /// </summary>
    public class ProductBundleService
    {
        private const int MaxNameLength = 200;
        private const int MaxCodeLength = 50;
        private const int MaxDescriptionLength = 1000;
        private const int MaxComponents = 20;
        private const string StockViewPermission = "Stock.View";

        private readonly IProductBundleRepository _repository;
        private readonly ICatalogRepository _catalog;
        private readonly IInventoryRepository _inventory;
        private readonly IBranchContext _branches;
        private readonly ITenantContext _tenant;
        private readonly ICurrentUser _currentUser;

        public ProductBundleService(IProductBundleRepository repository, ICatalogRepository catalog, IInventoryRepository inventory,
            IBranchContext branches, ITenantContext tenant, ICurrentUser currentUser)
        {
            _repository = repository;
            _catalog = catalog;
            _inventory = inventory;
            _branches = branches;
            _tenant = tenant;
            _currentUser = currentUser;
        }

        private static DateOnly Today => ReportPeriod.TodayAt(ReportPeriod.DefaultUtcOffsetMinutes);

        public async Task<ProductBundleDto> CreateAsync(CreateProductBundleRequestDto request)
        {
            _tenant.RequireBusinessId();
            var (name, code, description) = await ValidateFieldsAsync(request.Name, request.Code, request.Description, request.RetailPrice,
                request.WholesalePrice, null);
            var components = await BuildComponentsAsync(request.Components);

            var bundle = new ProductBundle
            {
                ProductBundleId = Guid.NewGuid(),
                Name = name,
                Code = code,
                Description = description,
                RetailPrice = request.RetailPrice,
                WholesalePrice = request.WholesalePrice,
                CreatedBy = _currentUser.UserId,
                Components = components
            };
            foreach (var c in components)
                c.ProductBundleId = bundle.ProductBundleId;

            await _repository.CreateAsync(bundle);
            return await GetAsync(bundle.ProductBundleId);
        }

        public async Task<ProductBundleDto> UpdateAsync(Guid productBundleId, UpdateProductBundleRequestDto request)
        {
            await RequireAsync(productBundleId);
            var (name, code, description) = await ValidateFieldsAsync(request.Name, request.Code, request.Description, request.RetailPrice,
                request.WholesalePrice, productBundleId);
            var components = await BuildComponentsAsync(request.Components);
            foreach (var c in components)
                c.ProductBundleId = productBundleId;

            await _repository.UpdateAsync(productBundleId, name, code, description, request.RetailPrice, request.WholesalePrice, components,
                _currentUser.UserId);
            return await GetAsync(productBundleId);
        }

        public async Task<ProductBundleDto> SetActiveAsync(Guid productBundleId, bool isActive)
        {
            await RequireAsync(productBundleId);
            await _repository.SetActiveAsync(productBundleId, isActive, _currentUser.UserId);
            return await GetAsync(productBundleId);
        }

        public async Task<ProductBundleDto> GetAsync(Guid productBundleId)
        {
            var bundle = await RequireAsync(productBundleId);
            return (await ToDtosAsync(new[] { bundle })).Single();
        }

        public async Task<IReadOnlyList<ProductBundleDto>> ListAsync(bool? isActive, string? search)
        {
            _tenant.RequireBusinessId();
            if (search is { Length: > 100 })
                throw new ValidationException("search", "Search text cannot exceed 100 characters.");
            return await ToDtosAsync(await _repository.ListAsync(isActive, string.IsNullOrWhiteSpace(search) ? null : search.Trim()));
        }

        private async Task<ProductBundle> RequireAsync(Guid productBundleId)
        {
            _tenant.RequireBusinessId();
            return await _repository.GetAsync(productBundleId)
                ?? throw new NotFoundException($"Bundle '{productBundleId}' not found.");
        }

        private async Task<(string Name, string? Code, string? Description)> ValidateFieldsAsync(string? name, string? code, string? description,
            decimal retailPrice, decimal? wholesalePrice, Guid? exceptBundleId)
        {
            var errors = new Dictionary<string, string[]>();
            if (string.IsNullOrWhiteSpace(name))
                errors["name"] = new[] { "A bundle needs a name, e.g. \"20L Heavy-duty bottle + water\"." };
            else if (name.Trim().Length > MaxNameLength)
                errors["name"] = new[] { $"The name is at most {MaxNameLength} characters." };
            var cleanCode = string.IsNullOrWhiteSpace(code) ? null : code.Trim().ToUpperInvariant();
            if (cleanCode is { Length: > MaxCodeLength })
                errors["code"] = new[] { $"The code is at most {MaxCodeLength} characters." };
            else if (cleanCode is not null && await _repository.CodeExistsAsync(cleanCode, exceptBundleId))
                errors["code"] = new[] { $"Another bundle already uses the code '{cleanCode}'." };
            if (description is { Length: > MaxDescriptionLength })
                errors["description"] = new[] { $"The description is at most {MaxDescriptionLength} characters." };
            if (retailPrice <= 0)
                errors["retailPrice"] = new[] { "The retail price must be greater than zero." };
            if (wholesalePrice is <= 0)
                errors["wholesalePrice"] = new[] { "The wholesale price must be greater than zero, or left out to charge the retail price." };
            if (errors.Count > 0)
                throw new ValidationException(errors);
            return (name!.Trim(), cleanCode, string.IsNullOrWhiteSpace(description) ? null : description.Trim());
        }

        /// <summary>Every part must be something the till could sell on its own line: an active finished good, in a unit it has.</summary>
        private async Task<List<ProductBundleComponent>> BuildComponentsAsync(IEnumerable<ProductBundleComponentRequestDto>? given)
        {
            var requested = (given ?? Enumerable.Empty<ProductBundleComponentRequestDto>()).ToList();
            if (requested.Count == 0)
                throw new ValidationException("components", "A bundle needs at least one part, e.g. 20 litres of water and one bottle.");
            if (requested.Count > MaxComponents)
                throw new ValidationException("components", $"A bundle has at most {MaxComponents} parts.");
            if (requested.Any(c => c.Quantity <= 0))
                throw new ValidationException("components", "Each part's quantity must be greater than zero.");
            if (requested.GroupBy(c => (c.ProductId, c.UnitOfMeasureId)).Any(g => g.Count() > 1))
                throw new ValidationException("components", "Each product and unit can appear only once in a bundle — combine the quantities.");

            var products = (await _catalog.GetProductsByIdsAsync(requested.Select(c => c.ProductId))).ToDictionary(p => p.ProductId);
            var result = new List<ProductBundleComponent>();
            foreach (var c in requested)
            {
                if (!products.TryGetValue(c.ProductId, out var product))
                    throw new NotFoundException($"Product '{c.ProductId}' not found.");
                if (!product.IsActive)
                    throw new InvalidAssignmentException($"'{product.Name}' is inactive.");
                if (product.ItemType != ItemType.FinishedGood)
                    throw new InvalidAssignmentException($"'{product.Name}' is a {product.ItemType}: only finished goods can be sold, in a bundle or not.");
                if (product.RequiresPrescription)
                    throw new InvalidAssignmentException($"'{product.Name}' is prescription-only and cannot be sold in a bundle.");
                if (await _catalog.GetConversionFactorAsync(product.ProductId, c.UnitOfMeasureId) is null)
                    throw new InvalidAssignmentException($"'{product.Name}' is not sold in the unit given: use its base unit or one of its pack units.");

                result.Add(new ProductBundleComponent
                {
                    ProductBundleComponentId = Guid.NewGuid(),
                    ProductId = product.ProductId,
                    UnitOfMeasureId = c.UnitOfMeasureId,
                    Quantity = c.Quantity
                });
            }
            return result;
        }

        private async Task<IReadOnlyList<ProductBundleDto>> ToDtosAsync(IReadOnlyCollection<ProductBundle> bundles)
        {
            var factors = new Dictionary<(Guid, Guid), decimal>();
            foreach (var c in bundles.SelectMany(b => b.Components))
                if (!factors.ContainsKey((c.ProductId, c.UnitOfMeasureId)))
                    factors[(c.ProductId, c.UnitOfMeasureId)] = await _catalog.GetConversionFactorAsync(c.ProductId, c.UnitOfMeasureId) ?? 0m;

            // Stock is shown to those who may see it, at the branch they are working in, less what is held for open delivery orders.
            Dictionary<Guid, decimal>? sellable = null;
            if (_currentUser.HasPermission(StockViewPermission) && _branches.ActiveBranchId is { } branchId)
            {
                var productIds = bundles.SelectMany(b => b.Components).Select(c => c.ProductId).Distinct().ToList();
                sellable = await _inventory.GetSellableQuantitiesAsync(productIds, Today);
                foreach (var ((productId, reservedAt), quantity) in await _inventory.GetReservedQuantitiesAsync(productIds))
                    if (reservedAt == branchId)
                        sellable[productId] = sellable.GetValueOrDefault(productId) - quantity;
            }

            return bundles.Select(b =>
            {
                var components = b.Components.OrderBy(c => c.Product?.Name).Select(c =>
                {
                    var perBundle = c.Quantity * factors[(c.ProductId, c.UnitOfMeasureId)];
                    return new ProductBundleComponentDto(c.ProductBundleComponentId, c.ProductId, c.Product?.Name ?? string.Empty,
                        c.Product?.Sku ?? string.Empty, c.Product?.IsActive ?? false, c.UnitOfMeasureId, c.UnitOfMeasure?.Symbol, c.Quantity, perBundle,
                        c.Product?.BaseUnitOfMeasure?.Symbol, sellable?.GetValueOrDefault(c.ProductId));
                }).ToList();
                int? available = sellable is null || components.Count == 0
                    ? null
                    : (int)Math.Max(0m, components.Min(c => c.QuantityInBaseUnits <= 0 ? 0m : Math.Floor((c.AvailableInBaseUnits ?? 0m) / c.QuantityInBaseUnits)));
                return new ProductBundleDto(b.ProductBundleId, b.Name, b.Code, b.Description, b.RetailPrice, b.WholesalePrice, b.IsActive, available,
                    components, b.CreatedAt, b.LastUpdatedAt);
            }).ToList();
        }
    }

    /// <summary>
    /// How a bundle's price is split over its parts, so each part's sale line carries a share of the money (for revenue per product,
    /// VAT per product, and refunds of a single part). The rule, in order:
    ///   1. A part with a price of its own (in the unit and price list used) counts at that price: 20 litres of water at 10 a litre is 200.
    ///   2. Parts without one take what is left, shared equally: the bottle of a 1500 "20L Heavy-duty + water" bundle is 1300. Bottles
    ///      that are never sold empty need no price of their own.
    ///   3. If the priced parts come to more than the bundle (a bundle sold below the price of its parts), the bundle price is shared
    ///      over the priced parts in proportion to their prices, and parts without a price get nothing.
    ///   4. If every part has a price, the bundle price is shared in proportion to them (a pack sold cheaper than its parts gives each
    ///      part the same discount, in percent).
    /// The shares always add up to the bundle price exactly: rounding is put on the largest share.
    /// </summary>
    public static class BundlePricing
    {
        /// <param name="total">The bundle line's value before discounts, rounded to cents.</param>
        /// <param name="ownPrices">Per part, its own value (price x quantity, rounded to cents), or null when it has no price.</param>
        public static IReadOnlyList<decimal> Split(decimal total, IReadOnlyList<decimal?> ownPrices)
        {
            if (ownPrices.Count == 0)
                return Array.Empty<decimal>();
            var priced = ownPrices.Select((p, i) => (p, i)).Where(x => x.p.HasValue).ToList();
            var unpriced = ownPrices.Select((p, i) => (p, i)).Where(x => !x.p.HasValue).Select(x => x.i).ToList();
            var pricedSum = priced.Sum(x => x.p!.Value);

            var shares = new decimal[ownPrices.Count];
            if (unpriced.Count > 0 && pricedSum <= total)
            {
                foreach (var (p, i) in priced)
                    shares[i] = p!.Value;
                var each = Proportional(total - pricedSum, unpriced.Select(_ => 1m).ToList());
                for (var k = 0; k < unpriced.Count; k++)
                    shares[unpriced[k]] = each[k];
                return shares;
            }

            // Every part priced, or the priced parts alone are worth more than the bundle: share in proportion to the prices.
            var weights = ownPrices.Select(p => p ?? 0m).ToList();
            if (weights.Sum() <= 0)
                weights = ownPrices.Select(_ => 1m).ToList();
            return Proportional(total, weights);
        }

        /// <summary>Splits <paramref name="total"/> in proportion to <paramref name="weights"/>, in cents; the rounding remainder goes on the largest.</summary>
        private static IReadOnlyList<decimal> Proportional(decimal total, IReadOnlyList<decimal> weights)
        {
            var sum = weights.Sum();
            var result = weights.Select(w => sum <= 0 ? 0m : Math.Round(total * w / sum, 2, MidpointRounding.AwayFromZero)).ToArray();
            var largest = weights.Select((w, i) => (w, i)).OrderByDescending(x => x.w).ThenBy(x => x.i).First().i;
            result[largest] += total - result.Sum();
            return result;
        }
    }
}
