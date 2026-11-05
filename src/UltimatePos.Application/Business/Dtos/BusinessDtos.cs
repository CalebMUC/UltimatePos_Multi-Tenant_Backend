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
    /// <param name="EtimsEnabled">Reports to KRA eTIMS. Off for a business that does not need it: it then sees nothing of eTIMS.</param>
    /// <param name="Invoice">How customers pay its invoices (bank, branch, account, till). Null = none yet; the owner can add them later.</param>
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
    string? MpesaPaybillNumber = null,
    bool EtimsEnabled = false,
    InvoiceSettingsDto? Invoice = null);

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
        MonthlyDiscountScope MonthlyDiscountScope = MonthlyDiscountScope.RestOfMonth,
        bool EtimsEnabled = false, // reports to KRA eTIMS; when false the "etims" module is absent and nothing of eTIMS is shown
        InvoiceSettingsDto? Invoice = null, // how customers pay an invoice, and its terms
        AgeRestrictedSalesSettingsDto? AgeRestrictedSales = null); // the minimum age and the selling hours for alcohol and tobacco

    /// <summary>
    /// Who may buy age-restricted products (alcohol, tobacco) and when they may be sold, as the business's liquor licence and county law
    /// say. Also the body of PUT /businesses/current/age-restricted-sales, a full replace. Times are East Africa Time ("17:00").
    /// </summary>
    /// <param name="MinimumAge">The age the till asks the cashier to check for: 18 by law today, 18-25.</param>
    /// <param name="WeekdayFrom">Start of the selling hours. With WeekdayTo; both null = any time.</param>
    /// <param name="WeekdayTo">End of the selling hours. Before WeekdayFrom = past midnight (17:00-02:00).</param>
    /// <param name="WeekendFrom">Saturday and Sunday, when they differ. With WeekendTo; both null = the weekday hours.</param>
    /// <param name="Enabled">The business uses age-restricted sales. Only a Liquor business can have it on (the default there); off = no
    /// age check, no selling hours and no register, whatever products are marked. Left out of a PUT = on.</param>
    public record AgeRestrictedSalesSettingsDto(int MinimumAge = 18, TimeOnly? WeekdayFrom = null, TimeOnly? WeekdayTo = null,
        TimeOnly? WeekendFrom = null, TimeOnly? WeekendTo = null, bool Enabled = true)
    {
        /// <summary>The business's settings; Enabled is whether they apply (a Liquor business with the switch on).</summary>
        public static AgeRestrictedSalesSettingsDto From(BusinessProfile b) => new(b.MinimumPurchaseAge, b.RestrictedSalesFrom,
            b.RestrictedSalesTo, b.RestrictedSalesWeekendFrom, b.RestrictedSalesWeekendTo, BusinessModules.AgeRestrictedSalesOn(b));

        /// <summary>Copies these settings, already checked, onto the business.</summary>
        public void ApplyTo(BusinessProfile b)
        {
            b.MinimumPurchaseAge = MinimumAge;
            b.RestrictedSalesFrom = WeekdayFrom;
            b.RestrictedSalesTo = WeekdayTo;
            b.RestrictedSalesWeekendFrom = WeekendFrom;
            b.RestrictedSalesWeekendTo = WeekendTo;
            b.AgeRestrictedSalesEnabled = Enabled;
        }
    }

    /// <summary>
    /// What an invoice prints under "Send payment to", and its terms. Every field is optional: a blank one is left off the invoice.
    /// Also the body of PUT /businesses/current/invoice-settings, a full replace.
    /// </summary>
    /// <param name="BankName">The bank customers pay into, e.g. Family Bank.</param>
    /// <param name="BankBranch">The branch of that bank holding the account, e.g. Kasarani.</param>
    /// <param name="BankPaybillNumber">The bank's M-Pesa paybill, 5-10 digits.</param>
    /// <param name="BankAccountName">The name the account is held in, so a customer depositing at the bank can check it.</param>
    /// <param name="BankAccountNumber">The business's account at that bank: the paybill's account number.</param>
    /// <param name="TillNumber">A Buy Goods till customers may pay an invoice to, 5-7 digits.</param>
    /// <param name="SendMoneyPhoneNumber">A mobile number customers may send M-Pesa to directly. Stored as 2547XXXXXXXX.</param>
    /// <param name="DueDays">Days to pay, 0-365. Null = no due date, unless the customer promised one.</param>
    /// <param name="Note">Printed at the foot of every invoice. At most 500 characters.</param>
    public record InvoiceSettingsDto(string? BankName, string? BankBranch, string? BankPaybillNumber, string? BankAccountName,
        string? BankAccountNumber, string? TillNumber, string? SendMoneyPhoneNumber, int? DueDays, string? Note)
    {
        public static InvoiceSettingsDto From(BusinessProfile b) => new(b.InvoiceBankName, b.InvoiceBankBranch, b.InvoiceBankPaybill,
            b.InvoiceBankAccountName, b.InvoiceBankAccountNumber, b.InvoiceTillNumber, b.InvoiceSendMoneyPhone, b.InvoiceDueDays, b.InvoiceNote);

        /// <summary>Copies these settings, already checked and tidied, onto the business.</summary>
        public void ApplyTo(BusinessProfile b)
        {
            b.InvoiceBankName = BankName;
            b.InvoiceBankBranch = BankBranch;
            b.InvoiceBankPaybill = BankPaybillNumber;
            b.InvoiceBankAccountName = BankAccountName;
            b.InvoiceBankAccountNumber = BankAccountNumber;
            b.InvoiceTillNumber = TillNumber;
            b.InvoiceSendMoneyPhone = SendMoneyPhoneNumber;
            b.InvoiceDueDays = DueDays;
            b.InvoiceNote = Note;
        }
    }

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
