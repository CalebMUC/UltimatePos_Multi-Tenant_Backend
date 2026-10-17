using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UltimatePos.Application.Sales.Dtos;
using UltimatePos.Domain.Entities;
using static UltimatePos.Domain.Enums.PosEnums;

namespace UltimatePos.Application.Sales
{
    public interface ISalesRepository
    {
        Task<int> GetNextDocumentNumberAsync(string prefix);

        // Stock leaves inside the repository, line by line: batch-tracked products are issued first-expiry-first-out from lots that
        // have not expired by `today` (recorded on the line as batch allocations); other products as before, allowed to go negative.

        // Cash / Credit / Till — sale + lines + stock + ledger all commit together, immediately.
        Task RecordCompletedSaleAsync(Sale sale, CustomerLedgerEntry? ledgerEntry, DateOnly today);
        // STK — sale + lines persist now; stock/ledger are deliberately untouched (still Pending).
        Task RecordPendingSaleAsync(Sale sale);
        // Called once payment is confirmed (callback or reconciliation query). The customer has paid, so a batch shortfall that
        // appeared since the prompt was sent is issued without a batch and noted on the sale rather than refused.
        Task CompletePendingSaleAsync(Guid saleId, CustomerLedgerEntry? ledgerEntry, string? mpesaReceiptNumber, DateOnly today);
        // Called if payment failed/was cancelled/timed out — nothing to reverse, since Pending never touched stock/ledger.
        Task FailPendingSaleAsync(Guid saleId, string reason);

        Task<(IEnumerable<Sale> Items, int TotalCount)> GetSalesAsync(int page, int pageSize, SaleQuery query, DateTime? fromUtc, DateTime? toUtcExclusive);
        Task<Sale?> GetSaleByIdAsync(Guid saleId);
        Task<Dictionary<Guid, string>> GetUserNamesAsync(IEnumerable<Guid> userIds);

        /// <summary>Puts every line's stock back (into the lots it came from) and reverses the ledger entry, if any.</summary>
        Task VoidSaleAsync(Guid saleId, CustomerLedgerEntry? reversalLedgerEntry, Guid? updatedBy);

        /// <summary>What has already been refunded per sale line, VAT included.</summary>
        Task<Dictionary<Guid, (decimal Amount, decimal TaxAmount)>> GetReturnedAmountsByLineAsync(Guid saleId);
        /// <summary>
        /// Records the return, restocks the lines marked Restock (into their original lots), posts the credit note if any and
        /// moves the sale's returned totals — re-checking under a lock that nothing is returned twice.
        /// </summary>
        Task RecordReturnAsync(SaleReturn saleReturn, CustomerLedgerEntry? creditNote);
        Task<IEnumerable<SaleReturn>> GetReturnsForSaleAsync(Guid saleId);
        Task<(IEnumerable<SaleReturn> Items, int TotalCount)> GetReturnsAsync(int page, int pageSize, DateTime? fromUtc, DateTime? toUtcExclusive);

        Task<IEnumerable<Sale>> GetPrescriptionSalesAsync(DateTime fromUtc, DateTime toUtcExclusive, string? search);
    }
}
