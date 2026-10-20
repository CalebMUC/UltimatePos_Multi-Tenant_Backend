using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UltimatePos.Application.Business;
using UltimatePos.Application.Business.Dtos;
using UltimatePos.Application.Catalog;
using UltimatePos.Application.Expenses;
using UltimatePos.Application.Platform;
using UltimatePos.Domain.Entities;
using UltimatePos.Domain.Exceptions;
using UltimatePos.Infrastructure.Persistence;

namespace Ultimate.Api.Tests;

/// <summary>The starting point each sector gets: pure, no database.</summary>
public class BusinessSectorDefaultsTests
{
    [Fact]
    public void Every_sector_has_a_profile()
    {
        foreach (var sector in Enum.GetValues<BusinessSector>())
            Assert.True(BusinessDefaults.Sectors.ContainsKey(sector), $"{sector} has no entry in BusinessDefaults.Sectors.");
    }

    [Fact]
    public void A_sector_never_repeats_a_unit_symbol_or_expense_code_a_business_already_gets()
    {
        // Unit symbols and expense codes are unique per business: a repeat would make registration fail.
        foreach (var sector in Enum.GetValues<BusinessSector>())
        {
            var symbols = BusinessDefaults.UnitsFor(sector).Select(u => u.Symbol.ToLowerInvariant()).ToList();
            Assert.True(symbols.Count == symbols.Distinct().Count(), $"{sector} repeats a unit symbol.");
            var codes = BusinessDefaults.ExpenseCategoriesFor(sector).Select(c => c.Code.ToUpperInvariant()).ToList();
            Assert.True(codes.Count == codes.Distinct().Count(), $"{sector} repeats an expense category code.");
        }
    }

    [Fact]
    public void Only_a_water_refill_station_starts_with_overselling_off_and_an_agrovet_must_choose_its_kind()
    {
        Assert.False(BusinessDefaults.For(BusinessSector.WaterRefill).AllowNegativeStock);
        Assert.All(Enum.GetValues<BusinessSector>().Where(s => s != BusinessSector.WaterRefill),
            s => Assert.True(BusinessDefaults.For(s).AllowNegativeStock));
        Assert.Null(BusinessDefaults.For(BusinessSector.Agrovet).Kind);   // makes its own feeds, or only resells
        Assert.Null(BusinessDefaults.For(BusinessSector.General).Kind);
        Assert.Equal(BusinessKind.Trader, BusinessDefaults.For(BusinessSector.Supermarket).Kind);
    }
}

/// <summary>Sectors end to end: registration defaults, explicit values winning, reclassification by a super user, platform reporting.</summary>
public class BusinessSectorPostgresTests : IClassFixture<MultiBusinessDatabase>
{
    private readonly MultiBusinessDatabase _db;
    public BusinessSectorPostgresTests(MultiBusinessDatabase db) => _db = db;

    private void AsSuperUser()
    {
        _db.Caller.UserId = _db.SuperUserId;
        _db.Caller.BusinessId = null;
        _db.Caller.BranchId = null;
        _db.Caller.BranchIds = new List<Guid>();
        _db.Caller.Permissions = null;
    }

    private Task<T> Platform<T>(Func<PlatformService, Task<T>> call) => _db.RunAsync(sp => call(sp.GetRequiredService<PlatformService>()));

    /// <summary>Signs in to a business registered here, as its owner, at its head office.</summary>
    private async Task SignInAsync(Guid businessId)
    {
        if (!_db.HeadOffice.ContainsKey(businessId))
            _db.HeadOffice[businessId] = await _db.RunAsync(sp => sp.GetRequiredService<UltimatePosDbContext>().Branches.IgnoreQueryFilters()
                .Where(b => b.BusinessId == businessId && b.BranchNumber == 0).Select(b => b.BranchId).SingleAsync());
        _db.SignInTo(businessId);
    }

    private Task<RegisterBusinessResultDto> RegisterAsync(BusinessSector? sector, BusinessKind? kind, bool? allowNegativeStock = null)
    {
        AsSuperUser();
        var name = $"{sector?.ToString() ?? "Unsectored"} {Guid.NewGuid():N}"[..20];
        var pin = "P05" + Random.Shared.Next(1000000, 9999999) + "S";
        return Platform(p => p.RegisterBusinessAsync(new RegisterBusinessRequestDto(name, null, BusinessType.Retailer, kind, null, pin,
            null, "Kiambu", null, null, null, null,
            Owner: new BusinessOwnerRequestDto(name + " Owner", $"owner-{Guid.NewGuid():N}@sector.test", "0722000000"),
            AllowNegativeStock: allowNegativeStock, Sector: sector)));
    }

    [PostgresFact]
    public async Task A_water_refill_station_starts_as_a_trader_that_never_oversells_with_its_own_units_and_expense_categories()
    {
        var result = await RegisterAsync(BusinessSector.WaterRefill, kind: null);
        var business = result.Business;
        Assert.Equal((BusinessSector.WaterRefill, BusinessKind.Trader, false), (business.Sector, business.Kind, business.AllowNegativeStock));

        await SignInAsync(business.BusinessId);
        var units = (await _db.RunAsync(sp => sp.GetRequiredService<CatalogService>().GetUnitsOfMeasureAsync())).Select(u => u.Symbol).ToList();
        Assert.Contains("l", units);   // the general defaults...
        Assert.Contains("10L", units); // ...and the sector's
        Assert.Contains("20L", units);
        var categories = (await _db.RunAsync(sp => sp.GetRequiredService<ExpenseService>().GetCategoriesAsync(activeOnly: true)))
            .Select(c => c.Code).ToList();
        Assert.Contains("RENT", categories);
        Assert.Contains("FILT", categories);
        Assert.Contains("SAN", categories);

        // The owner sees it from their own business profile.
        Assert.Equal(BusinessSector.WaterRefill, (await _db.RunAsync(sp => sp.GetRequiredService<BusinessService>().GetCurrentBusinessAsync())).Sector);
    }

    [PostgresFact]
    public async Task What_registration_says_explicitly_wins_over_the_sector_defaults()
    {
        var station = await RegisterAsync(BusinessSector.WaterRefill, BusinessKind.Manufacturer, allowNegativeStock: true); // purifies and bottles
        Assert.Equal((BusinessKind.Manufacturer, true), (station.Business.Kind, station.Business.AllowNegativeStock));
    }

    [PostgresFact]
    public async Task Kind_is_still_required_where_the_sector_cannot_suggest_one()
    {
        var agrovet = await Assert.ThrowsAsync<ValidationException>(() => RegisterAsync(BusinessSector.Agrovet, kind: null));
        Assert.Contains("kind", agrovet.Errors.Keys);
        await Assert.ThrowsAsync<ValidationException>(() => RegisterAsync(sector: null, kind: null));

        // A business registered as before, with no sector, is General and gets only the general defaults.
        var general = await RegisterAsync(sector: null, BusinessKind.Trader);
        Assert.Equal((BusinessSector.General, true), (general.Business.Sector, general.Business.AllowNegativeStock));
        await SignInAsync(general.Business.BusinessId);
        Assert.DoesNotContain(await _db.RunAsync(sp => sp.GetRequiredService<CatalogService>().GetUnitsOfMeasureAsync()), u => u.Symbol == "20L");
    }

    [PostgresFact]
    public async Task A_super_user_reclassifies_a_business_and_the_platform_reports_by_sector()
    {
        var shop = await RegisterAsync(sector: null, BusinessKind.Trader);
        var id = shop.Business.BusinessId;

        var moved = await Platform(p => p.ChangeSectorAsync(id, new ChangeSectorRequestDto(BusinessSector.Minimart)));
        Assert.Equal(BusinessSector.Minimart, moved.Business.Sector);
        await Assert.ThrowsAsync<InvalidAssignmentException>(() => Platform(p => p.ChangeSectorAsync(id, new ChangeSectorRequestDto(BusinessSector.Minimart))));
        await Assert.ThrowsAsync<ValidationException>(() => Platform(p => p.ChangeSectorAsync(id, new ChangeSectorRequestDto((BusinessSector)999))));

        // Only the label moved: nothing the business started with was added or removed.
        await SignInAsync(id);
        Assert.DoesNotContain(await _db.RunAsync(sp => sp.GetRequiredService<CatalogService>().GetUnitsOfMeasureAsync()), u => u.Symbol == "doz");

        AsSuperUser();
        var minimarts = await Platform(p => p.GetBusinessesAsync(BusinessSector.Minimart));
        Assert.Contains(minimarts, b => b.Business.BusinessId == id);
        Assert.All(minimarts, b => Assert.Equal(BusinessSector.Minimart, b.Business.Sector));

        var dashboard = await Platform(p => p.GetDashboardAsync(null, null, 180));
        Assert.Equal(BusinessSector.Minimart, dashboard.Businesses.Single(b => b.BusinessId == id).Sector);
        Assert.Equal(dashboard.BusinessCount, dashboard.BySector!.Sum(s => s.BusinessCount));
        Assert.Contains(dashboard.BySector!, s => s.Sector == BusinessSector.Minimart && s.BusinessCount >= 1);
    }
}
