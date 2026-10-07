using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UltimatePos.Domain.Entities;
using UltimatePos.Domain.Exceptions;

namespace UltimatePos.Infrastructure.Persistence
{
    /// <summary>
    /// The only code that changes a stock balance. Balance changes are relative, atomic SQL — never read-modify-write
    /// in C# — so concurrent operations cannot lose updates or drive stock negative. MUST be called inside a transaction
    /// opened by the caller (same DbContext), so the balance change and its movement row commit together.
    /// </summary>
    public class StockLedger
    {
        private readonly UltimatePosDbContext _context;
        public StockLedger(UltimatePosDbContext context) => _context = context;

        /// <param name="allowNegative">
        /// false (default) hard-blocks anything that would drive stock negative — correct for Purchasing/Production,
        /// where a short ingredient should stop the operation. true skips the guard — used by Sales, where refusing
        /// a transaction a cashier is physically ringing up is worse than a temporarily negative count to be corrected
        /// by an adjustment.
        /// </param>
        public async Task ApplyAsync(StockMovement movement, bool allowNegative = false)
        {
            if (movement.QuantityChange == 0)
                return;

            if (movement.QuantityChange > 0 || allowNegative)
            {
                await _context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "StockLevels" ("ProductId", "QuantityOnHand", "LastUpdatedAt")
                VALUES ({movement.ProductId}, {movement.QuantityChange}, now())
                ON CONFLICT ("ProductId") DO UPDATE
                SET "QuantityOnHand" = "StockLevels"."QuantityOnHand" + EXCLUDED."QuantityOnHand",
                    "LastUpdatedAt" = now()
                """);
            }
            else
            {
                var rows = await _context.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "StockLevels"
                SET "QuantityOnHand" = "QuantityOnHand" + {movement.QuantityChange}, "LastUpdatedAt" = now()
                WHERE "ProductId" = {movement.ProductId}
                  AND "QuantityOnHand" + {movement.QuantityChange} >= 0
                """);

                if (rows == 0)
                    throw new InsufficientStockException(
                        $"Stock for product '{movement.ProductId}' is no longer sufficient — it changed while this operation was in progress. Review stock and retry.");
            }

            _context.StockMovements.Add(movement);
        }
    }
}
