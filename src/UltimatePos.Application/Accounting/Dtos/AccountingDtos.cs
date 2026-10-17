using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static UltimatePos.Domain.Enums.PosEnums;

namespace UltimatePos.Application.Accounting.Dtos
{
    /// <param name="AvailableCredit">How much more the customer may buy on credit. Null when they have no credit limit.</param>
    public record CustomerBalanceDto(Guid CustomerId, string CustomerName, decimal OutstandingBalance,
        decimal? CreditLimit = null, decimal? AvailableCredit = null);
    public record CustomerLedgerEntryDto(Guid CustomerLedgerEntryId, CustomerLedgerEntryType EntryType, decimal Amount,
        string ReferenceType, Guid ReferenceId, string? Notes, DateTime CreatedAt,
        SettlementMethod? PaymentMethod = null, string? PaymentReference = null);
    /// <param name="PaymentMethod">How the customer paid. Optional for older clients; when given, a reference is required for anything but cash.</param>
    public record RecordCustomerPaymentRequestDto(decimal Amount, string? Notes,
        SettlementMethod? PaymentMethod = null, string? PaymentReference = null);

    public record SupplierBalanceDto(Guid SupplierId, string SupplierName, decimal OutstandingBalance);
    public record SupplierLedgerEntryDto(Guid SupplierLedgerEntryId, SupplierLedgerEntryType EntryType, decimal Amount,
        string ReferenceType, Guid ReferenceId, string? SupplierInvoiceNumber, SettlementMethod? PaymentMethod, string? PaymentReference,
        string? Notes, DateTime CreatedAt);
    public record RecordSupplierPaymentRequestDto(decimal Amount, SettlementMethod PaymentMethod, string? PaymentReference, string? Notes);
}
