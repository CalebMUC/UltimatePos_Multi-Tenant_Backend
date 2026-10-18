using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UltimatePos.Application.Business;
using UltimatePos.Application.Business.Dtos;
using UltimatePos.Application.Catalog;
using UltimatePos.Application.Catalog.Dtos;
using UltimatePos.Application.Common.Interfaces;
using UltimatePos.Application.Identity;
using UltimatePos.Application.Identity.Dtos;
using UltimatePos.Application.Inventory;
using UltimatePos.Application.Inventory.Dtos;
using UltimatePos.Application.Notifications;
using UltimatePos.Application.Reports;
using UltimatePos.Application.Sales;
using UltimatePos.Application.Sales.Dtos;
using UltimatePos.Domain.Entities;
using UltimatePos.Domain.Exceptions;
using UltimatePos.Infrastructure.Persistence;
using static UltimatePos.Domain.Enums.PosEnums;

namespace Ultimate.Api.Tests;

/// <summary>
/// Events → notifications, end to end on Postgres: what each change records, who hears about it (by permission, in their business
/// only), that an alert is announced once until it clears, the email/SMS queue, the sweep, and the audit log.
/// </summary>
public class NotificationsPostgresTests : IClassFixture<MultiBusinessDatabase>
{
    private readonly MultiBusinessDatabase _db;
    public NotificationsPostgresTests(MultiBusinessDatabase db) => _db = db;

    private static readonly DateOnly Today = ReportPeriod.TodayAt(ReportPeriod.DefaultUtcOffsetMinutes);

    // ---- helpers ----

    /// <summary>What the background worker does: events → notifications and deliveries, then send the deliveries.</summary>
    private async Task DispatchAsync()
    {
        await _db.RunAsync(sp => sp.GetRequiredService<NotificationDispatcher>().DispatchPendingAsync());
        await _db.RunAsync(sp => sp.GetRequiredService<DeliveryProcessor>().ProcessDueAsync());
    }

    private async Task<List<NotificationDto>> InboxAsync(Guid userId, Guid businessId)
    {
        var (savedUser, savedBusiness, savedPermissions) = (_db.Caller.UserId, _db.Caller.BusinessId, _db.Caller.Permissions);
        _db.Caller.UserId = userId;
        _db.Caller.BusinessId = businessId;
        _db.Caller.Permissions = null;
        try
        {
            return (await _db.RunAsync(sp => sp.GetRequiredService<NotificationService>().GetNotificationsAsync(false, 1, 100))).Items.ToList();
        }
        finally
        {
            (_db.Caller.UserId, _db.Caller.BusinessId, _db.Caller.Permissions) = (savedUser, savedBusiness, savedPermissions);
        }
    }

    /// <summary>A member of <paramref name="businessId"/> holding only the given permissions (through a role of their own).</summary>
    private async Task<UserDto> MemberWithAsync(Guid businessId, string label, params string[] permissions)
    {
        _db.SignInTo(businessId);
        var role = await _db.RunAsync(sp => sp.GetRequiredService<AuthService>().CreateRoleAsync(new CreateRoleRequestDto($"{label} {Guid.NewGuid():N}"[..20], null)));
        var all = await _db.RunAsync(sp => sp.GetRequiredService<AuthService>().GetPermissionsAsync());
        await _db.RunAsync(sp => sp.GetRequiredService<AuthService>().AssignPermissionsToRoleAsync(role.RoleId,
            new AssignPermissionsRequestDto(all.Where(p => permissions.Contains(p.Name)).Select(p => p.PermissionId))));
        return await _db.RunAsync(sp => sp.GetRequiredService<AuthService>().RegisterAsync(new RegisterRequest(
            $"{label.ToLowerInvariant()}-{Guid.NewGuid():N}@test.local", "Pass1234!", $"{label} User", "0711000000", role.RoleName)));
    }

    private async Task<(ProductDto Product, Guid Pcs)> StockedProductAsync(Guid businessId, string name, decimal reorderLevel, decimal openingStock)
    {
        _db.SignInTo(businessId);
        var pcs = await _db.RunAsync(async sp => (await sp.GetRequiredService<CatalogService>().GetUnitsOfMeasureAsync()).Single(u => u.Symbol == "pcs").UnitOfMeasureId);
        var code = $"N{Guid.NewGuid():N}"[..8].ToUpperInvariant();
        var category = await _db.RunAsync(sp => sp.GetRequiredService<CatalogService>().CreateCategoryAsync(new CreateCategoryRequestDto("Cat " + code, code, null)));
        var product = await _db.RunAsync(sp => sp.GetRequiredService<CatalogService>().CreateProductAsync(new CreateProductRequestDto(
            name, null, category.CategoryId, ItemType.FinishedGood, TaxClassification.Exempt, pcs, reorderLevel)));
        await _db.RunAsync(sp => sp.GetRequiredService<CatalogService>().AddPriceTierAsync(product.ProductId,
            new AddPriceTierRequestDto(pcs, PriceType.Retail, 10, DateTime.UtcNow.AddMinutes(-1))));
        if (openingStock > 0)
            await _db.RunAsync(sp => sp.GetRequiredService<InventoryService>().AdjustStockAsync(new CreateStockAdjustmentRequestDto(
                product.ProductId, pcs, openingStock, "Opening stock", 5)));
        return (product, pcs);
    }

    private Task SellAsync(Guid productId, Guid pcs, decimal quantity) =>
        _db.RunAsync(sp => sp.GetRequiredService<SalesService>().RecordSaleAsync(new CreateSaleRequestDto(
            null, PaymentMethod.Cash, null, new[] { new CreateSaleLineRequestDto(productId, pcs, quantity) }, quantity * 10, null, null)));

    // ---------------------------------------------------------------- Low stock

    [PostgresFact]
    public async Task Low_stock_is_announced_once_to_the_right_people_in_the_right_business_and_again_after_a_restock()
    {
        var buyer = await MemberWithAsync(_db.Afya, "Buyer", "PurchaseOrders.Create");
        var cashier = await MemberWithAsync(_db.Afya, "Cashier", "Sales.Create", "Sales.View");
        var alvetBuyer = await MemberWithAsync(_db.Alvet, "AlvetBuyer", "PurchaseOrders.Create");
        var (product, pcs) = await StockedProductAsync(_db.Afya, "Cetirizine 10mg", reorderLevel: 10, openingStock: 15);
        await DispatchAsync();

        bool HasLowAlert(IEnumerable<NotificationDto> inbox) =>
            inbox.Any(n => n.EventType == SystemEventTypes.StockBelowReorderLevel && n.Message.StartsWith("Cetirizine 10mg"));

        // 15 → 8 crosses the reorder level of 10.
        _db.SignInTo(_db.Afya);
        await SellAsync(product.ProductId, pcs, 7);
        await DispatchAsync();

        var buyerInbox = await InboxAsync(buyer.Id, _db.Afya);
        var low = Assert.Single(buyerInbox, n => n.EventType == SystemEventTypes.StockBelowReorderLevel && n.Message.StartsWith("Cetirizine 10mg"));
        Assert.Equal(("Low stock", EventSeverity.Warning), (low.Title, low.Severity));
        Assert.Contains("down to 8", low.Message);
        Assert.Equal($"/inventory?productId={product.ProductId}", low.Link);
        Assert.False(HasLowAlert(await InboxAsync(cashier.Id, _db.Afya)));        // no stock permission
        Assert.False(HasLowAlert(await InboxAsync(alvetBuyer.Id, _db.Alvet)));    // another business

        // Selling further below the line announces nothing new; running out is its own, more serious, alert.
        _db.SignInTo(_db.Afya);
        await SellAsync(product.ProductId, pcs, 3);
        await DispatchAsync();
        Assert.Single(await InboxAsync(buyer.Id, _db.Afya), n => HasLowAlert(new[] { n }));
        await SellAsync(product.ProductId, pcs, 5);
        await DispatchAsync();
        var outAlert = Assert.Single(await InboxAsync(buyer.Id, _db.Afya),
            n => n.EventType == SystemEventTypes.StockOutOfStock && n.Message.StartsWith("Cetirizine 10mg"));
        Assert.Equal(EventSeverity.Critical, outAlert.Severity);

        // Restocking closes the alerts; dropping again announces again.
        _db.SignInTo(_db.Afya);
        await _db.RunAsync(sp => sp.GetRequiredService<InventoryService>().AdjustStockAsync(new CreateStockAdjustmentRequestDto(product.ProductId, pcs, 30, "Delivery", 5)));
        await DispatchAsync();
        await SellAsync(product.ProductId, pcs, 25);
        await DispatchAsync();
        Assert.Equal(2, (await InboxAsync(buyer.Id, _db.Afya)).Count(n => HasLowAlert(new[] { n })));
    }

    // ---------------------------------------------------------------- Security events, email and SMS

    [PostgresFact]
    public async Task A_locked_account_alerts_the_user_managers_and_emails_and_texts_the_user()
    {
        var manager = await MemberWithAsync(_db.Afya, "Manager", "Users.Lock", "Users.View");
        var victim = await MemberWithAsync(_db.Afya, "Victim", "Sales.View");

        _db.Caller.UserId = null;
        _db.Caller.BusinessId = null;
        for (var i = 0; i < 5; i++)
            await Assert.ThrowsAsync<InvalidCredentialsException>(() => _db.RunAsync(sp =>
                sp.GetRequiredService<AuthService>().LoginAsync(new LoginRequest(victim.Email, "wrong-password"))));
        await DispatchAsync();

        var alert = Assert.Single(await InboxAsync(manager.Id, _db.Afya), n => n.EventType == SystemEventTypes.UserAccountLocked && n.Message.Contains(victim.Email));
        Assert.Equal(EventSeverity.Critical, alert.Severity);

        var deliveries = await _db.RunAsync(sp => sp.GetRequiredService<UltimatePosDbContext>().NotificationDeliveries.IgnoreQueryFilters()
            .Where(d => d.UserId == victim.Id || d.UserId == manager.Id).ToListAsync());
        Assert.Contains(deliveries, d => d.UserId == victim.Id && d.Channel == DeliveryChannel.Email && d.Recipient == victim.Email);
        Assert.Contains(deliveries, d => d.UserId == victim.Id && d.Channel == DeliveryChannel.Sms && d.Recipient == "0711000000");
        Assert.Contains(deliveries, d => d.UserId == manager.Id && d.Channel == DeliveryChannel.Email);
        Assert.All(deliveries, d => Assert.Equal(DeliveryStatus.Sent, d.Status)); // through the log-only senders
        // The victim's inbox also has the welcome email from when the account was created; pick the lock email.
        var email = Assert.Single(deliveries, d => d.UserId == victim.Id && d.Channel == DeliveryChannel.Email && d.Subject!.EndsWith("Account locked"));
        Assert.Equal("[Afya Pharmacy] Account locked", email.Subject);
        Assert.Contains("https://pos.test/users/", email.Body);
        Assert.True(deliveries.Single(d => d.UserId == victim.Id && d.Channel == DeliveryChannel.Sms).Body.Length <= 160);

        // The failed attempts and the lock are in the audit log.
        _db.SignInTo(_db.Afya);
        var log = await _db.RunAsync(sp => sp.GetRequiredService<NotificationService>().GetAuditLogAsync(1, 50, null, null, 180, "User", null,
            "User", victim.Id, null, null));
        Assert.Equal(4, log.Items.Count(e => e.EventType == SystemEventTypes.UserLoginFailed));
        Assert.Single(log.Items, e => e.EventType == SystemEventTypes.UserAccountLocked);
    }

    // ---------------------------------------------------------------- Sweep

    [PostgresFact]
    public async Task The_sweep_raises_expiry_alerts_once_and_closes_them_when_the_batch_is_gone()
    {
        var stockKeeper = await MemberWithAsync(_db.Afya, "Stores", "Stock.Adjust", "Stock.View");
        _db.SignInTo(_db.Afya);
        var pcs = await _db.RunAsync(async sp => (await sp.GetRequiredService<CatalogService>().GetUnitsOfMeasureAsync()).Single(u => u.Symbol == "pcs").UnitOfMeasureId);
        var code = $"X{Guid.NewGuid():N}"[..8].ToUpperInvariant();
        var category = await _db.RunAsync(sp => sp.GetRequiredService<CatalogService>().CreateCategoryAsync(new CreateCategoryRequestDto("Exp " + code, code, null)));
        var insulin = await _db.RunAsync(sp => sp.GetRequiredService<CatalogService>().CreateProductAsync(new CreateProductRequestDto(
            "Insulin vial " + code, null, category.CategoryId, ItemType.FinishedGood, TaxClassification.Exempt, pcs, 0, IsBatchTracked: true)));
        var lot = "LOT-" + code;
        await _db.RunAsync(sp => sp.GetRequiredService<InventoryService>().AdjustStockAsync(new CreateStockAdjustmentRequestDto(
            insulin.ProductId, pcs, 12, "Opening stock", 300, BatchNumber: lot, ExpiryDate: Today.AddDays(10))));

        async Task<int> SweepAsync()
        {
            _db.Caller.UserId = null;
            _db.Caller.BusinessId = null;
            return await _db.RunAsync(async sp =>
            {
                using var tenant = sp.GetRequiredService<ITenantContext>().Use(_db.Afya);
                return await sp.GetRequiredService<AlertSweepService>().RunAsync();
            });
        }

        Assert.True(await SweepAsync() >= 1);
        await DispatchAsync();
        Assert.Equal(0, await SweepAsync()); // everything it found is now an open alert: nothing is raised twice
        await DispatchAsync();

        var inbox = await InboxAsync(stockKeeper.Id, _db.Afya);
        var expiring = Assert.Single(inbox, n => n.EventType == SystemEventTypes.BatchExpiringSoon && n.Message.Contains(lot));
        Assert.Contains("expires in 10 days", expiring.Message);

        // Writing the lot off closes the alert at the next sweep.
        _db.SignInTo(_db.Afya);
        var batch = (await _db.RunAsync(sp => sp.GetRequiredService<InventoryService>().GetBatchesAsync(insulin.ProductId, null, true, false))).Single();
        await _db.RunAsync(sp => sp.GetRequiredService<InventoryService>().AdjustStockAsync(new CreateStockAdjustmentRequestDto(
            insulin.ProductId, pcs, -12, "Returned to supplier", StockBatchId: batch.StockBatchId)));
        await SweepAsync();
        var open = await _db.RunAsync(sp => sp.GetRequiredService<UltimatePosDbContext>().AlertStates.IgnoreQueryFilters()
            .SingleAsync(a => a.BusinessId == _db.Afya && a.AlertKey == AlertKeys.BatchExpiring(batch.StockBatchId)));
        Assert.False(open.IsActive);
    }

    // ---------------------------------------------------------------- Audit log and the bell

    [PostgresFact]
    public async Task The_audit_log_records_who_did_what_and_stays_inside_its_business()
    {
        // A sign-in and sign-out in Afya, and a user created there.
        _db.Caller.UserId = null;
        _db.Caller.BusinessId = null;
        var login = await _db.RunAsync(sp => sp.GetRequiredService<AuthService>().LoginAsync(new LoginRequest(DbSeeder.DevAdminEmail, DbSeeder.DevAdminPassword, _db.Afya)));
        _db.SignInTo(_db.Afya);
        await _db.RunAsync(sp => sp.GetRequiredService<AuthService>().LogoutAsync(new LogoutRequest(login.RefreshToken)));
        var newcomer = await MemberWithAsync(_db.Afya, "Newcomer", "Sales.View");

        _db.SignInTo(_db.Afya);
        var log = (await _db.RunAsync(sp => sp.GetRequiredService<NotificationService>().GetAuditLogAsync(1, 200, Today, Today, 180,
            null, null, null, null, null, null))).Items.ToList();
        Assert.Contains(log, e => e.EventType == SystemEventTypes.UserLoggedIn && e.ActorUserId == _db.AdminUserId && e.ActorName == "admin");
        Assert.Contains(log, e => e.EventType == SystemEventTypes.UserLoggedOut && e.SubjectUserId == _db.AdminUserId);
        var created = Assert.Single(log, e => e.EventType == SystemEventTypes.UserCreated && e.SubjectUserId == newcomer.Id);
        Assert.Equal(newcomer.FullName, created.SubjectName);
        Assert.Contains("\"role\"", created.Data);

        // Filter by area, and nothing of Afya's shows in Alvet.
        var userArea = (await _db.RunAsync(sp => sp.GetRequiredService<NotificationService>().GetAuditLogAsync(1, 200, null, null, 180,
            "User", null, null, null, null, null))).Items;
        Assert.All(userArea, e => Assert.StartsWith("User.", e.EventType));
        _db.SignInTo(_db.Alvet);
        var alvetLog = (await _db.RunAsync(sp => sp.GetRequiredService<NotificationService>().GetAuditLogAsync(1, 200, null, null, 180,
            null, null, null, null, null, null))).Items;
        Assert.DoesNotContain(alvetLog, e => e.SubjectUserId == newcomer.Id);

        // The welcome email went to the new user; their roles notification tells them nothing in-app (email only for user creation).
        await DispatchAsync();
        var welcome = await _db.RunAsync(sp => sp.GetRequiredService<UltimatePosDbContext>().NotificationDeliveries.IgnoreQueryFilters()
            .SingleAsync(d => d.UserId == newcomer.Id && d.Channel == DeliveryChannel.Email));
        Assert.Equal("[Afya Pharmacy] New user", welcome.Subject);
    }

    [PostgresFact]
    public async Task Users_read_and_clear_only_their_own_notifications()
    {
        var buyer = await MemberWithAsync(_db.Afya, "Reader", "PurchaseOrders.Create");
        var (product, pcs) = await StockedProductAsync(_db.Afya, "Reader test " + Guid.NewGuid().ToString("N")[..6], reorderLevel: 5, openingStock: 6);
        _db.SignInTo(_db.Afya);
        await SellAsync(product.ProductId, pcs, 2);
        await DispatchAsync();

        _db.Caller.UserId = buyer.Id;
        _db.Caller.BusinessId = _db.Afya;
        _db.Caller.Permissions = null;
        var service = (IServiceProvider sp) => sp.GetRequiredService<NotificationService>();
        var unread = await _db.RunAsync(sp => service(sp).GetUnreadCountAsync());
        Assert.True(unread >= 1);
        var first = (await _db.RunAsync(sp => service(sp).GetNotificationsAsync(true, 1, 20))).Items.First();
        await _db.RunAsync(sp => service(sp).MarkReadAsync(first.NotificationId));
        Assert.Equal(unread - 1, await _db.RunAsync(sp => service(sp).GetUnreadCountAsync()));

        // Someone else's notification cannot be marked from this account.
        _db.Caller.UserId = _db.AdminUserId;
        await Assert.ThrowsAsync<NotFoundException>(() => _db.RunAsync(sp => service(sp).MarkReadAsync(first.NotificationId)));

        _db.Caller.UserId = buyer.Id;
        await _db.RunAsync(sp => service(sp).MarkAllReadAsync());
        Assert.Equal(0, await _db.RunAsync(sp => service(sp).GetUnreadCountAsync()));
    }
}
