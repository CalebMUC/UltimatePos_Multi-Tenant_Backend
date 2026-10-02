using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UltimatePos.Application.Payments
{
    public record StkPushResult(bool Success, string? MerchantRequestId, string? CheckoutRequestId, string? ErrorMessage);
    public record StkQueryResult(bool Found, int? ResultCode, string? ResultDesc);

    public interface IMpesaClient
    {
        Task<StkPushResult> InitiateStkPushAsync(string phoneNumber, decimal amount, string accountReference, string transactionDesc);
        Task<StkQueryResult> QueryStkStatusAsync(string checkoutRequestId);
        Task RegisterC2BUrlsAsync(string confirmationUrl, string validationUrl);
    }
}
