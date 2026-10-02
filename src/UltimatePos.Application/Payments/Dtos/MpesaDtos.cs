using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static UltimatePos.Domain.Enums.PosEnums;

namespace UltimatePos.Application.Payments.Dtos
{
    public record MpesaTransactionDto(Guid MpesaTransactionId, MpesaTransactionType TransactionType, MpesaTransactionStatus Status,
    string? MpesaReceiptNumber, string PhoneNumber, decimal Amount, DateTime? TransactionDate, Guid? SaleId);

    // Shapes matching Daraja's actual callback/confirmation payloads — see note on JsonElement below.
    public record StkCallbackPayload(StkCallbackBody Body);
    public record StkCallbackBody(StkCallback StkCallback);
    public record StkCallback(string MerchantRequestID, string CheckoutRequestID, int ResultCode, string ResultDesc, CallbackMetadata? CallbackMetadata);
    public record CallbackMetadata(List<CallbackMetadataItem> Item);
    public record CallbackMetadataItem(string Name, System.Text.Json.JsonElement Value);

    public record C2bConfirmationPayload(string TransID, string TransTime, string TransAmount, string BusinessShortCode, string? BillRefNumber, string MSISDN, string? FirstName);
}
