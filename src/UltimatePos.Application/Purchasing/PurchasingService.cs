using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UltimatePos.Application.Notifications;
using UltimatePos.Application.Business;
using UltimatePos.Application.Catalog;
using UltimatePos.Application.Common.Dtos;
using UltimatePos.Application.Common.Helpers;
using UltimatePos.Application.Common.Interfaces;
using UltimatePos.Application.Costing;
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
        private readonly IBusinessRepository _businessRepository;
        private readonly ITenantContext _tenant;
        private readonly ISystemEventRecorder _events;
        public PurchasingService(IPurchasingRepository repository, ICatalogRepository catalogRepository,
                  IBusinessRepository businessRepository,  ICurrentUser currentUser, ITenantContext tenant, ISystemEventRecorder events)
        {
            _events = events;
            _repository = repository;
            _catalogRepository = catalogRepository;
            _currentUser = currentUser;
            _businessRepository = businessRepository;
            _tenant = tenant;
        }

        /// <summary>The business signed in to; a business id sent by an older client must match it.</summary>
        private Guid CurrentBusiness(Guid? given)
        {
            var businessId = _tenant.RequireBusinessId();
            if (given is { } g && g != Guid.Empty && g != businessId)
                throw new ForbiddenException("You can only work with suppliers and purchases of the business you are signed in to.");
            return businessId;
        }

        public async Task<SupplierDto> RegisterSupplierAsync(CreateSupplierRequestDto request)
        {
            var businessId = CurrentBusiness(request.BusinessId);
            if (string.IsNullOrWhiteSpace(request.SupplierName))
                throw new ValidationException("supplierName", "Supplier name is required.");
            if (request.PaymentTermDays is < 0 or > 365)
                throw new ValidationException("paymentTermDays", "Payment terms must be between 0 and 365 days.");

            var supplier = new Supplier
            {
                BusinessId = businessId,
                SupplierName = request.SupplierName.Trim(),
                KraPin = request.KraPin,
                ContactPerson = request.ContactPerson,
                PhoneNumber = PhoneNumberHelper.NormalizePhoneNumber(request.PhoneNumber),
                Email = request.Email,
                PhysicalAddress = request.PhysicalAddress,
                PaymentTermDays = request.PaymentTermDays,
                CreatedBy = _currentUser.UserId
            };
            return ToDto(await _repository.CreateSupplierAsync(supplier));
        }

        public async Task<IEnumerable<SupplierDto>> GetSuppliersAsync(Guid? businessId = null)
        {
            CurrentBusiness(businessId);
            return (await _repository.GetSuppliersAsync()).Select(ToDto);
        }

        public async Task<SupplierDto> GetSupplierByIdAsync(Guid supplierId)
        {
            var supplier = await _repository.GetSupplierByIdAsync(supplierId)
                ?? throw new NotFoundException($"Supplier '{supplierId}' not found.");
            return ToDto(supplier);
        }

        public async Task<SupplierDto> UpdateSupplierAsync(Guid supplierId, UpdateSupplierRequestDto request)
        {
            if (string.IsNullOrWhiteSpace(request.SupplierName))
                throw new ValidationException("supplierName", "Supplier name is required.");
            if (request.PaymentTermDays is < 0 or > 365)
                throw new ValidationException("paymentTermDays", "Payment terms must be between 0 and 365 days.");
            return ToDto(await _repository.UpdateSupplierAsync(supplierId, request.SupplierName.Trim(), request.KraPin, request.ContactPerson,
                PhoneNumberHelper.NormalizePhoneNumber(request.PhoneNumber), request.Email, request.PhysicalAddress, request.PaymentTermDays, _currentUser.UserId));
        }

        public async Task<SupplierDto> SetSupplierActiveStatusAsync(Guid supplierId, bool isActive) =>
            ToDto(await _repository.SetSupplierActiveStatusAsync(supplierId, isActive, _currentUser.UserId));

        //public async Task<PurchaseOrderDto> CreatePurchaseOrderAsync(CreatePurchaseOrderRequestDto request)
        //{
        //    if (await _repository.GetSupplierByIdAsync(request.SupplierId) is null)
        //        throw new NotFoundException($"Supplier '{request.SupplierId}' not found.");

        //    var lines = request.Lines.ToList();
        //    if (lines.Count == 0)
        //        throw new InvalidAssignmentException("A purchase order needs at least one line.");
        //    if (lines.Any(l => l.QuantityOrdered <= 0))
        //        throw new InvalidAssignmentException("Quantity ordered must be greater than zero on every line.");
        //    if (lines.Any(l => l.UnitCost < 0))
        //        throw new InvalidAssignmentException("Unit cost cannot be negative.");

        //    // Fail now, not at receive time: every line's product must exist and convert in the unit ordered.
        //    foreach (var line in lines)
        //    {
        //        if (await _catalogRepository.GetConversionFactorAsync(line.ProductId, line.UnitOfMeasureId) is null)
        //            throw new InvalidAssignmentException(
        //                $"Product '{line.ProductId}' does not exist or has no conversion for the unit ordered — " +
        //                "add one via POST /products/{id}/unit-conversions first.");
        //    }

        //    var number = await _repository.GetNextDocumentNumberAsync("PO");

        //    var order = new PurchaseOrder
        //    {
        //        OrderNumber = $"PO-{number:D6}",
        //        SupplierId = request.SupplierId,
        //        Status = PurchaseOrderStatus.Ordered,
        //        ExpectedDeliveryDate = request.ExpectedDeliveryDate,
        //        Notes = request.Notes,
        //        CreatedBy = _currentUser.UserId,
        //        Lines = lines.Select(l => new PurchaseOrderLine
        //        {
        //            ProductId = l.ProductId,
        //            UnitOfMeasureId = l.UnitOfMeasureId,
        //            QuantityOrdered = l.QuantityOrdered,
        //            UnitCost = l.UnitCost,
        //            CreatedBy = _currentUser.UserId
        //        }).ToList()
        //    };

        //    return ToDto(await _repository.CreatePurchaseOrderAsync(order));
        //}


        public async Task<PurchaseOrderDto> CreatePurchaseOrderAsync(CreatePurchaseOrderRequestDto request)
        {
            var supplier = await _repository.GetSupplierByIdAsync(request.SupplierId)
                ?? throw new NotFoundException($"Supplier '{request.SupplierId}' not found.");

            if (!supplier.IsActive)
                throw new InvalidAssignmentException($"Supplier '{supplier.SupplierName}' is deactivated.");

            var lines = (request.Lines ?? Enumerable.Empty<CreatePurchaseOrderLineRequestDto>()).ToList();
            if (lines.Count == 0)
                throw new InvalidAssignmentException("A purchase order needs at least one line.");
            if (lines.Any(l => l.QuantityOrdered <= 0))
                throw new InvalidAssignmentException("Quantity ordered must be greater than zero on every line.");
            if (lines.Any(l => l.UnitCost < 0))
                throw new InvalidAssignmentException("Unit cost cannot be negative.");

            // The buying business is the supplier's owner; what it may buy depends on its kind.
            await EnsureProductsPurchasableAsync(supplier.BusinessId, lines.Select(l => l.ProductId));

            // Fail now, not at receive time: every line's product must exist and convert in the unit ordered.
            foreach (var line in lines)
            {
                if (await _catalogRepository.GetConversionFactorAsync(line.ProductId, line.UnitOfMeasureId) is null)
                    throw new InvalidAssignmentException(
                        $"Product '{line.ProductId}' does not exist or has no conversion for the unit ordered — " +
                        "add one via POST /products/{id}/unit-conversions first.");
            }

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

            var created = await _repository.CreatePurchaseOrderAsync(order);
            return await GetPurchaseOrderByIdAsync(created.PurchaseOrderId);
        }

        public async Task<PagedResult<PurchaseOrderDto>> GetPurchaseOrdersAsync(int page, int pageSize, Guid? supplierId, PurchaseOrderStatus? status)
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);
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

            _events.Record(new SystemEventDraft(SystemEventTypes.PurchaseOrderCancelled, $"{order.OrderNumber} was cancelled.",
                EntityType: "PurchaseOrder", EntityId: purchaseOrderId, Data: new { order.OrderNumber },
                ClearsAlertKeys: new[] { AlertKeys.PurchaseOrderOverdue(purchaseOrderId) }));
            await _repository.SetPurchaseOrderStatusAsync(purchaseOrderId, PurchaseOrderStatus.Cancelled, _currentUser.UserId);
            return await GetPurchaseOrderByIdAsync(purchaseOrderId);
        }

        //public async Task<PurchaseOrderDto> ReceivePurchaseOrderAsync(Guid purchaseOrderId, ReceivePurchaseOrderRequestDto request)
        //{
        //    var order = await _repository.GetPurchaseOrderByIdAsync(purchaseOrderId)
        //        ?? throw new NotFoundException($"Purchase order '{purchaseOrderId}' not found.");

        //    if (order.Status is PurchaseOrderStatus.Received or PurchaseOrderStatus.Cancelled)
        //        throw new InvalidAssignmentException($"Purchase order is already {order.Status} and cannot be received against.");

        //    var receiveLines = request.Lines.ToList();
        //    if (receiveLines.Count == 0)
        //        throw new InvalidAssignmentException("At least one line must be specified to receive.");

        //    var lineUpdates = new List<(Guid, decimal)>();
        //    var movements = new List<(Guid, decimal)>();

        //    foreach (var receiveLine in receiveLines)
        //    {
        //        var line = order.Lines.FirstOrDefault(l => l.PurchaseOrderLineId == receiveLine.PurchaseOrderLineId)
        //            ?? throw new NotFoundException($"Line '{receiveLine.PurchaseOrderLineId}' not found on this order.");

        //        if (receiveLine.QuantityReceived <= 0)
        //            throw new InvalidAssignmentException("Quantity received must be greater than zero.");

        //        var newTotal = line.QuantityReceived + receiveLine.QuantityReceived;
        //        if (newTotal > line.QuantityOrdered)
        //            throw new InvalidAssignmentException(
        //                $"Receiving {receiveLine.QuantityReceived} on line '{line.PurchaseOrderLineId}' would exceed the ordered quantity " +
        //                $"({line.QuantityOrdered}, already received {line.QuantityReceived}).");

        //        var factor = await _catalogRepository.GetConversionFactorAsync(line.ProductId, line.UnitOfMeasureId)
        //            ?? throw new InvalidAssignmentException(
        //                $"No unit conversion exists for product '{line.ProductId}' in the ordered unit — add one via POST /products/{{id}}/unit-conversions first.");

        //        lineUpdates.Add((line.PurchaseOrderLineId, newTotal));
        //        movements.Add((line.ProductId, receiveLine.QuantityReceived * factor));
        //    }

        //    var finalQuantities = order.Lines.ToDictionary(l => l.PurchaseOrderLineId, l => l.QuantityReceived);
        //    foreach (var (lineId, newQty) in lineUpdates)
        //        finalQuantities[lineId] = newQty;

        //    var allReceived = order.Lines.All(l => finalQuantities[l.PurchaseOrderLineId] >= l.QuantityOrdered);
        //    var anyReceived = order.Lines.Any(l => finalQuantities[l.PurchaseOrderLineId] > 0);
        //    var newStatus = allReceived ? PurchaseOrderStatus.Received : anyReceived ? PurchaseOrderStatus.PartiallyReceived : order.Status;

        //    var batchId = Guid.NewGuid();
        //    await _repository.ReceivePurchaseOrderAsync(purchaseOrderId, newStatus, lineUpdates, movements, batchId, request.Notes, _currentUser.UserId);

        //    return await GetPurchaseOrderByIdAsync(purchaseOrderId);
        //}


        public async Task<PurchaseOrderDto> ReceivePurchaseOrderAsync(Guid purchaseOrderId, ReceivePurchaseOrderRequestDto request)
        {
            var order = await _repository.GetPurchaseOrderByIdAsync(purchaseOrderId)
                ?? throw new NotFoundException($"Purchase order '{purchaseOrderId}' not found.");

            if (order.Status is PurchaseOrderStatus.Received or PurchaseOrderStatus.Cancelled)
                throw new InvalidAssignmentException($"Purchase order is already {order.Status} and cannot be received against.");

            var receiveLines = request.Lines.ToList();
            if (receiveLines.Count == 0)
                throw new InvalidAssignmentException("At least one line must be specified to receive.");

            // Re-check at the point stock actually changes. Only the lines being received are checked, so a
            // legitimate line on an order raised before this rule existed isn't blocked by an illegitimate one.
            var supplier = await _repository.GetSupplierByIdAsync(order.SupplierId)
                ?? throw new NotFoundException($"Supplier '{order.SupplierId}' not found.");
            var receivingLineIds = receiveLines.Select(r => r.PurchaseOrderLineId).ToHashSet();
            await EnsureProductsPurchasableAsync(supplier.BusinessId,
                order.Lines.Where(l => receivingLineIds.Contains(l.PurchaseOrderLineId)).Select(l => l.ProductId));

            var products = (await _catalogRepository.GetProductsByIdsAsync(order.Lines.Select(l => l.ProductId))).ToDictionary(p => p.ProductId);
            var finalQuantities = order.Lines.ToDictionary(l => l.PurchaseOrderLineId, l => l.QuantityReceived);
            var movements = new List<ReceiptMovement>();
            decimal billAmount = 0m;

            // A line may appear more than once — one entry per batch received — so quantities accumulate per line.
            foreach (var receiveLine in receiveLines)
            {
                var line = order.Lines.FirstOrDefault(l => l.PurchaseOrderLineId == receiveLine.PurchaseOrderLineId)
                    ?? throw new NotFoundException($"Line '{receiveLine.PurchaseOrderLineId}' not found on this order.");
                var product = products[line.ProductId];

                if (receiveLine.QuantityReceived <= 0)
                    throw new InvalidAssignmentException("Quantity received must be greater than zero.");

                var newTotal = finalQuantities[line.PurchaseOrderLineId] + receiveLine.QuantityReceived;
                if (newTotal > line.QuantityOrdered)
                    throw new InvalidAssignmentException(
                        $"Receiving {receiveLine.QuantityReceived} of '{product.Name}' would exceed the ordered quantity " +
                        $"({line.QuantityOrdered}, already received {finalQuantities[line.PurchaseOrderLineId]}).");
                finalQuantities[line.PurchaseOrderLineId] = newTotal;

                string? batchNumber = null;
                if (product.IsBatchTracked)
                {
                    if (string.IsNullOrWhiteSpace(receiveLine.BatchNumber))
                        throw new ValidationException("lines", $"'{product.Name}' is batch-tracked — give the batch number and expiry date of the stock received.");
                    batchNumber = receiveLine.BatchNumber.Trim();
                    if (batchNumber.Length > 64)
                        throw new ValidationException("lines", "A batch number is at most 64 characters.");
                    if (receiveLine.ExpiryDate is { } expiry && receiveLine.ManufactureDate is { } made && made > expiry)
                        throw new ValidationException("lines", $"The manufacture date of batch '{batchNumber}' is after its expiry date.");
                }
                else if (!string.IsNullOrWhiteSpace(receiveLine.BatchNumber))
                    throw new ValidationException("lines", $"'{product.Name}' is not batch-tracked — leave the batch out.");

                var factor = await _catalogRepository.GetConversionFactorAsync(line.ProductId, line.UnitOfMeasureId)
                    ?? throw new InvalidAssignmentException(
                        $"No unit conversion exists for product '{line.ProductId}' in the ordered unit — add one via POST /products/{{id}}/unit-conversions first.");

                // The supplier's price is per ordered unit (a 25kg bag); stock is costed per base unit (kg). A line with no
                // price (0) is received at the current average rather than blended in as free goods.
                decimal? unitCostPerBase = line.UnitCost > 0 ? CostingCalculator.ToUnitCostPerBaseUnit(line.UnitCost, factor) : null;
                movements.Add(new ReceiptMovement(line.ProductId, receiveLine.QuantityReceived * factor, unitCostPerBase,
                    batchNumber, product.IsBatchTracked ? receiveLine.ExpiryDate : null, product.IsBatchTracked ? receiveLine.ManufactureDate : null));
                billAmount += receiveLine.QuantityReceived * line.UnitCost;
            }

            var lineUpdates = finalQuantities
                .Where(kv => order.Lines.First(l => l.PurchaseOrderLineId == kv.Key).QuantityReceived != kv.Value)
                .Select(kv => (kv.Key, kv.Value))
                .ToList();

            var allReceived = order.Lines.All(l => finalQuantities[l.PurchaseOrderLineId] >= l.QuantityOrdered);
            var anyReceived = order.Lines.Any(l => finalQuantities[l.PurchaseOrderLineId] > 0);
            var newStatus = allReceived ? PurchaseOrderStatus.Received : anyReceived ? PurchaseOrderStatus.PartiallyReceived : order.Status;

            // Goods in on account: what the business now owes the supplier, at the ordered prices.
            billAmount = Math.Round(billAmount, 2, MidpointRounding.AwayFromZero);
            var bill = billAmount <= 0 ? null : new SupplierLedgerEntry
            {
                SupplierId = order.SupplierId,
                EntryType = SupplierLedgerEntryType.Bill,
                Amount = billAmount,
                ReferenceType = "PurchaseOrder",
                ReferenceId = purchaseOrderId,
                SupplierInvoiceNumber = string.IsNullOrWhiteSpace(request.SupplierInvoiceNumber) ? null : request.SupplierInvoiceNumber.Trim(),
                Notes = $"Goods received against {order.OrderNumber}",
                CreatedBy = _currentUser.UserId
            };

            _events.Record(new SystemEventDraft(SystemEventTypes.PurchaseOrderReceived,
                $"Goods received against {order.OrderNumber}: {receiveLines.Count} line(s), {billAmount:0.00} on account. Order is now {newStatus}.",
                EntityType: "PurchaseOrder", EntityId: purchaseOrderId,
                Data: new { order.OrderNumber, billAmount, status = newStatus.ToString(), request.SupplierInvoiceNumber },
                ClearsAlertKeys: newStatus == PurchaseOrderStatus.Received ? new[] { AlertKeys.PurchaseOrderOverdue(purchaseOrderId) } : null));
            var batchId = Guid.NewGuid();
            await _repository.ReceivePurchaseOrderAsync(purchaseOrderId, order.SupplierId, newStatus, lineUpdates, movements, batchId, request.Notes, _currentUser.UserId, bill);

            return await GetPurchaseOrderByIdAsync(purchaseOrderId);
        }

        /// <summary>Item types the business may purchase, so clients can filter the product picker up front.</summary>
        public async Task<IEnumerable<ItemType>> GetPurchasableItemTypesAsync(Guid? requestedBusinessId = null)
        {
            var businessId = CurrentBusiness(requestedBusinessId);
            var business = await _businessRepository.GetBusinessByIdAsync(businessId)
                ?? throw new NotFoundException($"Business '{businessId}' not found.");
            return PurchasePolicy.AllowedItemTypes(business.Kind);
        }

        private async Task EnsureProductsPurchasableAsync(Guid businessId, IEnumerable<Guid> productIds)
        {
            var ids = productIds.Distinct().ToList();
            if (ids.Count == 0) return;

            var business = await _businessRepository.GetBusinessByIdAsync(businessId)
                ?? throw new NotFoundException($"Business '{businessId}' not found.");
            var products = (await _catalogRepository.GetProductsByIdsAsync(ids)).ToDictionary(p => p.ProductId);

            var rejected = new List<string>();
            foreach (var id in ids)
            {
                if (!products.TryGetValue(id, out var product))
                    throw new NotFoundException($"Product '{id}' not found.");
                if (!PurchasePolicy.CanPurchase(business.Kind, product.ItemType))
                    rejected.Add($"'{product.Name}' ({product.ItemType})");
            }

            if (rejected.Count > 0)
            {
                var allowed = string.Join(", ", PurchasePolicy.AllowedItemTypes(business.Kind));
                throw new PurchaseNotAllowedException(
                    $"A {business.Kind} business can only purchase {allowed}. Not allowed: {string.Join(", ", rejected)}.");
            }
        }

        private static SupplierDto ToDto(Supplier s) =>
            new(s.SupplierId, s.BusinessId, s.SupplierName, s.KraPin, s.ContactPerson, s.PhoneNumber, s.Email, s.PhysicalAddress, s.PaymentTermDays, s.IsActive);

        private static PurchaseOrderDto ToDto(PurchaseOrder o) =>
            new(o.PurchaseOrderId, o.OrderNumber, o.SupplierId, o.Status, o.OrderDate, o.ExpectedDeliveryDate, o.Notes, o.IsActive,
                o.Lines.Select(l => new PurchaseOrderLineDto(l.PurchaseOrderLineId, l.ProductId, l.UnitOfMeasureId, l.QuantityOrdered, l.QuantityReceived, l.UnitCost,
                    l.Product?.Name, l.Product?.Sku, l.UnitOfMeasure?.Symbol, l.Product?.IsBatchTracked ?? false)),
                o.Supplier?.SupplierName);
    }
}
