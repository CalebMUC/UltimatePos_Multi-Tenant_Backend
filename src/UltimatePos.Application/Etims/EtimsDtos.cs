using System.Text.Json;
using UltimatePos.Domain.Entities;

namespace UltimatePos.Application.Etims
{
    /// <param name="DeviceSerialNumber">The serial number the PIN's device was registered with on the eTIMS portal.</param>
    /// <param name="DefaultItemClassCode">Item classification for products with none of their own or from their category.</param>
    /// <param name="Tin">Defaults to the business's KRA PIN.</param>
    /// <param name="CmcKey">Only for a device KRA has already initialised (it will not initialise twice): the key it issued then.</param>
    /// <param name="LastInvoiceNumber">The last invoice number KRA holds for the device, if known (e.g. after testing). Optional: when KRA
    /// refuses a number and names the one it expects, the device follows it anyway. Likewise the stock in/out number and item sequence.</param>
    public record RegisterEtimsDeviceRequestDto(Guid BranchId, string DeviceSerialNumber, string DefaultItemClassCode, string? Tin = null,
        string? CmcKey = null, long LastInvoiceNumber = 0, long LastStockAdjustmentNumber = 0, long LastItemSequence = 0,
        string? ReceiptTopMessage = null, string? ReceiptBottomMessage = null);

    /// <summary>Full replace of the editable settings. A deactivated device stops reporting; nothing it missed is sent later.</summary>
    public record UpdateEtimsDeviceRequestDto(string DefaultItemClassCode, bool IsActive, string? ReceiptTopMessage, string? ReceiptBottomMessage);

    public record EtimsDeviceDto(Guid EtimsDeviceId, Guid BranchId, string BranchName, string Tin, string BhfId, string DeviceSerialNumber,
        string CmcKeyHint, string? SdcId, string? MrcNo, string? TaxpayerName, string? KraBranchName, string? DefaultItemClassCode,
        string? ReceiptTopMessage, string? ReceiptBottomMessage, bool IsActive, DateTime ActivatedAt, DateTime SweptUntil,
        long LastInvoiceNumber, long LastStockAdjustmentNumber, long LastItemSequence, DateTime? OpeningStockQueuedAt, DateTime? LastCodeSyncAt,
        IReadOnlyDictionary<string, int> SubmissionCounts);

    public record EtimsReferenceSyncDto(string Kind, string? ResultCode, string? ResultMessage, int Count);

    public record EtimsCodeDto(string CodeClass, string CodeClassName, string Code, string Name, string? UseYn);

    public record EtimsItemClassDto(string ItemClassCode, string ItemClassName, string? Level, string? TaxTypeCode, string? UseYn);

    /// <summary>A read from KRA, passed through: data is KRA's own JSON.</summary>
    public record EtimsLookupDto(string Endpoint, string? ResultCode, string? ResultMessage, string? ResultDate, JsonElement? Data, string? Error);

    /// <summary>KRA registers an item once and refuses to save it again, so this can change only until KRA first accepts the item.</summary>
    /// <param name="ItemClassCode">Null = the category's, else the device default.</param>
    /// <param name="QuantityUnitCode">Two letters (KG, BX, NO ...): KRA refuses item codes built on any other.</param>
    public record UpdateEtimsProductProfileRequestDto(string? ItemClassCode, string? PackagingUnitCode, string? QuantityUnitCode,
        string? OriginCountryCode);

    /// <param name="ItemCode">Null until KRA accepts the item.</param>
    public record EtimsProductProfileDto(Guid ProductId, string? ItemCode, string? ItemClassCode, string? EffectiveItemClassCode,
        string PackagingUnitCode, string QuantityUnitCode, string OriginCountryCode, bool IsRegistered);

    public record SetEtimsCategoryClassRequestDto(string ItemClassCode);

    public record EtimsSubmissionDto(Guid EtimsSubmissionId, Guid EtimsDeviceId, Guid BranchId, EtimsDocumentType DocumentType,
        string SourceType, Guid SourceId, long? DocumentNumber, string Endpoint, EtimsSubmissionStatus Status, int Attempts,
        DateTime NextAttemptAt, DateTime? LastAttemptAt, DateTime? SentAt, string? ResultCode, string? ResultMessage,
        long? ReceiptNumber, DateTime CreatedAt);

    public record EtimsSubmissionDetailDto(EtimsSubmissionDto Submission, JsonElement Request, JsonElement? Response);

    /// <summary>What a receipt prints for KRA: the signature and the QR link that verifies it. Null fields until KRA has signed it.</summary>
    public record EtimsReceiptDto(Guid SaleId, EtimsSubmissionStatus Status, long? InvoiceNumber, long? ReceiptNumber,
        long? TotalReceiptNumber, string? InternalData, string? ReceiptSignature, string? SdcId, string? MrcNo, string? SdcDateTime,
        string? VerificationUrl, string? ResultMessage);

    public record EtimsRunResultDto(int Queued, int Sent, int Failed, int Waiting);

    public record RegisterEtimsBranchUserRequestDto(string UserId, string UserName, string Password, string? Address, string? Contact);

    public record ConvertEtimsImportedItemRequestDto(string TaskCode, string DeclarationDate, int ItemSequence, string HsCode,
        Guid ProductId, string ImportItemStatusCode, string? Remark);
}
