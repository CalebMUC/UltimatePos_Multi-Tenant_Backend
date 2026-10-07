using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UltimatePos.Domain.Entities;
using static UltimatePos.Domain.Enums.PosEnums;

namespace UltimatePos.Application.Sales
{
    public interface ISalesRepository
    {
        Task<int> GetNextDocumentNumberAsync(string prefix);

        // Cash / Credit / Till — sale + lines + stock + ledger all commit together, immediately.
        Task RecordCompletedSaleAsync(Sale sale, IEnumerable<StockMovement> movements, CustomerLedgerEntry? ledgerEntry);

        // STK — sale + lines persist now; stock/ledger are deliberately untouched (still Pending).
        Task RecordPendingSaleAsync(Sale sale);

        // Called once payment is confirmed (callback or reconciliation query).
        Task CompletePendingSaleAsync(Guid saleId, IEnumerable<StockMovement> movements, CustomerLedgerEntry? ledgerEntry, string? mpesaReceiptNumber);

        // Called if payment failed/was cancelled/timed out — nothing to reverse, since Pending never touched stock/ledger.
        Task FailPendingSaleAsync(Guid saleId, string reason);

        Task<(IEnumerable<Sale> Items, int TotalCount)> GetSalesAsync(int page, int pageSize, Guid? customerId, SaleStatus? status);
        Task<Sale?> GetSaleByIdAsync(Guid saleId);

        Task VoidSaleAsync(Guid saleId, IEnumerable<StockMovement> reversalMovements, CustomerLedgerEntry? reversalLedgerEntry, Guid? updatedBy);
    }
}
