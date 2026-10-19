using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UltimatePos.Application.Common.Interfaces;
using UltimatePos.Application.Notifications;
using UltimatePos.Domain.Entities;
using UltimatePos.Domain.Exceptions;
using static UltimatePos.Domain.Enums.PosEnums;

namespace UltimatePos.Infrastructure.Persistence
{
    /// <summary>
    /// The only code that changes a stock balance or its average cost. Changes are relative, atomic SQL — never
    /// read-modify-write in C# — so concurrent operations cannot lose updates or drive stock negative. MUST be called
    /// inside a transaction opened by the caller (same DbContext), so the change and its movement row commit together.
    ///
    /// Stock is held PER BRANCH: one StockLevels row per (product, branch), each with its own average cost, and batches belong to
    /// one branch. A movement's BranchId says where it happens; left empty, it is the caller's active branch.
    ///
    /// Costing is a weighted moving average per product and branch, kept in the same row as the quantity so both change in one statement:
    ///   - Stock IN at a cost:   newAvg = (oldQty x oldAvg + inQty x inCost) / (oldQty + inQty).
    ///                           If the product has no positive stock (zero or negative), the average resets to the incoming
    ///                           cost — a phantom or empty balance has no value worth blending with.
    ///   - Stock IN, no cost:    quantity only; the average is untouched (voids of pre-costing sales, uncosted receipts).
    ///   - Stock OUT:            the average is untouched, and the cost it was at the instant of the removal is stamped on the
    ///                           movement (movement.UnitCost) — that is the cost of goods sold / consumed.
    /// </summary>
    public class StockLedger
    {
        private readonly UltimatePosDbContext _context;
        private readonly ISystemEventRecorder _events;
        private readonly IBranchContext _branches;

        public StockLedger(UltimatePosDbContext context, ISystemEventRecorder events, IBranchContext branches)
        {
            _context = context;
            _events = events;
            _branches = branches;
        }

        /// <summary>Where the movement happens: its own BranchId, else the caller's active branch (set on the movement).</summary>
        private Guid BranchOf(StockMovement movement)
        {
            if (movement.BranchId == Guid.Empty)
                movement.BranchId = _branches.RequireActiveBranchId();
            return movement.BranchId;
        }

        /// <summary>
        /// Locks the existing stock rows for these products, one at a time, in the SAME order every other operation uses
        /// (OrderBy on the Guid in C# — NOT SQL ORDER BY, which sorts uuids differently). Call first in any operation that
        /// moves several products and needs a stable average cost to read mid-way (production), so two such operations
        /// can never wait on each other in opposite orders.
        /// </summary>
        public async Task LockAsync(IEnumerable<Guid> productIds, Guid? branchId = null)
        {
            var branch = branchId ?? _branches.RequireActiveBranchId();
            foreach (var id in productIds.Distinct().OrderBy(i => i))
            {
                await _context.Database.ExecuteSqlInterpolatedAsync($"""
                    SELECT "ProductId" FROM "StockLevels" WHERE "ProductId" = {id} AND "BranchId" = {branch} FOR UPDATE
                    """);
            }
        }

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
            BranchOf(movement);

            LedgerRow after;
            if (movement.QuantityChange > 0)
            {
                after = movement.UnitCost.HasValue
                    ? await ReceiveAtCostAsync(movement)
                    : await ReceiveAtCurrentCostAsync(movement);
            }
            else
            {
                after = allowNegative
                    ? await IssueAllowingNegativeAsync(movement)
                    : await IssueGuardedAsync(movement);
                movement.UnitCost = after.AverageUnitCost;
            }

            _context.StockMovements.Add(movement);
            await RecordThresholdCrossingAsync(movement, after);
        }

        /// <summary>
        /// The moment stock crosses its reorder level — down (low or out) or back up (replenished) — an event is recorded in the same
        /// transaction. Crossings only, so a busy product does not raise an event per sale; the alert sweep covers everything else.
        /// A reorder level of 0 means "never set" and raises nothing but out-of-stock.
        /// </summary>
        private async Task RecordThresholdCrossingAsync(StockMovement movement, LedgerRow after)
        {
            var before = after.QuantityOnHand - movement.QuantityChange;
            var reorder = after.ReorderLevel;
            var wentOut = before > 0 && after.QuantityOnHand <= 0;
            var wentLow = reorder > 0 && before > reorder && after.QuantityOnHand <= reorder && after.QuantityOnHand > 0;
            var recovered = before <= reorder && after.QuantityOnHand > reorder && (reorder > 0 || before <= 0);
            if (!wentOut && !wentLow && !recovered)
                return;

            var product = await _context.Products.AsNoTracking()
                .Where(p => p.ProductId == movement.ProductId)
                .Select(p => new { p.Name, Unit = p.BaseUnitOfMeasure.Symbol })
                .FirstAsync();
            var branch = await _context.Branches.AsNoTracking().IgnoreQueryFilters()
                .Where(b => b.BranchId == movement.BranchId)
                .Select(b => new { b.BranchNumber, b.Name })
                .FirstAsync();
            var at = $"at {Branch.FormatKraBranchId(branch.BranchNumber)} {branch.Name}";
            var unit = string.IsNullOrEmpty(product.Unit) ? string.Empty : " " + product.Unit;
            var data = new { before, after = after.QuantityOnHand, reorderLevel = reorder, movement = movement.MovementType.ToString(), movement.ReferenceId,
                movement.BranchId };
            var low = AlertKeys.StockLow(movement.ProductId, movement.BranchId);
            var outOf = AlertKeys.StockOut(movement.ProductId, movement.BranchId);

            if (wentOut)
                _events.Record(new SystemEventDraft(SystemEventTypes.StockOutOfStock,
                    $"{product.Name} is out of stock {at} ({after.QuantityOnHand:0.##}{unit} after {Describe(movement)}).",
                    EventSeverity.Critical, "Product", movement.ProductId, data,
                    AlertKey: outOf, ClearsAlertKeys: new[] { low }, BranchId: movement.BranchId));
            else if (wentLow)
                _events.Record(new SystemEventDraft(SystemEventTypes.StockBelowReorderLevel,
                    $"{product.Name} is down to {after.QuantityOnHand:0.##}{unit} {at} (reorder level {reorder:0.##}{unit}).",
                    EventSeverity.Warning, "Product", movement.ProductId, data, AlertKey: low, BranchId: movement.BranchId));
            else
                _events.Record(new SystemEventDraft(SystemEventTypes.StockReplenished,
                    $"{product.Name} is back to {after.QuantityOnHand:0.##}{unit} {at}.",
                    EntityType: "Product", EntityId: movement.ProductId, Data: data,
                    ClearsAlertKeys: new[] { low, outOf }, BranchId: movement.BranchId));
        }

        private static string Describe(StockMovement m) => m.MovementType switch
        {
            StockMovementType.Sale => "a sale",
            StockMovementType.ProductionConsumption => "a production run",
            StockMovementType.Adjustment => "a stock adjustment",
            StockMovementType.TransferOut => "a stock transfer",
            _ => m.MovementType.ToString(),
        };

        /// <summary>The stock row after a change, and the product's reorder level, from one statement.</summary>
        private sealed class LedgerRow
        {
            public decimal AverageUnitCost { get; set; }
            public decimal QuantityOnHand { get; set; }
            public decimal ReorderLevel { get; set; }
        }

        private Task<List<LedgerRow>> QueryRow(FormattableString sql) => _context.Database.SqlQuery<LedgerRow>(sql).ToListAsync();

        // ---------------------------------------------------------------- Batches
        //
        // For batch-tracked products the product's StockLevel is still the total and still carries the average cost; the batch
        // rows split that total into lots. Every batch change is paired with a product-level movement through ApplyAsync, so
        // the total always equals the sum of the batches. Lock order is always: batch rows, then the product row.

        /// <summary>
        /// Adds stock into the named lot, creating the lot on first receipt. A lot number already on file must carry the same
        /// expiry date — two expiry dates under one lot number is a data-entry mistake, not a second lot.
        /// </summary>
        public async Task<Guid> ReceiveIntoBatchAsync(StockMovement movement, Guid businessId, string batchNumber, DateOnly? expiryDate,
            DateOnly? manufactureDate, Guid? supplierId, Guid? purchaseOrderId)
        {
            if (movement.QuantityChange <= 0)
                throw new InvalidOperationException("ReceiveIntoBatchAsync only adds stock.");
            var branchId = BranchOf(movement);

            var rows = await _context.Database.SqlQuery<BatchRow>($"""
                INSERT INTO "StockBatches" ("StockBatchId", "BusinessId", "BranchId", "ProductId", "BatchNumber", "ExpiryDate", "ManufactureDate",
                                            "QuantityOnHand", "UnitCost", "SupplierId", "PurchaseOrderId", "CreatedAt", "CreatedBy", "LastUpdatedAt")
                VALUES ({Guid.NewGuid()}, {businessId}, {branchId}, {movement.ProductId}, {batchNumber}, {expiryDate}, {manufactureDate},
                        {movement.QuantityChange}, {movement.UnitCost}, {supplierId}, {purchaseOrderId}, now(), {movement.CreatedBy}, now())
                ON CONFLICT ("BranchId", "ProductId", "BatchNumber") DO UPDATE
                SET "QuantityOnHand" = "StockBatches"."QuantityOnHand" + EXCLUDED."QuantityOnHand",
                    "LastUpdatedAt" = now()
                RETURNING "StockBatchId", "ExpiryDate", "QuantityOnHand"
                """).ToListAsync();

            var batch = rows.Single();
            if (batch.ExpiryDate != expiryDate)
                throw new InvalidAssignmentException(
                    $"Batch '{batchNumber}' is already on file with expiry {batch.ExpiryDate?.ToString("yyyy-MM-dd") ?? "none"} — " +
                    $"check the expiry date ({expiryDate?.ToString("yyyy-MM-dd") ?? "none"} given) or the batch number.");

            movement.StockBatchId = batch.StockBatchId;
            await ApplyAsync(movement);
            return batch.StockBatchId;
        }

        /// <summary>Moves stock into or out of one specific lot (stock adjustments, returns, voids). Removals never take a lot below zero.</summary>
        public async Task ApplyToBatchAsync(StockMovement movement, Guid stockBatchId)
        {
            var branchId = BranchOf(movement);
            var updated = await _context.Database.SqlQuery<decimal>($"""
                UPDATE "StockBatches"
                SET "QuantityOnHand" = "QuantityOnHand" + {movement.QuantityChange}, "LastUpdatedAt" = now()
                WHERE "StockBatchId" = {stockBatchId} AND "ProductId" = {movement.ProductId} AND "BranchId" = {branchId}
                  AND "QuantityOnHand" + {movement.QuantityChange} >= 0
                RETURNING "QuantityOnHand" AS "Value"
                """).ToListAsync();

            if (updated.Count == 0)
                throw new InsufficientStockException(
                    $"Batch '{stockBatchId}' does not hold enough stock of this product at this branch for this change.");

            movement.StockBatchId = stockBatchId;
            await ApplyAsync(movement);
        }

        /// <summary>
        /// Issues <paramref name="template"/>'s quantity first-expiry-first-out, from lots that have not expired by
        /// <paramref name="today"/>. Lots without an expiry date go last. Fails rather than issuing expired stock or going
        /// negative. Returns how much came from each lot, with one movement recorded per lot.
        /// </summary>
        public async Task<IReadOnlyList<(Guid StockBatchId, decimal Quantity, decimal? UnitCost)>> IssueFefoAsync(StockMovement template, DateOnly today)
        {
            var required = -template.QuantityChange;
            if (required <= 0)
                throw new InvalidOperationException("IssueFefoAsync only removes stock.");
            var branchId = BranchOf(template);

            // Lock the usable lots in FEFO order, so a concurrent sale of the same product waits rather than double-allocating.
            var lots = await _context.Database.SqlQuery<BatchRow>($"""
                SELECT "StockBatchId", "ExpiryDate", "QuantityOnHand"
                FROM "StockBatches"
                WHERE "ProductId" = {template.ProductId} AND "BranchId" = {branchId} AND "QuantityOnHand" > 0
                  AND ("ExpiryDate" IS NULL OR "ExpiryDate" >= {today})
                ORDER BY "ExpiryDate" ASC NULLS LAST, "CreatedAt" ASC, "StockBatchId" ASC
                FOR UPDATE
                """).ToListAsync();

            var available = lots.Sum(l => l.QuantityOnHand);
            if (available < required)
                throw new InsufficientStockException(
                    $"Only {available:0.####} of product '{template.ProductId}' is in unexpired batches at this branch; {required:0.####} is needed. " +
                    "Expired stock cannot be sold — write it off with a stock adjustment.");

            var issued = new List<(Guid, decimal, decimal?)>();
            var remaining = required;
            foreach (var lot in lots)
            {
                if (remaining <= 0) break;
                var take = Math.Min(lot.QuantityOnHand, remaining);
                remaining -= take;

                var movement = new StockMovement
                {
                    ProductId = template.ProductId,
                    BranchId = branchId,
                    MovementType = template.MovementType,
                    QuantityChange = -take,
                    ReferenceType = template.ReferenceType,
                    ReferenceId = template.ReferenceId,
                    BatchId = template.BatchId,
                    Notes = template.Notes,
                    CreatedBy = template.CreatedBy
                };
                await ApplyToBatchAsync(movement, lot.StockBatchId);
                issued.Add((lot.StockBatchId, take, movement.UnitCost));
            }

            return issued;
        }

        private sealed class BatchRow
        {
            public Guid StockBatchId { get; set; }
            public DateOnly? ExpiryDate { get; set; }
            public decimal QuantityOnHand { get; set; }
        }

        /// <summary>Sets the average cost without touching quantity. Returns the cost it replaced.</summary>
        public async Task<decimal> RevalueAsync(Guid productId, decimal newUnitCost, Guid? branchId = null)
        {
            var branch = branchId ?? _branches.RequireActiveBranchId();
            var previous = await _context.Database.SqlQuery<decimal>($"""
                UPDATE "StockLevels" AS s
                SET "AverageUnitCost" = {newUnitCost}, "LastUpdatedAt" = now()
                FROM (SELECT "ProductId", "BranchId", "AverageUnitCost" FROM "StockLevels"
                      WHERE "ProductId" = {productId} AND "BranchId" = {branch} FOR UPDATE) AS prev
                WHERE s."ProductId" = prev."ProductId" AND s."BranchId" = prev."BranchId"
                RETURNING prev."AverageUnitCost" AS "Value"
                """).ToListAsync();

            if (previous.Count == 0)
                throw new NotFoundException(
                    $"No stock record for product '{productId}' at this branch — nothing has been received, produced or adjusted against it here yet. Receive or adjust stock first.");

            return previous[0];
        }

        private async Task<LedgerRow> ReceiveAtCostAsync(StockMovement movement)
        {
            var rows = await QueryRow($"""
                INSERT INTO "StockLevels" ("ProductId", "BranchId", "QuantityOnHand", "AverageUnitCost", "LastUpdatedAt")
                VALUES ({movement.ProductId}, {movement.BranchId}, {movement.QuantityChange}, {movement.UnitCost!.Value}, now())
                ON CONFLICT ("ProductId", "BranchId") DO UPDATE
                SET "AverageUnitCost" = CASE
                        WHEN "StockLevels"."QuantityOnHand" <= 0 THEN EXCLUDED."AverageUnitCost"
                        ELSE ROUND(("StockLevels"."QuantityOnHand" * "StockLevels"."AverageUnitCost"
                                  + EXCLUDED."QuantityOnHand" * EXCLUDED."AverageUnitCost")
                                  / ("StockLevels"."QuantityOnHand" + EXCLUDED."QuantityOnHand"), 6) END,
                    "QuantityOnHand" = "StockLevels"."QuantityOnHand" + EXCLUDED."QuantityOnHand",
                    "LastUpdatedAt" = now()
                RETURNING "AverageUnitCost", "QuantityOnHand",
                          (SELECT p."ReorderLevel" FROM "Products" p WHERE p."ProductId" = {movement.ProductId}) AS "ReorderLevel"
                """);
            return rows.Single();
        }

        private async Task<LedgerRow> ReceiveAtCurrentCostAsync(StockMovement movement)
        {
            var rows = await QueryRow($"""
                INSERT INTO "StockLevels" ("ProductId", "BranchId", "QuantityOnHand", "AverageUnitCost", "LastUpdatedAt")
                VALUES ({movement.ProductId}, {movement.BranchId}, {movement.QuantityChange}, 0, now())
                ON CONFLICT ("ProductId", "BranchId") DO UPDATE
                SET "QuantityOnHand" = "StockLevels"."QuantityOnHand" + EXCLUDED."QuantityOnHand",
                    "LastUpdatedAt" = now()
                RETURNING "AverageUnitCost", "QuantityOnHand",
                          (SELECT p."ReorderLevel" FROM "Products" p WHERE p."ProductId" = {movement.ProductId}) AS "ReorderLevel"
                """);
            return rows.Single();
        }

        private async Task<LedgerRow> IssueAllowingNegativeAsync(StockMovement movement)
        {
            var rows = await QueryRow($"""
                INSERT INTO "StockLevels" ("ProductId", "BranchId", "QuantityOnHand", "AverageUnitCost", "LastUpdatedAt")
                VALUES ({movement.ProductId}, {movement.BranchId}, {movement.QuantityChange}, 0, now())
                ON CONFLICT ("ProductId", "BranchId") DO UPDATE
                SET "QuantityOnHand" = "StockLevels"."QuantityOnHand" + EXCLUDED."QuantityOnHand",
                    "LastUpdatedAt" = now()
                RETURNING "AverageUnitCost", "QuantityOnHand",
                          (SELECT p."ReorderLevel" FROM "Products" p WHERE p."ProductId" = {movement.ProductId}) AS "ReorderLevel"
                """);
            return rows.Single();
        }

        private async Task<LedgerRow> IssueGuardedAsync(StockMovement movement)
        {
            var rows = await QueryRow($"""
                UPDATE "StockLevels"
                SET "QuantityOnHand" = "QuantityOnHand" + {movement.QuantityChange}, "LastUpdatedAt" = now()
                WHERE "ProductId" = {movement.ProductId} AND "BranchId" = {movement.BranchId}
                  AND "QuantityOnHand" + {movement.QuantityChange} >= 0
                RETURNING "AverageUnitCost", "QuantityOnHand",
                          (SELECT p."ReorderLevel" FROM "Products" p WHERE p."ProductId" = {movement.ProductId}) AS "ReorderLevel"
                """);

            if (rows.Count == 0)
                throw new InsufficientStockException(
                    $"Stock for product '{movement.ProductId}' at this branch is no longer sufficient — it changed while this operation was in progress. Review stock and retry.");

            return rows[0];
        }
    }
}