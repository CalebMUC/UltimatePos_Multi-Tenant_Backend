using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UltimatePos.Application.Business;
using UltimatePos.Application.Catalog;
using UltimatePos.Application.Common.Dtos;
using UltimatePos.Application.Common.Interfaces;
using UltimatePos.Application.Inventory;
using UltimatePos.Application.Payments;
using UltimatePos.Application.Sales.Dtos;
using UltimatePos.Domain.Entities;
using UltimatePos.Domain.Exceptions;
using static UltimatePos.Domain.Enums.PosEnums;

namespace UltimatePos.Application.Sales
{
    public class SalesService
    {
        private const int QuantityDecimals = 4;

        private readonly ISalesRepository _repository;
        private readonly ICatalogRepository _catalogRepository;
        private readonly IBusinessRepository _businessRepository;
        private readonly IInventoryRepository _inventoryRepository;
        private readonly IMpesaRepository _mpesaRepository;
        private readonly IMpesaClient _mpesaClient;
        private readonly ICurrentUser _currentUser;

        public SalesService(
            ISalesRepository repository, ICatalogRepository catalogRepository, IBusinessRepository businessRepository,
            IInventoryRepository inventoryRepository, IMpesaRepository mpesaRepository, IMpesaClient mpesaClient,
            ICurrentUser currentUser)
        {
            _repository = repository;
            _catalogRepository = catalogRepository;
            _businessRepository = businessRepository;
            _inventoryRepository = inventoryRepository;
            _mpesaRepository = mpesaRepository;
            _mpesaClient = mpesaClient;
            _currentUser = currentUser;
        }

        public async Task<SalePreviewDto> PreviewSaleAsync(CreateSaleRequestDto request)
        {
            var plan = await BuildSalePlanAsync(request);
            return new SalePreviewDto(
                request.CustomerId, plan.PriceType, plan.Lines.Sum(l => l.LineTotal),
                plan.Lines.All(l => l.StockSufficient),
                plan.Lines.Select(l => new SalePreviewLineDto(
                    l.ProductId, l.ProductName, l.Sku, l.UnitOfMeasureId, l.Quantity, l.UnitPrice, l.LineTotal,
                    l.AvailableInBaseUnits, l.RequiredInBaseUnits, l.StockSufficient)));
        }

        public async Task<SaleDto> RecordSaleAsync(CreateSaleRequestDto request)
        {
            var plan = await BuildSalePlanAsync(request);
            var userId = _currentUser.UserId;
            var total = plan.Lines.Sum(l => l.LineTotal);

            var sale = new Sale
            {
                SaleId = Guid.NewGuid(),
                CustomerId = request.CustomerId,
                PaymentMethod = request.PaymentMethod,
                TotalAmount = total,
                Notes = request.Notes,
                CreatedBy = userId,
                Lines = plan.Lines.Select(l => new SaleLine
                {
                    ProductId = l.ProductId,
                    UnitOfMeasureId = l.UnitOfMeasureId,
                    Quantity = l.Quantity,
                    QuantityInBaseUnits = l.RequiredInBaseUnits,
                    UnitPrice = l.UnitPrice,
                    LineTotal = l.LineTotal,
                    CreatedBy = userId
                }).ToList()
            };

            return request.PaymentMethod switch
            {
                PaymentMethod.Cash => await RecordCashSaleAsync(sale, request, userId),
                PaymentMethod.Credit => await RecordCreditSaleAsync(sale, userId),
                PaymentMethod.MpesaTill => await RecordTillSaleAsync(sale, request, userId),
                PaymentMethod.MpesaStkPush => await InitiateStkPushSaleAsync(sale, request, userId),
                _ => throw new InvalidAssignmentException("Unsupported payment method.")
            };
        }

        public async Task<PagedResult<SaleDto>> GetSalesAsync(int page, int pageSize, Guid? customerId, SaleStatus? status)
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);

            var (items, totalCount) = await _repository.GetSalesAsync(page, pageSize, customerId, status);
            return new PagedResult<SaleDto>(items.Select(ToDto), page, pageSize, totalCount);
        }

        public async Task<SaleDto> GetSaleByIdAsync(Guid saleId)
        {
            var sale = await _repository.GetSaleByIdAsync(saleId)
                ?? throw new NotFoundException($"Sale '{saleId}' not found.");
            return ToDto(sale);
        }

        public async Task<SaleDto> VoidSaleAsync(Guid saleId)
        {
            var sale = await _repository.GetSaleByIdAsync(saleId)
                ?? throw new NotFoundException($"Sale '{saleId}' not found.");

            if (sale.Status != SaleStatus.Completed)
                throw new InvalidAssignmentException($"Only a Completed sale can be voided — this one is {sale.Status}.");

            var userId = _currentUser.UserId;

            var reversalMovements = sale.Lines.Select(l => new StockMovement
            {
                ProductId = l.ProductId,
                MovementType = StockMovementType.SaleVoid,
                QuantityChange = l.QuantityInBaseUnits,
                ReferenceType = "Sale",
                ReferenceId = saleId,
                BatchId = saleId,
                Notes = "Sale voided",
                CreatedBy = userId
            }).ToList();

            CustomerLedgerEntry? reversal = null;
            if (sale.PaymentMethod == PaymentMethod.Credit && sale.CustomerId.HasValue)
            {
                reversal = new CustomerLedgerEntry
                {
                    CustomerId = sale.CustomerId.Value,
                    EntryType = CustomerLedgerEntryType.Reversal,
                    Amount = sale.TotalAmount,
                    ReferenceType = "Sale",
                    ReferenceId = saleId,
                    Notes = $"Reversal of voided {sale.SaleNumber}",
                    CreatedBy = userId
                };
            }

            await _repository.VoidSaleAsync(saleId, reversalMovements, reversal, userId);
            return await GetSaleByIdAsync(saleId);
        }

        // ---- Called from MpesaService once a pending STK sale is confirmed or fails ----

        public async Task<SaleDto> CompletePendingMpesaSaleAsync(Guid saleId, string? mpesaReceiptNumber)
        {
            var sale = await _repository.GetSaleByIdAsync(saleId)
                ?? throw new NotFoundException($"Sale '{saleId}' not found.");

            if (sale.Status != SaleStatus.Pending)
                return ToDto(sale); // Idempotent — a duplicate callback for an already-settled sale is a no-op, not an error.

            var movements = BuildSaleMovements(sale, null);
            await _repository.CompletePendingSaleAsync(saleId, movements, null, mpesaReceiptNumber);
            return await GetSaleByIdAsync(saleId);
        }

        public async Task<SaleDto> FailPendingMpesaSaleAsync(Guid saleId, string reason)
        {
            var sale = await _repository.GetSaleByIdAsync(saleId)
                ?? throw new NotFoundException($"Sale '{saleId}' not found.");

            if (sale.Status != SaleStatus.Pending)
                return ToDto(sale);

            await _repository.FailPendingSaleAsync(saleId, reason);
            return await GetSaleByIdAsync(saleId);
        }

        // ---- Payment-method branches ----

        private async Task<SaleDto> RecordCashSaleAsync(Sale sale, CreateSaleRequestDto request, Guid? userId)
        {
            if (request.AmountTendered is null || request.AmountTendered < sale.TotalAmount)
                throw new InvalidAssignmentException($"Amount tendered must be at least the total ({sale.TotalAmount}).");

            sale.Status = SaleStatus.Completed;
            sale.AmountTendered = request.AmountTendered;
            sale.ChangeDue = Math.Round(request.AmountTendered.Value - sale.TotalAmount, 2, MidpointRounding.AwayFromZero);
            sale.SaleNumber = $"INV-{await _repository.GetNextDocumentNumberAsync("INV"):D6}";

            await _repository.RecordCompletedSaleAsync(sale, BuildSaleMovements(sale, userId), null);
            return await GetSaleByIdAsync(sale.SaleId);
        }

        private async Task<SaleDto> RecordCreditSaleAsync(Sale sale, Guid? userId)
        {
            sale.Status = SaleStatus.Completed;
            sale.SaleNumber = $"INV-{await _repository.GetNextDocumentNumberAsync("INV"):D6}";

            var ledgerEntry = new CustomerLedgerEntry
            {
                CustomerId = sale.CustomerId!.Value,
                EntryType = CustomerLedgerEntryType.Invoice,
                Amount = sale.TotalAmount,
                ReferenceType = "Sale",
                ReferenceId = sale.SaleId,
                Notes = $"Invoice {sale.SaleNumber}",
                CreatedBy = userId
            };

            await _repository.RecordCompletedSaleAsync(sale, BuildSaleMovements(sale, userId), ledgerEntry);
            return await GetSaleByIdAsync(sale.SaleId);
        }

        private async Task<SaleDto> RecordTillSaleAsync(Sale sale, CreateSaleRequestDto request, Guid? userId)
        {
            if (request.MpesaTransactionId is null)
                throw new InvalidAssignmentException("A till payment must reference the matching M-Pesa transaction.");

            var transaction = await _mpesaRepository.GetByIdAsync(request.MpesaTransactionId.Value)
                ?? throw new NotFoundException($"M-Pesa transaction '{request.MpesaTransactionId}' not found.");

            if (transaction.Status != MpesaTransactionStatus.Received)
                throw new InvalidAssignmentException("This M-Pesa transaction is already matched to another sale, or is not an unmatched till payment.");
            if (transaction.Amount != sale.TotalAmount)
                throw new InvalidAssignmentException($"The M-Pesa transaction amount ({transaction.Amount}) does not match the sale total ({sale.TotalAmount}).");

            sale.Status = SaleStatus.Completed;
            sale.SaleNumber = $"INV-{await _repository.GetNextDocumentNumberAsync("INV"):D6}";

            await _repository.RecordCompletedSaleAsync(sale, BuildSaleMovements(sale, userId), null);
            await _mpesaRepository.MatchToSaleAsync(transaction.MpesaTransactionId, sale.SaleId);

            return await GetSaleByIdAsync(sale.SaleId);
        }

        private async Task<SaleDto> InitiateStkPushSaleAsync(Sale sale, CreateSaleRequestDto request, Guid? userId)
        {
            if (string.IsNullOrWhiteSpace(request.MpesaPhoneNumber))
                throw new InvalidAssignmentException("A phone number is required to send an STK push.");

            var phone = MpesaPhoneNumber.Normalize(request.MpesaPhoneNumber);

            sale.Status = SaleStatus.Pending;
            sale.SaleNumber = $"INV-{await _repository.GetNextDocumentNumberAsync("INV"):D6}";

            // Persisted now — stock/ledger deliberately untouched until the push is confirmed.
            await _repository.RecordPendingSaleAsync(sale);

            var pushResult = await _mpesaClient.InitiateStkPushAsync(phone, sale.TotalAmount, sale.SaleNumber, "Sale payment");

            if (!pushResult.Success)
            {
                await _repository.FailPendingSaleAsync(sale.SaleId, pushResult.ErrorMessage ?? "STK push could not be sent.");
                throw new InvalidAssignmentException($"Could not send the payment prompt: {pushResult.ErrorMessage}");
            }

            await _mpesaRepository.CreateAsync(new MpesaTransaction
            {
                MpesaTransactionId = Guid.NewGuid(),
                TransactionType = MpesaTransactionType.StkPush,
                Status = MpesaTransactionStatus.Pending,
                PhoneNumber = phone,
                Amount = sale.TotalAmount,
                MerchantRequestId = pushResult.MerchantRequestId,
                CheckoutRequestId = pushResult.CheckoutRequestId,
                SaleId = sale.SaleId,
                CreatedBy = userId
            });

            return await GetSaleByIdAsync(sale.SaleId);
        }

        private static List<StockMovement> BuildSaleMovements(Sale sale, Guid? userId) =>
            sale.Lines.Select(l => new StockMovement
            {
                ProductId = l.ProductId,
                MovementType = StockMovementType.Sale,
                QuantityChange = -l.QuantityInBaseUnits,
                ReferenceType = "Sale",
                ReferenceId = sale.SaleId,
                BatchId = sale.SaleId,
                CreatedBy = userId
            }).ToList();

        // ---- Planning (shared by preview and record) ----

        private sealed record SaleLinePlan(Guid ProductId, string ProductName, string Sku, Guid UnitOfMeasureId,
            decimal Quantity, decimal UnitPrice, decimal LineTotal, decimal RequiredInBaseUnits, decimal AvailableInBaseUnits)
        {
            public bool StockSufficient => AvailableInBaseUnits >= RequiredInBaseUnits;
        }

        private sealed record SalePlan(PriceType PriceType, IReadOnlyList<SaleLinePlan> Lines);

        private async Task<SalePlan> BuildSalePlanAsync(CreateSaleRequestDto request)
        {
            var lines = request.Lines.ToList();
            if (lines.Count == 0)
                throw new InvalidAssignmentException("A sale needs at least one line.");
            if (lines.Any(l => l.Quantity <= 0))
                throw new InvalidAssignmentException("Quantity must be greater than zero on every line.");
            if (request.PaymentMethod == PaymentMethod.Credit && !request.CustomerId.HasValue)
                throw new InvalidAssignmentException("A credit sale must be linked to a registered customer.");

            var priceType = PriceType.Retail;
            if (request.CustomerId.HasValue)
            {
                var customer = await _businessRepository.GetCustomerByIdAsync(request.CustomerId.Value)
                    ?? throw new NotFoundException($"Customer '{request.CustomerId}' not found.");
                if (!customer.IsActive)
                    throw new InvalidAssignmentException("This customer is deactivated.");
                priceType = customer.PriceType;
            }

            var productIds = lines.Select(l => l.ProductId).Distinct().ToList();
            var products = (await _catalogRepository.GetProductsByIdsAsync(productIds)).ToDictionary(p => p.ProductId);
            var onHand = await _inventoryRepository.GetQuantitiesOnHandAsync(productIds);

            var planLines = new List<SaleLinePlan>();
            foreach (var line in lines)
            {
                if (!products.TryGetValue(line.ProductId, out var product))
                    throw new NotFoundException($"Product '{line.ProductId}' not found.");
                if (!product.IsActive)
                    throw new InvalidAssignmentException($"Product '{product.Name}' is inactive.");
                if (product.ItemType != ItemType.FinishedGood)
                    throw new InvalidAssignmentException($"Product '{product.Name}' is a {product.ItemType} — only finished goods can be sold directly.");

                var price = await _catalogRepository.GetActivePriceAsync(line.ProductId, line.UnitOfMeasureId, priceType)
                    ?? throw new InvalidAssignmentException($"No {priceType} price is set for '{product.Name}' in the unit given.");

                var factor = await _catalogRepository.GetConversionFactorAsync(line.ProductId, line.UnitOfMeasureId)
                    ?? throw new InvalidAssignmentException($"Product '{product.Name}' has no conversion for the unit given.");

                var requiredBase = Round(line.Quantity * factor);
                if (requiredBase <= 0)
                    throw new InvalidAssignmentException($"Quantity is too small — '{product.Name}' rounds to zero.");

                onHand.TryGetValue(line.ProductId, out var available);

                planLines.Add(new SaleLinePlan(
                    line.ProductId, product.Name, product.Sku, line.UnitOfMeasureId,
                    line.Quantity, price, Round(line.Quantity * price), requiredBase, available));
            }

            return new SalePlan(priceType, planLines);
        }

        private static decimal Round(decimal value) => Math.Round(value, QuantityDecimals, MidpointRounding.AwayFromZero);

        private static SaleDto ToDto(Sale s) =>
            new(s.SaleId, s.SaleNumber, s.CustomerId, s.PaymentMethod, s.Status, s.TotalAmount, s.AmountTendered, s.ChangeDue, s.Notes, s.CreatedAt,
                s.Lines.Select(l => new SaleLineDto(l.SaleLineId, l.ProductId, l.Product.Name, l.UnitOfMeasureId, l.Quantity, l.UnitPrice, l.LineTotal)));
    }
}
