using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UltimatePos.Application.Accounting;
using UltimatePos.Application.Business;
using UltimatePos.Application.Catalog;
using UltimatePos.Application.Common;
using UltimatePos.Application.Common.Dtos;
using UltimatePos.Application.Common.Interfaces;
using UltimatePos.Application.Inventory;
using UltimatePos.Application.Payments;
using UltimatePos.Application.Reports;
using UltimatePos.Application.Sales.Dtos;
using UltimatePos.Domain.Entities;
using UltimatePos.Domain.Exceptions;
using static UltimatePos.Domain.Enums.PosEnums;

namespace UltimatePos.Application.Sales
{
    public class SalesService
    {
        public const string ApplyDiscountPermission = "Sales.ApplyDiscount";
        public const string OverrideCreditLimitPermission = "Sales.OverrideCreditLimit";
        public const string DispensePermission = "Prescriptions.Dispense";

        private const int QuantityDecimals = 4;
        private const int MoneyDecimals = 2;

        private readonly ISalesRepository _repository;
        private readonly ICatalogRepository _catalogRepository;
        private readonly IBusinessRepository _businessRepository;
        private readonly IInventoryRepository _inventoryRepository;
        private readonly IAccountingRepository _accountingRepository;
        private readonly IMpesaRepository _mpesaRepository;
        private readonly IMpesaClient _mpesaClient;
        private readonly ICurrentUser _currentUser;
        private readonly ITenantContext _tenant;

        public SalesService(
            ISalesRepository repository, ICatalogRepository catalogRepository, IBusinessRepository businessRepository,
            IInventoryRepository inventoryRepository, IAccountingRepository accountingRepository, IMpesaRepository mpesaRepository,
            IMpesaClient mpesaClient, ICurrentUser currentUser, ITenantContext tenant)
        {
            _repository = repository;
            _catalogRepository = catalogRepository;
            _businessRepository = businessRepository;
            _inventoryRepository = inventoryRepository;
            _accountingRepository = accountingRepository;
            _mpesaRepository = mpesaRepository;
            _mpesaClient = mpesaClient;
            _currentUser = currentUser;
            _tenant = tenant;
        }

        /// <summary>The business's calendar day, for expiry checks (East Africa Time).</summary>
        private static DateOnly Today => ReportPeriod.TodayAt(ReportPeriod.DefaultUtcOffsetMinutes);

        public async Task<SalePreviewDto> PreviewSaleAsync(CreateSaleRequestDto request)
        {
            var plan = await BuildSalePlanAsync(request);
            return new SalePreviewDto(
                request.CustomerId, plan.PriceType, plan.Total,
                plan.Lines.All(l => l.StockSufficient),
                plan.Lines.Select(l => new SalePreviewLineDto(
                    l.ProductId, l.ProductName, l.Sku, l.UnitOfMeasureId, l.Quantity, l.UnitPrice, l.LineTotal,
                    l.AvailableInBaseUnits, l.RequiredInBaseUnits, l.StockSufficient,
                    l.DiscountAmount, l.TaxRate, l.TaxAmount, l.RequiresPrescription, l.IsBatchTracked)),
                plan.Subtotal, plan.DiscountTotal, plan.TaxTotal, plan.RequiresPrescription,
                plan.CustomerBalance, plan.CreditLimit, plan.CreditLimitExceeded(request.PaymentMethod));
        }

        public async Task<SaleDto> RecordSaleAsync(CreateSaleRequestDto request)
        {
            var plan = await BuildSalePlanAsync(request);
            var userId = _currentUser.UserId;

            if (plan.DiscountTotal > 0 && !_currentUser.HasPermission(ApplyDiscountPermission))
                throw new ForbiddenException("You do not have permission to give discounts.");

            // Batch-tracked stock (medicines) is never sold beyond what is in unexpired lots. Other stock may go negative, as
            // before: refusing a sale a cashier is physically ringing up is worse than a count to correct later.
            var shortBatched = plan.Lines.Where(l => l.IsBatchTracked && !l.StockSufficient).ToList();
            if (shortBatched.Count > 0)
                throw new InsufficientStockException(
                    "Not enough unexpired stock for: " +
                    string.Join(", ", shortBatched.Select(l => $"'{l.ProductName}' ({l.AvailableInBaseUnits:0.####} available)")) + ".");

            if (plan.CreditLimitExceeded(request.PaymentMethod))
            {
                if (!request.OverrideCreditLimit)
                    throw new CreditLimitExceededException(
                        $"This sale would take the customer to {plan.CustomerBalance + plan.Total:0.00}, past their credit limit of {plan.CreditLimit:0.00}.");
                if (!_currentUser.HasPermission(OverrideCreditLimitPermission))
                    throw new ForbiddenException("You do not have permission to override a customer's credit limit.");
            }

            var prescription = BuildPrescription(plan, request.Prescription, userId);

            var sale = new Sale
            {
                SaleId = Guid.NewGuid(),
                CustomerId = request.CustomerId,
                PaymentMethod = request.PaymentMethod,
                Subtotal = plan.Subtotal,
                DiscountTotal = plan.DiscountTotal,
                TaxTotal = plan.TaxTotal,
                TotalAmount = plan.Total,
                Notes = request.Notes,
                CreatedBy = userId,
                Prescription = prescription,
                Lines = plan.Lines.Select(l => new SaleLine
                {
                    SaleLineId = Guid.NewGuid(),
                    ProductId = l.ProductId,
                    UnitOfMeasureId = l.UnitOfMeasureId,
                    Quantity = l.Quantity,
                    QuantityInBaseUnits = l.RequiredInBaseUnits,
                    UnitPrice = l.UnitPrice,
                    DiscountAmount = l.DiscountAmount,
                    TaxRate = l.TaxRate,
                    TaxAmount = l.TaxAmount,
                    LineTotal = l.LineTotal,
                    CreatedBy = userId
                }).ToList()
            };
            if (prescription is not null)
                prescription.SaleId = sale.SaleId;

            return request.PaymentMethod switch
            {
                PaymentMethod.Cash => await RecordCashSaleAsync(sale, request),
                PaymentMethod.Credit => await RecordCreditSaleAsync(sale, userId),
                PaymentMethod.MpesaTill => await RecordTillSaleAsync(sale, request),
                PaymentMethod.MpesaStkPush => await InitiateStkPushSaleAsync(sale, request, userId),
                _ => throw new InvalidAssignmentException("Unsupported payment method.")
            };
        }

        public async Task<PagedResult<SaleDto>> GetSalesAsync(int page, int pageSize, SaleQuery query)
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);
            if (query.Search is { Length: > 100 })
                throw new ValidationException("search", "Search text cannot exceed 100 characters.");

            var (fromUtc, toUtc) = ResolveRange(query.From, query.To, query.UtcOffsetMinutes);
            var (items, totalCount) = await _repository.GetSalesAsync(page, pageSize, query, fromUtc, toUtc);
            var list = items.ToList();
            var names = await _repository.GetUserNamesAsync(list.Where(s => s.CreatedBy.HasValue).Select(s => s.CreatedBy!.Value));
            return new PagedResult<SaleDto>(list.Select(s => ToDto(s, names)), page, pageSize, totalCount);
        }

        public async Task<SaleDto> GetSaleByIdAsync(Guid saleId)
        {
            var sale = await _repository.GetSaleByIdAsync(saleId)
                ?? throw new NotFoundException($"Sale '{saleId}' not found.");
            var names = await _repository.GetUserNamesAsync(sale.CreatedBy.HasValue ? new[] { sale.CreatedBy.Value } : Array.Empty<Guid>());
            return ToDto(sale, names);
        }

        public async Task<SaleDto> VoidSaleAsync(Guid saleId)
        {
            var sale = await _repository.GetSaleByIdAsync(saleId)
                ?? throw new NotFoundException($"Sale '{saleId}' not found.");

            if (sale.Status != SaleStatus.Completed)
                throw new InvalidAssignmentException($"Only a Completed sale can be voided — this one is {sale.Status}.");
            if (sale.ReturnedAmount > 0)
                throw new InvalidAssignmentException("Goods have already been returned against this sale. Return the rest instead of voiding it.");

            var userId = _currentUser.UserId;
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

            await _repository.VoidSaleAsync(saleId, reversal, userId);
            return await GetSaleByIdAsync(saleId);
        }

        // ---- Called from MpesaService once a pending STK sale is confirmed or fails ----

        public async Task<SaleDto> CompletePendingMpesaSaleAsync(Guid saleId, string? mpesaReceiptNumber)
        {
            var sale = await _repository.GetSaleByIdAsync(saleId)
                ?? throw new NotFoundException($"Sale '{saleId}' not found.");

            if (sale.Status != SaleStatus.Pending)
                return await GetSaleByIdAsync(saleId); // Idempotent — a duplicate callback for an already-settled sale is a no-op, not an error.

            await _repository.CompletePendingSaleAsync(saleId, null, mpesaReceiptNumber, Today);
            return await GetSaleByIdAsync(saleId);
        }

        public async Task<SaleDto> FailPendingMpesaSaleAsync(Guid saleId, string reason)
        {
            var sale = await _repository.GetSaleByIdAsync(saleId)
                ?? throw new NotFoundException($"Sale '{saleId}' not found.");

            if (sale.Status != SaleStatus.Pending)
                return await GetSaleByIdAsync(saleId);

            await _repository.FailPendingSaleAsync(saleId, reason);
            return await GetSaleByIdAsync(saleId);
        }

        // ---- Returns ----

        public async Task<SaleReturnDto> CreateReturnAsync(Guid saleId, CreateSaleReturnRequestDto request)
        {
            var sale = await _repository.GetSaleByIdAsync(saleId)
                ?? throw new NotFoundException($"Sale '{saleId}' not found.");
            if (sale.Status != SaleStatus.Completed)
                throw new InvalidAssignmentException($"Goods can only be returned against a Completed sale — this one is {sale.Status}.");

            var errors = new Dictionary<string, string[]>();
            if (string.IsNullOrWhiteSpace(request.Reason))
                errors["reason"] = new[] { "A reason is required for every return." };
            if (!Enum.IsDefined(request.RefundMethod))
                errors["refundMethod"] = new[] { "Refund method must be Cash, Mpesa or CreditNote." };
            else if (sale.PaymentMethod == PaymentMethod.Credit && request.RefundMethod != RefundMethod.CreditNote)
                errors["refundMethod"] = new[] { "A credit sale is refunded with a credit note to the customer's account." };
            else if (request.RefundMethod == RefundMethod.CreditNote && !sale.CustomerId.HasValue)
                errors["refundMethod"] = new[] { "A credit note needs a registered customer — refund a walk-in sale in cash or M-Pesa." };
            else if (request.RefundMethod == RefundMethod.Mpesa && string.IsNullOrWhiteSpace(request.RefundReference))
                errors["refundReference"] = new[] { "Give the M-Pesa transaction code of the refund." };

            var requested = (request.Lines ?? Enumerable.Empty<CreateSaleReturnLineRequestDto>()).ToList();
            if (requested.Count == 0)
                errors["lines"] = new[] { "At least one line must be returned." };
            if (requested.GroupBy(r => r.SaleLineId).Any(g => g.Count() > 1))
                errors["lines"] = new[] { "Each sale line can appear only once in a return." };
            if (errors.Count > 0)
                throw new ValidationException(errors);

            var alreadyReturned = await _repository.GetReturnedAmountsByLineAsync(saleId);
            var userId = _currentUser.UserId;
            var returnLines = new List<SaleReturnLine>();

            foreach (var r in requested)
            {
                var line = sale.Lines.FirstOrDefault(l => l.SaleLineId == r.SaleLineId)
                    ?? throw new NotFoundException($"Line '{r.SaleLineId}' is not on sale {sale.SaleNumber}.");
                var remaining = line.Quantity - line.QuantityReturned;
                if (r.Quantity <= 0)
                    throw new ValidationException("lines", "Return quantity must be greater than zero.");
                if (r.Quantity > remaining)
                    throw new ValidationException("lines", $"Only {remaining:0.####} of '{line.Product.Name}' is left to return on this sale.");

                // The final return of a line refunds exactly what is left of it, so rounding never over- or under-refunds the line.
                alreadyReturned.TryGetValue(line.SaleLineId, out var done);
                var isLast = r.Quantity == remaining;
                var amount = isLast ? line.LineTotal - done.Amount : RoundMoney(line.LineTotal * r.Quantity / line.Quantity);
                var tax = isLast ? line.TaxAmount - done.TaxAmount : RoundMoney(line.TaxAmount * r.Quantity / line.Quantity);
                var quantityInBase = isLast
                    ? line.QuantityInBaseUnits - RoundQuantity(line.QuantityInBaseUnits * line.QuantityReturned / line.Quantity)
                    : RoundQuantity(line.QuantityInBaseUnits * r.Quantity / line.Quantity);

                returnLines.Add(new SaleReturnLine
                {
                    SaleReturnLineId = Guid.NewGuid(),
                    SaleLineId = line.SaleLineId,
                    ProductId = line.ProductId,
                    Quantity = r.Quantity,
                    QuantityInBaseUnits = quantityInBase,
                    Amount = amount,
                    TaxAmount = tax,
                    UnitCost = line.UnitCost,
                    Restock = r.Restock
                });
            }

            var saleReturn = new SaleReturn
            {
                SaleReturnId = Guid.NewGuid(),
                ReturnNumber = $"RET-{await _repository.GetNextDocumentNumberAsync("RET"):D6}",
                SaleId = saleId,
                Reason = request.Reason.Trim(),
                RefundMethod = request.RefundMethod,
                RefundReference = string.IsNullOrWhiteSpace(request.RefundReference) ? null : request.RefundReference.Trim(),
                TotalAmount = returnLines.Sum(l => l.Amount),
                TaxAmount = returnLines.Sum(l => l.TaxAmount),
                CreatedBy = userId,
                Lines = returnLines
            };

            CustomerLedgerEntry? creditNote = null;
            if (request.RefundMethod == RefundMethod.CreditNote)
            {
                creditNote = new CustomerLedgerEntry
                {
                    CustomerId = sale.CustomerId!.Value,
                    EntryType = CustomerLedgerEntryType.Return,
                    Amount = saleReturn.TotalAmount,
                    ReferenceType = "SaleReturn",
                    ReferenceId = saleReturn.SaleReturnId,
                    Notes = $"Credit note {saleReturn.ReturnNumber} for {sale.SaleNumber}",
                    CreatedBy = userId
                };
            }

            await _repository.RecordReturnAsync(saleReturn, creditNote);
            return (await GetReturnsForSaleAsync(saleId)).First(r => r.SaleReturnId == saleReturn.SaleReturnId);
        }

        public async Task<IEnumerable<SaleReturnDto>> GetReturnsForSaleAsync(Guid saleId)
        {
            if (await _repository.GetSaleByIdAsync(saleId) is null)
                throw new NotFoundException($"Sale '{saleId}' not found.");
            return (await _repository.GetReturnsForSaleAsync(saleId)).Select(ToDto);
        }

        public async Task<PagedResult<SaleReturnDto>> GetReturnsAsync(int page, int pageSize, DateOnly? from, DateOnly? to, int utcOffsetMinutes)
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);
            var (fromUtc, toUtc) = ResolveRange(from, to, utcOffsetMinutes);
            var (items, total) = await _repository.GetReturnsAsync(page, pageSize, fromUtc, toUtc);
            return new PagedResult<SaleReturnDto>(items.Select(ToDto), page, pageSize, total);
        }

        // ---- Prescription register ----

        /// <summary>Every sale dispensed against a prescription in the period, newest first — the pharmacy's prescription book.</summary>
        public async Task<IEnumerable<PrescriptionRegisterEntryDto>> GetPrescriptionRegisterAsync(DateOnly from, DateOnly to, int utcOffsetMinutes, string? search)
        {
            var period = ReportPeriod.Create(from, to, utcOffsetMinutes);
            var sales = (await _repository.GetPrescriptionSalesAsync(period.FromUtc, period.ToUtcExclusive, search)).ToList();
            var names = await _repository.GetUserNamesAsync(sales.Select(s => s.Prescription!.DispensedBy ?? s.CreatedBy).OfType<Guid>());

            return sales.Select(s => new PrescriptionRegisterEntryDto(s.SaleId, s.SaleNumber, s.CreatedAt, s.Status,
                (s.Prescription!.DispensedBy ?? s.CreatedBy) is { } by && names.TryGetValue(by, out var name) ? name : null,
                ToDto(s.Prescription!),
                s.Lines.Where(l => l.Product.RequiresPrescription).Select(l => new PrescriptionRegisterLineDto(l.ProductId, l.Product.Name,
                    l.Product.GenericName, l.Product.Strength, l.Quantity, l.UnitOfMeasure?.Symbol,
                    l.BatchAllocations.Select(a => a.StockBatch.BatchNumber).Distinct().ToList()))));
        }

        // ---- Payment-method branches ----

        private async Task<SaleDto> RecordCashSaleAsync(Sale sale, CreateSaleRequestDto request)
        {
            if (request.AmountTendered is null || request.AmountTendered < sale.TotalAmount)
                throw new ValidationException("amountTendered", $"Amount tendered must be at least the total ({sale.TotalAmount:0.00}).");

            sale.Status = SaleStatus.Completed;
            sale.AmountTendered = request.AmountTendered;
            sale.ChangeDue = Math.Round(request.AmountTendered.Value - sale.TotalAmount, 2, MidpointRounding.AwayFromZero);
            sale.SaleNumber = $"INV-{await _repository.GetNextDocumentNumberAsync("INV"):D6}";

            await _repository.RecordCompletedSaleAsync(sale, null, Today);
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

            await _repository.RecordCompletedSaleAsync(sale, ledgerEntry, Today);
            return await GetSaleByIdAsync(sale.SaleId);
        }

        private async Task<SaleDto> RecordTillSaleAsync(Sale sale, CreateSaleRequestDto request)
        {
            if (request.MpesaTransactionId is null)
                throw new ValidationException("mpesaTransactionId", "A till payment must reference the matching M-Pesa transaction.");

            var transaction = await _mpesaRepository.GetByIdAsync(request.MpesaTransactionId.Value)
                ?? throw new NotFoundException($"M-Pesa transaction '{request.MpesaTransactionId}' not found.");

            if (transaction.Status != MpesaTransactionStatus.Received)
                throw new InvalidAssignmentException("This M-Pesa transaction is already matched to another sale, or is not an unmatched till payment.");

            if (transaction.Amount != sale.TotalAmount)
                throw new InvalidAssignmentException($"The M-Pesa transaction amount ({transaction.Amount}) does not match the sale total ({sale.TotalAmount}).");

            sale.Status = SaleStatus.Completed;
            sale.SaleNumber = $"INV-{await _repository.GetNextDocumentNumberAsync("INV"):D6}";

            await _repository.RecordCompletedSaleAsync(sale, null, Today);
            await _mpesaRepository.MatchToSaleAsync(transaction.MpesaTransactionId, sale.SaleId);

            return await GetSaleByIdAsync(sale.SaleId);
        }

        private async Task<SaleDto> InitiateStkPushSaleAsync(Sale sale, CreateSaleRequestDto request, Guid? userId)
        {
            if (string.IsNullOrWhiteSpace(request.MpesaPhoneNumber))
                throw new ValidationException("mpesaPhoneNumber", "A phone number is required to send an STK push.");

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
                BusinessId = _tenant.RequireBusinessId(),
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

        // ---- Planning (shared by preview and record) ----

        private sealed record SaleLinePlan(Guid ProductId, string ProductName, string Sku, Guid UnitOfMeasureId,
            decimal Quantity, decimal UnitPrice, decimal Gross, decimal DiscountAmount, decimal LineTotal, decimal TaxRate, decimal TaxAmount,
            decimal RequiredInBaseUnits, decimal AvailableInBaseUnits, bool IsBatchTracked, bool RequiresPrescription)
        {
            public bool StockSufficient => AvailableInBaseUnits >= RequiredInBaseUnits;
        }

        private sealed record SalePlan(PriceType PriceType, IReadOnlyList<SaleLinePlan> Lines, decimal? CustomerBalance, decimal? CreditLimit)
        {
            public decimal Subtotal => Lines.Sum(l => l.Gross);
            public decimal DiscountTotal => Lines.Sum(l => l.DiscountAmount);
            public decimal TaxTotal => Lines.Sum(l => l.TaxAmount);
            public decimal Total => Lines.Sum(l => l.LineTotal);
            public bool RequiresPrescription => Lines.Any(l => l.RequiresPrescription);

            public bool CreditLimitExceeded(PaymentMethod method) =>
                method == PaymentMethod.Credit && CreditLimit is { } limit && (CustomerBalance ?? 0m) + Total > limit;
        }

        private async Task<SalePlan> BuildSalePlanAsync(CreateSaleRequestDto request)
        {
            var lines = (request.Lines ?? Enumerable.Empty<CreateSaleLineRequestDto>()).ToList();
            if (lines.Count == 0)
                throw new ValidationException("lines", "A sale needs at least one line.");
            if (lines.Any(l => l.Quantity <= 0))
                throw new ValidationException("lines", "Quantity must be greater than zero on every line.");
            if (!Enum.IsDefined(request.PaymentMethod))
                throw new ValidationException("paymentMethod", "Unsupported payment method.");
            if (request.PaymentMethod == PaymentMethod.Credit && !request.CustomerId.HasValue)
                throw new ValidationException("customerId", "A credit sale must be linked to a registered customer.");

            var business = await _businessRepository.GetBusinessByIdAsync(_tenant.RequireBusinessId())
                ?? throw new NotFoundException("Business not found.");

            var priceType = PriceType.Retail;
            decimal? customerBalance = null, creditLimit = null;
            if (request.CustomerId.HasValue)
            {
                var customer = await _businessRepository.GetCustomerByIdAsync(request.CustomerId.Value)
                    ?? throw new NotFoundException($"Customer '{request.CustomerId}' not found.");
                if (!customer.IsActive)
                    throw new InvalidAssignmentException("This customer is deactivated.");
                priceType = customer.PriceType;
                creditLimit = customer.CreditLimit;
                customerBalance = (await _accountingRepository.GetCustomerBalanceAsync(customer.CustomerId))?.OutstandingBalance ?? 0m;
            }

            var productIds = lines.Select(l => l.ProductId).Distinct().ToList();
            var products = (await _catalogRepository.GetProductsByIdsAsync(productIds)).ToDictionary(p => p.ProductId);
            var sellable = await _inventoryRepository.GetSellableQuantitiesAsync(productIds, Today);

            // First pass: price and line discount.
            var priced = new List<(CreateSaleLineRequestDto Line, Product Product, decimal Price, decimal Gross, decimal LineDiscount, decimal RequiredBase)>();
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

                var requiredBase = RoundQuantity(line.Quantity * factor);
                if (requiredBase <= 0)
                    throw new InvalidAssignmentException($"Quantity is too small — '{product.Name}' rounds to zero.");

                var gross = RoundMoney(line.Quantity * price);
                priced.Add((line, product, price, gross, LineDiscount(line, gross, product.Name), requiredBase));
            }

            // Second pass: spread the sale-level discount over the lines in proportion to what is left of each.
            var saleDiscount = request.DiscountAmount ?? 0m;
            if (saleDiscount < 0)
                throw new ValidationException("discountAmount", "A discount cannot be negative.");
            var afterLineDiscounts = priced.Sum(p => p.Gross - p.LineDiscount);
            if (saleDiscount > afterLineDiscounts)
                throw new ValidationException("discountAmount", "The discount cannot be more than the sale total.");
            var allocations = AllocateDiscount(priced.Select(p => p.Gross - p.LineDiscount).ToList(), RoundMoney(saleDiscount));

            var planLines = new List<SaleLinePlan>();
            var drawn = new Dictionary<Guid, decimal>(); // a product on two lines draws on one pool of stock
            for (var i = 0; i < priced.Count; i++)
            {
                var (line, product, price, gross, lineDiscount, requiredBase) = priced[i];
                var discount = lineDiscount + allocations[i];
                var lineTotal = gross - discount;
                var taxRate = business.IsVatRegistered ? TaxRates.For(product.TaxClassification) : 0m;
                var taxAmount = taxRate == 0 ? 0m : RoundMoney(lineTotal * taxRate / (100m + taxRate)); // prices include VAT

                sellable.TryGetValue(line.ProductId, out var available);
                var alreadyDrawn = drawn.GetValueOrDefault(line.ProductId);
                drawn[line.ProductId] = alreadyDrawn + requiredBase;

                planLines.Add(new SaleLinePlan(product.ProductId, product.Name, product.Sku, line.UnitOfMeasureId,
                    line.Quantity, price, gross, discount, lineTotal, taxRate, taxAmount, requiredBase, available - alreadyDrawn,
                    product.IsBatchTracked, product.RequiresPrescription));
            }

            var plan = new SalePlan(priceType, planLines, customerBalance, creditLimit);

            if (business.MaxDiscountPercent is { } cap && plan.Subtotal > 0 && plan.DiscountTotal / plan.Subtotal * 100m > cap)
                throw new ValidationException("discountAmount", $"Discounts on one sale cannot exceed {cap:0.##}% of its value.");

            return plan;
        }

        private static decimal LineDiscount(CreateSaleLineRequestDto line, decimal gross, string productName)
        {
            if (line.DiscountAmount.HasValue && line.DiscountPercent.HasValue)
                throw new ValidationException("lines", $"Give either a discount amount or a discount percent for '{productName}', not both.");

            var discount = line.DiscountAmount ?? (line.DiscountPercent is { } pct ? RoundMoney(gross * pct / 100m) : 0m);
            if (line.DiscountPercent is < 0 or > 100)
                throw new ValidationException("lines", $"The discount percent for '{productName}' must be between 0 and 100.");
            if (discount < 0 || discount > gross)
                throw new ValidationException("lines", $"The discount for '{productName}' must be between 0 and the line value ({gross:0.00}).");
            return RoundMoney(discount);
        }

        /// <summary>Splits <paramref name="discount"/> over lines in proportion to their value; the rounding remainder goes on the largest line.</summary>
        private static List<decimal> AllocateDiscount(IReadOnlyList<decimal> values, decimal discount)
        {
            var result = values.Select(_ => 0m).ToList();
            var total = values.Sum();
            if (discount <= 0 || total <= 0)
                return result;

            for (var i = 0; i < values.Count; i++)
                result[i] = RoundMoney(discount * values[i] / total);

            var largest = values.Select((v, i) => (v, i)).OrderByDescending(x => x.v).First().i;
            result[largest] += discount - result.Sum();
            return result;
        }

        private SalePrescription? BuildPrescription(SalePlan plan, SalePrescriptionRequestDto? request, Guid? userId)
        {
            if (request is null)
            {
                if (plan.RequiresPrescription)
                    throw new PrescriptionRequiredException(
                        "This sale includes prescription-only products: " +
                        string.Join(", ", plan.Lines.Where(l => l.RequiresPrescription).Select(l => $"'{l.ProductName}'").Distinct()) +
                        ". Record the prescription (prescriber, patient and date) to dispense them.");
                return null;
            }

            if (plan.RequiresPrescription && !_currentUser.HasPermission(DispensePermission))
                throw new ForbiddenException("Only staff allowed to dispense (Prescriptions.Dispense) can sell prescription-only products.");

            var errors = new Dictionary<string, string[]>();
            if (string.IsNullOrWhiteSpace(request.PrescriberName))
                errors["prescription.prescriberName"] = new[] { "The prescriber's name is required." };
            if (string.IsNullOrWhiteSpace(request.PatientName))
                errors["prescription.patientName"] = new[] { "The patient's name is required." };
            if (request.PrescriptionDate == default)
                errors["prescription.prescriptionDate"] = new[] { "The prescription date is required." };
            else if (request.PrescriptionDate > Today)
                errors["prescription.prescriptionDate"] = new[] { "The prescription date cannot be in the future." };
            if (request.PatientAgeYears is < 0 or > 130)
                errors["prescription.patientAgeYears"] = new[] { "Patient age must be between 0 and 130." };
            if (errors.Count > 0)
                throw new ValidationException(errors);

            return new SalePrescription
            {
                PrescriptionNumber = Clean(request.PrescriptionNumber),
                PrescriberName = request.PrescriberName.Trim(),
                PrescriberRegistrationNumber = Clean(request.PrescriberRegistrationNumber),
                PrescriberFacility = Clean(request.PrescriberFacility),
                PatientName = request.PatientName.Trim(),
                PatientPhoneNumber = Clean(request.PatientPhoneNumber),
                PatientAgeYears = request.PatientAgeYears,
                PrescriptionDate = request.PrescriptionDate,
                Notes = Clean(request.Notes),
                DispensedBy = userId
            };
        }

        private static (DateTime? FromUtc, DateTime? ToUtcExclusive) ResolveRange(DateOnly? from, DateOnly? to, int utcOffsetMinutes)
        {
            ReportPeriod.ValidateOffset(utcOffsetMinutes);
            if (from.HasValue && to.HasValue && from > to)
                throw new ValidationException("from", "'from' cannot be after 'to'.");
            var offset = TimeSpan.FromMinutes(utcOffsetMinutes);
            return (from.HasValue ? ReportPeriod.StartOfLocalDayUtc(from.Value, offset) : null,
                    to.HasValue ? ReportPeriod.StartOfLocalDayUtc(to.Value.AddDays(1), offset) : null);
        }

        private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        private static decimal RoundQuantity(decimal value) => Math.Round(value, QuantityDecimals, MidpointRounding.AwayFromZero);
        private static decimal RoundMoney(decimal value) => Math.Round(value, MoneyDecimals, MidpointRounding.AwayFromZero);

        // ---- Mapping ----

        private static SaleDto ToDto(Sale s, IReadOnlyDictionary<Guid, string> userNames) =>
            new(s.SaleId, s.SaleNumber, s.CustomerId, s.PaymentMethod, s.Status, s.TotalAmount, s.AmountTendered, s.ChangeDue, s.Notes, s.CreatedAt,
                s.Lines.Select(l => new SaleLineDto(l.SaleLineId, l.ProductId, l.Product.Name, l.UnitOfMeasureId, l.Quantity, l.UnitPrice, l.LineTotal,
                    l.DiscountAmount, l.TaxRate, l.TaxAmount, l.QuantityReturned, l.Product.Sku, l.UnitOfMeasure?.Symbol,
                    l.BatchAllocations.Select(a => new SaleLineBatchDto(a.StockBatchId, a.StockBatch?.BatchNumber ?? string.Empty, a.StockBatch?.ExpiryDate,
                        a.QuantityInBaseUnits, a.QuantityReturnedInBaseUnits)).ToList())),
                s.Customer?.CustomerName, s.CreatedBy,
                s.CreatedBy is { } by && userNames.TryGetValue(by, out var name) ? name : null,
                s.Subtotal, s.DiscountTotal, s.TaxTotal, s.ReturnedAmount,
                s.Prescription is null ? null : ToDto(s.Prescription));

        private static SalePrescriptionDto ToDto(SalePrescription p) =>
            new(p.PrescriptionNumber, p.PrescriberName, p.PrescriberRegistrationNumber, p.PrescriberFacility, p.PatientName, p.PatientPhoneNumber,
                p.PatientAgeYears, p.PrescriptionDate, p.Notes, p.DispensedBy);

        private static SaleReturnDto ToDto(SaleReturn r) =>
            new(r.SaleReturnId, r.ReturnNumber, r.SaleId, r.Sale?.SaleNumber ?? string.Empty, r.Reason, r.RefundMethod, r.RefundReference,
                r.TotalAmount, r.TaxAmount, r.CreatedAt, r.CreatedBy,
                r.Lines.Select(l => new SaleReturnLineDto(l.SaleReturnLineId, l.SaleLineId, l.ProductId, l.Product?.Name ?? string.Empty,
                    l.Quantity, l.QuantityInBaseUnits, l.Amount, l.TaxAmount, l.Restock)));
    }
}
