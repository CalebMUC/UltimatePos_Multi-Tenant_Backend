using System.Text.Json;

namespace UltimatePos.Application.Etims
{
    /// <summary>What a device sends with every call after /initialize.</summary>
    public sealed record EtimsDeviceKey(string Tin, string BhfId, string CmcKey);

    /// <summary>
    /// One call to KRA. ResultCode is KRA's verdict (000 accepted, 001 nothing to return, anything else refused); it is null when no
    /// verdict came back at all (network, gateway or token failure), which is always worth retrying.
    /// </summary>
    public sealed record EtimsResult(string Endpoint, int HttpStatus, string? ResultCode, string? ResultMessage, string? ResultDate,
        JsonElement? Data, string RequestJson, string? ResponseJson, string? Error, TimeSpan Elapsed)
    {
        public bool IsSuccess => ResultCode == EtimsCodes.Success;
        public bool IsNoData => ResultCode == EtimsCodes.NoSearchResult;
        public bool HasVerdict => ResultCode is not null;

        public string Describe() => ResultCode is not null ? $"{ResultCode}: {ResultMessage}" : Error ?? $"HTTP {HttpStatus}";

        /// <summary>The first property of that name anywhere in data (KRA nests some replies, e.g. data.info.cmcKey).</summary>
        public string? Find(params string[] names)
        {
            if (Data is not { } data) return null;
            foreach (var name in names)
                if (FindIn(data, name) is { } found)
                    return found.ValueKind == JsonValueKind.String ? found.GetString() : found.ValueKind == JsonValueKind.Null ? null : found.GetRawText();
            return null;
        }

        private static JsonElement? FindIn(JsonElement element, string name)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in element.EnumerateObject())
                    if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                        return property.Value;
                foreach (var property in element.EnumerateObject())
                    if (FindIn(property.Value, name) is { } nested)
                        return nested;
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in element.EnumerateArray())
                    if (FindIn(item, name) is { } nested)
                        return nested;
            }
            return null;
        }
    }

    /// <summary>Transport to the KRA eTIMS OSCU API (through KRA's Apigee gateway). Never throws for a failed call.</summary>
    public interface IEtimsClient
    {
        Task<EtimsResult> InitializeAsync(EtimsInitializeRequest request, CancellationToken cancellationToken = default);
        Task<EtimsResult> PostAsync(string endpoint, EtimsDeviceKey device, string jsonBody, CancellationToken cancellationToken = default);
    }

    /// <summary>OSCU paths, relative to Etims:BaseUrl.</summary>
    public static class EtimsEndpoints
    {
        public const string Initialize = "initialize";
        public const string SelectCodeList = "selectCodeList";
        public const string SelectItemClass = "selectItemClass";
        public const string SelectTaxpayerInfo = "selectTaxpayerInfo";
        public const string CustomerPinInfo = "customerPinInfo";
        public const string BranchList = "branchList";
        public const string SelectNoticeList = "selectNoticeList";
        public const string BranchUserAccount = "branchUserAccount";
        public const string BranchSendCustomerInfo = "branchSendCustomerInfo";
        public const string BranchInsuranceInfo = "branchInsuranceInfo";
        public const string SaveItem = "saveItem";
        public const string SaveItemComposition = "saveItemComposition";
        public const string ItemInfo = "itemInfo";
        public const string StockIO = "insert/stockIO";
        public const string StockMaster = "save/stockMaster";
        public const string GetPurchaseTransactionInfo = "getPurchaseTransactionInfo";
        public const string SendPurchaseTransactionInfo = "sendPurchaseTransactionInfo";
        public const string SendSalesTransaction = "sendSalesTransaction";
        public const string SelectInvoiceDetail = "selectInvoiceDetail";
        public const string SelectSalesTransactions = "selectSalesTransactions";
        public const string ImportedItemInfo = "importedItemInfo";
        public const string ImportedItemConvertedInfo = "importedItemConvertedInfo";
        public const string SelectStockMoveLists = "selectStockMoveLists";

        public static string EndpointFor(Domain.Entities.EtimsDocumentType type) => type switch
        {
            Domain.Entities.EtimsDocumentType.Item => SaveItem,
            Domain.Entities.EtimsDocumentType.Sale or Domain.Entities.EtimsDocumentType.CreditNote => SendSalesTransaction,
            Domain.Entities.EtimsDocumentType.Purchase => SendPurchaseTransactionInfo,
            Domain.Entities.EtimsDocumentType.StockIO => StockIO,
            _ => StockMaster,
        };
    }

    /// <summary>Every OSCU operation, typed. Used by the sync service, the admin endpoints and the sandbox runner.</summary>
    public sealed class EtimsApi
    {
        /// <summary>lastReqDt that asks for everything.</summary>
        public const string FromTheBeginning = "20180101000000";

        private readonly IEtimsClient _client;
        private readonly EtimsDeviceKey _device;

        public EtimsApi(IEtimsClient client, EtimsDeviceKey device)
        {
            _client = client;
            _device = device;
        }

        public EtimsDeviceKey Device => _device;

        public Task<EtimsResult> SendAsync(string endpoint, object body, CancellationToken ct = default) =>
            _client.PostAsync(endpoint, _device, EtimsJson.Serialize(body), ct);

        private Task<EtimsResult> SelectAsync(string endpoint, string? lastRequest, CancellationToken ct) =>
            SendAsync(endpoint, new EtimsLastRequest(_device.Tin, _device.BhfId, lastRequest ?? FromTheBeginning), ct);

        public Task<EtimsResult> CodeListAsync(string? lastRequest = null, CancellationToken ct = default) => SelectAsync(EtimsEndpoints.SelectCodeList, lastRequest, ct);
        public Task<EtimsResult> ItemClassesAsync(string? lastRequest = null, CancellationToken ct = default) => SelectAsync(EtimsEndpoints.SelectItemClass, lastRequest, ct);
        public Task<EtimsResult> TaxpayerInfoAsync(string? lastRequest = null, CancellationToken ct = default) => SelectAsync(EtimsEndpoints.SelectTaxpayerInfo, lastRequest, ct);
        public Task<EtimsResult> BranchesAsync(string? lastRequest = null, CancellationToken ct = default) => SelectAsync(EtimsEndpoints.BranchList, lastRequest, ct);
        public Task<EtimsResult> NoticesAsync(string? lastRequest = null, CancellationToken ct = default) => SelectAsync(EtimsEndpoints.SelectNoticeList, lastRequest, ct);
        public Task<EtimsResult> ItemsAsync(string? lastRequest = null, CancellationToken ct = default) => SelectAsync(EtimsEndpoints.ItemInfo, lastRequest, ct);
        public Task<EtimsResult> PurchasesFromSuppliersAsync(string? lastRequest = null, CancellationToken ct = default) => SelectAsync(EtimsEndpoints.GetPurchaseTransactionInfo, lastRequest, ct);
        public Task<EtimsResult> SalesAsync(string? lastRequest = null, CancellationToken ct = default) => SelectAsync(EtimsEndpoints.SelectSalesTransactions, lastRequest, ct);
        public Task<EtimsResult> ImportedItemsAsync(string? lastRequest = null, CancellationToken ct = default) => SelectAsync(EtimsEndpoints.ImportedItemInfo, lastRequest, ct);
        public Task<EtimsResult> StockMovesAsync(string? lastRequest = null, CancellationToken ct = default) => SelectAsync(EtimsEndpoints.SelectStockMoveLists, lastRequest, ct);

        public Task<EtimsResult> CustomerPinAsync(string pin, CancellationToken ct = default) =>
            SendAsync(EtimsEndpoints.CustomerPinInfo, new EtimsCustomerPinRequest(_device.Tin, _device.BhfId, FromTheBeginning, pin), ct);
        public Task<EtimsResult> InvoiceDetailAsync(long invoiceNumber, CancellationToken ct = default) =>
            SendAsync(EtimsEndpoints.SelectInvoiceDetail, new EtimsInvoiceDetailRequest(_device.Tin, _device.BhfId, invoiceNumber.ToString()), ct);

        public Task<EtimsResult> SaveBranchUserAsync(EtimsBranchUserRequest body, CancellationToken ct = default) => SendAsync(EtimsEndpoints.BranchUserAccount, body, ct);
        public Task<EtimsResult> SaveCustomerAsync(EtimsBranchCustomerRequest body, CancellationToken ct = default) => SendAsync(EtimsEndpoints.BranchSendCustomerInfo, body, ct);
        public Task<EtimsResult> SaveInsuranceAsync(EtimsBranchInsuranceRequest body, CancellationToken ct = default) => SendAsync(EtimsEndpoints.BranchInsuranceInfo, body, ct);
        public Task<EtimsResult> SaveItemAsync(EtimsItemRequest body, CancellationToken ct = default) => SendAsync(EtimsEndpoints.SaveItem, body, ct);
        public Task<EtimsResult> SaveItemCompositionAsync(EtimsItemCompositionRequest body, CancellationToken ct = default) => SendAsync(EtimsEndpoints.SaveItemComposition, body, ct);
        public Task<EtimsResult> SaveStockIoAsync(EtimsStockIoRequest body, CancellationToken ct = default) => SendAsync(EtimsEndpoints.StockIO, body, ct);
        public Task<EtimsResult> SaveStockMasterAsync(EtimsStockMasterRequest body, CancellationToken ct = default) => SendAsync(EtimsEndpoints.StockMaster, body, ct);
        public Task<EtimsResult> SendPurchaseAsync(EtimsPurchaseRequest body, CancellationToken ct = default) => SendAsync(EtimsEndpoints.SendPurchaseTransactionInfo, body, ct);
        public Task<EtimsResult> SendSaleAsync(EtimsSalesRequest body, CancellationToken ct = default) => SendAsync(EtimsEndpoints.SendSalesTransaction, body, ct);
        public Task<EtimsResult> ConvertImportedItemAsync(EtimsImportedItemConvertedRequest body, CancellationToken ct = default) => SendAsync(EtimsEndpoints.ImportedItemConvertedInfo, body, ct);
    }
}
