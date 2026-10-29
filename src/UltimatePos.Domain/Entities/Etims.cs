using System;
using System.Collections.Generic;

namespace UltimatePos.Domain.Entities
{
    /// <summary>What a submission tells KRA eTIMS about. Items go first: KRA refuses documents that name an unregistered item.</summary>
    public enum EtimsDocumentType { Item, Sale, CreditNote, Purchase, StockIO, StockMaster }

    /// <summary>
    /// Pending: waiting for its first send (or for an item it needs). Failed: a send failed and it will be retried at NextAttemptAt.
    /// Sent: KRA accepted it (resultCd 000). Rejected: gave up after Etims:MaxAttempts; a manager fixes the cause and retries it.
    /// </summary>
    public enum EtimsSubmissionStatus { Pending, Failed, Sent, Rejected }

    /// <summary>
    /// A branch's eTIMS OSCU device: the KRA PIN and branch id ("bhfId") it reports under, and the communication key (cmcKey) KRA
    /// issued when it was initialised. One per branch. Sales, returns, purchases and stock movements at the branch from ActivatedAt
    /// on are reported through it; earlier ones are not.
    /// </summary>
    public class EtimsDevice : ITenantEntity
    {
        public Guid EtimsDeviceId { get; set; }
        public Guid BusinessId { get; set; }
        public Guid BranchId { get; set; }
        /// <summary>The taxpayer's KRA PIN ("tin").</summary>
        public string Tin { get; set; } = string.Empty;
        /// <summary>KRA's branch id: "00" for the head office.</summary>
        public string BhfId { get; set; } = string.Empty;
        /// <summary>The device serial number the PIN was registered with on the eTIMS portal ("dvcSrlNo").</summary>
        public string DeviceSerialNumber { get; set; } = string.Empty;
        /// <summary>The communication key from /initialize. Sent on every call; treat as a secret.</summary>
        public string CmcKey { get; set; } = string.Empty;
        // Returned by /initialize and printed on receipts.
        public string? SdcId { get; set; }
        public string? MrcNo { get; set; }
        public string? DeviceId { get; set; }
        public string? TaxpayerName { get; set; }
        public string? KraBranchName { get; set; }
        /// <summary>The raw /initialize response, kept because a device usually cannot be initialised twice.</summary>
        public string? InitializationResponse { get; set; }
        /// <summary>Item classification for products that have none of their own or from their category.</summary>
        public string? DefaultItemClassCode { get; set; }
        public string? ReceiptTopMessage { get; set; }
        public string? ReceiptBottomMessage { get; set; }
        public bool IsActive { get; set; } = true;
        /// <summary>Reporting starts here. Documents created before it are never sent.</summary>
        public DateTime ActivatedAt { get; set; } = DateTime.UtcNow;
        /// <summary>The sweep has queued every document created before this (less a margin for transactions still committing).</summary>
        public DateTime SweptUntil { get; set; } = DateTime.UtcNow;
        // The last number KRA accepted in each of its strict series: the next document must carry exactly the next one. A number is
        // taken when a document is sent and kept only if KRA accepts it; when KRA names the number it expects, the counter follows it.
        /// <summary>invcNo of sales and credit notes.</summary>
        public long LastInvoiceNumber { get; set; }
        /// <summary>sarNo of stock in/outs.</summary>
        public long LastStockAdjustmentNumber { get; set; }
        /// <summary>The 7-digit sequence ending each item code. KRA counts it per taxpayer, whatever the rest of the code.</summary>
        public long LastItemSequence { get; set; }
        /// <summary>When the branch's stock on hand at activation was queued as opening stock (KRA refuses to sell an item it holds no
        /// stock of). Null = not yet.</summary>
        public DateTime? OpeningStockQueuedAt { get; set; }
        public DateTime? LastCodeSyncAt { get; set; }
        /// <summary>Last time every product was checked for (re-)registration. Null = check them all on the next sweep.</summary>
        public DateTime? ItemsCheckedAt { get; set; }
        /// <summary>Postgres xmin: two sweeps of the same device can never both take the same numbers.</summary>
        public uint Version { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public Guid? CreatedBy { get; set; }
        public DateTime? LastUpdatedAt { get; set; }
        public Guid? LastUpdatedBy { get; set; }

        public Branch Branch { get; set; } = null!;
    }

    /// <summary>
    /// One document sent (or to be sent) to KRA through a device: the outbox. The request is built and frozen when it is queued, so a
    /// retry sends exactly the same thing. Unique per device by SourceKey, so a document is never queued twice.
    /// </summary>
    public class EtimsSubmission : ITenantEntity
    {
        public Guid EtimsSubmissionId { get; set; }
        public Guid BusinessId { get; set; }
        public Guid BranchId { get; set; }
        public Guid EtimsDeviceId { get; set; }
        public EtimsDocumentType DocumentType { get; set; }
        /// <summary>What it reports: Product, Sale, SaleVoid, SaleReturn, or a stock movement type for StockIO.</summary>
        public string SourceType { get; set; } = string.Empty;
        /// <summary>The product, sale, sale return or stock movement batch it reports.</summary>
        public Guid SourceId { get; set; }
        /// <summary>Unique per device: "Sale:{id}", "Item:{productId}:{version}", "StockIO:{batchId}:{type}" ...</summary>
        public string SourceKey { get; set; } = string.Empty;
        /// <summary>invcNo (sales, credit notes, purchases) or sarNo (stock IO). Null for items and stock master.</summary>
        public long? DocumentNumber { get; set; }
        /// <summary>The products it names: it waits until each is registered on the device.</summary>
        public List<Guid> ProductIds { get; set; } = new();
        /// <summary>Another submission that must be Sent first (the sale a credit note reverses).</summary>
        public Guid? DependsOnSubmissionId { get; set; }
        /// <summary>The OSCU path, e.g. "sendSalesTransaction".</summary>
        public string Endpoint { get; set; } = string.Empty;
        public string RequestJson { get; set; } = "{}";
        public string? ResponseJson { get; set; }
        public EtimsSubmissionStatus Status { get; set; } = EtimsSubmissionStatus.Pending;
        public int Attempts { get; set; }
        public DateTime NextAttemptAt { get; set; } = DateTime.UtcNow;
        public DateTime? LastAttemptAt { get; set; }
        public DateTime? SentAt { get; set; }
        public string? ResultCode { get; set; }
        public string? ResultMessage { get; set; }
        // A sale or credit note's signature, from KRA's reply. Printed on the receipt with its QR code.
        public long? ReceiptNumber { get; set; }
        public long? TotalReceiptNumber { get; set; }
        public string? InternalData { get; set; }
        public string? ReceiptSignature { get; set; }
        public string? SdcDateTime { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? LastUpdatedAt { get; set; }
    }

    /// <summary>
    /// How a product is described to eTIMS. Created with defaults the first time the product is reported. KRA registers an item once
    /// and refuses to save it again, so nothing here can change once KRA has accepted it.
    /// </summary>
    public class EtimsProductProfile : ITenantEntity
    {
        public Guid ProductId { get; set; }
        public Guid BusinessId { get; set; }
        /// <summary>KE + item type + packaging unit + quantity unit + 7-digit sequence, e.g. KE2NTBX0000002. Null until KRA accepts the
        /// item: the sequence is KRA's, so the code is made when the item is sent.</summary>
        public string? ItemCode { get; set; }
        /// <summary>From KRA's item classification list. Null = the category's, else the device default.</summary>
        public string? ItemClassCode { get; set; }
        public string PackagingUnitCode { get; set; } = "NT";
        public string QuantityUnitCode { get; set; } = string.Empty;
        public string OriginCountryCode { get; set; } = "KE";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? LastUpdatedAt { get; set; }
        public Guid? LastUpdatedBy { get; set; }

        public Product Product { get; set; } = null!;
    }

    /// <summary>
    /// How much of a product KRA believes a device's branch holds: the sum of the stock in/outs it accepted. KRA refuses a stock master
    /// (rsdQty) that disagrees with it.
    /// </summary>
    public class EtimsStockBalance : ITenantEntity
    {
        public Guid EtimsDeviceId { get; set; }
        public Guid ProductId { get; set; }
        public Guid BusinessId { get; set; }
        public decimal Quantity { get; set; }
        public DateTime LastUpdatedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>The item classification a category's products get unless their own profile says otherwise.</summary>
    public class EtimsCategoryProfile : ITenantEntity
    {
        public Guid CategoryId { get; set; }
        public Guid BusinessId { get; set; }
        public string ItemClassCode { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? LastUpdatedAt { get; set; }
        public Guid? LastUpdatedBy { get; set; }
    }

    /// <summary>The last copy of a KRA list (code list, item classifications, notices) fetched through a device.</summary>
    public class EtimsReferenceData : ITenantEntity
    {
        public Guid EtimsReferenceDataId { get; set; }
        public Guid BusinessId { get; set; }
        public Guid EtimsDeviceId { get; set; }
        /// <summary>"codes", "itemClasses" or "notices".</summary>
        public string Kind { get; set; } = string.Empty;
        public string Payload { get; set; } = "{}";
        public DateTime FetchedAt { get; set; } = DateTime.UtcNow;
    }
}
