using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static UltimatePos.Domain.Enums.PosEnums;

namespace UltimatePos.Domain.Entities
{
    public class CustomerLedgerEntry
    {
        public Guid CustomerLedgerEntryId { get; set; }
        public Guid CustomerId { get; set; }
        public CustomerLedgerEntryType EntryType { get; set; }
        public decimal Amount { get; set; }
        public string ReferenceType { get; set; } = string.Empty;
        public Guid ReferenceId { get; set; }
        public string? Notes { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public Guid? CreatedBy { get; set; }

        public Customer Customer { get; set; } = null!;
    }
}
