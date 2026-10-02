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
        Task<MpesaTransaction?> GetByCheckoutRequestIdAsync(string checkoutRequestId);
        Task<MpesaTransaction?> GetBySaleIdAsync(Guid saleId);
        Task<IEnumerable<MpesaTransaction>> GetUnmatchedAsync();
        Task MatchToSaleAsync(Guid mpesaTransactionId, Guid saleId);
        Task ConfirmAsync(Guid mpesaTransactionId, string? mpesaReceiptNumber, DateTime? transactionDate);
        Task FailAsync(Guid mpesaTransactionId);
    }
}
