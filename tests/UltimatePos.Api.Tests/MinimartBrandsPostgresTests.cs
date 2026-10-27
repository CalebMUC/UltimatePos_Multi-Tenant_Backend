using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UltimatePos.Application.Business.Dtos;
using UltimatePos.Application.Catalog;
using UltimatePos.Application.Catalog.Dtos;
using UltimatePos.Application.Inventory;
using UltimatePos.Application.Inventory.Dtos;
using UltimatePos.Application.Platform;
using UltimatePos.Application.Sales;
using UltimatePos.Application.Sales.Dtos;
using UltimatePos.Domain.Entities;
using UltimatePos.Domain.Exceptions;
using UltimatePos.Infrastructure.Persistence;
using static UltimatePos.Domain.Enums.PosEnums;

namespace Ultimate.Api.Tests;

/// <summary>
/// A minimart's maize flour shelf: the same item (Maize Flour 2kg) from several brands — Soko, Pembe, Jogoo — told apart by brand.
/// The minimart is not VAT-registered, so nothing it sells carries VAT whatever the product's tax class. Products are created with
/// their prices and their bale (12 packets) in one step. Prices are test data.
/// </summary>
public class MinimartBrandsPostgresTests : IClassFixture<MultiBusinessDatabase>
{
    private const string MinimartKraPin = "P051000021M";

    private readonly MultiBusinessDatabase _db;
    public MinimartBrandsPostgresTests(MultiBusinessDatabase db) => _db = db;

    private Task<T> Run<T>(Func<IServiceProvider, Task<T>> call) => _db.RunAsync(call);

    private async Task SignInToMinimartAsync(params string[] permissions)
    {
        var minimartId = await Run(sp => sp.GetRequiredService<UltimatePosDbContext>().Businesses.IgnoreQueryFilters()
            .Where(b => b.KraPin == MinimartKraPin).Select(b => (Guid?)b.BusinessId).FirstOrDefaultAsync());
        if (minimartId is null)
        {
            _db.Caller.UserId = _db.SuperUserId;
            _db.Caller.BusinessId = null;
            _db.Caller.Permissions = null;
            var registered = await Run(sp => sp.GetRequiredService<PlatformService>().RegisterBusinessAsync(new RegisterBusinessRequestDto(
                "Test Minimart", null, BusinessType.Retailer, null, null, MinimartKraPin, null, "Nairobi", null, null, null, null,
                IsVatRegistered: false, Owner: new BusinessOwnerRequestDto("admin", DbSeeder.DevAdminEmail, null), Sector: BusinessSector.Minimart)));
            minimartId = registered.Business.BusinessId;
        }
        if (!_db.HeadOffice.ContainsKey(minimartId.Value))
            _db.HeadOffice[minimartId.Value] = await Run(sp => sp.GetRequiredService<UltimatePosDbContext>().Branches.IgnoreQueryFilters()
                .Where(b => b.BusinessId == minimartId && b.BranchNumber == 0).Select(b => b.BranchId).SingleAsync());
        _db.SignInTo(minimartId.Value, permissions);
    }

    private static string Unique(string prefix) => prefix + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

    /// <summary>Staples &gt; Maize Flour, the way the onboarding guide sets it up. Codes are unique per test.</summary>
    private Task<(CategoryDto Flour, Guid Pieces)> MaizeFlourShelfAsync() => Run(async sp =>
    {
        var catalog = sp.GetRequiredService<CatalogService>();
        var staples = await catalog.CreateCategoryAsync(new CreateCategoryRequestDto("Staples & Dry Food", Unique("DRY"), null));
        var flour = await catalog.CreateCategoryAsync(new CreateCategoryRequestDto("Maize Flour", Unique("FLR"), staples.CategoryId));
        var pieces = (await catalog.GetUnitsOfMeasureAsync()).Single(u => u.Symbol == "pcs").UnitOfMeasureId;
        return (flour, pieces);
    });

    private Task<BrandDto> BrandAsync(string name) =>
        Run(sp => sp.GetRequiredService<CatalogService>().CreateBrandAsync(new CreateBrandRequestDto(name)));

    private Task<ProductDto> FlourAsync(string name, Guid categoryId, Guid pieces, Guid? brandId) =>
        Run(sp => sp.GetRequiredService<CatalogService>().CreateProductAsync(new CreateProductRequestDto(
            name, null, categoryId, ItemType.FinishedGood, TaxClassification.Standard, pieces, 10, BrandId: brandId)));

    private static UpdateProductRequestDto Edit(ProductDto p, Guid? brandId = null, bool removeBrand = false) =>
        new(p.Name, p.Description, p.CategoryId, p.ItemType, p.TaxClassification, p.BaseUnitOfMeasureId, p.ReorderLevel,
            p.Barcode, BrandId: brandId, RemoveBrand: removeBrand);

    [PostgresFact]
    public async Task Maize_flour_from_several_brands_is_told_apart_and_filtered_by_brand()
    {
        await SignInToMinimartAsync();
        var (flour, pieces) = await MaizeFlourShelfAsync();
        var soko = await BrandAsync(Unique("Soko "));
        var pembe = await BrandAsync(Unique("Pembe "));

        // One brand name per business, whatever the case; a name is required.
        await Assert.ThrowsAsync<ValidationException>(() => BrandAsync(soko.Name.ToLowerInvariant()));
        await Assert.ThrowsAsync<ValidationException>(() => BrandAsync("  "));

        var soko2 = await FlourAsync(Unique("Soko Maize Meal 2kg "), flour.CategoryId, pieces, soko.BrandId);
        var soko1 = await FlourAsync(Unique("Soko Maize Meal 1kg "), flour.CategoryId, pieces, soko.BrandId);
        var pembe2 = await FlourAsync(Unique("Pembe Maize Flour 2kg "), flour.CategoryId, pieces, pembe.BrandId);
        var unbranded = await FlourAsync(Unique("Posho Mill Flour 1kg "), flour.CategoryId, pieces, null);

        Assert.Equal(soko.BrandId, soko2.BrandId);
        Assert.Equal(soko.Name, soko2.BrandName);
        Assert.Null(unbranded.BrandId);
        Assert.Equal(pembe.Name, (await Run(sp => sp.GetRequiredService<CatalogService>().GetProductByIdAsync(pembe2.ProductId))).BrandName);

        // Filter by brand within the category; search finds a product by its brand's name.
        var sokoOnly = await Run(sp => sp.GetRequiredService<CatalogService>().GetProductsAsync(1, 50,
            new ProductQuery(CategoryId: flour.CategoryId, BrandId: soko.BrandId)));
        Assert.Equal(new[] { soko1.ProductId, soko2.ProductId }.OrderBy(i => i), sokoOnly.Items.Select(p => p.ProductId).OrderBy(i => i));
        var byBrandName = await Run(sp => sp.GetRequiredService<CatalogService>().GetProductsAsync(1, 50,
            new ProductQuery(CategoryId: flour.CategoryId, Search: pembe.Name)));
        Assert.Equal(pembe2.ProductId, Assert.Single(byBrandName.Items).ProductId);

        var brands = (await Run(sp => sp.GetRequiredService<CatalogService>().GetBrandsAsync(null, null))).ToList();
        Assert.Equal(2, brands.Single(b => b.BrandId == soko.BrandId).ProductCount);
        Assert.Equal(1, brands.Single(b => b.BrandId == pembe.BrandId).ProductCount);
    }

    [PostgresFact]
    public async Task Editing_a_product_keeps_its_brand_unless_told_otherwise()
    {
        await SignInToMinimartAsync();
        var (flour, pieces) = await MaizeFlourShelfAsync();
        var soko = await BrandAsync(Unique("Soko "));
        var pembe = await BrandAsync(Unique("Pembe "));
        var product = await FlourAsync(Unique("Maize Meal 2kg "), flour.CategoryId, pieces, soko.BrandId);
        Task<ProductDto> Update(UpdateProductRequestDto request) =>
            Run(sp => sp.GetRequiredService<CatalogService>().UpdateProductAsync(product.ProductId, request));

        // A client that knows nothing of brands sends no brandId: the brand stays.
        Assert.Equal(soko.BrandId, (await Update(Edit(product) with { ReorderLevel = 20 })).BrandId);
        Assert.Equal(pembe.BrandId, (await Update(Edit(product, pembe.BrandId))).BrandId);
        await Assert.ThrowsAsync<ValidationException>(() => Update(Edit(product, soko.BrandId, removeBrand: true)));
        await Assert.ThrowsAsync<ValidationException>(() => Update(Edit(product, Guid.NewGuid())));
        Assert.Null((await Update(Edit(product, removeBrand: true))).BrandId);
    }

    [PostgresFact]
    public async Task A_deactivated_brand_stays_on_its_products_but_is_not_given_to_new_ones_and_a_rename_shows_everywhere()
    {
        await SignInToMinimartAsync();
        var (flour, pieces) = await MaizeFlourShelfAsync();
        var pembe = await BrandAsync(Unique("Pembe "));
        var product = await FlourAsync(Unique("Pembe Maize Flour 2kg "), flour.CategoryId, pieces, pembe.BrandId);

        var off = await Run(sp => sp.GetRequiredService<CatalogService>().SetBrandActiveStatusAsync(pembe.BrandId, false));
        Assert.False(off.IsActive);
        Assert.Equal(1, off.ProductCount);
        await Assert.ThrowsAsync<ValidationException>(() => FlourAsync(Unique("Pembe Maize Flour 1kg "), flour.CategoryId, pieces, pembe.BrandId));
        // Saving the product that already has it is fine.
        var kept = await Run(sp => sp.GetRequiredService<CatalogService>().UpdateProductAsync(product.ProductId, Edit(product, pembe.BrandId)));
        Assert.Equal(pembe.BrandId, kept.BrandId);
        Assert.DoesNotContain(await Run(sp => sp.GetRequiredService<CatalogService>().GetBrandsAsync(null, true)), b => b.BrandId == pembe.BrandId);

        await Run(sp => sp.GetRequiredService<CatalogService>().SetBrandActiveStatusAsync(pembe.BrandId, true));
        var renamed = Unique("Pembe Gold ");
        await Run(sp => sp.GetRequiredService<CatalogService>().UpdateBrandAsync(pembe.BrandId, new UpdateBrandRequestDto(renamed)));
        Assert.Equal(renamed, (await Run(sp => sp.GetRequiredService<CatalogService>().GetProductByIdAsync(product.ProductId))).BrandName);
    }

    [PostgresFact]
    public async Task The_bulk_upload_takes_a_brand_column_and_creates_each_new_brand_once()
    {
        await SignInToMinimartAsync();
        var (flour, _) = await MaizeFlourShelfAsync();
        var soko = await BrandAsync(Unique("Soko "));
        var retired = await BrandAsync(Unique("Retired "));
        await Run(sp => sp.GetRequiredService<CatalogService>().SetBrandActiveStatusAsync(retired.BrandId, false));
        var jogoo = Unique("Jogoo ");

        Stream Sheet(params (string Name, string? Brand)[] rows)
        {
            using var workbook = new XLWorkbook();
            var sheet = workbook.Worksheets.Add("Products");
            string[] headers = { "Name", "Category Code", "Item Type", "Tax Classification", "Unit Symbol", "Reorder Level",
                "Wholesale Price", "Retail Price", "Description", "Brand" };
            for (var i = 0; i < headers.Length; i++)
                sheet.Cell(1, i + 1).Value = headers[i];
            for (var r = 0; r < rows.Length; r++)
            {
                sheet.Cell(r + 2, 1).Value = rows[r].Name;
                sheet.Cell(r + 2, 2).Value = flour.Code;
                sheet.Cell(r + 2, 3).Value = "FinishedGood";
                sheet.Cell(r + 2, 4).Value = "Standard";
                sheet.Cell(r + 2, 5).Value = "pcs";
                sheet.Cell(r + 2, 6).Value = 10;
                sheet.Cell(r + 2, 8).Value = 190;
                sheet.Cell(r + 2, 10).Value = rows[r].Brand ?? "";
            }
            var stream = new MemoryStream();
            workbook.SaveAs(stream);
            stream.Position = 0;
            return stream;
        }
        Task<ProductBulkImportResultDto> Upload(Stream file, bool dryRun) =>
            Run(sp => sp.GetRequiredService<CatalogService>().BulkImportProductsAsync(file, dryRun));

        var names = new[] { Unique("Jogoo Maize Meal 2kg "), Unique("Jogoo Maize Meal 1kg "), Unique("Soko Maize Meal 2kg "), Unique("Loose Flour ") };
        // A deactivated brand is refused, and nothing is saved.
        var refused = await Upload(Sheet((names[0], jogoo), (Unique("Old "), retired.Name)), dryRun: false);
        Assert.False(refused.Committed);
        Assert.Contains("deactivated", refused.Rows.Single(r => !r.IsValid).Error);

        // A new brand named twice, in any case, is created once; a known one is matched by name; no brand is fine.
        var done = await Upload(Sheet((names[0], jogoo), (names[1], jogoo.ToUpperInvariant()), (names[2], soko.Name.ToLowerInvariant()), (names[3], null)), dryRun: false);
        Assert.True(done.Committed);

        var products = (await Run(sp => sp.GetRequiredService<CatalogService>().GetProductsAsync(1, 50, new ProductQuery(CategoryId: flour.CategoryId))))
            .Items.ToDictionary(p => p.Name);
        var jogooBrand = Assert.Single(await Run(sp => sp.GetRequiredService<CatalogService>().GetBrandsAsync(jogoo, null)));
        Assert.Equal(jogoo, jogooBrand.Name);
        Assert.Equal(2, jogooBrand.ProductCount);
        Assert.Equal(jogooBrand.BrandId, products[names[1]].BrandId);
        Assert.Equal(soko.BrandId, products[names[2]].BrandId);
        Assert.Null(products[names[3]].BrandId);
    }

    [PostgresFact]
    public async Task A_minimart_that_is_not_VAT_registered_charges_no_VAT_on_a_standard_rated_product()
    {
        await SignInToMinimartAsync();
        var (flour, pieces) = await MaizeFlourShelfAsync();
        var soko = await BrandAsync(Unique("Soko "));
        var product = await FlourAsync(Unique("Soko Maize Meal 2kg "), flour.CategoryId, pieces, soko.BrandId);
        var sale = await Run(async sp =>
        {
            var catalog = sp.GetRequiredService<CatalogService>();
            await catalog.AddPriceTierAsync(product.ProductId, new AddPriceTierRequestDto(pieces, PriceType.Retail, 190m, DateTime.UtcNow.AddMinutes(-1)));
            await sp.GetRequiredService<InventoryService>().AdjustStockAsync(new CreateStockAdjustmentRequestDto(product.ProductId, pieces, 24, "Opening stock", 165m));
            return await sp.GetRequiredService<SalesService>().RecordSaleAsync(new CreateSaleRequestDto(
                null, PaymentMethod.Cash, null, new[] { new CreateSaleLineRequestDto(product.ProductId, pieces, 2) }, 500m, null, null));
        });

        Assert.Equal(380m, sale.TotalAmount);
        Assert.Equal(0m, sale.TaxTotal);
        Assert.Equal(120m, sale.ChangeDue);
    }

    private Task<Guid> BaleAsync() => Run(async sp =>
    {
        var catalog = sp.GetRequiredService<CatalogService>();
        return (await catalog.GetUnitsOfMeasureAsync()).FirstOrDefault(u => u.Symbol == "bale")?.UnitOfMeasureId
            ?? (await catalog.CreateUnitOfMeasureAsync(new CreateUnitOfMeasureRequestDto("Bale", "bale"))).UnitOfMeasureId;
    });

    private static CreateProductRequestDto PricedFlour(string name, Guid categoryId, Guid pieces, Guid bale, decimal? balePrice = 2200m) =>
        new(name, null, categoryId, ItemType.FinishedGood, TaxClassification.Standard, pieces, 12,
            RetailPrice: 190m, WholesalePrice: 180m, Packs: new[] { new NewProductPackDto(bale, 12, balePrice) });

    private Task<int> CountNamedAsync(string name) =>
        Run(sp => sp.GetRequiredService<UltimatePosDbContext>().Products.CountAsync(p => p.Name == name));

    [PostgresFact]
    public async Task A_product_is_created_with_its_prices_and_its_bale_in_one_step_and_sells_by_the_bale()
    {
        await SignInToMinimartAsync();
        var (flour, pieces) = await MaizeFlourShelfAsync();
        var bale = await BaleAsync();
        var created = await Run(sp => sp.GetRequiredService<CatalogService>().CreateProductAsync(
            PricedFlour(Unique("Soko Maize Meal 2kg "), flour.CategoryId, pieces, bale)));

        var detail = await Run(sp => sp.GetRequiredService<CatalogService>().GetProductByIdAsync(created.ProductId));
        var conversion = Assert.Single(detail.UnitConversions);
        Assert.Equal((bale, 12m), (conversion.PackUnitOfMeasureId, conversion.ConversionFactor));
        Assert.Equal(new[] { (pieces, PriceType.Retail, 190m), (pieces, PriceType.Wholesale, 180m), (bale, PriceType.Retail, 2200m) }.OrderBy(t => t.ToString()),
            detail.PriceTiers.Select(t => (t.UnitOfMeasureId, t.PriceType, t.Price)).OrderBy(t => t.ToString()));

        // Sellable at once: no separate pricing step. One bale is 12 packets out of stock.
        var sale = await Run(async sp =>
        {
            await sp.GetRequiredService<InventoryService>().AdjustStockAsync(new CreateStockAdjustmentRequestDto(created.ProductId, pieces, 48, "Opening stock", 165m));
            return await sp.GetRequiredService<SalesService>().RecordSaleAsync(new CreateSaleRequestDto(null, PaymentMethod.Cash, null,
                new[] { new CreateSaleLineRequestDto(created.ProductId, bale, 1), new CreateSaleLineRequestDto(created.ProductId, pieces, 1) }, 3000m, null, null));
        });
        Assert.Equal(2390m, sale.TotalAmount);
        var stock = await Run(sp => sp.GetRequiredService<UltimatePosDbContext>().StockLevels.Where(l => l.ProductId == created.ProductId).SumAsync(l => l.QuantityOnHand));
        Assert.Equal(35m, stock);
    }

    [PostgresFact]
    public async Task A_bad_price_or_pack_creates_nothing()
    {
        await SignInToMinimartAsync();
        var (flour, pieces) = await MaizeFlourShelfAsync();
        var bale = await BaleAsync();
        var name = Unique("Pembe Maize Flour 2kg ");
        Task Create(CreateProductRequestDto request) => Run(sp => sp.GetRequiredService<CatalogService>().CreateProductAsync(request));

        var badBalePrice = await Assert.ThrowsAsync<ValidationException>(() => Create(PricedFlour(name, flour.CategoryId, pieces, bale, balePrice: 0m)));
        Assert.Contains("packs[0].retailPrice", badBalePrice.Errors.Keys);
        await Assert.ThrowsAsync<ValidationException>(() => Create(PricedFlour(name, flour.CategoryId, pieces, bale) with { RetailPrice = -5m }));
        await Assert.ThrowsAsync<ValidationException>(() => Create(PricedFlour(name, flour.CategoryId, pieces, bale) with
            { Packs = new[] { new NewProductPackDto(bale, 12), new NewProductPackDto(bale, 24) } }));
        await Assert.ThrowsAsync<ValidationException>(() => Create(PricedFlour(name, flour.CategoryId, pieces, bale) with
            { Packs = new[] { new NewProductPackDto(pieces, 1) } }));
        await Assert.ThrowsAsync<ValidationException>(() => Create(PricedFlour(name, flour.CategoryId, pieces, bale) with
            { Packs = new[] { new NewProductPackDto(bale, 0) } }));
        Assert.Equal(0, await CountNamedAsync(name));

        // Without prices or packs it is created exactly as before.
        await Create(new CreateProductRequestDto(name, null, flour.CategoryId, ItemType.FinishedGood, TaxClassification.Standard, pieces, 12));
        Assert.Equal(1, await CountNamedAsync(name));
    }

    [PostgresFact]
    public async Task Creating_a_product_is_no_way_round_the_pricing_and_pack_permissions()
    {
        await SignInToMinimartAsync();
        var (flour, pieces) = await MaizeFlourShelfAsync();
        var bale = await BaleAsync();
        var name = Unique("Jogoo Maize Meal 2kg ");
        Task Create(CreateProductRequestDto request) => Run(sp => sp.GetRequiredService<CatalogService>().CreateProductAsync(request));

        await SignInToMinimartAsync("Products.Create", "Products.ManageUnitConversions");
        await Assert.ThrowsAsync<ForbiddenException>(() => Create(PricedFlour(name, flour.CategoryId, pieces, bale)));
        await SignInToMinimartAsync("Products.Create", "Products.ManagePricing");
        await Assert.ThrowsAsync<ForbiddenException>(() => Create(PricedFlour(name, flour.CategoryId, pieces, bale)));
        Assert.Equal(0, await CountNamedAsync(name));

        // Prices alone need only the pricing permission.
        await Create(new CreateProductRequestDto(name, null, flour.CategoryId, ItemType.FinishedGood, TaxClassification.Standard, pieces, 12, RetailPrice: 180m));
        Assert.Equal(1, await CountNamedAsync(name));
    }
}
