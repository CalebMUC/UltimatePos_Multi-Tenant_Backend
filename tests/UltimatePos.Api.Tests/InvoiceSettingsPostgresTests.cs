using Microsoft.Extensions.DependencyInjection;
using UltimatePos.Application.Business;
using UltimatePos.Application.Business.Dtos;
using UltimatePos.Application.Platform;
using UltimatePos.Domain.Entities;
using UltimatePos.Domain.Exceptions;

namespace Ultimate.Api.Tests;

/// <summary>
/// What invoices print under "Send payment to": a refill station that has no M-Pesa till of its own is paid into its bank through the
/// bank's paybill, to a till, or by M-Pesa to a phone. Saved on the business, so every device prints the same.
/// </summary>
public class InvoiceSettingsPostgresTests : IClassFixture<MultiBusinessDatabase>
{
    private readonly MultiBusinessDatabase _db;
    public InvoiceSettingsPostgresTests(MultiBusinessDatabase db) => _db = db;

    private Task<BusinessDto> SaveAsync(InvoiceSettingsDto settings) =>
        _db.RunAsync(sp => sp.GetRequiredService<BusinessService>().UpdateInvoiceSettingsAsync(settings));

    private Task<BusinessDto> CurrentAsync() => _db.RunAsync(sp => sp.GetRequiredService<BusinessService>().GetCurrentBusinessAsync());

    private static readonly InvoiceSettingsDto Blank = new(null, null, null, null, null, null, null, null, null);

    [PostgresFact]
    public async Task Saves_the_bank_and_phone_on_the_business_tidied()
    {
        _db.SignInTo(_db.Afya, "Businesses.Update");
        var saved = await SaveAsync(new InvoiceSettingsDto(" Family Bank ", " Kasarani ", "222111", " Afya Pharmacy Ltd ", "789123", "5123456",
            "0712 345 678", 14, "Thank you for buying from us."));

        var expected = new InvoiceSettingsDto("Family Bank", "Kasarani", "222111", "Afya Pharmacy Ltd", "789123", "5123456", "254712345678", 14,
            "Thank you for buying from us.");
        Assert.Equal(expected, saved.Invoice);
        Assert.Equal(expected, (await CurrentAsync()).Invoice);

        // A full replace: blanks clear what was there. The M-Pesa till and paybill are not touched.
        var before = await CurrentAsync();
        var cleared = await SaveAsync(new InvoiceSettingsDto("", " ", "  ", "", null, " ", null, null, null));
        Assert.Equal(Blank, cleared.Invoice);
        Assert.Equal((before.MpesaTillNumber, before.MpesaPaybillNumber, before.MpesaShortCode),
            (cleared.MpesaTillNumber, cleared.MpesaPaybillNumber, cleared.MpesaShortCode));
    }

    [PostgresFact]
    public async Task Refuses_numbers_that_cannot_be_paid_to()
    {
        _db.SignInTo(_db.Afya, "Businesses.Update");
        var error = await Assert.ThrowsAsync<ValidationException>(() => SaveAsync(new InvoiceSettingsDto("Family Bank", new string('k', 101),
            "22-11", new string('a', 101), new string('9', 51), "51234567", "020 123 4567", 400, new string('x', 501))));

        Assert.Equal(new[] { "bankAccountName", "bankAccountNumber", "bankBranch", "bankPaybillNumber", "dueDays", "note", "sendMoneyPhoneNumber",
            "tillNumber" }, error.Errors.Keys.Order());
    }

    [PostgresFact]
    public async Task The_super_user_can_give_the_bank_details_when_onboarding_a_business()
    {
        _db.Caller.UserId = _db.SuperUserId;
        _db.Caller.BusinessId = null;
        var pin = "P05" + Random.Shared.Next(1000000, 9999999) + "I";
        var registered = await _db.RunAsync(sp => sp.GetRequiredService<PlatformService>().RegisterBusinessAsync(new RegisterBusinessRequestDto(
            "Jirani Minimart", null, BusinessType.Retailer, BusinessKind.Trader, null, pin, null, null, null, null, null, null,
            Owner: new BusinessOwnerRequestDto("Jirani Owner", $"owner-{Guid.NewGuid():N}@invoice.test", null),
            Invoice: new InvoiceSettingsDto("Equity Bank", "Ruiru", "247247", "Jirani Minimart", "0123456789", null, null, 7, null))));

        Assert.Equal(new InvoiceSettingsDto("Equity Bank", "Ruiru", "247247", "Jirani Minimart", "0123456789", null, null, 7, null),
            registered.Business.Invoice);
    }
}
