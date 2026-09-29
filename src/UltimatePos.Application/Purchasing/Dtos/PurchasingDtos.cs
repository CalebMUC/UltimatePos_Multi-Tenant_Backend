using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static UltimatePos.Domain.Enums.PosEnums;

namespace UltimatePos.Application.Purchasing.Dtos
{
    public record CreateSupplierRequestDto(Guid BusinessId, string SupplierName, string? KraPin, string? ContactPerson, string? PhoneNumber, string? Email, string? PhysicalAddress, int? PaymentTermDays);
    public record UpdateSupplierRequestDto(string SupplierName, string? KraPin, string? ContactPerson, string? PhoneNumber, string? Email, string? PhysicalAddress, int? PaymentTermDays);
    public record SupplierDto(Guid SupplierId, Guid BusinessId, string SupplierName, string? KraPin, string? ContactPerson, string? PhoneNumber, string? Email, string? PhysicalAddress, int? PaymentTermDays, bool IsActive);

    public record CreatePurchaseOrderLineRequestDto(Guid ProductId, Guid UnitOfMeasureId, decimal QuantityOrdered, decimal UnitCost);
    public record CreatePurchaseOrderRequestDto(Guid SupplierId, DateTime? ExpectedDeliveryDate, string? Notes, IEnumerable<CreatePurchaseOrderLineRequestDto> Lines);

    public record PurchaseOrderLineDto(Guid PurchaseOrderLineId, Guid ProductId, Guid UnitOfMeasureId, decimal QuantityOrdered, decimal QuantityReceived, decimal UnitCost);
    public record PurchaseOrderDto(Guid PurchaseOrderId, string OrderNumber, Guid SupplierId, PurchaseOrderStatus Status, DateTime OrderDate, DateTime? ExpectedDeliveryDate, string? Notes, bool IsActive, IEnumerable<PurchaseOrderLineDto> Lines);

    public record ReceiveLineRequestDto(Guid PurchaseOrderLineId, decimal QuantityReceived);
    public record ReceivePurchaseOrderRequestDto(IEnumerable<ReceiveLineRequestDto> Lines, string? Notes);
}
