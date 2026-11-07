using Microsoft.EntityFrameworkCore;

namespace UltimatePos.Infrastructure.Persistence
{
    /// <summary>
    /// What a branch could have sold of each product at a past moment, for a backdated sale (one entered after a power failure).
    ///
    /// It is the stock on hand at that moment, worked back from today's balance through the movements since, and never more than the
    /// lowest the balance has been since then: goods sold at that moment were not on the shelf for anything that came after, so a
    /// backdated sale may not take any later balance below zero. Without that, two slips from the paper book entered out of order could
    /// together sell more than the shelf held (6 on hand, 5 sold at 10:00 entered first, then 3 sold at 09:30).
    ///
    /// A delivery received since the sale does not help it (the goods were not there yet), and today's stock on the shelf is not what is
    /// checked: a product out of stock now can still take a backdated sale if it was there then and nothing since has used it.
    /// </summary>
    internal static class StockHistory
    {
        /// <param name="day">The sale's local calendar day: batch-tracked stock (medicines) must also be in lots that had not expired by then,
        /// and still hold it now, since the sale is issued from those lots.</param>
        public static async Task<Dictionary<Guid, decimal>> AvailableAtAsync(UltimatePosDbContext context, Guid branchId,
            IReadOnlyCollection<Guid> productIds, DateTime atUtc, DateOnly day)
        {
            var result = new Dictionary<Guid, decimal>();
            if (productIds.Count == 0)
                return result;

            var onHand = await context.StockLevels.AsNoTracking()
                .Where(s => productIds.Contains(s.ProductId) && s.BranchId == branchId)
                .ToDictionaryAsync(s => s.ProductId, s => s.QuantityOnHand);
            var since = (await context.StockMovements.AsNoTracking()
                    .Where(m => m.BranchId == branchId && productIds.Contains(m.ProductId) && m.CreatedAt > atUtc)
                    .OrderBy(m => m.CreatedAt)
                    .Select(m => new { m.ProductId, m.QuantityChange })
                    .ToListAsync())
                .ToLookup(m => m.ProductId, m => m.QuantityChange);

            foreach (var productId in productIds)
            {
                var changes = since[productId].ToList();
                var balance = onHand.GetValueOrDefault(productId) - changes.Sum(); // what was on hand at atUtc
                var lowest = balance;
                foreach (var change in changes)
                {
                    balance += change;
                    lowest = Math.Min(lowest, balance);
                }
                result[productId] = lowest;
            }

            var batchTracked = await context.Products.AsNoTracking()
                .Where(p => productIds.Contains(p.ProductId) && p.IsBatchTracked)
                .Select(p => p.ProductId)
                .ToListAsync();
            if (batchTracked.Count > 0)
            {
                var inLots = await context.StockBatches.AsNoTracking()
                    .Where(b => batchTracked.Contains(b.ProductId) && b.BranchId == branchId && b.QuantityOnHand > 0
                                && (b.ExpiryDate == null || b.ExpiryDate >= day))
                    .GroupBy(b => b.ProductId)
                    .Select(g => new { g.Key, Quantity = g.Sum(b => b.QuantityOnHand) })
                    .ToDictionaryAsync(g => g.Key, g => g.Quantity);
                foreach (var productId in batchTracked)
                    result[productId] = Math.Min(result[productId], inLots.GetValueOrDefault(productId));
            }

            return result;
        }
    }
}
