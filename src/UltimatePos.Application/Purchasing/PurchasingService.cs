using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UltimatePos.Application.Catalog;
using UltimatePos.Application.Common.Dtos;
using UltimatePos.Application.Common.Interfaces;
using UltimatePos.Application.Purchasing.Dtos;
using UltimatePos.Domain.Entities;
using UltimatePos.Domain.Exceptions;
using static UltimatePos.Domain.Enums.PosEnums;

namespace UltimatePos.Application.Purchasing
{
    public class PurchasingService
    {
        private readonly IPurchasingRepository _repository;
        private readonly ICatalogRepository _catalogRepository;
        private readonly ICurrentUser _currentUser;

        public PurchasingService(IPurchasingRepository repository, ICatalogRepository catalogRepository, ICurrentUser currentUser)
        {
            _repository = repository;
            _catalogRepository = catalogRepository;
            _currentUser = currentUser;
        }

        public async Task<SupplierDto> RegisterSupplierAsync(CreateSupplierRequestDto request)
        {
            var supplier = new Supplier
            {
                BusinessId = request.BusinessId,
                SupplierName = request.SupplierName,
                KraPin = request.KraPin,
                ContactPerson = request.ContactPerson,
                PhoneNumber = request.PhoneNumber,
                Email = request.Email,
                PhysicalAddress = request.PhysicalAddress,
                PaymentTermDays = request.PaymentTermDays,
                CreatedBy = _currentUser.UserId
            };
            return ToDto(await _repository.CreateSupplierAsync(supplier));
        }

        public async Task<IEnumerable<SupplierDto>> GetSuppliersByBusinessAsync(Guid businessId) =>
            (await _repository.GetSuppliersByBusinessAsync(businessId)).Select(ToDto);

        public async Task<SupplierDto> GetSupplierByIdAsync(Guid supplierId)
        {
            var supplier = await _repository.GetSupplierByIdAsync(supplierId)
                ?? throw new NotFoundException($"Supplier '{supplierId}' not found.");
            return ToDto(supplier);
        }

        public async Task<SupplierDto> UpdateSupplierAsync(Guid supplierId, UpdateSupplierRequestDto request) =>
            ToDto(await _repository.UpdateSupplierAsync(supplierId, request.SupplierName, request.KraPin, request.ContactPerson,
                request.PhoneNumber, request.Email, request.PhysicalAddress, request.PaymentTermDays, _currentUser.UserId));

        public async Task<SupplierDto> SetSupplierActiveStatusAsync(Guid supplierId, bool isActive) =>
            ToDto(await _repository.SetSupplierActiveStatusAsync(supplierId, isActive, _currentUser.UserId));

        public async Task<PurchaseOrderDto> CreatePurchaseOrderAsync(CreatePurchaseOrderRequestDto request)
        {
            if (await _repository.GetSupplierByIdAsync(request.SupplierId) is null)
                throw new NotFoundException($"Supplier '{request.SupplierId}' not found.");

            var lines = request.Lines.ToList();
            if (lines.Count == 0)
                throw new InvalidAssignmentException("A purchase order needs at least one line.");
            if (lines.Any(l => l.QuantityOrdered <= 0))
                throw new InvalidAssignmentException("Quantity ordered must be greater than zero on every line.");
            if (lines.Any(l => l.UnitCost < 0))
                throw new InvalidAssignmentException("Unit cost cannot be negative.");

            var number = await _repository.GetNextDocumentNumberAsync("PO");

            var order = new PurchaseOrder
            {
                OrderNumber = $"PO-{number:D6}",
                SupplierId = request.SupplierId,
                Status = PurchaseOrderStatus.Ordered,
                ExpectedDeliveryDate = request.ExpectedDeliveryDate,
                Notes = request.Notes,
                CreatedBy = _currentUser.UserId,
                Lines = lines.Select(l => new PurchaseOrderLine
                {
                    ProductId = l.ProductId,
                    UnitOfMeasureId = l.UnitOfMeasureId,
                    QuantityOrdered = l.QuantityOrdered,
                    UnitCost = l.UnitCost,
                    CreatedBy = _currentUser.UserId
                }).ToList()
            };

            return ToDto(await _repository.CreatePurchaseOrderAsync(order));
        }

        public async Task<PagedResult<PurchaseOrderDto>> GetPurchaseOrdersAsync(int page, int pageSize, Guid? supplierId, PurchaseOrderStatus? status)
        {
            var (items, totalCount) = await _repository.GetPurchaseOrdersAsync(page, pageSize, supplierId, status);
            return new PagedResult<PurchaseOrderDto>(items.Select(ToDto), page, pageSize, totalCount);
        }

        public async Task<PurchaseOrderDto> GetPurchaseOrderByIdAsync(Guid purchaseOrderId)
        {
            var order = await _repository.GetPurchaseOrderByIdAsync(purchaseOrderId)
                ?? throw new NotFoundException($"Purchase order '{purchaseOrderId}' not found.");
            return ToDto(order);
        }

        public async Task<PurchaseOrderDto> CancelPurchaseOrderAsync(Guid purchaseOrderId)
        {
            var order = await _repository.GetPurchaseOrderByIdAsync(purchaseOrderId)
                ?? throw new NotFoundException($"Purchase order '{purchaseOrderId}' not found.");

            if (order.Status is PurchaseOrderStatus.Received or PurchaseOrderStatus.PartiallyReceived)
                throw new InvalidAssignmentException("Cannot cancel a purchase order that has already received stock — received quantities must be reconciled first.");

            return ToDto(await _repository.SetPurchaseOrderStatusAsync(purchaseOrderId, PurchaseOrderStatus.Cancelled, _currentUser.UserId));
        }

        public async Task<PurchaseOrderDto> ReceivePurchaseOrderAsync(Guid purchaseOrderId, ReceivePurchaseOrderRequestDto request)
        {
            var order = await _repository.GetPurchaseOrderByIdAsync(purchaseOrderId)
                ?? throw new NotFoundException($"Purchase order '{purchaseOrderId}' not found.");

            if (order.Status is PurchaseOrderStatus.Received or PurchaseOrderStatus.Cancelled)
                throw new InvalidAssignmentException($"Purchase order is already {order.Status} and cannot be received against.");

            var receiveLines = request.Lines.ToList();
            if (receiveLines.Count == 0)
                throw new InvalidAssignmentException("At least one line must be specified to receive.");

            var lineUpdates = new List<(Guid, decimal)>();
            var movements = new List<(Guid, decimal)>();

            foreach (var receiveLine in receiveLines)
            {
                var line = order.Lines.FirstOrDefault(l => l.PurchaseOrderLineId == receiveLine.PurchaseOrderLineId)
                    ?? throw new NotFoundException($"Line '{receiveLine.PurchaseOrderLineId}' not found on this order.");

                if (receiveLine.QuantityReceived <= 0)
                    throw new InvalidAssignmentException("Quantity received must be greater than zero.");

                var newTotal = line.QuantityReceived + receiveLine.QuantityReceived;
                if (newTotal > line.QuantityOrdered)
                    throw new InvalidAssignmentException(
                        $"Receiving {receiveLine.QuantityReceived} on line '{line.PurchaseOrderLineId}' would exceed the ordered quantity " +
                        $"({line.QuantityOrdered}, already received {line.QuantityReceived}).");

                var factor = await _catalogRepository.GetConversionFactorAsync(line.ProductId, line.UnitOfMeasureId)
                    ?? throw new InvalidAssignmentException(
                        $"No unit conversion exists for product '{line.ProductId}' in the ordered unit — add one via POST /products/{{id}}/unit-conversions first.");

                lineUpdates.Add((line.PurchaseOrderLineId, newTotal));
                movements.Add((line.ProductId, receiveLine.QuantityReceived * factor));
            }

            var finalQuantities = order.Lines.ToDictionary(l => l.PurchaseOrderLineId, l => l.QuantityReceived);
            foreach (var (lineId, newQty) in lineUpdates)
                finalQuantities[lineId] = newQty;

            var allReceived = order.Lines.All(l => finalQuantities[l.PurchaseOrderLineId] >= l.QuantityOrdered);
            var anyReceived = order.Lines.Any(l => finalQuantities[l.PurchaseOrderLineId] > 0);
            var newStatus = allReceived ? PurchaseOrderStatus.Received : anyReceived ? PurchaseOrderStatus.PartiallyReceived : order.Status;

            var batchId = Guid.NewGuid();
            await _repository.ReceivePurchaseOrderAsync(purchaseOrderId, newStatus, lineUpdates, movements, batchId, request.Notes, _currentUser.UserId);

            return await GetPurchaseOrderByIdAsync(purchaseOrderId);
        }

        private static SupplierDto ToDto(Supplier s) =>
            new(s.SupplierId, s.BusinessId, s.SupplierName, s.KraPin, s.ContactPerson, s.PhoneNumber, s.Email, s.PhysicalAddress, s.PaymentTermDays, s.IsActive);

        private static PurchaseOrderDto ToDto(PurchaseOrder o) =>
            new(o.PurchaseOrderId, o.OrderNumber, o.SupplierId, o.Status, o.OrderDate, o.ExpectedDeliveryDate, o.Notes, o.IsActive,
                o.Lines.Select(l => new PurchaseOrderLineDto(l.PurchaseOrderLineId, l.ProductId, l.UnitOfMeasureId, l.QuantityOrdered, l.QuantityReceived, l.UnitCost)));
    }
}
