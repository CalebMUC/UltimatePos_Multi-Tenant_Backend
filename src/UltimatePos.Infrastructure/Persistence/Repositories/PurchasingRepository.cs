using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UltimatePos.Application.Purchasing;
using UltimatePos.Domain.Entities;
using UltimatePos.Domain.Exceptions;
using static UltimatePos.Domain.Enums.PosEnums;

namespace UltimatePos.Infrastructure.Persistence.Repositories
{
    public class PurchasingRepository : IPurchasingRepository
    {
        private readonly UltimatePosDbContext _context;
        private readonly StockLedger _stockLedger;

        public PurchasingRepository(UltimatePosDbContext context, StockLedger stockLedger)
        {
            _context = context;
            _stockLedger = stockLedger;
        }

        public async Task<int> GetNextDocumentNumberAsync(string prefix)
        {
            var results = await _context.Database
                .SqlQuery<int>($"""
                INSERT INTO "SkuSequences" ("Prefix", "LastNumber") VALUES ({prefix}, 1)
                ON CONFLICT ("Prefix") DO UPDATE SET "LastNumber" = "SkuSequences"."LastNumber" + 1
                RETURNING "LastNumber" AS "Value"
                """)
                .ToListAsync();
            return results.Single();
        }

        public async Task<Supplier> CreateSupplierAsync(Supplier supplier)
        {
            _context.Suppliers.Add(supplier);
            await _context.SaveChangesAsync();
            return supplier;
        }

        public async Task<IEnumerable<Supplier>> GetSuppliersByBusinessAsync(Guid businessId) =>
            await _context.Suppliers.AsNoTracking().Where(s => s.BusinessId == businessId).ToListAsync();

        public async Task<Supplier?> GetSupplierByIdAsync(Guid supplierId) =>
            await _context.Suppliers.AsNoTracking().FirstOrDefaultAsync(s => s.SupplierId == supplierId);

        public async Task<Supplier> UpdateSupplierAsync(Guid supplierId, string supplierName, string? kraPin, string? contactPerson,
            string? phoneNumber, string? email, string? physicalAddress, int? paymentTermDays, Guid? updatedBy)
        {
            var supplier = await _context.Suppliers.FirstOrDefaultAsync(s => s.SupplierId == supplierId)
                ?? throw new NotFoundException($"Supplier '{supplierId}' not found.");

            supplier.SupplierName = supplierName;
            supplier.KraPin = kraPin;
            supplier.ContactPerson = contactPerson;
            supplier.PhoneNumber = phoneNumber;
            supplier.Email = email;
            supplier.PhysicalAddress = physicalAddress;
            supplier.PaymentTermDays = paymentTermDays;
            supplier.LastUpdatedBy = updatedBy;
            supplier.LastUpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return supplier;
        }

        public async Task<Supplier> SetSupplierActiveStatusAsync(Guid supplierId, bool isActive, Guid? updatedBy)
        {
            var supplier = await _context.Suppliers.FirstOrDefaultAsync(s => s.SupplierId == supplierId)
                ?? throw new NotFoundException($"Supplier '{supplierId}' not found.");

            supplier.IsActive = isActive;
            supplier.LastUpdatedBy = updatedBy;
            supplier.LastUpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return supplier;
        }

        public async Task<PurchaseOrder> CreatePurchaseOrderAsync(PurchaseOrder order)
        {
            _context.PurchaseOrders.Add(order);
            await _context.SaveChangesAsync();
            return order;
        }

        public async Task<(IEnumerable<PurchaseOrder> Items, int TotalCount)> GetPurchaseOrdersAsync(int page, int pageSize, Guid? supplierId, PurchaseOrderStatus? status)
        {
            var query = _context.PurchaseOrders.AsNoTracking().Include(o => o.Lines).AsQueryable();

            if (supplierId.HasValue)
                query = query.Where(o => o.SupplierId == supplierId.Value);
            if (status.HasValue)
                query = query.Where(o => o.Status == status.Value);

            var totalCount = await query.CountAsync();
            var items = await query.OrderByDescending(o => o.OrderDate).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

            return (items, totalCount);
        }

        public async Task<PurchaseOrder?> GetPurchaseOrderByIdAsync(Guid purchaseOrderId) =>
            await _context.PurchaseOrders.AsNoTracking().Include(o => o.Lines).FirstOrDefaultAsync(o => o.PurchaseOrderId == purchaseOrderId);

        public async Task<PurchaseOrder> SetPurchaseOrderStatusAsync(Guid purchaseOrderId, PurchaseOrderStatus status, Guid? updatedBy)
        {
            var order = await _context.PurchaseOrders.Include(o => o.Lines).FirstOrDefaultAsync(o => o.PurchaseOrderId == purchaseOrderId)
                ?? throw new NotFoundException($"Purchase order '{purchaseOrderId}' not found.");

            order.Status = status;
            order.LastUpdatedBy = updatedBy;
            order.LastUpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return order;
        }

        public async Task ReceivePurchaseOrderAsync(
            Guid purchaseOrderId, PurchaseOrderStatus newStatus,
            IEnumerable<(Guid PurchaseOrderLineId, decimal NewQuantityReceived)> lineUpdates,
            IEnumerable<(Guid ProductId, decimal QuantityInBaseUnits)> movements,
            Guid batchId, string? notes, Guid? updatedBy)
        {
            await using var tx = await _context.Database.BeginTransactionAsync();

            var order = await _context.PurchaseOrders.FirstOrDefaultAsync(o => o.PurchaseOrderId == purchaseOrderId)
                ?? throw new NotFoundException($"Purchase order '{purchaseOrderId}' not found.");

            order.Status = newStatus;
            order.LastUpdatedBy = updatedBy;
            order.LastUpdatedAt = DateTime.UtcNow;

            foreach (var (lineId, newQty) in lineUpdates)
            {
                var line = await _context.PurchaseOrderLines.FirstOrDefaultAsync(l => l.PurchaseOrderLineId == lineId)
                    ?? throw new NotFoundException($"Purchase order line '{lineId}' not found.");
                line.QuantityReceived = newQty;
            }

            // Deterministic order by product so concurrent operations lock stock rows in the same sequence (no deadlocks).
            foreach (var (productId, quantity) in movements.OrderBy(m => m.ProductId))
            {
                await _stockLedger.ApplyAsync(new StockMovement
                {
                    ProductId = productId,
                    MovementType = StockMovementType.PurchaseReceipt,
                    QuantityChange = quantity,
                    ReferenceType = "PurchaseOrder",
                    ReferenceId = purchaseOrderId,
                    BatchId = batchId,
                    Notes = notes,
                    CreatedBy = updatedBy
                });
            }

            await _context.SaveChangesAsync();
            await tx.CommitAsync();
        }



    }
}
