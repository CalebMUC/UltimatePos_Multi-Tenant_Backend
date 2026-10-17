using Microsoft.EntityFrameworkCore;
using UltimatePos.Domain.Entities;
using static UltimatePos.Domain.Enums.PosEnums;

namespace UltimatePos.Infrastructure.Persistence
{
    /// <summary>
    /// The only code that changes what the business owes a supplier. Same shape as CustomerLedger: relative, atomic SQL and no
    /// floor (a negative balance means the business has paid ahead). MUST be called inside a transaction opened by the caller.
    /// </summary>
    public class SupplierLedger
    {
        private readonly UltimatePosDbContext _context;
        public SupplierLedger(UltimatePosDbContext context) => _context = context;

        public async Task ApplyAsync(SupplierLedgerEntry entry)
        {
            var signedAmount = entry.EntryType == SupplierLedgerEntryType.Bill ? entry.Amount : -entry.Amount;

            await _context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "SupplierBalances" ("SupplierId", "OutstandingBalance", "LastUpdatedAt")
            VALUES ({entry.SupplierId}, {signedAmount}, now())
            ON CONFLICT ("SupplierId") DO UPDATE
            SET "OutstandingBalance" = "SupplierBalances"."OutstandingBalance" + EXCLUDED."OutstandingBalance",
                "LastUpdatedAt" = now()
            """);

            _context.SupplierLedgerEntries.Add(entry);
        }
    }
}
