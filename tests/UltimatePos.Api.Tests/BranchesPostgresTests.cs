using Microsoft.Extensions.DependencyInjection;
using UltimatePos.Application.Branches;
using UltimatePos.Application.Business.Dtos;
using UltimatePos.Application.Catalog;
using UltimatePos.Application.Catalog.Dtos;
using UltimatePos.Application.Costing;
using UltimatePos.Application.Identity;
using UltimatePos.Application.Identity.Dtos;
using UltimatePos.Application.Inventory;
using UltimatePos.Application.Inventory.Dtos;
using UltimatePos.Application.Platform;
using UltimatePos.Application.Sales;
using UltimatePos.Application.Sales.Dtos;
using UltimatePos.Domain.Entities;
using UltimatePos.Domain.Exceptions;
using UltimatePos.Infrastructure.Persistence;
using static UltimatePos.Domain.Enums.PosEnums;

namespace Ultimate.Api.Tests;

/// <summary>Branches: KRA numbering, per-branch stock and average cost, transfers, visibility, sign-in and the licence's branch limit.</summary>
public class BranchesPostgresTests : IClassFixture<MultiBusinessDatabase>
{
    private readonly MultiBusinessDatabase _db;
    public BranchesPostgresTests(MultiBusinessDatabase db) => _db = db;

    private static readonly string[] BranchStaff =
        { "Stock.View", "Stock.Adjust", "Sales.Create", "Sales.View", "StockTransfers.View", "StockTransfers.Receive", "StockTransfers.Dispatch" };

    // ---- Builders, run as the caller ----

    private Task<T> Run<T>(Func<IServiceProvider, Task<T>> call) => _db.RunAsync(call);
    private Task<Guid> PcsAsync() =>
        Run(async sp => (await sp.GetRequiredService<CatalogService>().GetUnitsOfMeasureAsync()).Single(u => u.Symbol == "pcs").UnitOfMeasureId);

    private async Task<ProductDto> ProductAsync(string name, decimal price, bool batchTracked = false)
    {
        var pcs = await PcsAsync();
        var code = $"B{Guid.NewGuid():N}"[..8].ToUpperInvariant();
        var category = await Run(sp => sp.GetRequiredService<CatalogService>().CreateCategoryAsync(new CreateCategoryRequestDto("Cat " + code, code, null)));
        var product = await Run(sp => sp.GetRequiredService<CatalogService>().CreateProductAsync(new CreateProductRequestDto(
            name + " " + code, null, category.CategoryId, ItemType.FinishedGood, TaxClassification.Exempt, pcs, 0, IsBatchTracked: batchTracked)));
        await Run(sp => sp.GetRequiredService<CatalogService>().AddPriceTierAsync(product.ProductId,
            new AddPriceTierRequestDto(pcs, PriceType.Retail, price, DateTime.UtcNow.AddMinutes(-1))));
        return product;
    }

    private async Task AdjustAsync(Guid productId, decimal quantity, decimal? cost, string? batch = null, DateOnly? expiry = null)
    {
        var pcs = await PcsAsync();
        await Run(sp => sp.GetRequiredService<InventoryService>().AdjustStockAsync(
            new CreateStockAdjustmentRequestDto(productId, pcs, quantity, "Opening", cost, BatchNumber: batch, ExpiryDate: expiry)));
    }

    private Task<BranchDto> NewBranchAsync(string name, int? number = null) =>
        Run(sp => sp.GetRequiredService<BranchService>().CreateBranchAsync(new CreateBranchRequestDto(name, null, number, null, null, null, null, null)));

    private Task<List<StockLevelDto>> StockAsync(Guid productId) =>
        Run(async sp => (await sp.GetRequiredService<InventoryService>().GetStockLevelsAsync(false)).Where(s => s.ProductId == productId).ToList());

    private async Task<StockTransferDto> TransferAsync(Guid from, Guid to, Guid productId, decimal quantity)
    {
        var pcs = await PcsAsync();
        return await Run(sp => sp.GetRequiredService<StockTransferService>().CreateAsync(new CreateStockTransferRequestDto(
            from, to, null, new[] { new CreateStockTransferLineRequestDto(productId, pcs, quantity) })));
    }

    // ---------------------------------------------------------------- Numbering (KRA eTIMS: 00, 01, ...)

    [PostgresFact]
    public async Task A_business_starts_with_head_office_00_and_branches_take_the_next_two_digit_number()
    {
        _db.SignInTo(_db.Afya);
        var branches = await Run(sp => sp.GetRequiredService<BranchService>().GetBranchesAsync());
        var head = Assert.Single(branches, b => b.IsHeadOffice);
        Assert.Equal((0, "00"), (head.BranchNumber, head.KraBranchId));

        var next = await NewBranchAsync("Westlands");
        Assert.Equal(branches.Max(b => b.BranchNumber) + 1, next.BranchNumber);
        Assert.Equal(next.BranchNumber.ToString("00"), next.KraBranchId);

        var chosen = await NewBranchAsync("Kisumu", 42);
        Assert.Equal("42", chosen.KraBranchId);

        var taken = await Assert.ThrowsAsync<ValidationException>(() => NewBranchAsync("Again", 42));
        Assert.Contains("branchNumber", taken.Errors.Keys);
        await Assert.ThrowsAsync<ValidationException>(() => NewBranchAsync("Second head office", 0));
        await Assert.ThrowsAsync<ValidationException>(() => NewBranchAsync("Too far", 100));

        await Assert.ThrowsAsync<InvalidAssignmentException>(() =>
            Run(sp => sp.GetRequiredService<BranchService>().SetBranchActiveAsync(head.BranchId, false)));
    }

    // ---------------------------------------------------------------- Stock and cost per branch

    [PostgresFact]
    public async Task Each_branch_has_its_own_stock_and_average_cost_and_a_transfer_carries_the_cost()
    {
        _db.SignInTo(_db.Alvet);
        var head = _db.HeadOffice[_db.Alvet];
        var town = await NewBranchAsync("Town");
        var product = await ProductAsync("Dewormer", 100);
        await AdjustAsync(product.ProductId, 10, 20); // head office: 10 @ 20

        var transfer = await TransferAsync(head, town.BranchId, product.ProductId, 4);
        Assert.Equal(StockTransferStatus.Draft, transfer.Status);
        Assert.StartsWith("TRF-", transfer.TransferNumber);
        Assert.Equal(10, (await StockAsync(product.ProductId)).Single(s => s.BranchId == head).QuantityOnHand); // a draft moves nothing

        transfer = await Run(sp => sp.GetRequiredService<StockTransferService>().DispatchAsync(transfer.StockTransferId));
        Assert.Equal(StockTransferStatus.Dispatched, transfer.Status);
        Assert.Equal(20, transfer.Lines.Single().UnitCost);
        Assert.Equal(80, transfer.DispatchedValue);
        var stock = await StockAsync(product.ProductId);
        Assert.Equal(6, stock.Single(s => s.BranchId == head).QuantityOnHand);
        Assert.DoesNotContain(stock, s => s.BranchId == town.BranchId); // in transit

        // Only 3 of the 4 arrive.
        var line = transfer.Lines.Single();
        transfer = await Run(sp => sp.GetRequiredService<StockTransferService>().ReceiveAsync(transfer.StockTransferId,
            new ReceiveStockTransferRequestDto(new[] { new ReceiveStockTransferLineRequestDto(line.StockTransferLineId, 3) }, "One box crushed")));
        Assert.Equal(StockTransferStatus.Received, transfer.Status);
        Assert.Equal((3m, 1m), (transfer.Lines.Single().QuantityReceivedInBaseUnits!.Value, transfer.Lines.Single().ShortageInBaseUnits));
        Assert.Equal(20, transfer.ShortageValue);

        // The town branch buys 3 more at 30: its average is (3x20 + 3x30) / 6 = 25. The head office stays at 20.
        _db.SignInTo(_db.Alvet, town.BranchId);
        await AdjustAsync(product.ProductId, 3, 30);
        var costs = await Run(sp => sp.GetRequiredService<IInventoryRepository>().GetAverageCostsByBranchAsync(new[] { product.ProductId }));
        Assert.Equal(25, costs[(product.ProductId, town.BranchId)]);
        Assert.Equal(20, costs[(product.ProductId, head)]);

        // Valued across both branches: 6 x 20 + 6 x 25 = 270; the average shown is the value over the quantity.
        var valuation = await Run(sp => sp.GetRequiredService<CostingService>().GetStockValuationAsync(null));
        var row = valuation.Items.Single(r => r.ProductId == product.ProductId);
        Assert.Equal((12m, 270m, 22.5m), (row.QuantityOnHand, row.StockValue, row.AverageUnitCost));

        // A sale at the town branch takes stock from the town branch only, at the town's cost.
        var pcs = await PcsAsync();
        var sale = await Run(sp => sp.GetRequiredService<SalesService>().RecordSaleAsync(new CreateSaleRequestDto(
            null, PaymentMethod.Cash, null, new[] { new CreateSaleLineRequestDto(product.ProductId, pcs, 2) }, 200, null, null)));
        Assert.Equal(town.BranchId, sale.BranchId);
        Assert.Equal(town.KraBranchId, sale.KraBranchId);
        stock = await StockAsync(product.ProductId);
        Assert.Equal((6m, 4m), (stock.Single(s => s.BranchId == head).QuantityOnHand, stock.Single(s => s.BranchId == town.BranchId).QuantityOnHand));
    }

    [PostgresFact]
    public async Task A_transfer_of_a_batch_tracked_product_moves_the_lots_with_their_expiry()
    {
        _db.SignInTo(_db.Alvet);
        var head = _db.HeadOffice[_db.Alvet];
        var depot = await NewBranchAsync("Depot");
        var vaccine = await ProductAsync("Vaccine", 500, batchTracked: true);
        var soon = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(3);
        var later = soon.AddMonths(6);
        await AdjustAsync(vaccine.ProductId, 5, 100, "LOT-A", soon);
        await AdjustAsync(vaccine.ProductId, 5, 100, "LOT-B", later);

        var transfer = await TransferAsync(head, depot.BranchId, vaccine.ProductId, 7);
        transfer = await Run(sp => sp.GetRequiredService<StockTransferService>().DispatchAsync(transfer.StockTransferId));
        Assert.Equal(new[] { ("LOT-A", 5m), ("LOT-B", 2m) },
            transfer.Lines.Single().Batches.Select(b => (b.BatchNumber, b.QuantityInBaseUnits)).ToArray()); // first expiry first out
        await Run(sp => sp.GetRequiredService<StockTransferService>().ReceiveAsync(transfer.StockTransferId, new ReceiveStockTransferRequestDto(null, null)));

        var lots = await Run(async sp => (await sp.GetRequiredService<InventoryService>().GetBatchesAsync(vaccine.ProductId, null, true, false)).ToList());
        Assert.Equal(new[] { ("LOT-A", soon, 5m), ("LOT-B", later, 2m) },
            lots.Where(l => l.BranchId == depot.BranchId).OrderBy(l => l.BatchNumber).Select(l => (l.BatchNumber, l.ExpiryDate!.Value, l.QuantityOnHand)).ToArray());
        Assert.Equal(3, lots.Single(l => l.BranchId == head && l.BatchNumber == "LOT-B").QuantityOnHand);

        // The depot now holds stock, so it can't be closed until that stock goes somewhere.
        await Assert.ThrowsAsync<InvalidAssignmentException>(() =>
            Run(sp => sp.GetRequiredService<BranchService>().SetBranchActiveAsync(depot.BranchId, false)));
    }

    [PostgresFact]
    public async Task A_transfer_cannot_send_more_than_the_branch_holds_and_only_the_receiving_branch_can_receive()
    {
        _db.SignInTo(_db.Alvet);
        var head = _db.HeadOffice[_db.Alvet];
        var shop = await NewBranchAsync("Shop");
        var product = await ProductAsync("Spray", 50);
        await AdjustAsync(product.ProductId, 2, 10);

        var tooMuch = await TransferAsync(head, shop.BranchId, product.ProductId, 5);
        await Assert.ThrowsAsync<InsufficientStockException>(() =>
            Run(sp => sp.GetRequiredService<StockTransferService>().DispatchAsync(tooMuch.StockTransferId)));
        var cancelled = await Run(sp => sp.GetRequiredService<StockTransferService>().CancelAsync(tooMuch.StockTransferId));
        Assert.Equal(StockTransferStatus.Cancelled, cancelled.Status);

        var ok = await TransferAsync(head, shop.BranchId, product.ProductId, 1);
        await Run(sp => sp.GetRequiredService<StockTransferService>().DispatchAsync(ok.StockTransferId));

        // Head-office staff (no Branches.AccessAll) can see the transfer they sent, but not receive it at the shop.
        _db.SignInTo(_db.Alvet, head, BranchStaff);
        await Assert.ThrowsAsync<ForbiddenException>(() => Run(sp => sp.GetRequiredService<StockTransferService>().ReceiveAsync(
            ok.StockTransferId, new ReceiveStockTransferRequestDto(null, null))));
        _db.SignInTo(_db.Alvet, shop.BranchId, BranchStaff);
        var received = await Run(sp => sp.GetRequiredService<StockTransferService>().ReceiveAsync(ok.StockTransferId, new ReceiveStockTransferRequestDto(null, null)));
        Assert.Equal(StockTransferStatus.Received, received.Status);
    }

    // ---------------------------------------------------------------- Who sees what

    [PostgresFact]
    public async Task Branch_staff_see_only_their_branch_and_managers_can_narrow_to_one()
    {
        _db.SignInTo(_db.Alvet);
        var head = _db.HeadOffice[_db.Alvet];
        var mall = await NewBranchAsync("Mall");
        var product = await ProductAsync("Collar", 80);
        await AdjustAsync(product.ProductId, 10, 30);
        var transfer = await TransferAsync(head, mall.BranchId, product.ProductId, 4);
        await Run(sp => sp.GetRequiredService<StockTransferService>().DispatchAsync(transfer.StockTransferId));
        await Run(sp => sp.GetRequiredService<StockTransferService>().ReceiveAsync(transfer.StockTransferId, new ReceiveStockTransferRequestDto(null, null)));

        // A manager (Branches.AccessAll) sees both branches.
        Assert.Equal(2, (await StockAsync(product.ProductId)).Count);

        // Narrowed to one branch (what ?branchId= does), the list and the single-product read are that branch's.
        var narrowed = await Run(async sp =>
        {
            using (sp.GetRequiredService<UltimatePos.Application.Common.Interfaces.IBranchContext>().Narrow(mall.BranchId))
                return (List: (await sp.GetRequiredService<InventoryService>().GetStockLevelsAsync(false)).Where(s => s.ProductId == product.ProductId).ToList(),
                        One: await sp.GetRequiredService<InventoryService>().GetStockLevelAsync(product.ProductId));
        });
        Assert.Equal(mall.BranchId, Assert.Single(narrowed.List).BranchId);
        Assert.Equal((mall.BranchId, 4m), (narrowed.One.BranchId, narrowed.One.QuantityOnHand));

        // Mall staff see only the mall: its stock, its sales.
        _db.SignInTo(_db.Alvet, mall.BranchId, BranchStaff);
        var stock = await StockAsync(product.ProductId);
        Assert.Equal(mall.BranchId, Assert.Single(stock).BranchId);
        var pcs = await PcsAsync();
        var sale = await Run(sp => sp.GetRequiredService<SalesService>().RecordSaleAsync(new CreateSaleRequestDto(
            null, PaymentMethod.Cash, null, new[] { new CreateSaleLineRequestDto(product.ProductId, pcs, 1) }, 80, null, null)));
        var sales = await Run(sp => sp.GetRequiredService<SalesService>().GetSalesAsync(1, 100, new SaleQuery()));
        Assert.All(sales.Items, s => Assert.Equal(mall.BranchId, s.BranchId));
        Assert.Contains(sales.Items, s => s.SaleId == sale.SaleId);

        // And can't see another branch at all.
        await Assert.ThrowsAsync<NotFoundException>(() => Run(sp => sp.GetRequiredService<BranchService>().GetBranchAsync(head)));
    }

    [PostgresFact]
    public async Task Restricted_staff_can_name_every_open_branch_and_till_payments_carry_their_branch()
    {
        _db.SignInTo(_db.Alvet);
        var head = _db.HeadOffice[_db.Alvet];
        var till = $"7{Random.Shared.Next(10000, 99999)}";
        var kiosk = await Run(sp => sp.GetRequiredService<BranchService>().CreateBranchAsync(
            new CreateBranchRequestDto("Kiosk", null, null, null, null, null, null, till)));
        var open = (await Run(sp => sp.GetRequiredService<BranchService>().GetBranchesAsync())).Count();

        // Kiosk staff see only the kiosk in GET /branches, but the directory names every open branch (for the transfer "To" list).
        _db.SignInTo(_db.Alvet, kiosk.BranchId, BranchStaff);
        Assert.Equal(kiosk.BranchId, Assert.Single(await Run(sp => sp.GetRequiredService<BranchService>().GetBranchesAsync())).BranchId);
        var directory = (await Run(sp => sp.GetRequiredService<BranchService>().GetDirectoryAsync())).ToList();
        Assert.Equal(open, directory.Count);
        Assert.Equal(new[] { kiosk.BranchId }, directory.Where(b => b.IsMine).Select(b => b.BranchId));
        Assert.Contains(directory, b => b.BranchId == head && b.IsHeadOffice && b.KraBranchId == "00");

        // The licence page counts every open branch, whoever looks.
        var license = await Run(sp => sp.GetRequiredService<UltimatePos.Application.Business.BusinessService>().GetCurrentLicenseAsync());
        Assert.Equal(open, license.OpenBranches);

        // A payment to the kiosk's own till belongs to the kiosk, and says so in the unmatched list.
        var receipt = "BR" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        _db.Caller.UserId = null;
        _db.Caller.BusinessId = null;
        _db.Caller.BranchId = null;
        await _db.RunAsync(sp => sp.GetRequiredService<UltimatePos.Application.Payments.MpesaService>().HandleC2bConfirmationAsync(
            new UltimatePos.Application.Payments.Dtos.C2bConfirmationPayload(receipt, "20261019101500", "120.00", till, null, "254700000001", null)));
        _db.SignInTo(_db.Alvet, kiosk.BranchId, BranchStaff);
        var unmatched = await Run(sp => sp.GetRequiredService<UltimatePos.Application.Payments.MpesaService>().GetUnmatchedTillTransactionsAsync());
        var payment = Assert.Single(unmatched, t => t.MpesaReceiptNumber == receipt);
        Assert.Equal((kiosk.BranchId, kiosk.KraBranchId, "Kiosk"), (payment.BranchId!.Value, payment.KraBranchId, payment.BranchName));
    }

    // ---------------------------------------------------------------- Sign-in

    [PostgresFact]
    public async Task Sign_in_picks_the_default_branch_and_a_user_can_switch_to_another_they_work_at()
    {
        _db.SignInTo(_db.Afya);
        var head = _db.HeadOffice[_db.Afya];
        var annex = await NewBranchAsync("Annex");

        var login = await Run(sp => sp.GetRequiredService<AuthService>().LoginAsync(new LoginRequest(DbSeeder.DevAdminEmail, DbSeeder.DevAdminPassword, _db.Afya)));
        Assert.Equal(head, login.ActiveBranchId);
        Assert.Contains(login.Branches!, b => b.BranchId == annex.BranchId); // the owner (Branches.AccessAll) may use every branch

        _db.Caller.UserId = login.User.Id;
        _db.Caller.BusinessId = _db.Afya;
        _db.Caller.BranchId = head;
        var switched = await Run(sp => sp.GetRequiredService<AuthService>().SwitchBranchAsync(new SwitchBranchRequest(annex.BranchId, login.RefreshToken)));
        Assert.Equal(annex.BranchId, switched.ActiveBranchId);

        // A cashier assigned to the annex only signs in there, and can't switch to the head office.
        var email = $"cashier-{Guid.NewGuid():N}@t.test";
        _db.SignInTo(_db.Afya);
        var cashierRole = await Run(sp => sp.GetRequiredService<AuthService>().CreateRoleAsync(new CreateRoleRequestDto("Cashier " + Guid.NewGuid().ToString("N")[..6], null)));
        var cashier = await Run(sp => sp.GetRequiredService<AuthService>().RegisterAsync(
            new RegisterRequest(email, "Pass1234!", "Cashier", "0700", cashierRole.RoleName, new[] { annex.BranchId })));
        var branches = await Run(sp => sp.GetRequiredService<BranchService>().GetUserBranchesAsync(cashier.Id));
        Assert.Equal((false, annex.BranchId), (branches.AccessAll, Assert.Single(branches.Branches).BranchId));

        var cashierLogin = await Run(sp => sp.GetRequiredService<AuthService>().LoginAsync(new LoginRequest(email, "Pass1234!")));
        Assert.Equal(annex.BranchId, cashierLogin.ActiveBranchId);
        _db.Caller.UserId = cashier.Id;
        _db.Caller.BusinessId = _db.Afya;
        _db.Caller.BranchId = annex.BranchId;
        _db.Caller.BranchIds = new List<Guid> { annex.BranchId };
        _db.Caller.Permissions = new HashSet<string>();
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            Run(sp => sp.GetRequiredService<AuthService>().SwitchBranchAsync(new SwitchBranchRequest(head, null))));

        // Unassigned from every branch, they can't sign in to the business until given one.
        _db.SignInTo(_db.Afya);
        await Run(sp => sp.GetRequiredService<BranchService>().AssignUserBranchesAsync(cashier.Id, new AssignUserBranchesRequestDto(Array.Empty<Guid>(), null)));
        var refused = await Assert.ThrowsAsync<BusinessAccessDeniedException>(() =>
            Run(sp => sp.GetRequiredService<AuthService>().LoginAsync(new LoginRequest(email, "Pass1234!"))));
        Assert.Equal("NO_BRANCH", refused.Code);
    }

    // ---------------------------------------------------------------- Licence limit

    [PostgresFact]
    public async Task The_licence_branch_limit_stops_new_branches_and_reopening_closed_ones()
    {
        _db.Caller.UserId = _db.SuperUserId;
        _db.Caller.BusinessId = null;
        _db.Caller.BranchId = null;
        _db.Caller.Permissions = null;
        var registered = await Run(sp => sp.GetRequiredService<PlatformService>().RegisterBusinessAsync(new RegisterBusinessRequestDto(
            "Two Branch Vet " + Guid.NewGuid().ToString("N")[..6], null, BusinessType.Retailer, BusinessKind.Trader, null,
            $"P{Random.Shared.Next(100000000, 999999999)}Z", null, null, null, null, null, null,
            Owner: new BusinessOwnerRequestDto("admin", DbSeeder.DevAdminEmail, null))));
        var businessId = registered.Business.BusinessId;
        // The limit is the licence in force today's: replace the (unlimited) trial.
        await Run(sp => sp.GetRequiredService<PlatformService>().CancelLicenseAsync(registered.TrialLicense!.BusinessLicenseId, new ReasonRequestDto("Replaced")));
        var license = await Run(sp => sp.GetRequiredService<PlatformService>().IssueLicenseAsync(businessId, new IssueLicenseRequestDto(
            LicenseKind.Paid, "Two branches", 12, null, null, null, 12000, SettlementMethod.Cash, null, null, MaxBranches: 2)));
        Assert.Equal(2, license.MaxBranches);

        var headOffice = await Run(sp => sp.GetRequiredService<IBranchRepository>().GetHeadOfficeAsync(businessId));
        _db.SignInTo(businessId, headOffice!.BranchId);
        var second = await NewBranchAsync("Second");
        var full = await Assert.ThrowsAsync<BusinessAccessDeniedException>(() => NewBranchAsync("Third"));
        Assert.Equal("BRANCH_LIMIT_REACHED", full.Code);

        // Closing one frees the place; reopening it while another is open is refused again.
        await Run(sp => sp.GetRequiredService<BranchService>().SetBranchActiveAsync(second.BranchId, false));
        var third = await NewBranchAsync("Third");
        Assert.Equal("02", third.KraBranchId); // numbers are never reused
        var reopen = await Assert.ThrowsAsync<BusinessAccessDeniedException>(() =>
            Run(sp => sp.GetRequiredService<BranchService>().SetBranchActiveAsync(second.BranchId, true)));
        Assert.Equal("BRANCH_LIMIT_REACHED", reopen.Code);
    }
}
