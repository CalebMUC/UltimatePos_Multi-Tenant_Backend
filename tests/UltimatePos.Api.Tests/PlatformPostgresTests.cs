using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UltimatePos.Application.Business;
using UltimatePos.Application.Business.Dtos;
using UltimatePos.Application.Catalog;
using UltimatePos.Application.Catalog.Dtos;
using UltimatePos.Application.Identity;
using UltimatePos.Application.Identity.Dtos;
using UltimatePos.Application.Inventory;
using UltimatePos.Application.Inventory.Dtos;
using UltimatePos.Application.Licensing;
using UltimatePos.Application.Notifications;
using UltimatePos.Application.Platform;
using UltimatePos.Application.Sales;
using UltimatePos.Application.Sales.Dtos;
using UltimatePos.Domain.Entities;
using UltimatePos.Domain.Exceptions;
using UltimatePos.Infrastructure.Persistence;
using static UltimatePos.Domain.Enums.PosEnums;

namespace Ultimate.Api.Tests;

/// <summary>The super user: separate from every business, onboarding, licences and their enforcement, suspension, performance, support.</summary>
public class PlatformPostgresTests : IClassFixture<MultiBusinessDatabase>
{
    private readonly MultiBusinessDatabase _db;
    public PlatformPostgresTests(MultiBusinessDatabase db) => _db = db;

    private static DateOnly Today => LicensePolicy.Today;

    private void AsSuperUser()
    {
        _db.Caller.UserId = _db.SuperUserId;
        _db.Caller.BusinessId = null;
        _db.Caller.Permissions = null;
    }

    private void SignedOut()
    {
        _db.Caller.UserId = null;
        _db.Caller.BusinessId = null;
    }

    private Task<T> Platform<T>(Func<PlatformService, Task<T>> call) => _db.RunAsync(sp => call(sp.GetRequiredService<PlatformService>()));
    private Task<LoginResponseDto> LoginAsync(string email, string password, Guid? businessId = null) =>
        _db.RunAsync(sp => sp.GetRequiredService<AuthService>().LoginAsync(new LoginRequest(email, password, businessId)));

    /// <summary>A fresh business with a new owner, registered by the super user.</summary>
    private async Task<(RegisterBusinessResultDto Result, string OwnerEmail)> NewBusinessAsync(string name)
    {
        AsSuperUser();
        var email = $"owner-{Guid.NewGuid():N}@{name.ToLowerInvariant().Replace(" ", "")}.test";
        var pin = "P05" + Random.Shared.Next(1000000, 9999999) + "X";
        var result = await Platform(p => p.RegisterBusinessAsync(new RegisterBusinessRequestDto(name, null, BusinessType.Retailer, BusinessKind.Trader,
            null, pin, null, null, null, null, null, null, Owner: new BusinessOwnerRequestDto($"{name} Owner", email, "0722000000"))));
        return (result, email);
    }

    // ---------------------------------------------------------------- Separation

    [PostgresFact]
    public async Task The_super_user_registers_businesses_without_joining_them_and_cannot_work_inside_one()
    {
        var (result, ownerEmail) = await NewBusinessAsync("Kijani Chemist");
        var businessId = result.Business.BusinessId;

        // The owner was created, with a temporary password, as the business's Admin; the super user is not a member.
        Assert.True(result.Owner.Created);
        Assert.NotNull(result.Owner.TemporaryPassword);
        Assert.Equal(LicenseKind.Trial, result.TrialLicense!.Kind);
        Assert.Equal(Today.AddDays(13), result.TrialLicense.EndsOn);
        var detail = await Platform(p => p.GetBusinessAsync(businessId));
        Assert.Single(detail.Members);
        Assert.DoesNotContain(detail.Members, m => m.Id == _db.SuperUserId);
        Assert.Equal("ExpiringSoon", detail.Summary.Access.Status); // a 14-day trial is inside the 14-day warning window

        // The super user has no business: business data is out of reach.
        AsSuperUser();
        await Assert.ThrowsAsync<ForbiddenException>(() => _db.RunAsync(sp => sp.GetRequiredService<BusinessService>().GetCustomersAsync()));

        // A super user account can never be handed a business, nor can a business account become a super user.
        await Assert.ThrowsAsync<ValidationException>(() => Platform(p => p.AddBusinessAdminAsync(businessId,
            new BusinessOwnerRequestDto("Sneaky", DbSeeder.DevSuperUserEmail, null))));
        await Assert.ThrowsAsync<ValidationException>(() => Platform(p => p.CreateSuperUserAsync(
            new CreateSuperUserRequestDto("Owner", ownerEmail, null))));

        // The new owner must change the temporary password.
        SignedOut();
        var login = await LoginAsync(ownerEmail, result.Owner.TemporaryPassword!);
        Assert.Equal(businessId, login.ActiveBusinessId);
        Assert.True(login.MustChangePassword);
        _db.Caller.UserId = login.User.Id;
        _db.Caller.BusinessId = businessId;
        await _db.RunAsync(sp => sp.GetRequiredService<AuthService>().ChangePasswordAsync(new ChangePasswordRequest(result.Owner.TemporaryPassword!, "MyOwn2026!")));
        SignedOut();
        Assert.False((await LoginAsync(ownerEmail, "MyOwn2026!")).MustChangePassword);
    }

    // ---------------------------------------------------------------- Licences and enforcement

    [PostgresFact]
    public async Task Renewals_continue_from_the_current_period_and_overlaps_are_refused()
    {
        var (result, _) = await NewBusinessAsync("Baraka Pharmacy");
        var id = result.Business.BusinessId;
        var trialEnd = result.TrialLicense!.EndsOn;

        AsSuperUser();
        var paid = await Platform(p => p.IssueLicenseAsync(id, new IssueLicenseRequestDto(LicenseKind.Paid, "Pharmacy Standard", 12, null, null, 5,
            24000, SettlementMethod.Mpesa, "SKL1234567", null)));
        Assert.Equal((trialEnd.AddDays(1), trialEnd.AddDays(1).AddMonths(12).AddDays(-1)), (paid.StartsOn, paid.EndsOn));
        Assert.Matches("^LIC-0000\\d\\d$", paid.LicenseNumber);

        var business = (await Platform(p => p.GetBusinessAsync(id))).Summary;
        // Covered without a break to the end of the paid year. Today is still the trial, so the paid licence's 5-user limit applies from its first day.
        Assert.Equal(("Active", paid.EndsOn, (int?)null), (business.Access.Status, business.Business.LicenseEndsOn, business.Business.LicenseMaxUsers));

        await Assert.ThrowsAsync<ValidationException>(() => Platform(p => p.IssueLicenseAsync(id, new IssueLicenseRequestDto(
            LicenseKind.Paid, "Overlap", null, Today, Today.AddDays(30), null, 1000, SettlementMethod.Cash, null, null))));
        await Assert.ThrowsAsync<ValidationException>(() => Platform(p => p.IssueLicenseAsync(id, new IssueLicenseRequestDto(
            LicenseKind.Paid, "No reference", 1, null, null, null, 1000, SettlementMethod.Mpesa, null, null))));

        // Cancelling the paid period drops the business back to the trial's end.
        await Platform(p => p.CancelLicenseAsync(paid.BusinessLicenseId, new ReasonRequestDto("Payment reversed")));
        Assert.Equal(trialEnd, (await Platform(p => p.GetBusinessAsync(id))).Summary.Business.LicenseEndsOn);
    }

    [PostgresFact]
    public async Task Suspended_and_expired_businesses_are_refused_and_grace_only_warns()
    {
        var (result, ownerEmail) = await NewBusinessAsync("Uzima Chemist");
        var id = result.Business.BusinessId;
        var password = result.Owner.TemporaryPassword!;

        // Suspended for non-payment: the owner is refused with the reason, until reactivated.
        AsSuperUser();
        await Platform(p => p.SuspendAsync(id, new ReasonRequestDto("Licence fee unpaid")));
        SignedOut();
        var denied = await Assert.ThrowsAsync<BusinessAccessDeniedException>(() => LoginAsync(ownerEmail, password));
        Assert.Equal("BUSINESS_SUSPENDED", denied.Code);
        Assert.Contains("Licence fee unpaid", denied.Message);
        AsSuperUser();
        await Platform(p => p.ReactivateAsync(id));
        SignedOut();
        await LoginAsync(ownerEmail, password);

        // Licence ended 3 days ago: inside the 7-day grace, so allowed — with a warning.
        AsSuperUser();
        await Platform(p => p.CancelLicenseAsync(result.TrialLicense!.BusinessLicenseId, new ReasonRequestDto("Replaced for the test")));
        var lapsed = await Platform(p => p.IssueLicenseAsync(id, new IssueLicenseRequestDto(LicenseKind.Paid, "Monthly", null,
            Today.AddDays(-33), Today.AddDays(-3), null, 2000, SettlementMethod.Cash, null, null)));
        SignedOut();
        var graceLogin = await LoginAsync(ownerEmail, password);
        Assert.Contains("Access stops in 4 days", graceLogin.LicenseWarning);

        // Ended 10 days ago: past the grace, refused.
        AsSuperUser();
        await Platform(p => p.CancelLicenseAsync(lapsed.BusinessLicenseId, new ReasonRequestDto("Replaced for the test")));
        await Platform(p => p.IssueLicenseAsync(id, new IssueLicenseRequestDto(LicenseKind.Paid, "Monthly", null,
            Today.AddDays(-40), Today.AddDays(-10), null, 2000, SettlementMethod.Cash, null, null)));
        SignedOut();
        Assert.Equal("LICENSE_EXPIRED", (await Assert.ThrowsAsync<BusinessAccessDeniedException>(() => LoginAsync(ownerEmail, password))).Code);

        // Renewing restores access at once.
        AsSuperUser();
        await Platform(p => p.IssueLicenseAsync(id, new IssueLicenseRequestDto(LicenseKind.Paid, "Monthly", 1, null, null, null, 2000,
            SettlementMethod.Mpesa, "SKR0000001", null)));
        SignedOut();
        Assert.Null((await LoginAsync(ownerEmail, password)).LicenseWarning);
    }

    [PostgresFact]
    public async Task The_licence_user_limit_stops_new_members()
    {
        var (result, ownerEmail) = await NewBusinessAsync("Tiba Chemist");
        var id = result.Business.BusinessId;
        AsSuperUser();
        await Platform(p => p.CancelLicenseAsync(result.TrialLicense!.BusinessLicenseId, new ReasonRequestDto("Replaced")));
        await Platform(p => p.IssueLicenseAsync(id, new IssueLicenseRequestDto(LicenseKind.Paid, "Solo", 1, null, null, 2, 1000,
            SettlementMethod.Cash, null, null)));

        var owner = (await Platform(p => p.GetBusinessAsync(id))).Members.Single();
        _db.Caller.UserId = owner.Id;
        _db.Caller.BusinessId = id;
        _db.Caller.Permissions = null;
        await _db.RunAsync(sp => sp.GetRequiredService<AuthService>().RegisterAsync(new RegisterRequest($"second-{Guid.NewGuid():N}@t.test", "Pass1234!", "Second", "0700", "Admin")));
        var full = await Assert.ThrowsAsync<BusinessAccessDeniedException>(() => _db.RunAsync(sp => sp.GetRequiredService<AuthService>().RegisterAsync(
            new RegisterRequest($"third-{Guid.NewGuid():N}@t.test", "Pass1234!", "Third", "0700", "Admin"))));
        Assert.Equal("USER_LIMIT_REACHED", full.Code);
    }

    // ---------------------------------------------------------------- Performance, support, notifications

    [PostgresFact]
    public async Task The_dashboard_shows_each_businesss_sales_and_support_can_reset_a_users_password()
    {
        var (result, ownerEmail) = await NewBusinessAsync("Faraja Pharmacy");
        var id = result.Business.BusinessId;
        var owner = (await Platform(p => p.GetBusinessAsync(id))).Members.Single();

        // One cash sale of 3 x 50.
        _db.Caller.UserId = owner.Id;
        _db.Caller.BusinessId = id;
        var pcs = await _db.RunAsync(async sp => (await sp.GetRequiredService<CatalogService>().GetUnitsOfMeasureAsync()).Single(u => u.Symbol == "pcs").UnitOfMeasureId);
        var category = await _db.RunAsync(sp => sp.GetRequiredService<CatalogService>().CreateCategoryAsync(new CreateCategoryRequestDto("General", "GEN", null)));
        var product = await _db.RunAsync(sp => sp.GetRequiredService<CatalogService>().CreateProductAsync(new CreateProductRequestDto(
            "Plasters", null, category.CategoryId, ItemType.FinishedGood, TaxClassification.Exempt, pcs, 0)));
        await _db.RunAsync(sp => sp.GetRequiredService<CatalogService>().AddPriceTierAsync(product.ProductId, new AddPriceTierRequestDto(pcs, PriceType.Retail, 50, DateTime.UtcNow.AddMinutes(-1))));
        await _db.RunAsync(sp => sp.GetRequiredService<InventoryService>().AdjustStockAsync(new CreateStockAdjustmentRequestDto(product.ProductId, pcs, 10, "Opening", 20)));
        await _db.RunAsync(sp => sp.GetRequiredService<SalesService>().RecordSaleAsync(new CreateSaleRequestDto(null, PaymentMethod.Cash, null,
            new[] { new CreateSaleLineRequestDto(product.ProductId, pcs, 3) }, 150, null, null)));

        AsSuperUser();
        var dashboard = await Platform(p => p.GetDashboardAsync(null, null, 180));
        var row = Assert.Single(dashboard.Businesses, b => b.BusinessId == id);
        Assert.Equal((1, 150m, 150m, 1, 1), (row.SaleCount, row.Revenue, row.AverageSale, row.ActiveUsers, row.ActiveProducts));
        Assert.True(dashboard.BusinessCount >= 3);

        // Support: the owner is locked out; a reset gives a temporary password that works.
        var reset = await Platform(p => p.ResetBusinessUserPasswordAsync(id, owner.Id));
        SignedOut();
        Assert.True((await LoginAsync(ownerEmail, reset.TemporaryPassword!)).MustChangePassword);

        // The business's audit log, read by support, has the sale and the reset.
        AsSuperUser();
        var log = (await Platform(p => p.GetAuditLogAsync(id, 1, 100, null, null, 180, null, null, null))).Items;
        Assert.Contains(log, e => e.EventType == SystemEventTypes.SupportPasswordReset);
        Assert.Contains(log, e => e.EventType == SystemEventTypes.BusinessRegistered);
    }

    [PostgresFact]
    public async Task A_suspension_notifies_the_business_admin_in_the_business_and_the_super_users_on_the_platform()
    {
        var (result, _) = await NewBusinessAsync("Neema Chemist");
        var id = result.Business.BusinessId;
        var owner = (await Platform(p => p.GetBusinessAsync(id))).Members.Single();

        // A second super user, who should hear about it (the one who did it does not).
        AsSuperUser();
        var colleague = await Platform(p => p.CreateSuperUserAsync(new CreateSuperUserRequestDto("Colleague", $"su-{Guid.NewGuid():N}@platform.test", null)));
        await Platform(p => p.SuspendAsync(id, new ReasonRequestDto("Unpaid")));
        await _db.RunAsync(sp => sp.GetRequiredService<NotificationDispatcher>().DispatchPendingAsync());

        var notifications = await _db.RunAsync(sp => sp.GetRequiredService<UltimatePosDbContext>().Notifications.IgnoreQueryFilters()
            .Where(n => n.EventType == SystemEventTypes.BusinessSuspended).ToListAsync());
        var ownerCopy = Assert.Single(notifications, n => n.UserId == owner.Id);
        Assert.Equal(id, ownerCopy.BusinessId);
        var platformCopy = Assert.Single(notifications, n => n.UserId == colleague.User.Id && n.Link == $"/platform/businesses/{id}");
        Assert.Null(platformCopy.BusinessId);
        Assert.Equal($"/platform/businesses/{id}", platformCopy.Link);
        Assert.StartsWith("Neema Chemist:", platformCopy.Message);
        Assert.DoesNotContain(notifications, n => n.UserId == _db.SuperUserId && n.Link == $"/platform/businesses/{id}"); // the one who did it

        // The last active super user cannot be switched off.
        await Platform(p => p.SetSuperUserActiveAsync(colleague.User.Id, false));
        await Assert.ThrowsAsync<InvalidAssignmentException>(() => Platform(p => p.SetSuperUserActiveAsync(_db.SuperUserId, false)));
    }
}
