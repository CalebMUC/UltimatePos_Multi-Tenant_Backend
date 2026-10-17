using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static UltimatePos.Domain.Enums.PosEnums;

namespace UltimatePos.Application.Purchasing.Dtos
{
    /// <param name="BusinessId">Optional. Suppliers are always registered in the business signed in to; if given it must be that one.</param>
    public record CreateSupplierRequestDto(Guid? BusinessId, string SupplierName, string? KraPin, string? ContactPerson, string? PhoneNumber, string? Email, string? PhysicalAddress, int? PaymentTermDays);
    public record UpdateSupplierRequestDto(string SupplierName, string? KraPin, string? ContactPerson, string? PhoneNumber, string? Email, string? PhysicalAddress, int? PaymentTermDays);
    public record SupplierDto(Guid SupplierId, Guid BusinessId, string SupplierName, string? KraPin, string? ContactPerson, string? PhoneNumber, string? Email, string? PhysicalAddress, int? PaymentTermDays, bool IsActive);

    public record CreatePurchaseOrderLineRequestDto(Guid ProductId, Guid UnitOfMeasureId, decimal QuantityOrdered, decimal UnitCost);
    public record CreatePurchaseOrderRequestDto(Guid SupplierId, DateTime? ExpectedDeliveryDate, string? Notes, IEnumerable<CreatePurchaseOrderLineRequestDto> Lines);
    public record PurchaseOrderLineDto(Guid PurchaseOrderLineId, Guid ProductId, Guid UnitOfMeasureId, decimal QuantityOrdered, decimal QuantityReceived, decimal UnitCost,
        string? ProductName = null, string? Sku = null, string? UnitSymbol = null, bool IsBatchTracked = false);
    public record PurchaseOrderDto(Guid PurchaseOrderId, string OrderNumber, Guid SupplierId, PurchaseOrderStatus Status, DateTime OrderDate, DateTime? ExpectedDeliveryDate, string? Notes, bool IsActive, IEnumerable<PurchaseOrderLineDto> Lines,
        string? SupplierName = null);

    /// <param name="QuantityReceived">In the ordered unit.</param>
    /// <param name="BatchNumber">Required for batch-tracked products. A line may appear several times to receive several batches.</param>
    public record ReceiveLineRequestDto(Guid PurchaseOrderLineId, decimal QuantityReceived,
        string? BatchNumber = null, DateOnly? ExpiryDate = null, DateOnly? ManufactureDate = null);
    /// <param name="SupplierInvoiceNumber">The supplier's invoice or delivery note, recorded on the bill this receipt posts.</param>
    public record ReceivePurchaseOrderRequestDto(IEnumerable<ReceiveLineRequestDto> Lines, string? Notes, string? SupplierInvoiceNumber = null);
}
