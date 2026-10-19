using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UltimatePos.Domain.Entities;
using static UltimatePos.Domain.Enums.PosEnums;

namespace UltimatePos.Application.Business.Dtos
{
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
    bool AllowNegativeStock = true);

    /// <summary>Full replace of the editable profile. Kind cannot change: it decides what the business may purchase.</summary>
    /// <param name="AllowNegativeStock">Null keeps the current setting (older clients do not send it).</param>
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
        bool? AllowNegativeStock = null);

    public record BusinessDto(
        Guid BusinessId, string BusinessName, string? TradingName, BusinessType BusinessType, BusinessKind Kind,
        string? RegistrationNumber, string KraPin, string? PhysicalAddress, string? County,
        string? PhoneNumber, string? Email, string? LogoUrl, string? BackgroundImageUrl, bool IsActive,
        bool IsVatRegistered, string? MpesaShortCode, decimal? MaxDiscountPercent,
        bool IsSuspended = false, string? SuspensionReason = null, DateOnly? LicenseEndsOn = null, int? LicenseMaxUsers = null,
        int? LicenseMaxBranches = null, bool AllowNegativeStock = true);

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

    public record CustomerDto(
        Guid CustomerId, Guid BusinessId, string CustomerName, string? KraPin, string? ContactPerson,
        string? PhoneNumber, string? Email, string? PhysicalAddress, bool IsActive, PriceType PriceType, decimal? CreditLimit);

    public record BusinessLicenseStatusDto(UltimatePos.Application.Platform.BusinessAccessDto Access, int? MaxUsers,
        IEnumerable<UltimatePos.Application.Platform.BusinessLicenseDto> Licenses, int? MaxBranches = null, int OpenBranches = 0);
}
