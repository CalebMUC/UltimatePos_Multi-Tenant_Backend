using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UltimatePos.Application.Payments;
using UltimatePos.Domain.Entities;
using UltimatePos.Domain.Exceptions;
using static UltimatePos.Domain.Enums.PosEnums;

namespace UltimatePos.Infrastructure.Persistence.Repositories
{
    public class MpesaRepository : IMpesaRepository
    {
        private readonly UltimatePosDbContext _context;
        public MpesaRepository(UltimatePosDbContext context) => _context = context;

        public async Task CreateAsync(MpesaTransaction transaction)
        {
            _context.MpesaTransactions.Add(transaction);
            await _context.SaveChangesAsync();
        }

        public async Task<MpesaTransaction?> GetByIdAsync(Guid mpesaTransactionId) =>
            await _context.MpesaTransactions.AsNoTracking().FirstOrDefaultAsync(t => t.MpesaTransactionId == mpesaTransactionId);

        public async Task<MpesaTransaction?> GetByCheckoutRequestIdAsync(string checkoutRequestId) =>
            await _context.MpesaTransactions.AsNoTracking().FirstOrDefaultAsync(t => t.CheckoutRequestId == checkoutRequestId);

        public async Task<MpesaTransaction?> GetBySaleIdAsync(Guid saleId) =>
            await _context.MpesaTransactions.AsNoTracking().FirstOrDefaultAsync(t => t.SaleId == saleId);

        public async Task<IEnumerable<MpesaTransaction>> GetUnmatchedAsync() =>
            await _context.MpesaTransactions.AsNoTracking()
                .Where(t => t.TransactionType == MpesaTransactionType.C2B && t.Status == MpesaTransactionStatus.Received)
                .OrderByDescending(t => t.CreatedAt)
                .ToListAsync();

        public async Task MatchToSaleAsync(Guid mpesaTransactionId, Guid saleId)
        {
            var transaction = await _context.MpesaTransactions.FirstOrDefaultAsync(t => t.MpesaTransactionId == mpesaTransactionId)
                ?? throw new NotFoundException($"M-Pesa transaction '{mpesaTransactionId}' not found.");

            transaction.Status = MpesaTransactionStatus.Matched;
            transaction.SaleId = saleId;
            transaction.LastUpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
        }

        public async Task ConfirmAsync(Guid mpesaTransactionId, string? mpesaReceiptNumber, DateTime? transactionDate)
        {
            var transaction = await _context.MpesaTransactions.FirstOrDefaultAsync(t => t.MpesaTransactionId == mpesaTransactionId)
                ?? throw new NotFoundException($"M-Pesa transaction '{mpesaTransactionId}' not found.");

            transaction.Status = MpesaTransactionStatus.Confirmed;
            if (mpesaReceiptNumber is not null) transaction.MpesaReceiptNumber = mpesaReceiptNumber;
            if (transactionDate is not null) transaction.TransactionDate = transactionDate;
            transaction.LastUpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
        }

        public async Task FailAsync(Guid mpesaTransactionId)
        {
            var transaction = await _context.MpesaTransactions.FirstOrDefaultAsync(t => t.MpesaTransactionId == mpesaTransactionId)
                ?? throw new NotFoundException($"M-Pesa transaction '{mpesaTransactionId}' not found.");

            transaction.Status = MpesaTransactionStatus.Failed;
            transaction.LastUpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
        }
    }
}
