using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static UltimatePos.Domain.Enums.PosEnums;

namespace UltimatePos.Application.Sales.Dtos
{
    /// <param name="DiscountAmount">Discount on this line in money. Give this or DiscountPercent, not both. Needs Sales.ApplyDiscount.</param>
    /// <param name="DiscountPercent">Discount on this line as a percentage of quantity x unit price.</param>
    public record CreateSaleLineRequestDto(Guid ProductId, Guid UnitOfMeasureId, decimal Quantity,
        decimal? DiscountAmount = null, decimal? DiscountPercent = null);

    /// <summary>
    /// A bundle rung up as one item: "2 x 20L Heavy-duty bottle + water". The sale gets one line per part (water, bottle), each taking
    /// its own stock, with the bundle's price split over them. Whole bundles only.
    /// </summary>
    /// <param name="DiscountAmount">Discount on this bundle in money (all of them, not each). Give this or DiscountPercent. Needs Sales.ApplyDiscount.</param>
    public record CreateSaleBundleRequestDto(Guid ProductBundleId, decimal Quantity, decimal? DiscountAmount = null, decimal? DiscountPercent = null);

    /// <summary>
    /// The prescription the sale is dispensed against. Required when any line is a prescription-only product, and recorded as
    /// given — the system checks it is complete, not what was prescribed.
    /// </summary>
    public record SalePrescriptionRequestDto(string? PrescriptionNumber, string PrescriberName, string? PrescriberRegistrationNumber,
        string? PrescriberFacility, string PatientName, string? PatientPhoneNumber, int? PatientAgeYears, DateOnly PrescriptionDate, string? Notes);

    /// <param name="DiscountAmount">A discount on the whole sale, spread across the lines in proportion to their value.</param>
    /// <param name="OverrideCreditLimit">Let a credit sale take the customer past their limit. Needs Sales.OverrideCreditLimit.</param>
    public record CreateSaleRequestDto(
        Guid? CustomerId, PaymentMethod PaymentMethod, string? Notes, IEnumerable<CreateSaleLineRequestDto> Lines,
        decimal? AmountTendered,      // Cash only
        string? MpesaPhoneNumber,     // MpesaStkPush only
        Guid? MpesaTransactionId,     // MpesaTill only — the unmatched transaction being linked
        decimal? DiscountAmount = null,
        bool OverrideCreditLimit = false,
        SalePrescriptionRequestDto? Prescription = null,
        DateOnly? PaymentDueDate = null, // Credit only — the day the customer promised to pay by
        IEnumerable<CreateSaleBundleRequestDto>? Bundles = null, // a sale needs at least one line or one bundle
        string? MpesaReference = null); // MpesaReference only — the code from the customer's SMS, e.g. SJK4H7LM2P

    public record SalePreviewLineDto(Guid ProductId, string ProductName, string Sku, Guid UnitOfMeasureId,
        decimal Quantity, decimal UnitPrice, decimal LineTotal, decimal AvailableInBaseUnits, decimal RequiredInBaseUnits, bool StockSufficient,
        decimal DiscountAmount = 0, decimal TaxRate = 0, decimal TaxAmount = 0, bool RequiresPrescription = false, bool IsBatchTracked = false,
        Guid? ProductBundleId = null, string? BundleName = null, decimal? BundleQuantity = null);

    /// <summary>A bundle on a sale, as the receipt shows it: its lines (one per part) share ProductBundleId, and Total is their sum.</summary>
    public record SaleBundleDto(Guid ProductBundleId, string Name, decimal Quantity, decimal Total, decimal DiscountAmount);

    /// <param name="TotalAmount">What the customer pays, VAT included. Subtotal - DiscountTotal.</param>
    /// <param name="CreditLimitExceeded">For credit sales: the sale would take the customer past their limit.</param>
    public record SalePreviewDto(Guid? CustomerId, PriceType PriceType, decimal TotalAmount, bool AllStockSufficient, IEnumerable<SalePreviewLineDto> Lines,
        decimal Subtotal = 0, decimal DiscountTotal = 0, decimal TaxTotal = 0, bool RequiresPrescription = false,
        decimal? CustomerBalance = null, decimal? CreditLimit = null, bool CreditLimitExceeded = false,
        IEnumerable<SaleBundleDto>? Bundles = null, decimal? CustomerDiscountPercent = null, decimal CustomerDiscountTotal = 0);

    public record SaleLineBatchDto(Guid StockBatchId, string BatchNumber, DateOnly? ExpiryDate, decimal QuantityInBaseUnits, decimal QuantityReturnedInBaseUnits);

    public record SaleLineDto(Guid SaleLineId, Guid ProductId, string ProductName, Guid UnitOfMeasureId, decimal Quantity, decimal UnitPrice, decimal LineTotal,
        decimal DiscountAmount = 0, decimal TaxRate = 0, decimal TaxAmount = 0, decimal QuantityReturned = 0, string? Sku = null, string? UnitSymbol = null,
        IEnumerable<SaleLineBatchDto>? Batches = null, Guid? ProductBundleId = null, string? BundleName = null, decimal? BundleQuantity = null);

    public record SalePrescriptionDto(string? PrescriptionNumber, string PrescriberName, string? PrescriberRegistrationNumber, string? PrescriberFacility,
        string PatientName, string? PatientPhoneNumber, int? PatientAgeYears, DateOnly PrescriptionDate, string? Notes, Guid? DispensedBy);

    public record SaleDto(Guid SaleId, string SaleNumber, Guid? CustomerId, PaymentMethod PaymentMethod, SaleStatus Status,
        decimal TotalAmount, decimal? AmountTendered, decimal? ChangeDue, string? Notes, DateTime CreatedAt, IEnumerable<SaleLineDto> Lines,
        string? CustomerName = null, Guid? CreatedBy = null, string? CreatedByName = null,
        decimal Subtotal = 0, decimal DiscountTotal = 0, decimal TaxTotal = 0, decimal ReturnedAmount = 0,
        SalePrescriptionDto? Prescription = null, Guid BranchId = default, string? KraBranchId = null, string? BranchName = null,
        DateOnly? PaymentDueDate = null, IEnumerable<SaleBundleDto>? Bundles = null, string? MpesaReference = null,
        DeliveryMode DeliveryMode = DeliveryMode.Counter, decimal? CustomerDiscountPercent = null, decimal CustomerDiscountAmount = 0);

    /// <summary>Filters for the sales list. from/to are local calendar days (inclusive), resolved with utcOffsetMinutes.
    /// Search matches the sale number, the customer's name or a typed M-Pesa code.</summary>
    public record SaleQuery(Guid? CustomerId = null, SaleStatus? Status = null, DateOnly? From = null, DateOnly? To = null,
        Guid? CreatedBy = null, PaymentMethod? PaymentMethod = null, string? Search = null, int UtcOffsetMinutes = 180,
        DeliveryMode? DeliveryMode = null);

    // ---- Returns ----

    /// <param name="Quantity">In the unit the line was sold in.</param>
    /// <param name="Restock">False for damaged, opened or expired goods: refunded, but not put back on the shelf.</param>
    public record CreateSaleReturnLineRequestDto(Guid SaleLineId, decimal Quantity, bool Restock = true);
    /// <param name="RefundMethod">Cash or Mpesa pays the customer back; CreditNote credits a registered customer's account (the only option for a credit sale).</param>
    public record CreateSaleReturnRequestDto(string Reason, RefundMethod RefundMethod, string? RefundReference, IEnumerable<CreateSaleReturnLineRequestDto> Lines);

    public record SaleReturnLineDto(Guid SaleReturnLineId, Guid SaleLineId, Guid ProductId, string ProductName, decimal Quantity, decimal QuantityInBaseUnits,
        decimal Amount, decimal TaxAmount, bool Restock);
    public record SaleReturnDto(Guid SaleReturnId, string ReturnNumber, Guid SaleId, string SaleNumber, string Reason, RefundMethod RefundMethod,
        string? RefundReference, decimal TotalAmount, decimal TaxAmount, DateTime CreatedAt, Guid? CreatedBy, IEnumerable<SaleReturnLineDto> Lines);

    // ---- Prescription register ----

    public record PrescriptionRegisterLineDto(Guid ProductId, string ProductName, string? GenericName, string? Strength, decimal Quantity, string? UnitSymbol,
        IEnumerable<string> BatchNumbers);
    public record PrescriptionRegisterEntryDto(Guid SaleId, string SaleNumber, DateTime DispensedAt, SaleStatus SaleStatus, string? DispensedByName,
        SalePrescriptionDto Prescription, IEnumerable<PrescriptionRegisterLineDto> Lines);
}
