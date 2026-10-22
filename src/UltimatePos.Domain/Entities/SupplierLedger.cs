using System;
using static UltimatePos.Domain.Enums.PosEnums;

namespace UltimatePos.Domain.Entities
{
    /// <summary>
    /// What the business owes a supplier, mirroring CustomerLedgerEntry. Receiving goods against a purchase order posts a Bill;
    /// paying the supplier posts a Payment. Append-only: a mistake is corrected with a Reversal, never an edit.
    /// </summary>
    public class SupplierLedgerEntry
    {
        public Guid SupplierLedgerEntryId { get; set; }
        public Guid SupplierId { get; set; }
        public SupplierLedgerEntryType EntryType { get; set; }
        /// <summary>Always positive; EntryType gives the direction.</summary>
        public decimal Amount { get; set; }
        public string ReferenceType { get; set; } = string.Empty;
        public Guid ReferenceId { get; set; }
        /// <summary>For bills: the supplier's own invoice / delivery note number.</summary>
        public string? SupplierInvoiceNumber { get; set; }
        public SettlementMethod? PaymentMethod { get; set; }
        public string? PaymentReference { get; set; }
        /// <summary>For payments: the branch it was paid from, so a cash payment counts in that till's cash-up. Null on older entries.</summary>
        public Guid? BranchId { get; set; }
        public string? Notes { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public Guid? CreatedBy { get; set; }

        public Supplier Supplier { get; set; } = null!;
    }

    /// <summary>Running balance per supplier, changed only by SupplierLedger. Positive = the business owes the supplier.</summary>
    public class SupplierBalance
    {
        public Guid SupplierId { get; set; }
        public decimal OutstandingBalance { get; set; }
        public DateTime LastUpdatedAt { get; set; } = DateTime.UtcNow;

        public Supplier Supplier { get; set; } = null!;
    }
}
