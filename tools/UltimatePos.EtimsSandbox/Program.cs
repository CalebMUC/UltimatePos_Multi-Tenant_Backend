// Runs the Postman sandbox collection's scenario (folders 1-9) through the POS's own eTIMS code: KraEtimsClient for transport and
// EtimsPayloadBuilder for every document body. Adds checks the collection does not make: the code list holds every code the POS
// sends, and items with the POS's default units (NO for pieces, three-letter units such as LTR) are accepted.
//
//   ETIMS_CONSUMER_KEY / ETIMS_CONSUMER_SECRET / ETIMS_APIGEE_APP_ID   the Apigee app (required)
//   ETIMS_TIN (P052446551J)  ETIMS_BHF_ID (00)  ETIMS_DVC_SRL_NO (UPOSP052446551J001)
//   ETIMS_CMC_KEY            the device's key, when KRA will not initialise it again
//   dotnet run --project tools/UltimatePos.EtimsSandbox -- --out results.json [--number-base 5000]
//
// Secrets are never written to the results: headers are not recorded and the cmcKey is masked.

using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using UltimatePos.Application.Etims;
using UltimatePos.Infrastructure.Etims;

string Env(string name, string fallback = "") => Environment.GetEnvironmentVariable(name) is { Length: > 0 } v ? v : fallback;
string? Arg(string name) => args.SkipWhile(a => a != name).Skip(1).FirstOrDefault();

var options = new EtimsOptions
{
    BaseUrl = Env("ETIMS_BASE_URL", "https://sbx.kra.go.ke/etims-oscu/api/v1"),
    TokenUrl = Env("ETIMS_TOKEN_URL", "https://sbx.kra.go.ke/v1/token/generate"),
    ConsumerKey = Env("ETIMS_CONSUMER_KEY"),
    ConsumerSecret = Env("ETIMS_CONSUMER_SECRET"),
    ApigeeAppId = Env("ETIMS_APIGEE_APP_ID"),
    TimeoutSeconds = 60,
};
if (!options.IsConfigured)
{
    Console.Error.WriteLine("Set ETIMS_CONSUMER_KEY, ETIMS_CONSUMER_SECRET and ETIMS_APIGEE_APP_ID.");
    return 2;
}

var tin = Env("ETIMS_TIN", "P052446551J");
var bhfId = Env("ETIMS_BHF_ID", "00");
var serial = Env("ETIMS_DVC_SRL_NO", "UPOSP052446551J001");
var outPath = Arg("--out") ?? "etims-sandbox-results.json";
// Invoice, purchase and stock numbers must be new on the device: by default, minutes since 2026-01-01 x 10 (unique per run).
var numberBase = long.TryParse(Arg("--number-base"), out var nb) ? nb : (long)(DateTime.UtcNow - new DateTime(2026, 1, 1)).TotalMinutes * 10;

var http = new HttpClient { BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/"), Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds) };
var client = new KraEtimsClient(http, new MemoryCache(new MemoryCacheOptions()), Options.Create(options));
var steps = new List<JsonObject>();
var checks = new List<JsonObject>();
var started = DateTime.UtcNow;

JsonNode? Parse(string? json)
{
    if (string.IsNullOrWhiteSpace(json)) return null;
    try { return JsonNode.Parse(json); } catch (JsonException) { return JsonValue.Create(json.Length > 2000 ? json[..2000] + "..." : json); }
}

// Big lists (code list, item classes) are summarised, not copied.
JsonNode? Shrink(JsonNode? node, int depth = 0)
{
    switch (node)
    {
        case JsonArray array when array.Count > 5:
            var kept = new JsonArray(array.Take(3).Select(n => Shrink(n?.DeepClone(), depth + 1)).ToArray());
            kept.Add(JsonValue.Create($"... {array.Count - 3} more ({array.Count} in all)"));
            return kept;
        case JsonArray array:
            return new JsonArray(array.Select(n => Shrink(n?.DeepClone(), depth + 1)).ToArray());
        case JsonObject obj:
            var copy = new JsonObject();
            foreach (var (key, value) in obj)
                copy[key] = key.Equals("cmcKey", StringComparison.OrdinalIgnoreCase) && value is not null ? "***masked***" : Shrink(value?.DeepClone(), depth + 1);
            return copy;
        default:
            return node?.DeepClone();
    }
}

EtimsResult Record(string folder, string name, EtimsResult result, string[] expected, string? note = null)
{
    var ok = result.ResultCode is not null && expected.Contains(result.ResultCode);
    steps.Add(new JsonObject
    {
        ["folder"] = folder, ["name"] = name, ["endpoint"] = result.Endpoint, ["ok"] = ok,
        ["expected"] = string.Join(" or ", expected), ["httpStatus"] = result.HttpStatus, ["resultCd"] = result.ResultCode,
        ["resultMsg"] = result.ResultMessage, ["resultDt"] = result.ResultDate, ["error"] = result.Error,
        ["elapsedMs"] = (int)result.Elapsed.TotalMilliseconds, ["note"] = note,
        ["request"] = Shrink(Parse(result.RequestJson)), ["response"] = Shrink(Parse(result.ResponseJson)),
    });
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {folder} / {name}: {result.Describe()} ({(int)result.Elapsed.TotalMilliseconds} ms)");
    return result;
}

void Check(string what, bool ok, string detail)
{
    checks.Add(new JsonObject { ["check"] = what, ["ok"] = ok, ["detail"] = detail });
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  check: {what} - {detail}");
}

var success = new[] { EtimsCodes.Success };
var read = new[] { EtimsCodes.Success, EtimsCodes.NoSearchResult };
var user = new EtimsUser("Admin", "Admin");

// ---- 1. Auth and Init ----
{
    var watch = Stopwatch.StartNew();
    using var request = new HttpRequestMessage(HttpMethod.Get, $"{options.TokenUrl}?grant_type=client_credentials");
    request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{options.ConsumerKey}:{options.ConsumerSecret}")));
    using var response = await http.SendAsync(request);
    var body = await response.Content.ReadAsStringAsync();
    var hasToken = body.Contains("access_token");
    steps.Add(new JsonObject
    {
        ["folder"] = "1. Auth and Init", ["name"] = "Access Token", ["endpoint"] = "token/generate", ["ok"] = response.IsSuccessStatusCode && hasToken,
        ["expected"] = "HTTP 200 with access_token", ["httpStatus"] = (int)response.StatusCode, ["resultCd"] = null,
        ["resultMsg"] = hasToken ? "access_token issued (not recorded)" : body.Length > 300 ? body[..300] : body,
        ["elapsedMs"] = (int)watch.ElapsedMilliseconds, ["request"] = null, ["response"] = null,
    });
    Console.WriteLine($"{(hasToken ? "PASS" : "FAIL")}  1. Auth and Init / Access Token: HTTP {(int)response.StatusCode}");
    if (!hasToken) return Finish(1);
}

var init = Record("1. Auth and Init", "OSCU initialization",
    await client.InitializeAsync(new EtimsInitializeRequest(tin, bhfId, serial)), success);
var cmcKey = init.IsSuccess ? init.Find("cmcKey") : null;
if (cmcKey is null && Env("ETIMS_CMC_KEY") is { Length: > 0 } given)
{
    cmcKey = given;
    steps[^1]["note"] = "KRA did not initialise the device again; continuing with the cmcKey from ETIMS_CMC_KEY.";
}
if (cmcKey is null)
{
    Console.Error.WriteLine("No cmcKey: initialization failed and ETIMS_CMC_KEY is not set.");
    return Finish(1);
}

var api = new EtimsApi(client, new EtimsDeviceKey(tin, bhfId, cmcKey));

// ---- 2. Code and Data Sync ----
var codes = Record("2. Code and Data Sync", "Get Code list", await api.CodeListAsync("20180523000000"), read);
var classes = Record("2. Code and Data Sync", "Get Item Classification List", await api.ItemClassesAsync("20180523000000"), read);
Record("2. Code and Data Sync", "Get Taxpayer Info", await api.TaxpayerInfoAsync("20180523000000"), read);
Record("2. Code and Data Sync", "Get Customer PIN Info (custmTin body, as in KRA's spec)", await api.CustomerPinAsync("A123456789Z"), read);
Record("2. Code and Data Sync", "Get Customer PIN Info (tin + bhfId + lastReqDt + custmTin)",
    await api.SendAsync(EtimsEndpoints.CustomerPinInfo, new { tin, bhfId, lastReqDt = "20180523000000", custmTin = "A123456789Z" }), read);
Record("2. Code and Data Sync", "Get Customer PIN Info (collection's body)",
    await api.SendAsync(EtimsEndpoints.CustomerPinInfo, new EtimsLastRequest(tin, bhfId, "20180523000000")), read);
Record("2. Code and Data Sync", "Branch List", await api.BranchesAsync("20180523000000"), read);
Record("2. Code and Data Sync", "Get notice list", await api.NoticesAsync("20180523000000"), read);

// Every code the POS sends must be in KRA's list for this taxpayer.
var codeClasses = new Dictionary<string, (string Name, HashSet<string> Codes)>();
if (codes.Data is { } codeData && codeData.TryGetProperty("clsList", out var clsList))
{
    foreach (var cls in clsList.EnumerateArray())
    {
        var cd = cls.GetProperty("cdCls").GetString() ?? "";
        var set = cls.TryGetProperty("dtlList", out var dtl) && dtl.ValueKind == JsonValueKind.Array
            ? dtl.EnumerateArray().Select(d => d.GetProperty("cd").GetString() ?? "").ToHashSet() : new HashSet<string>();
        codeClasses[cd] = (cls.TryGetProperty("cdClsNm", out var nm) ? nm.GetString() ?? "" : "", set);
    }
    Check("code list downloaded", codeClasses.Count > 0, $"{codeClasses.Count} code classes: " +
        string.Join("; ", codeClasses.OrderBy(c => c.Key).Select(c => $"{c.Key} {c.Value.Name} ({c.Value.Codes.Count})")));

    void Expect(string label, string nameHint, params string[] wanted)
    {
        var cls = codeClasses.FirstOrDefault(c => c.Value.Name.Contains(nameHint, StringComparison.OrdinalIgnoreCase));
        if (cls.Key is null) { Check(label, false, $"no code class named like '{nameHint}'"); return; }
        var missing = wanted.Where(w => !cls.Value.Codes.Contains(w)).ToList();
        Check(label, missing.Count == 0, $"class {cls.Key} {cls.Value.Name}: " +
            (missing.Count == 0 ? $"has {string.Join(", ", wanted)}" : $"missing {string.Join(", ", missing)}"));
    }
    foreach (var (cls, label) in new[] { ("10", "quantity units"), ("17", "packing units") })
        if (codes.Data is { } cd && cd.GetProperty("clsList").EnumerateArray().FirstOrDefault(c => c.GetProperty("cdCls").GetString() == cls) is { ValueKind: JsonValueKind.Object } list)
            Check($"{label} offered", true, string.Join(", ", list.GetProperty("dtlList").EnumerateArray().Select(d => $"{d.GetProperty("cd").GetString()} {d.GetProperty("cdNm").GetString()}")));
    Expect("tax types", "Tax", "A", "B", "C", "D", "E");
    Expect("payment types", "Payment", "01", "02", "06");
    Expect("packaging unit NT", "Packing", "NT");
    Expect("quantity units the POS maps to", "Quantity", "KG", "BX", "NO", "LTR", "GRM", "MGM", "TNE", "MTR", "BG", "PA", "DZ", "PR", "SET", "RO", "CA", "TU", "BE");
    Expect("stock in/out types", "Stock", "02", "03", "04", "05", "06", "11", "13", "14", "16");
    Expect("refund reason", "Refund", "06");
    Expect("item types", "Item Type", "1", "2", "3");
}
if (classes.Data is { } classData && classData.TryGetProperty("itemClsList", out var clsItems))
{
    var known = clsItems.EnumerateArray().Select(c => c.GetProperty("itemClsCd").GetString()).ToHashSet();
    Check("item classifications used exist", known.Contains("1010160100") && known.Contains("1010150000"),
        $"{known.Count} classifications; 1010160100: {known.Contains("1010160100")}, 1010150000: {known.Contains("1010150000")}");
}

// ---- 3. Branch Setup ----
Record("3. Branch Setup", "Send branch user account", await api.SaveBranchUserAsync(new EtimsBranchUserRequest(
    "Admin", "Admin", "Admin@1234", null, null, null, null, "Y", "Admin", "Admin", "Admin", "Admin")), success);
Record("3. Branch Setup", "Send customer information", await api.SaveCustomerAsync(new EtimsBranchCustomerRequest(
    "100000001", "A123456789Z", "TEST CUSTOMER LIMITED", null, null, null, null, "Y", null, "Admin", "Admin", "Admin", "Admin")), success);
Record("3. Branch Setup", "Send branch insurance information", await api.SaveInsuranceAsync(new EtimsBranchInsuranceRequest(
    "INS001", "Test Insurer", 10, "Y", "Admin", "Admin", "Admin", "Admin")), success);

// ---- 4. Items ----
// KRA numbers a taxpayer's item codes in one series, whatever the prefix, and accepts a new code only if it is exactly the next one
// ("Invalid itemCd Sequence ... either reused or not incremented properly"). Its own seeded items sit in a separate 1000000+ series.
// Read what it holds and continue from there; a code is used up only when KRA accepts it.
var held = Record("4. Items", "Get Item Info (where the item code series stands)", await api.ItemsAsync("20180523000000"), read);
var heldCodes = held.Data is { } heldData && heldData.TryGetProperty("itemList", out var heldList) && heldList.ValueKind == JsonValueKind.Array
    ? heldList.EnumerateArray().Select(i => i.GetProperty("itemCd").GetString() ?? "").ToList() : new List<string>();
int SeqOf(string code) => code.Length >= 7 && int.TryParse(code[^7..], out var n) ? n : 0;
// KRA expects the smallest number not yet used (codes registered elsewhere can sit higher up without moving it), and its refusal
// gives that number's last digit.
var used = heldCodes.Select(SeqOf).Where(n => n is > 0 and < 1_000_000).ToHashSet();
int SmallestUnused(int? lastDigit = null) => Enumerable.Range(1, 999_999).First(n => !used.Contains(n) && (lastDigit is null || n % 10 == lastDigit));
var next = SmallestUnused();
Check("item code series", true, $"KRA holds {heldCodes.Count} item(s) ({string.Join(", ", heldCodes)}); the smallest unused number is {next:D7}");

async Task<EtimsItemRef?> SaveNew(string name, string itemType, string quantityUnit, string codeUnit, string classCode, string taxType, decimal price, string? note = null,
    int digits = 7)
{
    EtimsItemRef Item() => new($"KE{itemType}NT{codeUnit}{next.ToString(new string('0', digits))}", classCode, name, null, "NT", quantityUnit, taxType);
    var item = Item();
    var result = await api.SaveItemAsync(EtimsPayloadBuilder.Item(item, itemType, "KE", price, true, user));
    var hint = System.Text.RegularExpressions.Regex.Match(result.Error ?? "", @"Expected sequence ending with:? \**(\d+)");
    if (!result.IsSuccess && hint.Success && int.Parse(hint.Groups[1].Value) != next)
    {
        Record("4. Items", $"Save item {item.ItemCode} ({name}) (first try, out of sequence)", result, success, "KRA names the number it expects; the runner moves to it.");
        next = int.Parse(hint.Groups[1].Value);
        item = Item();
        result = await api.SaveItemAsync(EtimsPayloadBuilder.Item(item, itemType, "KE", price, true, user));
    }
    Record("4. Items", $"Save item {item.ItemCode} ({name})", result, success, note);
    if (!result.IsSuccess) return null;
    used.Add(next);
    next = SmallestUnused();
    return item;
}

var itemA = await SaveNew("POS Test Product A", "2", "KG", "KG", "1010160100", EtimsCodes.TaxStandard, 1160m, "Collection item 1 (finished, 16% VAT).");
var itemB = await SaveNew("POS Test Product B", "2", "BX", "BX", "1010150000", EtimsCodes.TaxExempt, 500m, "Collection item 2 (finished, exempt).");
var itemC = await SaveNew("POS Test Raw Material C", "1", "KG", "KG", "1010160100", EtimsCodes.TaxStandard, 232m, "Collection item 3 (raw material, 16% VAT).");
if (itemA is null || itemB is null || itemC is null)
    return Finish(1);
var itemPcs = await SaveNew("POS Test Soap (NO)", "2", "NO", "NO", "1010160100", EtimsCodes.TaxStandard, 116m,
    "Not in the collection: NO (number), the unit the POS gives pieces.");
var itemXu = await SaveNew("POS Test Soap (U, 8-digit sequence)", "2", "U", "U", "1010160100", EtimsCodes.TaxStandard, 116m,
    "Not in the collection: the one-letter unit U, with an 8-digit sequence to keep the code 14 characters.", digits: 8);
var itemLtr = await SaveNew("POS Test Cooking Oil (LTR, 6-digit sequence)", "2", "LTR", "LTR", "1010160100", EtimsCodes.TaxStandard, 350m,
    "Not in the collection: the three-letter unit LTR, with a 6-digit sequence to keep the code 14 characters.", digits: 6);
Record("4. Items", "Save item again (update an existing item)",
    await api.SaveItemAsync(EtimsPayloadBuilder.Item(itemA with { ItemName = itemA.ItemName + " v2" }, "2", "KE", 1200m, true, user)), success,
    "Not in the collection: can a registered item be updated by saving it again?");
Record("4. Items", "Get Item Info", await api.ItemsAsync("20180523000000"), read);

// ---- 5. Opening Stock ----
// sarNo (and perhaps invcNo) must be exactly the next number KRA expects. Start anywhere; KRA says which it wants ("Expected: N").
async Task<EtimsResult> Numbered(string folder, string name, long[] counter, Func<long, Task<EtimsResult>> send, string? note = null)
{
    var result = await send(counter[0] + 1);
    if (!result.IsSuccess && System.Text.RegularExpressions.Regex.Match(result.Error ?? result.ResultMessage ?? "", @"expected:?\s*(\d+)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase) is { Success: true } m)
    {
        Record(folder, name + " (first try, number out of sequence)", result, success, "KRA names the number it expects; the runner uses it.");
        counter[0] = long.Parse(m.Groups[1].Value) - 1;
        result = await send(counter[0] + 1);
    }
    Record(folder, name, result, success, note);
    if (result.IsSuccess) counter[0]++;
    return result;
}
var now = DateTime.UtcNow;
var sar = new[] { numberBase };
var invc = new[] { numberBase };
await Numbered("5. Opening Stock", "Stock IN - opening stock (06)", sar, n => api.SaveStockIoAsync(EtimsPayloadBuilder.StockIo(new EtimsStockDocument(n, 0, "06", now,
    new[] { new EtimsLine(itemA, 100, 116000m), new EtimsLine(itemB, 100, 50000m), new EtimsLine(itemC, 10, 2320m) }, user))));
Record("5. Opening Stock", "Stock Master - item 1 = 100", await api.SaveStockMasterAsync(EtimsPayloadBuilder.StockMaster(itemA.ItemCode, 100, user)), success);
Record("5. Opening Stock", "Stock Master - item 2 = 100", await api.SaveStockMasterAsync(EtimsPayloadBuilder.StockMaster(itemB.ItemCode, 100, user)), success);
Record("5. Opening Stock", "Stock Master - item 3 = 10", await api.SaveStockMasterAsync(EtimsPayloadBuilder.StockMaster(itemC.ItemCode, 10, user)), success);
var extraStock = new[] { itemPcs, itemXu, itemLtr }.OfType<EtimsItemRef>().ToList();
if (extraStock.Count > 0)
{
    await Numbered("5. Opening Stock", "Stock IN - opening stock of the default-unit items (06)", sar, n => api.SaveStockIoAsync(EtimsPayloadBuilder.StockIo(
        new EtimsStockDocument(n, 0, "06", now, extraStock.Select(i => new EtimsLine(i, 10, 10 * 116m)).ToList(), user))),
        "Not in the collection: the items registered with the POS's default units get stock before they are sold.");
    foreach (var i in extraStock)
        Record("5. Opening Stock", $"Stock Master - {i.ItemCode} = 10", await api.SaveStockMasterAsync(EtimsPayloadBuilder.StockMaster(i.ItemCode, 10, user)), success);
}
Record("5. Opening Stock", "Send Item Composition (collection folder 4)", await api.SaveItemCompositionAsync(new EtimsItemCompositionRequest(itemA.ItemCode, itemC.ItemCode, 2, "Admin", "Admin")), success,
    "Moved after the opening stock: KRA refuses a composition whose component has no stock master yet.");

// ---- 6. Purchase ----
var purchases = Record("6. Purchase", "Get purchase transaction information", await api.PurchasesFromSuppliersAsync("20180523000000"), read);
Record("6. Purchase", "Send purchase transaction information", await api.SendPurchaseAsync(EtimsPayloadBuilder.Purchase(new EtimsPurchaseDocument(
    numberBase + 1, "P000000000D", "00", "TEST SUPPLIER", numberBase + 1, "01", now, new[] { new EtimsLine(itemC, 50, 11600m) }, user))), success,
    purchases.IsNoData ? "KRA returned no purchase records for this PIN, and it only accepts a purchase it already holds a record of." : null);

// ---- 7. Sales ----
var saleDoc = new EtimsSaleDocument(0, 0, false, "A123456789Z", "TEST CUSTOMER LIMITED", null, "01", now, null,
    new[] { new EtimsLine(itemA, 2, 2320m), new EtimsLine(itemB, 5, 2500m) },
    "Quickcrate Express Limited", "Kenya", "Thank you for shopping with us", "Welcome again", user);
var sale = await Numbered("7. Sales", "Save sales transaction (normal sale)", invc, n => api.SendSaleAsync(EtimsPayloadBuilder.Sale(saleDoc with { InvoiceNumber = n })));
var saleNo = invc[0];
if (sale.IsSuccess)
    Check("sale signed by KRA", sale.Find("rcptSign") is { Length: > 0 } && sale.Find("intrlData") is { Length: > 0 },
        $"curRcptNo {sale.Find("curRcptNo", "rcptNo")}, totRcptNo {sale.Find("totRcptNo")}, sdcDateTime {sale.Find("sdcDateTime", "vsdcRcptPbctDate")}, rcptSign present: {sale.Find("rcptSign") is { Length: > 0 }}");
await Numbered("7. Sales", "Stock OUT - sale (11)", sar, n => api.SaveStockIoAsync(EtimsPayloadBuilder.StockIo(new EtimsStockDocument(n, 0, "11", now,
    new[] { new EtimsLine(itemA, 2, 2320m), new EtimsLine(itemB, 5, 2500m) }, user))));
Record("7. Sales", "Stock Master - item 1 = 98", await api.SaveStockMasterAsync(EtimsPayloadBuilder.StockMaster(itemA.ItemCode, 98, user)), success);
Record("7. Sales", "Stock Master - item 2 = 95", await api.SaveStockMasterAsync(EtimsPayloadBuilder.StockMaster(itemB.ItemCode, 95, user)), success);
Record("7. Sales", "Select Invoice Details", await api.InvoiceDetailAsync(saleNo), read);
Record("7. Sales", "Select sales transactions", await api.SalesAsync("20180523000000"), read);
await Numbered("7. Sales", "Save sales transaction (credit note / refund)", invc, n => api.SendSaleAsync(EtimsPayloadBuilder.Sale(saleDoc with
    { InvoiceNumber = n, OriginalInvoiceNumber = saleNo, IsCreditNote = true, RefundReasonCode = EtimsCodes.RefundReasonReturn })));
await Numbered("7. Sales", "Stock IN - refund (03)", sar, n => api.SaveStockIoAsync(EtimsPayloadBuilder.StockIo(new EtimsStockDocument(n, 0, "03", now,
    new[] { new EtimsLine(itemA, 2, 2320m), new EtimsLine(itemB, 5, 2500m) }, user))), "Not in the collection: the POS reports returned stock as 03.");
Record("7. Sales", "Stock Master - item 1 = 100 (after the refund)", await api.SaveStockMasterAsync(EtimsPayloadBuilder.StockMaster(itemA.ItemCode, 100, user)), success,
    "Not in the collection: the refund put the stock back.");
var defaultUnitLines = new[] { itemPcs is null ? null : new EtimsLine(itemPcs, 3, 348m), itemXu is null ? null : new EtimsLine(itemXu, 2, 232m),
    itemLtr is null ? null : new EtimsLine(itemLtr, 1, 350m, 35m) }.OfType<EtimsLine>().ToList();
if (defaultUnitLines.Count > 0)
{
    var lines = defaultUnitLines;
    await Numbered("7. Sales", "Save sales transaction (POS default units, with a discount)", invc, n => api.SendSaleAsync(EtimsPayloadBuilder.Sale(new EtimsSaleDocument(
        n, 0, false, null, "Walk-in", null, "06", now, null, lines, "Quickcrate Express Limited", "Kenya", null, null, user))),
        "Not in the collection: a mobile-money sale of the items registered with the POS's default units, one line discounted 10%.");
}

// ---- 8. Imports ----
var imports = Record("8. Imports", "Get imported item information", await api.ImportedItemsAsync("20180523000000"), read);
var task = (Code: "20230209010498", Date: "01022023", Seq: 1, Hs: "63079000");
var importNote = "KRA's sample values (no import was returned to convert): a refusal is expected.";
if (imports.Data is { } importData && importData.TryGetProperty("itemList", out var importList) && importList.GetArrayLength() > 0)
{
    var first = importList[0];
    task = (first.GetProperty("taskCd").GetString()!, first.GetProperty("dclDe").GetString()!, first.GetProperty("itemSeq").GetInt32(),
        first.GetProperty("hsCd").GetString()!);
    importNote = "Converts the first import KRA returned.";
}
Record("8. Imports", "Send (converted) imported item information", await api.ConvertImportedItemAsync(new EtimsImportedItemConvertedRequest(
    task.Code, task.Date, task.Seq, task.Hs, itemA.ItemClassCode, itemA.ItemCode, "3", "remark", "Admin", "Admin")),
    importNote.StartsWith("KRA's") ? new[] { "000", "001", "910", "921", "999", "990", "994" } : success, importNote);

// ---- 9. Stock Reads ----
Record("9. Stock Reads", "get MoveList", await api.StockMovesAsync("20180523000000"), read);

return Finish(0);

int Finish(int code)
{
    var report = new JsonObject
    {
        ["runner"] = "UltimatePos.EtimsSandbox (KraEtimsClient + EtimsPayloadBuilder)",
        ["startedUtc"] = started.ToString("O"), ["finishedUtc"] = DateTime.UtcNow.ToString("O"),
        ["baseUrl"] = options.BaseUrl, ["tin"] = tin, ["bhfId"] = bhfId, ["dvcSrlNo"] = serial, ["numberBase"] = numberBase,
        ["passed"] = steps.Count(s => s["ok"]!.GetValue<bool>()), ["failed"] = steps.Count(s => !s["ok"]!.GetValue<bool>()),
        ["steps"] = new JsonArray(steps.ToArray()), ["checks"] = new JsonArray(checks.ToArray()),
    };
    File.WriteAllText(outPath, report.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"\n{report["passed"]} passed, {report["failed"]} failed. Results: {Path.GetFullPath(outPath)}");
    return code;
}
