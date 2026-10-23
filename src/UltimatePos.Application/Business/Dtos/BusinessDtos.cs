using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UltimatePos.Domain.Entities;
using static UltimatePos.Domain.Enums.PosEnums;

namespace UltimatePos.Application.Business.Dtos
{
    /// <param name="Kind">Required, unless the Sector suggests one (BusinessDefaults.Sectors).</param>
    /// <param name="AllowNegativeStock">Null = the Sector's default (a water refill station starts with overselling off).</param>
    /// <param name="Sector">What line of business it is in. Defaults to General; sets the units and expense categories it starts with.</param>
    /// <param name="MpesaShortCode">Older clients only: ignored when MpesaTillNumber or MpesaPaybillNumber is given, else stored as the till.</param>
    /// <param name="MpesaTillNumber">Buy Goods till, 5-7 digits. The short code that routes payments is the till, else the paybill.</param>
    /// <param name="MpesaPaybillNumber">Paybill, 5-7 digits.</param>
    public record RegisterBusinessRequestDto(
    string BusinessName,
    string? TradingName,
    BusinessType BusinessType,
    BusinessKind? Kind,
    string? RegistrationNumber,
    string KraPin,
    string? PhysicalAddress,
    string? County,
    string? PhoneNumber,
    string? Email,
    string? LogoUrl,
    string? BackgroundImageUrl,
    bool IsVatRegistered = false,
    string? MpesaShortCode = null,
    decimal? MaxDiscountPercent = null,
    UltimatePos.Application.Platform.BusinessOwnerRequestDto? Owner = null,
    bool? AllowNegativeStock = null,
    BusinessSector? Sector = null,
    string? MpesaTillNumber = null,
    string? MpesaPaybillNumber = null);

    /// <summary>Full replace of the editable profile. Kind cannot change: it decides what the business may purchase.</summary>
    /// <param name="AllowNegativeStock">Null keeps the current setting (older clients do not send it).</param>
    /// <param name="MpesaShortCode">Older clients only: ignored when MpesaTillNumber or MpesaPaybillNumber is given, else stored as the till.</param>
    public record UpdateBusinessRequestDto(
        string BusinessName,
        string? TradingName,
        BusinessType BusinessType,
        string? RegistrationNumber,
        string KraPin,
        string? PhysicalAddress,
        string? County,
        string? PhoneNumber,
        string? Email,
        string? LogoUrl,
        string? BackgroundImageUrl,
        bool IsVatRegistered = false,
        string? MpesaShortCode = null,
        decimal? MaxDiscountPercent = null,
        bool? AllowNegativeStock = null,
        string? MpesaTillNumber = null,
        string? MpesaPaybillNumber = null);

    public record BusinessDto(
        Guid BusinessId, string BusinessName, string? TradingName, BusinessType BusinessType, BusinessKind Kind,
        string? RegistrationNumber, string KraPin, string? PhysicalAddress, string? County,
        string? PhoneNumber, string? Email, string? LogoUrl, string? BackgroundImageUrl, bool IsActive,
        bool IsVatRegistered, string? MpesaShortCode, decimal? MaxDiscountPercent,
        bool IsSuspended = false, string? SuspensionReason = null, DateOnly? LicenseEndsOn = null, int? LicenseMaxUsers = null,
        int? LicenseMaxBranches = null, bool AllowNegativeStock = true, BusinessSector Sector = BusinessSector.General,
        IReadOnlyList<string>? Modules = null, // the app areas it uses (BusinessModules), from its sector and kind
        string? MpesaTillNumber = null, string? MpesaPaybillNumber = null, // where customers pay, for the POS payment screen
        decimal? MonthlyDiscountThreshold = null, decimal? MonthlyDiscountPercent = null, // the monthly-spend reward; null = off
        MonthlyDiscountScope MonthlyDiscountScope = MonthlyDiscountScope.RestOfMonth);

    /// <summary>
    /// The monthly-spend reward. Both Threshold and Percent set = on: once a registered customer has spent Threshold in a calendar month,
    /// later purchases that month get Percent off (Scope: all of them, or only the next one). Both null = off.
    /// </summary>
    public record UpdateMonthlyDiscountRequestDto(decimal? Threshold, decimal? Percent,
        MonthlyDiscountScope Scope = MonthlyDiscountScope.RestOfMonth);

    /// <param name="BusinessId">Optional. Customers are always registered in the business signed in to; if given it must be that one.</param>
    /// <param name="PriceType">Which price list the customer buys at. Defaults to Wholesale for a wholesaler, Retail for a retailer.</param>
    /// <param name="CreditLimit">Most the customer may owe on credit sales. Null = no limit.</param>
    public record RegisterCustomerRequestDto(
        Guid? BusinessId,
        string CustomerName,
        string? KraPin,
        string? ContactPerson,
        string? PhoneNumber,
        string? Email,
        string? PhysicalAddress,
        PriceType? PriceType = null,
        decimal? CreditLimit = null);

    /// <summary>Full replace. PriceType null keeps the current price type; CreditLimit null removes any limit.</summary>
    public record UpdateCustomerRequestDto(
        string CustomerName,
        string? KraPin,
        string? ContactPerson,
        string? PhoneNumber,
        string? Email,
        string? PhysicalAddress,
        PriceType? PriceType = null,
        decimal? CreditLimit = null);

    /// <param name="DiscountPercent">The customer's standing discount, applied to every sale to them. Null = none.</param>
    public record CustomerDto(
        Guid CustomerId, Guid BusinessId, string CustomerName, string? KraPin, string? ContactPerson,
        string? PhoneNumber, string? Email, string? PhysicalAddress, bool IsActive, PriceType PriceType, decimal? CreditLimit,
        decimal? DiscountPercent = null, string? DiscountNote = null, DateTime? DiscountSetAt = null);

    /// <summary>Sets (or, with DiscountPercent null or 0, removes) the customer's standing discount. Needs Customers.ManageDiscount.</summary>
    /// <param name="DiscountPercent">Off every sale to the customer, up to 2 decimals. At most the business's MaxDiscountPercent.</param>
    /// <param name="Note">Why: required when giving a discount, so the decision can be reviewed later.</param>
    public record SetCustomerDiscountRequestDto(decimal? DiscountPercent, string? Note);

    public record BusinessLicenseStatusDto(UltimatePos.Application.Platform.BusinessAccessDto Access, int? MaxUsers,
        IEnumerable<UltimatePos.Application.Platform.BusinessLicenseDto> Licenses, int? MaxBranches = null, int OpenBranches = 0);
}
