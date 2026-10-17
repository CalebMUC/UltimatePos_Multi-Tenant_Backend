using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UltimatePos.Domain.Entities;

namespace UltimatePos.Application.Payments
{
    public interface IMpesaRepository
    {
        Task CreateAsync(MpesaTransaction transaction);
        Task<MpesaTransaction?> GetByIdAsync(Guid mpesaTransactionId);
        /// <summary>For webhooks, which run with no business in scope. Do not use where a user is signed in.</summary>
        Task<MpesaTransaction?> GetByCheckoutRequestIdAnyBusinessAsync(string checkoutRequestId);
        Task<bool> ReceiptExistsAnyBusinessAsync(string mpesaReceiptNumber);
        Task<MpesaTransaction?> GetBySaleIdAsync(Guid saleId);
        Task<IEnumerable<MpesaTransaction>> GetUnmatchedAsync();
        Task MatchToSaleAsync(Guid mpesaTransactionId, Guid saleId);
        Task ConfirmAsync(Guid mpesaTransactionId, string? mpesaReceiptNumber, DateTime? transactionDate);
        Task FailAsync(Guid mpesaTransactionId);
    }
}
