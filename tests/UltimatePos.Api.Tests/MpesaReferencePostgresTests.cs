using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UltimatePos.Application.Business;
using UltimatePos.Application.Business.Dtos;
using UltimatePos.Application.Catalog;
using UltimatePos.Application.Catalog.Dtos;
using UltimatePos.Application.Deliveries;
using UltimatePos.Application.Inventory;
using UltimatePos.Application.Inventory.Dtos;
using UltimatePos.Application.Notifications;
using UltimatePos.Application.Platform;
using UltimatePos.Application.Reports;
using UltimatePos.Application.Sales;
using UltimatePos.Application.Sales.Dtos;
using UltimatePos.Domain.Entities;
using UltimatePos.Domain.Exceptions;
using UltimatePos.Infrastructure.Persistence;
using static UltimatePos.Domain.Enums.PosEnums;

namespace Ultimate.Api.Tests;

/// <summary>
/// A shop with no till wired to Daraja yet: the customer pays the business's M-Pesa and the cashier types the code from the SMS
/// (PaymentMethod.MpesaReference). Also the business profile's till and paybill, and the short code that follows them.
/// </summary>
public class MpesaReferencePostgresTests : IClassFixture<MultiBusinessDatabase>
{
    private const string ShopKraPin = "P051000005E";
    private const decimal Price = 250m;

    private readonly MultiBusinessDatabase _db;
    public MpesaReferencePostgresTests(MultiBusinessDatabase db) => _db = db;

    private Task<T> Run<T>(Func<IServiceProvider, Task<T>> call) => _db.RunAsync(call);

    private async Task<Guid> SignInToShopAsync()
    {
        var shopId = await Run(sp => sp.GetRequiredService<UltimatePosDbContext>().Businesses.IgnoreQueryFilters()
            .Where(b => b.KraPin == ShopKraPin).Select(b => (Guid?)b.BusinessId).FirstOrDefaultAsync());
        if (shopId is null)
        {
            _db.Caller.UserId = _db.SuperUserId;
            _db.Caller.BusinessId = null;
            _db.Caller.Permissions = null;
            var registered = await Run(sp => sp.GetRequiredService<PlatformService>().RegisterBusinessAsync(new RegisterBusinessRequestDto(
                "Test M-Pesa Shop", null, BusinessType.Retailer, BusinessKind.Trader, null, ShopKraPin, null, "Nairobi", "0722000555", null, null, null,
                Owner: new BusinessOwnerRequestDto("admin", DbSeeder.DevAdminEmail, null))));
            shopId = registered.Business.BusinessId;
        }
        if (!_db.HeadOffice.ContainsKey(shopId.Value))
            _db.HeadOffice[shopId.Value] = await Run(sp => sp.GetRequiredService<UltimatePosDbContext>().Branches.IgnoreQueryFilters()
                .Where(b => b.BusinessId == shopId && b.BranchNumber == 0).Select(b => b.BranchId).SingleAsync());
        _db.SignInTo(shopId.Value);
        return shopId.Value;
    }

    /// <summary>A product of this test's own, priced at 250 a piece, with 20 in stock.</summary>
    private async Task<(Guid ProductId, Guid Piece)> ProductAsync()
    {
        var code = $"M{Guid.NewGuid():N}"[..8].ToUpperInvariant();
        return await Run(async sp =>
        {
            var catalog = sp.GetRequiredService<CatalogService>();
            var piece = (await catalog.GetUnitsOfMeasureAsync()).FirstOrDefault(u => u.Symbol == "pcs")?.UnitOfMeasureId
                ?? (await catalog.CreateUnitOfMeasureAsync(new CreateUnitOfMeasureRequestDto("Piece", "pcs"))).UnitOfMeasureId;
            var category = await catalog.CreateCategoryAsync(new CreateCategoryRequestDto("Goods " + code, code, null));
            var product = await catalog.CreateProductAsync(new CreateProductRequestDto(
                "Item " + code, null, category.CategoryId, ItemType.FinishedGood, TaxClassification.Exempt, piece, 0));
            await catalog.AddPriceTierAsync(product.ProductId, new AddPriceTierRequestDto(piece, PriceType.Retail, Price, DateTime.UtcNow.AddMinutes(-1)));
            await sp.GetRequiredService<InventoryService>().AdjustStockAsync(
                new CreateStockAdjustmentRequestDto(product.ProductId, piece, 20, "Opening stock", 100m));
            return (product.ProductId, piece);
        });
    }

    /// <summary>A code no other test uses: 10 upper-case letters and digits.</summary>
    private static string NewCode() => ("S" + Guid.NewGuid().ToString("N")).ToUpperInvariant()[..10];

    private static CreateSaleRequestDto ByCode((Guid ProductId, Guid Piece) item, string? code, decimal quantity = 1) =>
        new(null, PaymentMethod.MpesaReference, null, new[] { new CreateSaleLineRequestDto(item.ProductId, item.Piece, quantity) },
            null, null, null, MpesaReference: code);

    private Task<SaleDto> SellAsync(CreateSaleRequestDto request) => Run(sp => sp.GetRequiredService<SalesService>().RecordSaleAsync(request));

    private Task<decimal> OnHandAsync(Guid productId) =>
        Run(async sp => (await sp.GetRequiredService<InventoryService>().GetStockLevelAsync(productId)).QuantityOnHand);

    // ---------------------------------------------------------------- Selling by a typed M-Pesa code

    [PostgresFact]
    public async Task A_sale_paid_by_a_typed_mpesa_code_completes_at_once_with_the_code_on_it()
    {
        await SignInToShopAsync();
        var item = await ProductAsync();
        var code = NewCode();

        var sale = await SellAsync(ByCode(item, code, quantity: 2));

        Assert.Equal((PaymentMethod.MpesaReference, SaleStatus.Completed, 500m, code), (sale.PaymentMethod, sale.Status, sale.TotalAmount, sale.MpesaReference));
        Assert.StartsWith("INV-", sale.SaleNumber);
        Assert.Null(sale.AmountTendered);
        Assert.Null(sale.ChangeDue);
        Assert.Equal(18m, await OnHandAsync(item.ProductId));

        // A manager finds it by the code, and the audit log has the code to check against the M-Pesa statement.
        var found = await Run(sp => sp.GetRequiredService<SalesService>().GetSalesAsync(1, 20, new SaleQuery(Search: code[2..8].ToLowerInvariant())));
        Assert.Equal(sale.SaleId, Assert.Single(found.Items).SaleId);
        var audit = await Run(sp => sp.GetRequiredService<UltimatePosDbContext>().SystemEvents.AsNoTracking()
            .Where(e => e.EventType == SystemEventTypes.SaleMpesaReferenceRecorded && e.EntityId == sale.SaleId).ToListAsync());
        Assert.Contains(code, Assert.Single(audit).Summary);
    }

    [PostgresFact]
    public async Task A_lowercase_code_with_spaces_is_stored_without_them_in_upper_case()
    {
        await SignInToShopAsync();
        var item = await ProductAsync();
        var code = NewCode();

        var sale = await SellAsync(ByCode(item, $" {code[..4].ToLowerInvariant()} {code[4..].ToLowerInvariant()}\t"));

        Assert.Equal(code, sale.MpesaReference);
    }

    [PostgresFact]
    public async Task A_code_that_is_missing_or_not_ten_letters_and_digits_is_refused_and_nothing_is_sold()
    {
        await SignInToShopAsync();
        var item = await ProductAsync();

        foreach (var bad in new[] { null, "", "SJK4H7LM2", "SJK4H7LM2PX", "SJK4-H7LM2" })
        {
            var refused = await Assert.ThrowsAsync<ValidationException>(() => SellAsync(ByCode(item, bad)));
            Assert.Equal("Enter the 10-character M-Pesa code from the customer's SMS, e.g. SJK4H7LM2P.", refused.Message);
            Assert.Contains("mpesaReference", refused.Errors.Keys);
        }
        Assert.Equal(20m, await OnHandAsync(item.ProductId));
    }

    [PostgresFact]
    public async Task The_same_code_twice_is_refused_naming_the_sale_it_is_on()
    {
        await SignInToShopAsync();
        var item = await ProductAsync();
        var code = NewCode();
        var first = await SellAsync(ByCode(item, code));

        var refused = await Assert.ThrowsAsync<DuplicateReferenceException>(() => SellAsync(ByCode(item, code.ToLowerInvariant())));

        Assert.Equal($"M-Pesa code {code} is already on sale {first.SaleNumber}.", refused.Message);
        Assert.Equal(19m, await OnHandAsync(item.ProductId));
        // The preview does not check it: the cashier sees the total while the code is still being typed.
        var preview = await Run(sp => sp.GetRequiredService<SalesService>().PreviewSaleAsync(ByCode(item, code)));
        Assert.Equal(Price, preview.TotalAmount);
    }

    [PostgresFact]
    public async Task Two_cashiers_typing_the_same_code_at_the_same_moment_make_one_sale()
    {
        await SignInToShopAsync();
        var item = await ProductAsync();
        var code = NewCode();

        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
        {
            try { await SellAsync(ByCode(item, code)); return null; }
            catch (Exception ex) { return ex; }
        })));

        Assert.Single(results, r => r is null);
        Assert.All(results.OfType<Exception>(), ex => Assert.IsType<DuplicateReferenceException>(ex));
        Assert.Equal(19m, await OnHandAsync(item.ProductId));
    }

    [PostgresFact]
    public async Task A_code_on_a_voided_sale_can_be_rung_up_again()
    {
        await SignInToShopAsync();
        var item = await ProductAsync();
        var code = NewCode();
        var mistaken = await SellAsync(ByCode(item, code));
        await Run(sp => sp.GetRequiredService<SalesService>().VoidSaleAsync(mistaken.SaleId));

        var again = await SellAsync(ByCode(item, code));

        Assert.Equal((code, SaleStatus.Completed), (again.MpesaReference, again.Status));
        Assert.NotEqual(mistaken.SaleId, again.SaleId);
    }

    [PostgresFact]
    public async Task Other_methods_ignore_a_code_and_a_code_sale_is_refunded_by_mpesa()
    {
        await SignInToShopAsync();
        var item = await ProductAsync();

        var cash = await SellAsync(new CreateSaleRequestDto(null, PaymentMethod.Cash, null,
            new[] { new CreateSaleLineRequestDto(item.ProductId, item.Piece, 1) }, 300m, null, null, MpesaReference: NewCode()));
        Assert.Null(cash.MpesaReference);

        var sale = await SellAsync(ByCode(item, NewCode()));
        var refund = await Run(sp => sp.GetRequiredService<SalesService>().CreateReturnAsync(sale.SaleId, new CreateSaleReturnRequestDto(
            "Wrong size", RefundMethod.Mpesa, "SRF1234567", new[] { new CreateSaleReturnLineRequestDto(sale.Lines.Single().SaleLineId, 1) })));
        Assert.Equal((RefundMethod.Mpesa, Price), (refund.RefundMethod, refund.TotalAmount));
    }

    [PostgresFact]
    public async Task A_delivery_paid_by_a_typed_code_is_counted_under_mpesa_not_cash()
    {
        await SignInToShopAsync();
        var item = await ProductAsync();
        var customer = await Run(sp => sp.GetRequiredService<BusinessService>().RegisterCustomerAsync(new RegisterCustomerRequestDto(
            null, "Customer " + Guid.NewGuid().ToString("N")[..6], null, null, "0733" + Random.Shared.Next(100000, 999999), null, "Ngong Road")));
        var deliveries = (IServiceProvider sp) => sp.GetRequiredService<DeliveryOrderService>();
        var order = await Run(sp => deliveries(sp).CreateAsync(new CreateDeliveryOrderRequestDto(customer.CustomerId, "Ngong Road, gate 4", null, null,
            new[] { new DeliveryOrderLineRequestDto(item.ProductId, item.Piece, 2) })));
        await Run(sp => deliveries(sp).DispatchAsync(order.DeliveryOrderId, new DispatchDeliveryOrderRequestDto("Rider " + Guid.NewGuid().ToString("N")[..6])));
        var code = NewCode();

        // Wrong code first: refused, the order is still out.
        await Assert.ThrowsAsync<ValidationException>(() => Run(sp => deliveries(sp).DeliverAsync(order.DeliveryOrderId,
            new DeliverDeliveryOrderRequestDto(PaymentMethod.MpesaReference, MpesaReference: "SHORT"))));
        order = await Run(sp => deliveries(sp).DeliverAsync(order.DeliveryOrderId,
            new DeliverDeliveryOrderRequestDto(PaymentMethod.MpesaReference, MpesaReference: code.ToLowerInvariant())));

        Assert.Equal((DeliveryOrderStatus.Delivered, PaymentMethod.MpesaReference), (order.Status, order.PaymentMethod!.Value));
        var sale = await Run(sp => sp.GetRequiredService<SalesService>().GetSaleByIdAsync(order.SaleId!.Value));
        Assert.Equal((code, 500m), (sale.MpesaReference, sale.TotalAmount));

        var today = ReportPeriod.TodayAt(ReportPeriod.DefaultUtcOffsetMinutes);
        var summary = await Run(sp => deliveries(sp).GetSummaryAsync(today, today, ReportPeriod.DefaultUtcOffsetMinutes));
        var rider = summary.ByRider.Single(r => r.RiderName == order.RiderName);
        Assert.Equal((0m, 500m), (rider.CashCollected, rider.PaidByMpesaTill));

        // The code is now taken, for deliveries too.
        var another = await Run(sp => deliveries(sp).CreateAsync(new CreateDeliveryOrderRequestDto(customer.CustomerId, "Ngong Road, gate 4", null, null,
            new[] { new DeliveryOrderLineRequestDto(item.ProductId, item.Piece, 1) })));
        await Run(sp => deliveries(sp).DispatchAsync(another.DeliveryOrderId, new DispatchDeliveryOrderRequestDto("Rider B")));
        await Assert.ThrowsAsync<DuplicateReferenceException>(() => Run(sp => deliveries(sp).DeliverAsync(another.DeliveryOrderId,
            new DeliverDeliveryOrderRequestDto(PaymentMethod.MpesaReference, MpesaReference: code))));
    }

    // ---------------------------------------------------------------- Till and paybill on the business profile

    /// <summary>A number no other business has: 5-7 digits.</summary>
    private static string NewNumber(int digits = 6) => Random.Shared.NextInt64((long)Math.Pow(10, digits - 1) * 8, (long)Math.Pow(10, digits)).ToString();

    private async Task<BusinessDto> SaveProfileAsync(Guid shopId, string? till, string? paybill, string? shortCode)
    {
        var shop = await Run(sp => sp.GetRequiredService<BusinessService>().GetCurrentBusinessAsync());
        return await Run(sp => sp.GetRequiredService<BusinessService>().UpdateBusinessAsync(shopId, new UpdateBusinessRequestDto(
            shop.BusinessName, shop.TradingName, shop.BusinessType, shop.RegistrationNumber, shop.KraPin, shop.PhysicalAddress, shop.County,
            shop.PhoneNumber, shop.Email, shop.LogoUrl, shop.BackgroundImageUrl, shop.IsVatRegistered, shortCode, shop.MaxDiscountPercent,
            MpesaTillNumber: till, MpesaPaybillNumber: paybill)));
    }

    [PostgresFact]
    public async Task The_short_code_follows_the_till_else_the_paybill()
    {
        var shopId = await SignInToShopAsync();
        string till = NewNumber(5), paybill = NewNumber(7), ignored = NewNumber();

        var tillOnly = await SaveProfileAsync(shopId, till, null, ignored);
        Assert.Equal((till, (string?)null, till), (tillOnly.MpesaTillNumber, tillOnly.MpesaPaybillNumber, tillOnly.MpesaShortCode));

        var paybillOnly = await SaveProfileAsync(shopId, null, paybill, ignored);
        Assert.Equal(((string?)null, paybill, paybill), (paybillOnly.MpesaTillNumber, paybillOnly.MpesaPaybillNumber, paybillOnly.MpesaShortCode));

        var both = await SaveProfileAsync(shopId, $" {till} ", paybill, paybill);
        Assert.Equal((till, paybill, till), (both.MpesaTillNumber, both.MpesaPaybillNumber, both.MpesaShortCode));

        var neither = await SaveProfileAsync(shopId, null, null, null);
        Assert.Equal(((string?)null, (string?)null, (string?)null), (neither.MpesaTillNumber, neither.MpesaPaybillNumber, neither.MpesaShortCode));

        // The profile as the POS reads it.
        await SaveProfileAsync(shopId, till, paybill, till);
        var current = await Run(sp => sp.GetRequiredService<BusinessService>().GetCurrentBusinessAsync());
        Assert.Equal((till, paybill, "254722000555"), (current.MpesaTillNumber, current.MpesaPaybillNumber, current.PhoneNumber));
    }

    [PostgresFact]
    public async Task An_older_client_sending_only_the_short_code_keeps_it_as_the_till()
    {
        var shopId = await SignInToShopAsync();
        var shortCode = NewNumber(8); // the old field allowed up to 10 digits

        var saved = await SaveProfileAsync(shopId, null, null, shortCode);

        Assert.Equal((shortCode, (string?)null, shortCode), (saved.MpesaTillNumber, saved.MpesaPaybillNumber, saved.MpesaShortCode));
        // Registered the old way too: Afya was registered with only MpesaShortCode 600999.
        _db.SignInTo(_db.Afya);
        var afya = await Run(sp => sp.GetRequiredService<BusinessService>().GetCurrentBusinessAsync());
        Assert.Equal(("600999", "600999"), (afya.MpesaTillNumber, afya.MpesaShortCode));
    }

    [PostgresFact]
    public async Task A_till_or_paybill_that_is_not_five_to_seven_digits_or_is_another_businesses_is_refused()
    {
        var shopId = await SignInToShopAsync();

        var badTill = await Assert.ThrowsAsync<ValidationException>(() => SaveProfileAsync(shopId, "1234", null, null));
        Assert.Equal("A till number is 5–7 digits.", badTill.Errors["mpesaTillNumber"].Single());
        var badPaybill = await Assert.ThrowsAsync<ValidationException>(() => SaveProfileAsync(shopId, null, "12345678", null));
        Assert.Equal("A paybill number is 5–7 digits.", badPaybill.Errors["mpesaPaybillNumber"].Single());
        await Assert.ThrowsAsync<ValidationException>(() => SaveProfileAsync(shopId, "12a456", null, null));

        // Afya's 600999, as a till or as a paybill.
        await Assert.ThrowsAsync<DuplicateBusinessException>(() => SaveProfileAsync(shopId, "600999", null, null));
        await Assert.ThrowsAsync<DuplicateBusinessException>(() => SaveProfileAsync(shopId, null, "600999", null));
        await Assert.ThrowsAsync<DuplicateBusinessException>(() => SaveProfileAsync(shopId, null, null, "600999"));
    }
}
