using UltimatePos.Application.Common.Dtos;
using UltimatePos.Domain.Entities;

namespace UltimatePos.Application.Etims
{
    /// <summary>The "Etims" section's worker and receipt settings. Credentials are read by the client (EtimsOptions).</summary>
    public sealed class EtimsSettings
    {
        /// <summary>Run the background sweep/send loop. It also needs credentials, and a device per reporting branch.</summary>
        public bool WorkersEnabled { get; set; } = true;
        public int IntervalSeconds { get; set; } = 15;
        /// <summary>Sends before a submission is Rejected and left for a manager to retry.</summary>
        public int MaxAttempts { get; set; } = 10;
        public int BatchSize { get; set; } = 50;
        /// <summary>Receipt QR codes link here followed by PIN + branch id + receipt signature.</summary>
        public string ReceiptVerificationUrl { get; set; } = "https://etims-sbx.kra.go.ke/common/link/etims/receipt/indexEtimsReceiptData?Data=";
    }

    public sealed record EtimsSubmissionQuery(Guid? DeviceId = null, EtimsSubmissionStatus? Status = null,
        EtimsDocumentType? DocumentType = null, Guid? SourceId = null);

    /// <param name="Status">Null = what needs attention: Failed and Rejected.</param>
    public sealed record PlatformEtimsSubmissionQuery(Guid? BusinessId = null, Guid? DeviceId = null, EtimsSubmissionStatus? Status = null,
        EtimsDocumentType? DocumentType = null);

    /// <summary>A business that reports to eTIMS (or has devices from when it did), with its devices and submissions counted.</summary>
    public sealed record EtimsBusinessRow(Guid BusinessId, string BusinessName, string KraPin, bool EtimsEnabled, bool IsActive, bool IsSuspended,
        int DeviceCount, int ActiveDeviceCount, IReadOnlyDictionary<EtimsSubmissionStatus, int> Counts, DateTime? LastSentAt, DateTime? OldestProblemAt);

    /// <summary>A submission with the business and device it went through.</summary>
    public sealed record EtimsSubmissionRow(EtimsSubmission Submission, string BusinessName, string BranchName, string Tin, string BhfId);

    /// <summary>What a branch held of a product when reporting started, and its average cost then.</summary>
    public sealed record EtimsOpeningStock(Product Product, decimal Quantity, decimal UnitCost);

    /// <summary>A product with what eTIMS needs to describe it.</summary>
    public sealed record EtimsProductSource(Product Product, EtimsProductProfile? Profile, string? CategoryClassCode,
        DateTime? CategoryClassUpdatedAt, decimal DefaultPrice);

    public interface IEtimsRepository
    {
        // ---- Devices ----
        Task<List<EtimsDevice>> GetDevicesAsync();
        /// <summary>Tracked.</summary>
        Task<EtimsDevice?> GetDeviceAsync(Guid deviceId);
        Task<bool> BranchHasDeviceAsync(Guid branchId);
        Task<Branch?> GetBranchAsync(Guid branchId);
        Task<BusinessProfile> GetBusinessAsync();
        void AddDevice(EtimsDevice device);
        /// <summary>For the worker, which runs with no business in scope.</summary>
        Task<List<(Guid BusinessId, Guid DeviceId)>> GetActiveDevicesAnyBusinessAsync();
        Task<List<Branch>> GetBranchesAsync();

        // ---- The platform: across businesses ----
        /// <summary>Businesses with eTIMS on, or with devices: the platform's overview.</summary>
        Task<List<EtimsBusinessRow>> GetBusinessesAnyBusinessAsync();
        /// <summary>Most recently attempted first.</summary>
        Task<PagedResult<EtimsSubmissionRow>> GetSubmissionsAnyBusinessAsync(int page, int pageSize, PlatformEtimsSubmissionQuery query);
        Task<Guid?> GetSubmissionBusinessIdAsync(Guid submissionId);

        // ---- KRA lists ----
        Task SaveReferenceDataAsync(Guid deviceId, string kind, string payload);
        Task<EtimsReferenceData?> GetReferenceDataAsync(Guid deviceId, string kind);

        // ---- How products are described ----
        Task<Product?> GetProductAsync(Guid productId);
        Task<bool> CategoryExistsAsync(Guid categoryId);
        /// <summary>Tracked.</summary>
        Task<EtimsProductProfile?> GetProductProfileAsync(Guid productId);
        /// <summary>Tracked.</summary>
        Task<EtimsCategoryProfile?> GetCategoryProfileAsync(Guid categoryId);
        void AddProductProfile(EtimsProductProfile profile);
        void AddCategoryProfile(EtimsCategoryProfile profile);
        Task<bool> ItemRegisteredAnywhereAsync(Guid productId);
        /// <summary>The highest sequence in any of the business's item codes (0 when none).</summary>
        Task<long> GetHighestItemSequenceAsync();
        /// <summary>The products named (or, when null, every active product and every product already described to eTIMS).</summary>
        Task<List<EtimsProductSource>> GetProductSourcesAsync(IReadOnlyCollection<Guid>? productIds);

        // ---- Sweep: what happened at the branch in a window ----
        /// <summary>Completed sales created or completed in [from, to), with lines, products and customer.</summary>
        Task<List<Sale>> GetCompletedSalesAsync(Guid branchId, DateTime from, DateTime to, DateTime notBefore);
        /// <summary>Voided sales whose void falls in [from, to), with lines and products.</summary>
        Task<List<Sale>> GetVoidedSalesAsync(Guid branchId, DateTime from, DateTime to, DateTime notBefore);
        Task<List<SaleReturn>> GetReturnsAsync(Guid branchId, DateTime from, DateTime to, DateTime notBefore);
        /// <summary>With their products.</summary>
        Task<List<StockMovement>> GetStockMovementsAsync(Guid branchId, DateTime from, DateTime to, DateTime notBefore);
        /// <summary>What the branch held of each product it stocks at <paramref name="at"/> (zero included): on hand now, less every movement since.</summary>
        Task<List<EtimsOpeningStock>> GetOpeningStockAsync(Guid branchId, DateTime at);
        Task<Dictionary<Guid, string>> GetUserNamesAsync(IReadOnlyCollection<Guid> userIds);
        Task<HashSet<string>> GetExistingSourceKeysAsync(Guid deviceId, IReadOnlyCollection<string> sourceKeys);
        /// <summary>Each sale's Sale submission on the device, if it has one.</summary>
        Task<Dictionary<Guid, EtimsSubmission>> GetSaleSubmissionsAsync(Guid deviceId, IReadOnlyCollection<Guid> saleIds);
        void AddSubmissions(IEnumerable<EtimsSubmission> submissions);

        // ---- Sending ----
        /// <summary>Tracked. Pending or Failed and due, items first, then oldest first.</summary>
        Task<List<EtimsSubmission>> GetDueSubmissionsAsync(Guid deviceId, DateTime now, int max);
        /// <summary>Each product's accepted registration on the device (its request holds the code and classification KRA keeps).</summary>
        Task<Dictionary<Guid, EtimsSubmission>> GetRegisteredItemsAsync(Guid deviceId, IReadOnlyCollection<Guid> productIds);
        Task<Dictionary<Guid, (EtimsSubmissionStatus Status, long? DocumentNumber)>> GetSubmissionNumbersAsync(IReadOnlyCollection<Guid> submissionIds);
        /// <summary>Tracked.</summary>
        Task<Dictionary<Guid, EtimsStockBalance>> GetStockBalancesAsync(Guid deviceId, IReadOnlyCollection<Guid> productIds);
        void AddStockBalance(EtimsStockBalance balance);

        // ---- Review ----
        Task<PagedResult<EtimsSubmission>> GetSubmissionsAsync(int page, int pageSize, EtimsSubmissionQuery query);
        /// <summary>Tracked.</summary>
        Task<EtimsSubmission?> GetSubmissionAsync(Guid submissionId);
        Task<EtimsSubmission?> GetLatestSubmissionForSourceAsync(Guid sourceId, EtimsDocumentType documentType);
        Task<Dictionary<EtimsSubmissionStatus, int>> CountSubmissionsAsync(Guid deviceId);
        Task<Customer?> GetCustomerAsync(Guid customerId);

        Task SaveChangesAsync();
    }
}
