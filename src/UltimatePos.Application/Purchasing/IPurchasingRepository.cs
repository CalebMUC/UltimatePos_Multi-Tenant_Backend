using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UltimatePos.Domain.Entities;
using static UltimatePos.Domain.Enums.PosEnums;

namespace UltimatePos.Application.Purchasing
{
    public interface IPurchasingRepository
    {
        Task<int> GetNextDocumentNumberAsync(string prefix);

        Task<Supplier> CreateSupplierAsync(Supplier supplier);
        /// <summary>Suppliers of the business in scope.</summary>
        Task<IEnumerable<Supplier>> GetSuppliersAsync();
        Task<Supplier?> GetSupplierByIdAsync(Guid supplierId);
        Task<Supplier> UpdateSupplierAsync(Guid supplierId, string supplierName, string? kraPin, string? contactPerson,
            string? phoneNumber, string? email, string? physicalAddress, int? paymentTermDays, Guid? updatedBy);
        Task<Supplier> SetSupplierActiveStatusAsync(Guid supplierId, bool isActive, Guid? updatedBy);

        Task<PurchaseOrder> CreatePurchaseOrderAsync(PurchaseOrder order);
        Task<(IEnumerable<PurchaseOrder> Items, int TotalCount)> GetPurchaseOrdersAsync(int page, int pageSize, Guid? supplierId, PurchaseOrderStatus? status);
        Task<PurchaseOrder?> GetPurchaseOrderByIdAsync(Guid purchaseOrderId);
        Task<PurchaseOrder> SetPurchaseOrderStatusAsync(Guid purchaseOrderId, PurchaseOrderStatus status, Guid? updatedBy);

        // Deliberately reaches into the stock tables directly — see the atomicity note above.
        // One SaveChangesAsync covering PurchaseOrder + PurchaseOrderLine + StockMovement + StockLevel together.
        // Batch-tracked lines go into their lot (created on first receipt); the bill, if any, posts to the supplier's account.
        Task ReceivePurchaseOrderAsync(
            Guid purchaseOrderId, Guid supplierId, PurchaseOrderStatus newStatus,
            IEnumerable<(Guid PurchaseOrderLineId, decimal NewQuantityReceived)> lineUpdates,
            IEnumerable<ReceiptMovement> movements,
            Guid batchId, string? notes, Guid? updatedBy, SupplierLedgerEntry? bill);
    }

    /// <summary>One receipt of stock: quantity and cost per base unit, and the lot it goes into for batch-tracked products.</summary>
    public record ReceiptMovement(Guid ProductId, decimal QuantityInBaseUnits, decimal? UnitCostPerBaseUnit,
        string? BatchNumber, DateOnly? ExpiryDate, DateOnly? ManufactureDate);
}
