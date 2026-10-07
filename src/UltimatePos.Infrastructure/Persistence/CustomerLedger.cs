using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UltimatePos.Domain.Entities;
using static UltimatePos.Domain.Enums.PosEnums;

namespace UltimatePos.Infrastructure.Persistence
{
    /// <summary>
    /// The only code that changes a customer's balance. No floor — unlike stock, a customer's balance CAN go
    /// negative (they've overpaid / carry a credit). Same relative, atomic SQL as StockLedger, no read-modify-write.
    /// MUST be called inside a transaction opened by the caller.
    /// </summary>
    public class CustomerLedger
    {
        private readonly UltimatePosDbContext _context;
        public CustomerLedger(UltimatePosDbContext context) => _context = context;

        public async Task ApplyAsync(CustomerLedgerEntry entry)
        {
            var signedAmount = entry.EntryType == CustomerLedgerEntryType.Invoice ? entry.Amount : -entry.Amount;

            await _context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "CustomerBalances" ("CustomerId", "OutstandingBalance", "LastUpdatedAt")
            VALUES ({entry.CustomerId}, {signedAmount}, now())
            ON CONFLICT ("CustomerId") DO UPDATE
            SET "OutstandingBalance" = "CustomerBalances"."OutstandingBalance" + EXCLUDED."OutstandingBalance",
                "LastUpdatedAt" = now()
            """);

            _context.CustomerLedgerEntries.Add(entry);
        }
    }
}
