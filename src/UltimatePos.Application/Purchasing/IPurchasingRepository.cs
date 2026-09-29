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
        Task<IEnumerable<Supplier>> GetSuppliersByBusinessAsync(Guid businessId);
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
        Task ReceivePurchaseOrderAsync(
            Guid purchaseOrderId, PurchaseOrderStatus newStatus,
            IEnumerable<(Guid PurchaseOrderLineId, decimal NewQuantityReceived)> lineUpdates,
            IEnumerable<(Guid ProductId, decimal QuantityInBaseUnits)> movements,
            Guid batchId, string? notes, Guid? updatedBy);
    }
}
