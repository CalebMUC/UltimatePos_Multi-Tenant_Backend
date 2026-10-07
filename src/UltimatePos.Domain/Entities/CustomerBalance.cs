using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UltimatePos.Domain.Entities
{
    // Mirrors StockLevel: the current running balance, mutated only through CustomerLedger.
    // Positive = customer owes the business. Negative = customer has prepaid / is in credit — no floor.
    public class CustomerBalance
    {
        public Guid CustomerId { get; set; }
        public decimal OutstandingBalance { get; set; }
        public DateTime LastUpdatedAt { get; set; } = DateTime.UtcNow;

        public Customer Customer { get; set; } = null!;
    }
}
