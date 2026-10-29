using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using UltimatePos.Application.Business.Dtos;
using UltimatePos.Application.Catalog;
using UltimatePos.Application.Catalog.Dtos;
using UltimatePos.Application.Common.Interfaces;
using UltimatePos.Application.Etims;
using UltimatePos.Application.Inventory;
using UltimatePos.Application.Inventory.Dtos;
using UltimatePos.Application.Platform;
using UltimatePos.Application.Sales;
using UltimatePos.Application.Sales.Dtos;
using UltimatePos.Domain.Entities;
using UltimatePos.Infrastructure.Etims;
using UltimatePos.Infrastructure.Persistence;
using static UltimatePos.Domain.Enums.PosEnums;

namespace Ultimate.Api.Tests;

/// <summary>
/// Runs only with a test database (ULTIMATEPOS_TEST_POSTGRES) and the KRA sandbox credentials of a device KRA already initialised:
/// ETIMS_CONSUMER_KEY, ETIMS_CONSUMER_SECRET, ETIMS_APIGEE_APP_ID, ETIMS_CMC_KEY (and ETIMS_TIN / ETIMS_DVC_SRL_NO if not the
/// collection's P052446551J / UPOSP052446551J001). It calls KRA's sandbox for real.
/// </summary>
public sealed class EtimsSandboxFactAttribute : FactAttribute
{
    public EtimsSandboxFactAttribute()
    {
        var missing = new[] { PostgresFactAttribute.ConnectionStringVariable, "ETIMS_CONSUMER_KEY", "ETIMS_CONSUMER_SECRET", "ETIMS_APIGEE_APP_ID", "ETIMS_CMC_KEY" }
            .Where(v => string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(v))).ToList();
        if (missing.Count > 0)
            Skip = $"Set {string.Join(", ", missing)} to run against the KRA eTIMS sandbox.";
    }
}

/// <summary>
/// The POS's own eTIMS reporting (EtimsService + EtimsSyncService + KraEtimsClient) against KRA's sandbox: a business with the sandbox
/// PIN registers its head office device, stocks two products, sells, takes a return and writes off damaged stock, and the worker's
/// pass reports all of it. Writes every submission to ETIMS_SANDBOX_REPORT when set.
/// </summary>
public class EtimsSandboxPostgresTests
{
    // Made in the test rather than as a class fixture, so a skipped run builds no database.
    private MultiBusinessDatabase _db = null!;

    private static string Env(string name, string fallback = "") => Environment.GetEnvironmentVariable(name) is { Length: > 0 } v ? v : fallback;

    private readonly EtimsSettings _settings = new() { MaxAttempts = 10 };
    private readonly IEtimsClient _kra = CreateClient();

    private static IEtimsClient CreateClient()
    {
        var options = new EtimsOptions
        {
            BaseUrl = Env("ETIMS_BASE_URL", "https://sbx.kra.go.ke/etims-oscu/api/v1"),
            TokenUrl = Env("ETIMS_TOKEN_URL", "https://sbx.kra.go.ke/v1/token/generate"),
            ConsumerKey = Env("ETIMS_CONSUMER_KEY"), ConsumerSecret = Env("ETIMS_CONSUMER_SECRET"), ApigeeAppId = Env("ETIMS_APIGEE_APP_ID"),
            TimeoutSeconds = 60,
        };
        var http = new HttpClient { BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/"), Timeout = TimeSpan.FromSeconds(60) };
        return new KraEtimsClient(http, new MemoryCache(new MemoryCacheOptions()), Options.Create(options));
    }

    private Task<T> Run<T>(Func<IServiceProvider, Task<T>> call) => _db.RunAsync(call);
    private EtimsSyncService Sync(IServiceProvider sp) => new(sp.GetRequiredService<IEtimsRepository>(), _kra, _settings);
    private EtimsService Etims(IServiceProvider sp) =>
        new(sp.GetRequiredService<IEtimsRepository>(), _kra, Sync(sp), _settings, sp.GetRequiredService<ICurrentUser>());

    [EtimsSandboxFact]
    public async Task A_head_office_reports_its_stock_sales_and_returns_to_the_KRA_sandbox()
    {
        _db = new MultiBusinessDatabase();
        await _db.InitializeAsync();
        try
        {
            await ReportToSandboxAsync();
        }
        finally
        {
            await _db.DisposeAsync();
        }
    }

    private async Task ReportToSandboxAsync()
    {
        var tin = Env("ETIMS_TIN", "P052446551J");
        _db.Caller.UserId = _db.SuperUserId;
        _db.Caller.BusinessId = null;
        _db.Caller.Permissions = null;
        var business = (await Run(sp => sp.GetRequiredService<PlatformService>().RegisterBusinessAsync(new RegisterBusinessRequestDto(
            "Quickcrate Express Limited", "Quickcrate", BusinessType.Retailer, null, null, tin, "Westlands", "Nairobi", null, null, null, null,
            IsVatRegistered: true, Owner: new BusinessOwnerRequestDto("admin", DbSeeder.DevAdminEmail, null), Sector: BusinessSector.Minimart,
            EtimsEnabled: true)))).Business;
        var headOffice = await Run(sp => sp.GetRequiredService<UltimatePosDbContext>().Branches.IgnoreQueryFilters()
            .Where(b => b.BusinessId == business.BusinessId && b.BranchNumber == 0).Select(b => b.BranchId).SingleAsync());
        _db.HeadOffice[business.BusinessId] = headOffice;
        _db.SignInTo(business.BusinessId);

        var pcs = (await Run(sp => sp.GetRequiredService<CatalogService>().GetUnitsOfMeasureAsync())).Single(u => u.Symbol == "pcs").UnitOfMeasureId;
        var shelf = await Run(sp => sp.GetRequiredService<CatalogService>().CreateCategoryAsync(new CreateCategoryRequestDto("Household", "HSE", null)));
        async Task<ProductDto> Product(string name, TaxClassification tax, decimal price)
        {
            var product = await Run(sp => sp.GetRequiredService<CatalogService>().CreateProductAsync(new CreateProductRequestDto(
                name, null, shelf.CategoryId, ItemType.FinishedGood, tax, pcs, 5)));
            await Run(sp => sp.GetRequiredService<CatalogService>().AddPriceTierAsync(product.ProductId,
                new AddPriceTierRequestDto(pcs, PriceType.Retail, price, DateTime.UtcNow.AddMinutes(-1))));
            return product;
        }
        var soap = await Product("Bar soap 800g", TaxClassification.Standard, 232m);
        var flour = await Product("Maize flour 2kg", TaxClassification.Exempt, 180m);
        // Stock the branch held before eTIMS: reported as opening stock.
        foreach (var p in new[] { soap, flour })
            await Run(sp => sp.GetRequiredService<InventoryService>().AdjustStockAsync(new CreateStockAdjustmentRequestDto(p.ProductId, pcs, 20, "Opening stock", 150m)));

        var device = await Run(sp => Etims(sp).RegisterDeviceAsync(new RegisterEtimsDeviceRequestDto(headOffice,
            Env("ETIMS_DVC_SRL_NO", "UPOSP052446551J001"), "1010160100", CmcKey: Env("ETIMS_CMC_KEY"),
            ReceiptTopMessage: "Karibu Quickcrate", ReceiptBottomMessage: "Asante")));
        var lists = (await Run(sp => Etims(sp).SyncReferenceDataAsync(device.EtimsDeviceId))).ToList();

        // A day's trading after eTIMS started.
        var sale = await Run(sp => sp.GetRequiredService<SalesService>().RecordSaleAsync(new CreateSaleRequestDto(null, PaymentMethod.Cash, null,
            new[] { new CreateSaleLineRequestDto(soap.ProductId, pcs, 2), new CreateSaleLineRequestDto(flour.ProductId, pcs, 3) }, 2000m, null, null)));
        await Run(sp => sp.GetRequiredService<SalesService>().CreateReturnAsync(sale.SaleId, new CreateSaleReturnRequestDto("Wrong size",
            RefundMethod.Cash, null, new[] { new CreateSaleReturnLineRequestDto(sale.Lines.Single(l => l.ProductId == soap.ProductId).SaleLineId, 1) })));
        await Run(sp => sp.GetRequiredService<InventoryService>().AdjustStockAsync(new CreateStockAdjustmentRequestDto(flour.ProductId, pcs, -1, "Torn bag", null)));

        for (var pass = 0; pass < 4; pass++)
            await Run(sp => Sync(sp).RunAsync(device.EtimsDeviceId));

        var submissions = await Run(sp => sp.GetRequiredService<UltimatePosDbContext>().EtimsSubmissions
            .Where(s => s.EtimsDeviceId == device.EtimsDeviceId).OrderBy(s => s.CreatedAt).ToListAsync());
        var deviceNow = await Run(sp => Etims(sp).GetDeviceAsync(device.EtimsDeviceId));
        var receipt = await Run(async sp => { try { return await Etims(sp).GetSaleReceiptAsync(sale.SaleId); } catch { return null; } });

        if (Env("ETIMS_SANDBOX_REPORT") is { Length: > 0 } reportPath)
        {
            JsonNode? Parse(string? json) { try { return json is null ? null : JsonNode.Parse(json); } catch { return json; } }
            var report = new JsonObject
            {
                ["tin"] = tin, ["bhfId"] = deviceNow.BhfId, ["sdcId"] = deviceNow.SdcId, ["mrcNo"] = deviceNow.MrcNo,
                ["lists"] = new JsonArray(lists.Select(l => (JsonNode)new JsonObject { ["kind"] = l.Kind, ["resultCd"] = l.ResultCode, ["resultMsg"] = l.ResultMessage, ["count"] = l.Count }).ToArray()),
                ["lastInvoiceNumber"] = deviceNow.LastInvoiceNumber, ["lastStockAdjustmentNumber"] = deviceNow.LastStockAdjustmentNumber,
                ["lastItemSequence"] = deviceNow.LastItemSequence,
                ["receipt"] = receipt is null ? null : JsonSerializer.SerializeToNode(receipt),
                ["submissions"] = new JsonArray(submissions.Select(s => (JsonNode)new JsonObject
                {
                    ["documentType"] = s.DocumentType.ToString(), ["sourceType"] = s.SourceType, ["endpoint"] = s.Endpoint,
                    ["documentNumber"] = s.DocumentNumber, ["status"] = s.Status.ToString(), ["attempts"] = s.Attempts,
                    ["resultCode"] = s.ResultCode, ["resultMessage"] = s.ResultMessage, ["receiptSignature"] = s.ReceiptSignature,
                    ["request"] = Parse(s.RequestJson), ["response"] = Parse(s.ResponseJson),
                }).ToArray()),
            };
            await File.WriteAllTextAsync(reportPath, report.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }

        // Everything that does not depend on the sandbox's invoice series is accepted.
        Assert.All(lists, l => Assert.Equal("000", l.ResultCode));
        Assert.All(submissions.Where(s => s.DocumentType is EtimsDocumentType.Item or EtimsDocumentType.StockIO or EtimsDocumentType.StockMaster),
            s => Assert.Equal(EtimsSubmissionStatus.Sent, s.Status));
        Assert.Contains(submissions, s => s.SourceType == "OpeningStock");
        Assert.Equal(4, submissions.Count(s => s.DocumentType == EtimsDocumentType.StockIO)); // opening, sale, return, write-off
        Assert.Contains(submissions, s => s.DocumentType == EtimsDocumentType.Sale && s.Attempts > 0);
    }
}
