using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static UltimatePos.Domain.Enums.PosEnums;

namespace UltimatePos.Domain.Entities
{
    /// <summary>
    /// A cost of running the business that is not stock: rent, power, wages, transport, licences...
    /// (Stock purchases belong to Purchasing and production labour to the production run.)
    /// Amount, date and payment method are fixed once posted — a mistake is corrected by voiding and re-entering,
    /// so the books always show what was originally recorded and who voided it.
    /// </summary>
    public class Expense : ITenantEntity
    {
        public Guid ExpenseId { get; set; }
        public Guid BusinessId { get; set; }
        /// <summary>EXP-000001 ...</summary>
        public string ExpenseNumber { get; set; } = string.Empty;
        /// <summary>The calendar day the cost was incurred (a date, not an instant — no time zone ambiguity).</summary>
        public DateOnly ExpenseDate { get; set; }
        public Guid ExpenseCategoryId { get; set; }
        public string Description { get; set; } = string.Empty;
        /// <summary>Total paid, including any VAT, to 2 decimal places.</summary>
        public decimal Amount { get; set; }
        public ExpensePaymentMethod PaymentMethod { get; set; }
        /// <summary>M-Pesa transaction code, bank reference or cheque number. Required for everything except cash.</summary>
        public string? PaymentReference { get; set; }

        /// <summary>Optional: the payee, when it is a registered supplier.</summary>
        public Guid? SupplierId { get; set; }
        /// <summary>Optional free-text payee, for people and businesses that are not registered suppliers.</summary>
        public string? PayeeName { get; set; }
        /// <summary>The payee's own invoice number.</summary>
        public string? SupplierInvoiceNumber { get; set; }
        /// <summary>Control number of the payee's eTIMS invoice. Without a valid eTIMS invoice the cost is generally not tax-deductible.</summary>
        public string? EtimsControlNumber { get; set; }
        /// <summary>Link to a scan/photo of the receipt. A plain URL — there is no upload pipeline yet.</summary>
        public string? ReceiptUrl { get; set; }
        public string? Notes { get; set; }

        public ExpenseStatus Status { get; set; } = ExpenseStatus.Posted;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public Guid? CreatedBy { get; set; }
        public DateTime? LastUpdatedAt { get; set; }
        public Guid? LastUpdatedBy { get; set; }
        public DateTime? VoidedAt { get; set; }
        public Guid? VoidedBy { get; set; }
        public string? VoidReason { get; set; }

        public ExpenseCategory Category { get; set; } = null!;
        public Supplier? Supplier { get; set; }
    }
}
