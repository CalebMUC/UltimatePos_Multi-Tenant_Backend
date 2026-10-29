using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UltimatePos.Application.Branches;
using UltimatePos.Application.Catalog;
using UltimatePos.Application.Catalog.Dtos;
using UltimatePos.Application.Common.Interfaces;
using UltimatePos.Application.Etims;
using UltimatePos.Application.Inventory;
using UltimatePos.Application.Inventory.Dtos;
using UltimatePos.Application.Purchasing;
using UltimatePos.Application.Purchasing.Dtos;
using UltimatePos.Application.Sales;
using UltimatePos.Application.Sales.Dtos;
using UltimatePos.Domain.Entities;
using UltimatePos.Domain.Exceptions;
using UltimatePos.Infrastructure.Persistence;
using static UltimatePos.Domain.Enums.PosEnums;

namespace Ultimate.Api.Tests;

/// <summary>A KRA that accepts everything (unless told otherwise) and signs every sale, recording what it was sent.</summary>
public sealed class FakeEtimsClient : IEtimsClient
{
    public List<(string Endpoint, EtimsDeviceKey? Device, string Body)> Calls { get; } = new();
    /// <summary>Return a result to answer a call yourself; null lets the default answer it.</summary>
    public Func<string, string, EtimsResult?>? Answer { get; set; }
    public bool InitializeRefuses { get; set; }
    /// <summary>
    /// Like the sandbox: item code sequence, invoice numbers and stock numbers are strict series, and a number out of turn is refused
    /// with the one KRA expects. Null = any number is accepted.
    /// </summary>
    public (long Item, long Invoice, long Stock)? Strict { get; set; }
    public HashSet<string> AcceptedItemCodes { get; } = new();
    /// <summary>The invoices KRA holds, by number: what selectInvoiceDetail returns. A number used twice is refused with 924.</summary>
    public Dictionary<long, string> Invoices { get; } = new();
    /// <summary>Accept the next sale but let its reply be lost on the way back.</summary>
    public bool LoseNextSaleReply { get; set; }
    private int _receipts;

    public Task<EtimsResult> InitializeAsync(EtimsInitializeRequest request, CancellationToken cancellationToken = default)
    {
        var body = EtimsJson.Serialize(request);
        Calls.Add((EtimsEndpoints.Initialize, null, body));
        return Task.FromResult(InitializeRefuses
            ? Result(EtimsEndpoints.Initialize, body, "902", "This device is installed", null)
            : Result(EtimsEndpoints.Initialize, body, "000", "It is succeeded",
                $$$"""{"info":{"tin":"{{{request.Tin}}}","bhfId":"{{{request.BhfId}}}","taxprNm":"AFYA PHARMACY","bhfNm":"Headquarter","dvcId":"9999911111","sdcId":"KRACU0300000001","mrcNo":"WIS00000001","cmcKey":"CMC-{{{request.DvcSrlNo}}}"}}"""));
    }

    public Task<EtimsResult> PostAsync(string endpoint, EtimsDeviceKey device, string jsonBody, CancellationToken cancellationToken = default)
    {
        Calls.Add((endpoint, device, jsonBody));
        if (Answer?.Invoke(endpoint, jsonBody) is { } answered)
            return Task.FromResult(answered);
        if (Strict is { } next && OutOfTurn(endpoint, jsonBody, next) is { } refusal)
            return Task.FromResult(refusal);
        if (endpoint == EtimsEndpoints.SelectInvoiceDetail)
        {
            var wanted = long.Parse(JsonDocument.Parse(jsonBody).RootElement.GetProperty("invcNo").GetString()!);
            return Task.FromResult(Invoices.TryGetValue(wanted, out var held)
                ? Result(endpoint, jsonBody, "000", "It is succeeded", held)
                : Result(endpoint, jsonBody, "001", "There is no search result", null));
        }
        string? data = null;
        if (endpoint == EtimsEndpoints.SendSalesTransaction)
        {
            var sale = JsonDocument.Parse(jsonBody).RootElement;
            var number = sale.GetProperty("invcNo").GetInt64();
            if (Invoices.ContainsKey(number))
                return Task.FromResult(Result(endpoint, jsonBody, "924", "Invoice number already exists.", null));
            data = $$"""{"curRcptNo":{{++_receipts}},"totRcptNo":{{_receipts}},"intrlData":"INTRL{{_receipts}}","rcptSign":"SIGN{{_receipts}}","sdcDateTime":"20261027120000"}""";
            Invoices[number] = $$"""{"invcNo":{{number}},"rcptTyCd":"{{sale.GetProperty("rcptTyCd").GetString()}}","totAmt":{{sale.GetProperty("totAmt").GetDecimal()}},"itemList":{{sale.GetProperty("itemList").GetRawText()}},"receipt":{{data}}}""";
            if (LoseNextSaleReply)
            {
                LoseNextSaleReply = false;
                return Task.FromResult(new EtimsResult(endpoint, 0, null, null, null, null, jsonBody, null, "No reply from KRA within 30s.", TimeSpan.Zero));
            }
        }
        return Task.FromResult(Result(endpoint, jsonBody, "000", "It is succeeded", data));
    }

    private EtimsResult? OutOfTurn(string endpoint, string body, (long Item, long Invoice, long Stock) next)
    {
        var json = JsonDocument.Parse(body).RootElement;
        string? error = null;
        if (endpoint == EtimsEndpoints.SaveItem)
        {
            var code = json.GetProperty("itemCd").GetString()!;
            var sequence = long.Parse(code[^7..]);
            if (AcceptedItemCodes.Contains(code)) return null; // the code another branch registered
            if (sequence == next.Item) { Strict = next with { Item = next.Item + 1 }; AcceptedItemCodes.Add(code); }
            else error = $"HTTP 400: Invalid itemCd Sequence. Expected sequence ending with: ********{next.Item} (The itemCd is either reused or not incremented properly. Expected sequence ending with ********{next.Item})";
        }
        else if (endpoint == EtimsEndpoints.SendSalesTransaction)
        {
            var number = json.GetProperty("invcNo").GetInt64();
            if (number == next.Invoice) Strict = next with { Invoice = next.Invoice + 1 };
            else error = $"HTTP 400: Invalid invcNo sequence, expected: {next.Invoice} but found: {number} (Invc No: {number} is invalid, use the expected value: {next.Invoice})";
        }
        else if (endpoint == EtimsEndpoints.StockIO)
        {
            var number = json.GetProperty("sarNo").GetInt64();
            if (number == next.Stock) Strict = next with { Stock = next.Stock + 1 };
            else error = $"HTTP 400: Validation failed (Invalid sarNo: Expected: {next.Stock} but found: {number})";
        }
        return error is null ? null : new EtimsResult(endpoint, 400, null, null, null, null, body, "{}", error, TimeSpan.Zero);
    }

    public static EtimsResult Result(string endpoint, string body, string code, string message, string? data) =>
        new(endpoint, 200, code, message, "20261027120000", data is null ? null : JsonDocument.Parse(data).RootElement.Clone(), body,
            $$"""{"resultCd":"{{code}}","resultMsg":"{{message}}","data":{{data ?? "null"}}}""", null, TimeSpan.Zero);

    public IEnumerable<JsonElement> Bodies(string endpoint) =>
        Calls.Where(c => c.Endpoint == endpoint).Select(c => JsonDocument.Parse(c.Body).RootElement.Clone());
}

/// <summary>
/// A branch with an eTIMS device, end to end on a real database: what the POS records (sales, a return, a void, a purchase, stock
/// adjustments) is queued, numbered and sent to a fake KRA, in the order KRA needs it.
/// </summary>
public class EtimsPostgresTests : IClassFixture<MultiBusinessDatabase>
{
    private readonly MultiBusinessDatabase _db;
    public EtimsPostgresTests(MultiBusinessDatabase db) => _db = db;

    private readonly FakeEtimsClient _kra = new();
    private readonly EtimsSettings _settings = new() { MaxAttempts = 3 };

    private Task<T> Run<T>(Func<IServiceProvider, Task<T>> call) => _db.RunAsync(call);

    private EtimsSyncService Sync(IServiceProvider sp) => new(sp.GetRequiredService<IEtimsRepository>(), _kra, _settings);
    private EtimsService Etims(IServiceProvider sp) =>
        new(sp.GetRequiredService<IEtimsRepository>(), _kra, Sync(sp), _settings, sp.GetRequiredService<ICurrentUser>());

    /// <summary>Runs passes until nothing more is sent (stock master follows stock in/out on the next pass).</summary>
    private async Task<List<EtimsRunResultDto>> DrainAsync(Guid deviceId)
    {
        var passes = new List<EtimsRunResultDto>();
        for (var i = 0; i < 5; i++)
        {
            var pass = await Run(sp => Sync(sp).RunAsync(deviceId));
            passes.Add(pass);
            if (pass.Queued == 0 && pass.Sent == 0) break;
        }
        return passes;
    }

    private Task<List<EtimsSubmission>> SubmissionsAsync(Guid deviceId) =>
        Run(sp => sp.GetRequiredService<UltimatePosDbContext>().EtimsSubmissions.Where(s => s.EtimsDeviceId == deviceId)
            .OrderBy(s => s.CreatedAt).ToListAsync());

    private async Task<Guid> NewBranchAsync() =>
        (await Run(sp => sp.GetRequiredService<BranchService>().CreateBranchAsync(new CreateBranchRequestDto(
            "Branch " + Guid.NewGuid().ToString("N")[..6], null, null, "Moi Avenue", "Nairobi", null, null, null)))).BranchId;

    private async Task<(ProductDto Product, Guid Unit)> ProductAsync(string name, TaxClassification tax, decimal price)
    {
        var pcs = (await Run(sp => sp.GetRequiredService<CatalogService>().GetUnitsOfMeasureAsync())).Single(u => u.Symbol == "pcs").UnitOfMeasureId;
        var code = $"E{Guid.NewGuid():N}"[..8].ToUpperInvariant();
        var category = await Run(sp => sp.GetRequiredService<CatalogService>().CreateCategoryAsync(new CreateCategoryRequestDto("Shelf " + code, code, null)));
        var product = await Run(sp => sp.GetRequiredService<CatalogService>().CreateProductAsync(new CreateProductRequestDto(
            name + " " + code, null, category.CategoryId, ItemType.FinishedGood, tax, pcs, 5)));
        await Run(sp => sp.GetRequiredService<CatalogService>().AddPriceTierAsync(product.ProductId,
            new AddPriceTierRequestDto(pcs, PriceType.Retail, price, DateTime.UtcNow.AddMinutes(-1))));
        return (product, pcs);
    }

    private Task<SaleDto> SellAsync(params (Guid ProductId, Guid Unit, decimal Quantity)[] lines) =>
        Run(sp => sp.GetRequiredService<SalesService>().RecordSaleAsync(new CreateSaleRequestDto(null, PaymentMethod.Cash, null,
            lines.Select(l => new CreateSaleLineRequestDto(l.ProductId, l.Unit, l.Quantity)).ToList(), 100000m, null, null)));

    private Task AdjustAsync(Guid productId, Guid unit, decimal change, decimal cost) =>
        Run(sp => sp.GetRequiredService<InventoryService>().AdjustStockAsync(new CreateStockAdjustmentRequestDto(productId, unit, change, "Opening stock", cost)));

    /// <summary>eTIMS is switched on per business by super users; the businesses these tests use have it on.</summary>
    private Task SwitchEtimsOnAsync() => Run(async sp =>
    {
        var businessId = _db.Caller.BusinessId!.Value;
        await sp.GetRequiredService<UltimatePosDbContext>().Businesses.IgnoreQueryFilters()
            .Where(b => b.BusinessId == businessId && !b.EtimsEnabled)
            .ExecuteUpdateAsync(u => u.SetProperty(b => b.EtimsEnabled, true).SetProperty(b => b.EtimsEnabledAt, DateTime.UtcNow));
        return 0;
    });

    private async Task<EtimsDeviceDto> RegisterAsync(Guid branchId, string serial)
    {
        await SwitchEtimsOnAsync();
        return await Run(sp => Etims(sp).RegisterDeviceAsync(new RegisterEtimsDeviceRequestDto(branchId, serial, "1010160100",
            ReceiptTopMessage: "Karibu", ReceiptBottomMessage: "Asante")));
    }

    [PostgresFact]
    public async Task A_branch_reports_its_sales_returns_voids_purchases_and_stock_to_KRA_in_order()
    {
        _db.SignInTo(_db.Afya); // VAT-registered
        var branchId = await NewBranchAsync();
        _db.SignInTo(_db.Afya, branchId);
        var (cream, pcs) = await ProductAsync("Hand cream", TaxClassification.Standard, 1160m);
        var (tabs, _) = await ProductAsync("Paracetamol", TaxClassification.Exempt, 500m);

        var device = await RegisterAsync(branchId, "UPOSTEST" + Guid.NewGuid().ToString("N")[..6]);
        Assert.StartsWith("CMC", device.CmcKeyHint);
        Assert.Equal(("KRACU0300000001", "WIS00000001"), (device.SdcId, device.MrcNo));

        // Day one: opening stock and two sales.
        await AdjustAsync(cream.ProductId, pcs, 100, 800m);
        await AdjustAsync(tabs.ProductId, pcs, 100, 300m);
        var sale = await SellAsync((cream.ProductId, pcs, 2), (tabs.ProductId, pcs, 5));
        var mistake = await SellAsync((tabs.ProductId, pcs, 1));
        await DrainAsync(device.EtimsDeviceId);

        var sent = await SubmissionsAsync(device.EtimsDeviceId);
        Assert.All(sent, s => Assert.Equal(EtimsSubmissionStatus.Sent, s.Status));
        // Items before anything that names them, then the documents as they happened.
        var firstDocument = _kra.Calls.FindIndex(c => c.Endpoint != EtimsEndpoints.Initialize && c.Endpoint != EtimsEndpoints.SaveItem);
        Assert.True(_kra.Calls.FindLastIndex(c => c.Endpoint == EtimsEndpoints.SaveItem) < firstDocument);
        Assert.Equal(2, sent.Count(s => s.DocumentType == EtimsDocumentType.Item));
        Assert.Equal(new long?[] { 1, 2 }, sent.Where(s => s.DocumentType == EtimsDocumentType.Sale).Select(s => s.DocumentNumber));
        Assert.Equal(new long[] { 1, 2 }, _kra.Bodies(EtimsEndpoints.SendSalesTransaction).Select(b => b.GetProperty("receipt").GetProperty("rptNo").GetInt64()));
        Assert.Equal(new[] { "Adjustment", "Adjustment", "Sale", "Sale" }, sent.Where(s => s.DocumentType == EtimsDocumentType.StockIO).Select(s => s.SourceType));
        Assert.Equal(new long?[] { 1, 2, 3, 4 }, sent.Where(s => s.DocumentType == EtimsDocumentType.StockIO).Select(s => s.DocumentNumber));
        Assert.Equal(5, sent.Count(s => s.DocumentType == EtimsDocumentType.StockMaster)); // one per item of each stock in/out
        Assert.All(_kra.Calls.Where(c => c.Device is not null), c => Assert.StartsWith("CMC-UPOSTEST", c.Device!.CmcKey));

        // The sale as KRA got it reconciles with the POS's own figures.
        var saleBody = _kra.Bodies(EtimsEndpoints.SendSalesTransaction).First();
        Assert.Equal(sale.TotalAmount, saleBody.GetProperty("totAmt").GetDecimal());
        Assert.Equal(sale.TaxTotal, saleBody.GetProperty("totTaxAmt").GetDecimal());
        Assert.Equal((2500m, 2000m, 320m), (saleBody.GetProperty("taxblAmtA").GetDecimal(), saleBody.GetProperty("taxblAmtB").GetDecimal(),
            saleBody.GetProperty("taxAmtB").GetDecimal()));
        Assert.Equal(("S", "01", "Karibu"), (saleBody.GetProperty("rcptTyCd").GetString(), saleBody.GetProperty("pmtTyCd").GetString(),
            saleBody.GetProperty("receipt").GetProperty("topMsg").GetString()));
        var creamItem = saleBody.GetProperty("itemList").EnumerateArray().Single(i => i.GetProperty("taxTyCd").GetString() == "B");
        Assert.Matches("^KE2NTNO\\d{7}$", creamItem.GetProperty("itemCd").GetString());
        Assert.Equal(("NO", 1m), (creamItem.GetProperty("qtyUnitCd").GetString(), creamItem.GetProperty("pkg").GetDecimal()));
        Assert.Equal(creamItem.GetProperty("itemCd").GetString(), (await Run(sp => Etims(sp).GetProductProfileAsync(cream.ProductId))).ItemCode);
        Assert.Equal("1010160100", creamItem.GetProperty("itemClsCd").GetString());
        var opening = _kra.Bodies(EtimsEndpoints.StockIO).First();
        Assert.Equal("06", opening.GetProperty("sarTyCd").GetString());
        var stockMaster = _kra.Bodies(EtimsEndpoints.StockMaster).Last();
        Assert.Equal(94m, stockMaster.GetProperty("rsdQty").GetDecimal()); // paracetamol: 100 - 5 - 1

        // The receipt carries KRA's signature and its verification link.
        var receipt = await Run(sp => Etims(sp).GetSaleReceiptAsync(sale.SaleId));
        Assert.Equal((EtimsSubmissionStatus.Sent, 1L), (receipt.Status, receipt.InvoiceNumber!.Value));
        Assert.Equal(("SIGN1", "INTRL1", "KRACU0300000001"), (receipt.ReceiptSignature, receipt.InternalData, receipt.SdcId));
        Assert.Equal($"{_settings.ReceiptVerificationUrl}P051000002B{device.BhfId}SIGN1", receipt.VerificationUrl);

        // Day two: one cream comes back, the mistaken sale is voided, a delivery is received.
        await Run(sp => sp.GetRequiredService<SalesService>().CreateReturnAsync(sale.SaleId, new CreateSaleReturnRequestDto("Wrong item",
            RefundMethod.Cash, null, new[] { new CreateSaleReturnLineRequestDto(sale.Lines.Single(l => l.ProductId == cream.ProductId).SaleLineId, 1) })));
        await Run(sp => sp.GetRequiredService<SalesService>().VoidSaleAsync(mistake.SaleId));
        var supplier = await Run(sp => sp.GetRequiredService<PurchasingService>().RegisterSupplierAsync(
            new CreateSupplierRequestDto(null, "Dawa Distributors", "P051234567Q", null, null, null, null, 30)));
        var order = await Run(sp => sp.GetRequiredService<PurchasingService>().CreatePurchaseOrderAsync(new CreatePurchaseOrderRequestDto(
            supplier.SupplierId, null, null, new[] { new CreatePurchaseOrderLineRequestDto(cream.ProductId, pcs, 50, 812m) })));
        await Run(sp => sp.GetRequiredService<PurchasingService>().ReceivePurchaseOrderAsync(order.PurchaseOrderId,
            new ReceivePurchaseOrderRequestDto(new[] { new ReceiveLineRequestDto(order.Lines.Single().PurchaseOrderLineId, 50) }, null, "DD-1001")));
        await DrainAsync(device.EtimsDeviceId);

        var all = await SubmissionsAsync(device.EtimsDeviceId);
        Assert.All(all, s => Assert.Equal(EtimsSubmissionStatus.Sent, s.Status));
        var credits = all.Where(s => s.DocumentType == EtimsDocumentType.CreditNote).ToList();
        Assert.Equal(new[] { ("SaleReturn", 3L), ("SaleVoid", 4L) }, credits.Select(c => (c.SourceType, c.DocumentNumber!.Value)).OrderBy(c => c.Item1));
        var creditBodies = _kra.Bodies(EtimsEndpoints.SendSalesTransaction).Where(b => b.GetProperty("rcptTyCd").GetString() == "R").ToList();
        var refund = creditBodies.Single(b => b.GetProperty("orgInvcNo").GetInt64() == 1);
        Assert.Equal((1160m, "06"), (refund.GetProperty("totAmt").GetDecimal(), refund.GetProperty("rfdRsnCd").GetString()));
        Assert.Equal(500m, creditBodies.Single(b => b.GetProperty("orgInvcNo").GetInt64() == 2).GetProperty("totAmt").GetDecimal());

        // KRA accepts a purchase only against its own record of the supplier's invoice, so a receipt is reported as stock in (02).
        Assert.Empty(_kra.Bodies(EtimsEndpoints.SendPurchaseTransactionInfo));
        var received = _kra.Bodies(EtimsEndpoints.StockIO).Skip(4).ToList();
        Assert.Equal(new[] { "03", "03", "02" }, received.Select(b => b.GetProperty("sarTyCd").GetString()));
        Assert.Equal(new long[] { 5, 6, 7 }, received.Select(b => b.GetProperty("sarNo").GetInt64()));
        Assert.Equal(40600m, received[2].GetProperty("totAmt").GetDecimal());

        // Nothing is sent twice, however often it runs.
        var calls = _kra.Calls.Count;
        await DrainAsync(device.EtimsDeviceId);
        Assert.Equal(calls, _kra.Calls.Count);
    }

    [PostgresFact]
    public async Task A_document_waits_for_its_items_and_a_refusal_is_retried_then_left_for_a_manager()
    {
        _db.SignInTo(_db.Alvet); // not VAT-registered: everything is reported as non-VAT (D)
        var branchId = await NewBranchAsync();
        _db.SignInTo(_db.Alvet, branchId);
        var (feed, pcs) = await ProductAsync("Dairy meal", TaxClassification.Standard, 2500m);
        var device = await RegisterAsync(branchId, "UPOSTEST" + Guid.NewGuid().ToString("N")[..6]);

        // KRA refuses the item at first, so the sale that names it must wait rather than be sent.
        var refuseItems = true;
        _kra.Answer = (endpoint, body) => endpoint == EtimsEndpoints.SaveItem && refuseItems
            ? FakeEtimsClient.Result(endpoint, body, "910", "Invalid itemClsCd", null) : null;
        await AdjustAsync(feed.ProductId, pcs, 10, 2000m);
        var sale = await SellAsync((feed.ProductId, pcs, 1));
        var pass = await Run(sp => Sync(sp).RunAsync(device.EtimsDeviceId));
        Assert.Equal((1, 3), (pass.Failed, pass.Waiting)); // the item; the sale and both stock in/outs (opening stock, sale) wait
        Assert.DoesNotContain(_kra.Calls, c => c.Endpoint == EtimsEndpoints.SendSalesTransaction);

        var item = (await SubmissionsAsync(device.EtimsDeviceId)).Single(s => s.DocumentType == EtimsDocumentType.Item);
        Assert.Equal((EtimsSubmissionStatus.Failed, 1, "910"), (item.Status, item.Attempts, item.ResultCode));
        Assert.True(item.NextAttemptAt > DateTime.UtcNow);

        // Every retry refused: after MaxAttempts it is Rejected and left alone.
        for (var i = 0; i < 2; i++)
        {
            await Run(sp => sp.GetRequiredService<UltimatePosDbContext>().EtimsSubmissions
                .Where(s => s.EtimsDeviceId == device.EtimsDeviceId).ExecuteUpdateAsync(u => u.SetProperty(s => s.NextAttemptAt, DateTime.UtcNow.AddMinutes(-1))));
            await Run(sp => Sync(sp).RunAsync(device.EtimsDeviceId));
        }
        item = (await SubmissionsAsync(device.EtimsDeviceId)).Single(s => s.DocumentType == EtimsDocumentType.Item);
        Assert.Equal((EtimsSubmissionStatus.Rejected, 3), (item.Status, item.Attempts));

        // A manager fixes it and retries: the item goes, then what waited on it.
        refuseItems = false;
        await Run(sp => Etims(sp).RetryAsync(item.EtimsSubmissionId));
        await Run(sp => sp.GetRequiredService<UltimatePosDbContext>().EtimsSubmissions
            .Where(s => s.EtimsDeviceId == device.EtimsDeviceId).ExecuteUpdateAsync(u => u.SetProperty(s => s.NextAttemptAt, DateTime.UtcNow.AddMinutes(-1))));
        await DrainAsync(device.EtimsDeviceId);
        Assert.All(await SubmissionsAsync(device.EtimsDeviceId), s => Assert.Equal(EtimsSubmissionStatus.Sent, s.Status));

        var body = _kra.Bodies(EtimsEndpoints.SendSalesTransaction).Single(b => b.GetProperty("remark").GetString() == sale.SaleNumber);
        Assert.Equal((0m, 2500m, 2500m), (body.GetProperty("totTaxAmt").GetDecimal(), body.GetProperty("taxblAmtD").GetDecimal(),
            body.GetProperty("totAmt").GetDecimal()));
    }

    [PostgresFact]
    public async Task Only_what_happens_after_the_device_is_registered_is_reported_and_one_device_per_branch()
    {
        _db.SignInTo(_db.Afya);
        var branchId = await NewBranchAsync();
        _db.SignInTo(_db.Afya, branchId);
        var (soap, pcs) = await ProductAsync("Soap", TaxClassification.Standard, 116m);
        await AdjustAsync(soap.ProductId, pcs, 10, 80m);
        var before = await SellAsync((soap.ProductId, pcs, 1));

        var serial = "UPOSTEST" + Guid.NewGuid().ToString("N")[..6];
        var device = await RegisterAsync(branchId, serial);
        await Assert.ThrowsAsync<InvalidAssignmentException>(() => RegisterAsync(branchId, serial));
        await DrainAsync(device.EtimsDeviceId);

        var submissions = await SubmissionsAsync(device.EtimsDeviceId);
        Assert.DoesNotContain(submissions, s => s.SourceId == before.SaleId);
        // What the branch held when reporting started goes to KRA as opening stock, so it can sell it.
        var opening = Assert.Single(submissions, s => s.DocumentType == EtimsDocumentType.StockIO);
        Assert.Equal("OpeningStock", opening.SourceType);
        var openingBody = _kra.Bodies(EtimsEndpoints.StockIO).Single();
        Assert.Equal(("06", 9m), (openingBody.GetProperty("sarTyCd").GetString(), openingBody.GetProperty("itemList")[0].GetProperty("qty").GetDecimal()));
        Assert.Equal(9m, _kra.Bodies(EtimsEndpoints.StockMaster).Single().GetProperty("rsdQty").GetDecimal());
        // Every product is still registered, so it can be sold.
        Assert.Contains(submissions, s => s.DocumentType == EtimsDocumentType.Item && s.SourceId == soap.ProductId);
        await Assert.ThrowsAsync<NotFoundException>(() => Run(sp => Etims(sp).GetSaleReceiptAsync(before.SaleId)));
    }

    [PostgresFact]
    public async Task A_device_KRA_already_initialised_is_kept_only_with_its_key()
    {
        _db.SignInTo(_db.Afya);
        var branchId = await NewBranchAsync();
        _kra.InitializeRefuses = true;
        await Assert.ThrowsAsync<InvalidAssignmentException>(() => RegisterAsync(branchId, "UPOSOLD000001"));

        await SwitchEtimsOnAsync();
        var device = await Run(sp => Etims(sp).RegisterDeviceAsync(new RegisterEtimsDeviceRequestDto(branchId, "UPOSOLD000001", "1010160100",
            CmcKey: "ISSUED-EARLIER", LastInvoiceNumber: 41)));
        Assert.Equal(41, device.LastInvoiceNumber);
        Assert.Equal(EtimsEndpoints.SelectNoticeList, _kra.Calls.Last().Endpoint); // the key was checked with a read
        Assert.Equal("ISSUED-EARLIER", _kra.Calls.Last().Device!.CmcKey);
    }

    [PostgresFact]
    public async Task A_product_is_described_before_KRA_registers_it_and_never_after()
    {
        _db.SignInTo(_db.Afya);
        var branchId = await NewBranchAsync();
        var (oil, _) = await ProductAsync("Cooking oil", TaxClassification.Standard, 350m);

        await SwitchEtimsOnAsync();
        // KRA builds item codes only on two-letter units.
        await Assert.ThrowsAsync<ValidationException>(() => Run(sp => Etims(sp).UpdateProductProfileAsync(oil.ProductId,
            new UpdateEtimsProductProfileRequestDto("5020150100", null, "LTR", null))));
        var profile = await Run(sp => Etims(sp).UpdateProductProfileAsync(oil.ProductId, new UpdateEtimsProductProfileRequestDto("5020150100", null, "BX", null)));
        Assert.Null(profile.ItemCode); // made when KRA takes it: the sequence is KRA's
        Assert.False(profile.IsRegistered);

        var device = await RegisterAsync(branchId, "UPOSTEST" + Guid.NewGuid().ToString("N")[..6]);
        await DrainAsync(device.EtimsDeviceId);
        profile = await Run(sp => Etims(sp).GetProductProfileAsync(oil.ProductId));
        Assert.Matches("^KE2NTBX\\d{7}$", profile.ItemCode);
        var registered = _kra.Bodies(EtimsEndpoints.SaveItem).Single(b => b.GetProperty("itemCd").GetString() == profile.ItemCode);
        Assert.Equal("5020150100", registered.GetProperty("itemClsCd").GetString());

        // KRA refuses to save a registered item again, so nothing about it can change any more.
        await Assert.ThrowsAsync<InvalidAssignmentException>(() => Run(sp => Etims(sp).UpdateProductProfileAsync(oil.ProductId,
            new UpdateEtimsProductProfileRequestDto("5020150200", null, null, null))));
        await Run(sp => sp.GetRequiredService<CatalogService>().UpdateProductAsync(oil.ProductId, new UpdateProductRequestDto(
            oil.Name + " 2L", null, oil.CategoryId, oil.ItemType, oil.TaxClassification, oil.BaseUnitOfMeasureId, oil.ReorderLevel, null)));
        await Run(sp => sp.GetRequiredService<UltimatePosDbContext>().EtimsDevices.Where(d => d.EtimsDeviceId == device.EtimsDeviceId)
            .ExecuteUpdateAsync(u => u.SetProperty(d => d.ItemsCheckedAt, (DateTime?)null)));
        await DrainAsync(device.EtimsDeviceId);
        Assert.Single(_kra.Bodies(EtimsEndpoints.SaveItem), b => b.GetProperty("itemCd").GetString() == profile.ItemCode);
    }

    [PostgresFact]
    public async Task Numbers_follow_KRA_s_strict_series_whatever_the_device_last_thought()
    {
        _db.SignInTo(_db.Afya);
        var branchId = await NewBranchAsync();
        _db.SignInTo(_db.Afya, branchId);
        var (rice, pcs) = await ProductAsync("Rice 1kg", TaxClassification.Exempt, 200m);
        var device = await RegisterAsync(branchId, "UPOSTEST" + Guid.NewGuid().ToString("N")[..6]);
        // KRA already holds the codes other branches registered, and its series stand further on (codes registered elsewhere, invoices
        // and stock numbers used while testing): it will name the numbers it wants.
        var held = await Run(sp => sp.GetRequiredService<UltimatePosDbContext>().EtimsProductProfiles
            .Where(p => p.ItemCode != null).Select(p => p.ItemCode!).ToListAsync());
        _kra.AcceptedItemCodes.UnionWith(held);
        var firstNew = held.Select(c => long.Parse(c[^7..])).DefaultIfEmpty(0).Max() + 5;
        _kra.Strict = (Item: firstNew, Invoice: 7, Stock: 5);

        await AdjustAsync(rice.ProductId, pcs, 50, 150m);
        var sale = await SellAsync((rice.ProductId, pcs, 2));
        await DrainAsync(device.EtimsDeviceId);

        var all = await SubmissionsAsync(device.EtimsDeviceId);
        Assert.All(all, s => Assert.Equal(EtimsSubmissionStatus.Sent, s.Status));
        Assert.All(all, s => Assert.Equal(1, s.Attempts)); // a refused number is corrected within the same attempt
        var deviceNow = await Run(sp => Etims(sp).GetDeviceAsync(device.EtimsDeviceId));
        Assert.Equal((7L, 6L), (deviceNow.LastInvoiceNumber, deviceNow.LastStockAdjustmentNumber)); // invoice 7; stock 5 and 6
        var newCodes = _kra.AcceptedItemCodes.Except(held).Select(c => long.Parse(c[^7..])).OrderBy(n => n).ToList();
        Assert.Equal(Enumerable.Range((int)firstNew, newCodes.Count).Select(n => (long)n), newCodes); // the new codes took KRA's next numbers
        Assert.Equal(newCodes.Last(), deviceNow.LastItemSequence);
        var accepted = all.Single(s => s.SourceId == sale.SaleId && s.DocumentType == EtimsDocumentType.Sale);
        Assert.Equal(7L, accepted.DocumentNumber);
        using var stored = JsonDocument.Parse(accepted.RequestJson); // what KRA holds, real numbers and codes in place of placeholders
        Assert.Equal(7L, stored.RootElement.GetProperty("invcNo").GetInt64());
        Assert.StartsWith("KE2NTNO", stored.RootElement.GetProperty("itemList")[0].GetProperty("itemCd").GetString());
    }

    [PostgresFact]
    public async Task A_sale_whose_reply_was_lost_is_found_at_KRA_and_a_number_used_elsewhere_is_skipped()
    {
        _db.SignInTo(_db.Afya);
        var branchId = await NewBranchAsync();
        _db.SignInTo(_db.Afya, branchId);
        var (milk, pcs) = await ProductAsync("Milk 500ml", TaxClassification.Exempt, 60m);
        var device = await RegisterAsync(branchId, "UPOSTEST" + Guid.NewGuid().ToString("N")[..6]);
        await AdjustAsync(milk.ProductId, pcs, 20, 45m);

        // Invoice 1 is someone else's (as in the sandbox, where KRA's own test made it): it is skipped.
        _kra.Invoices[1] = """{"invcNo":1,"rcptTyCd":"S","totAmt":13249.91,"itemList":[{"itemCd":"KE1NTXU1000003"}]}""";
        var first = await SellAsync((milk.ProductId, pcs, 1));
        await DrainAsync(device.EtimsDeviceId);
        var firstSubmission = (await SubmissionsAsync(device.EtimsDeviceId)).Single(s => s.SourceId == first.SaleId && s.DocumentType == EtimsDocumentType.Sale);
        Assert.Equal((EtimsSubmissionStatus.Sent, 2L, 1), (firstSubmission.Status, firstSubmission.DocumentNumber!.Value, firstSubmission.Attempts));

        // KRA takes the next sale but the reply never arrives. The retry finds invoice 3 already there, and it is this sale.
        _kra.LoseNextSaleReply = true;
        var second = await SellAsync((milk.ProductId, pcs, 2));
        await Run(sp => Sync(sp).RunAsync(device.EtimsDeviceId));
        var lost = (await SubmissionsAsync(device.EtimsDeviceId)).Single(s => s.SourceId == second.SaleId && s.DocumentType == EtimsDocumentType.Sale);
        Assert.Equal((EtimsSubmissionStatus.Failed, 1), (lost.Status, lost.Attempts));
        await Run(sp => sp.GetRequiredService<UltimatePosDbContext>().EtimsSubmissions.Where(s => s.EtimsSubmissionId == lost.EtimsSubmissionId)
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.NextAttemptAt, DateTime.UtcNow.AddMinutes(-1))));
        await DrainAsync(device.EtimsDeviceId);

        var found = (await SubmissionsAsync(device.EtimsDeviceId)).Single(s => s.SourceId == second.SaleId && s.DocumentType == EtimsDocumentType.Sale);
        Assert.Equal((EtimsSubmissionStatus.Sent, 3L, "SIGN2"), (found.Status, found.DocumentNumber!.Value, found.ReceiptSignature));
        Assert.Equal(3, _kra.Invoices.Count); // never sent as a second invoice
        Assert.Equal(3L, (await Run(sp => Etims(sp).GetDeviceAsync(device.EtimsDeviceId))).LastInvoiceNumber);
    }

    [PostgresFact]
    public async Task A_business_that_does_not_need_eTIMS_sees_none_of_it_until_a_super_user_switches_it_on()
    {
        void AsSuperUser() { _db.Caller.UserId = _db.SuperUserId; _db.Caller.BusinessId = null; _db.Caller.Permissions = null; }
        AsSuperUser();
        var kiosk = (await Run(sp => sp.GetRequiredService<UltimatePos.Application.Platform.PlatformService>().RegisterBusinessAsync(
            new UltimatePos.Application.Business.Dtos.RegisterBusinessRequestDto("Mama Njeri Kiosk", null, BusinessType.Retailer, BusinessKind.Trader, null,
                "P051" + Random.Shared.Next(100000, 999999) + "K", null, "Nakuru", null, null, null, null,
                Owner: new UltimatePos.Application.Platform.BusinessOwnerRequestDto("admin", DbSeeder.DevAdminEmail, null))))).Business;
        Assert.False(kiosk.EtimsEnabled);
        Assert.DoesNotContain("etims", kiosk.Modules!);
        _db.HeadOffice[kiosk.BusinessId] = await Run(sp => sp.GetRequiredService<UltimatePosDbContext>().Branches.IgnoreQueryFilters()
            .Where(b => b.BusinessId == kiosk.BusinessId && b.BranchNumber == 0).Select(b => b.BranchId).SingleAsync());

        // Off: nothing of eTIMS can be set up.
        _db.SignInTo(kiosk.BusinessId);
        var refused = await Assert.ThrowsAsync<InvalidAssignmentException>(() => Run(sp => Etims(sp).RegisterDeviceAsync(
            new RegisterEtimsDeviceRequestDto(_db.HeadOffice[kiosk.BusinessId], "UPOSKIOSK0001", "1010160100"))));
        Assert.Contains("not switched on", refused.Message);
        Assert.Empty(_kra.Calls);

        // A super user switches it on: the module appears and a device can be registered.
        AsSuperUser();
        var on = await Run(sp => sp.GetRequiredService<UltimatePos.Application.Platform.PlatformService>().ChangeEtimsAsync(kiosk.BusinessId, new(true)));
        Assert.True(on.Business.EtimsEnabled);
        Assert.Equal("etims", on.Business.Modules!.Last());
        await Assert.ThrowsAsync<InvalidAssignmentException>(() => Run(sp =>
            sp.GetRequiredService<UltimatePos.Application.Platform.PlatformService>().ChangeEtimsAsync(kiosk.BusinessId, new(true))));
        _db.SignInTo(kiosk.BusinessId);
        var device = await Run(sp => Etims(sp).RegisterDeviceAsync(new RegisterEtimsDeviceRequestDto(_db.HeadOffice[kiosk.BusinessId], "UPOSKIOSK0001", "1010160100")));
        Assert.Contains(await Run(sp => sp.GetRequiredService<IEtimsRepository>().GetActiveDevicesAnyBusinessAsync()), d => d.DeviceId == device.EtimsDeviceId);

        // Off again: the worker leaves it alone and a manual run sends nothing; what happens meanwhile is never reported.
        AsSuperUser();
        await Run(sp => sp.GetRequiredService<UltimatePos.Application.Platform.PlatformService>().ChangeEtimsAsync(kiosk.BusinessId, new(false)));
        Assert.DoesNotContain(await Run(sp => sp.GetRequiredService<IEtimsRepository>().GetActiveDevicesAnyBusinessAsync()), d => d.DeviceId == device.EtimsDeviceId);
        _db.SignInTo(kiosk.BusinessId);
        var (sweets, pcs) = await ProductAsync("Sweets", TaxClassification.Standard, 10m);
        await AdjustAsync(sweets.ProductId, pcs, 100, 5m);
        var whileOff = await SellAsync((sweets.ProductId, pcs, 3));
        Assert.Equal(new EtimsRunResultDto(0, 0, 0, 0), await Run(sp => Sync(sp).RunAsync(device.EtimsDeviceId)));
        Assert.Empty(await SubmissionsAsync(device.EtimsDeviceId));

        AsSuperUser();
        await Run(sp => sp.GetRequiredService<UltimatePos.Application.Platform.PlatformService>().ChangeEtimsAsync(kiosk.BusinessId, new(true)));
        _db.SignInTo(kiosk.BusinessId);
        var afterwards = await SellAsync((sweets.ProductId, pcs, 2));
        await DrainAsync(device.EtimsDeviceId);
        var submissions = await SubmissionsAsync(device.EtimsDeviceId);
        Assert.DoesNotContain(submissions, s => s.SourceId == whileOff.SaleId);
        // ...but KRA is told what the kiosk held when it came back (100 in, 3 sold), so it can sell it.
        Assert.Single(submissions, s => s.SourceType == "OpeningStock");
        var resync = _kra.Bodies(EtimsEndpoints.StockIO).First();
        Assert.Equal(("06", 97m), (resync.GetProperty("sarTyCd").GetString(), resync.GetProperty("itemList")[0].GetProperty("qty").GetDecimal()));
        Assert.Contains(submissions, s => s.SourceId == afterwards.SaleId && s.DocumentType == EtimsDocumentType.Sale && s.Status == EtimsSubmissionStatus.Sent);
    }

    [PostgresFact]
    public async Task The_platform_registers_a_business_s_device_and_sees_what_KRA_refused_across_businesses()
    {
        _db.SignInTo(_db.Alvet);
        var branchId = await NewBranchAsync();
        await SwitchEtimsOnAsync();
        var (soap, pcs) = await ProductAsync("Bar soap", TaxClassification.Standard, 116m);
        await AdjustAsync(soap.ProductId, pcs, 10, 50m);

        // A real super user: no business, no branch, and only platform permissions (not Branches.AccessAll).
        _db.Caller.UserId = _db.SuperUserId;
        _db.Caller.BusinessId = null;
        _db.Caller.BranchId = null;
        _db.Caller.BranchIds = new();
        _db.Caller.Permissions = UltimatePos.Application.Identity.PermissionCatalog.Platform.Select(p => p.Name).ToHashSet();
        PlatformEtimsService Platform(IServiceProvider sp) => new(sp.GetRequiredService<IEtimsRepository>(), Etims(sp),
            sp.GetRequiredService<UltimatePos.Application.Platform.IPlatformRepository>(), sp.GetRequiredService<ITenantContext>(),
            sp.GetRequiredService<UltimatePos.Application.Notifications.ISystemEventRecorder>());

        var before = await Run(sp => Platform(sp).GetBusinessAsync(_db.Alvet));
        Assert.Null(before.Branches.Single(b => b.BranchId == branchId).Device);
        var device = await Run(sp => Platform(sp).RegisterDeviceAsync(_db.Alvet,
            new RegisterEtimsDeviceRequestDto(branchId, "UPOSPLATFORM01", "1010160100")));
        Assert.Equal(device.EtimsDeviceId, (await Run(sp => Platform(sp).GetBusinessAsync(_db.Alvet))).Branches.Single(b => b.BranchId == branchId).Device?.EtimsDeviceId);
        Assert.True(await Run(sp => sp.GetRequiredService<UltimatePosDbContext>().SystemEvents.IgnoreQueryFilters()
            .AnyAsync(e => e.BusinessId == _db.Alvet && e.EventType == UltimatePos.Application.Notifications.SystemEventTypes.SupportEtimsDeviceRegistered
                           && e.EntityId == device.EtimsDeviceId && e.ActorUserId == _db.SuperUserId)));

        // KRA refuses everything: the failures show on the platform, across businesses, with the business and branch they came from.
        _kra.Answer = (endpoint, body) => FakeEtimsClient.Result(endpoint, body, "910", "Request parameter error", null);
        try
        {
            var pass = await Run(sp => Platform(sp).RunNowAsync(_db.Alvet, device.EtimsDeviceId));
            Assert.True(pass.Failed > 0);
        }
        finally { _kra.Answer = null; }

        var problems = (await Run(sp => Platform(sp).GetSubmissionsAsync(1, 200, new PlatformEtimsSubmissionQuery(DeviceId: device.EtimsDeviceId)))).Items.ToList();
        Assert.NotEmpty(problems);
        Assert.All(problems, p =>
        {
            Assert.Equal(_db.Alvet, p.BusinessId);
            Assert.Equal(device.BranchName, p.BranchName);
            Assert.Equal(EtimsSubmissionStatus.Failed, p.Submission.Status);
            Assert.Equal("910", p.Submission.ResultCode);
        });
        var row = (await Run(sp => Platform(sp).GetOverviewAsync())).Businesses.Single(b => b.BusinessId == _db.Alvet);
        Assert.True(row.Failed >= problems.Count);
        Assert.NotNull(row.OldestProblemAt);

        var detail = await Run(sp => Platform(sp).GetSubmissionAsync(problems[0].Submission.EtimsSubmissionId));
        Assert.Equal(problems[0].Submission.EtimsSubmissionId, detail.Submission.EtimsSubmissionId);
        var retried = await Run(sp => Platform(sp).RetryAsync(problems[0].Submission.EtimsSubmissionId));
        Assert.Equal(EtimsSubmissionStatus.Pending, retried.Status);

        // A device that is not the business's is not found through it.
        await Assert.ThrowsAsync<NotFoundException>(() => Run(sp => Platform(sp).SearchItemClassesAsync(_db.Afya, device.EtimsDeviceId, null)));
    }
}
