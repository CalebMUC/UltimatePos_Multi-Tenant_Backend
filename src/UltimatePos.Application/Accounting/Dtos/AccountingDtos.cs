using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static UltimatePos.Domain.Enums.PosEnums;

namespace UltimatePos.Application.Accounting.Dtos
{
    public record CustomerBalanceDto(Guid CustomerId, string CustomerName, decimal OutstandingBalance);
    public record CustomerLedgerEntryDto(Guid CustomerLedgerEntryId, CustomerLedgerEntryType EntryType, decimal Amount,
        string ReferenceType, Guid ReferenceId, string? Notes, DateTime CreatedAt);

    public record RecordCustomerPaymentRequestDto(decimal Amount, string? Notes);
}
