using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using UltimatePos.Application;
using UltimatePos.Application.Common.Interfaces;
using UltimatePos.Application.Dashboard;
using UltimatePos.Application.Reports;
using UltimatePos.Domain.Entities;
using UltimatePos.Infrastructure;
using UltimatePos.Infrastructure.Persistence;
using Xunit.Abstractions;
using static UltimatePos.Domain.Enums.PosEnums;

namespace Ultimate.Api.Tests;

/// <summary>
/// Runs only when ULTIMATEPOS_TEST_POSTGRES holds a connection string to a PostgreSQL server the tests may create and drop
/// databases on, e.g. Host=localhost;Port=5432;Username=postgres;Password=...  The database named in it is ignored.
/// </summary>
public sealed class PostgresFactAttribute : FactAttribute
{
    public const string ConnectionStringVariable = "ULTIMATEPOS_TEST_POSTGRES";

    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionStringVariable)))
            Skip = $"Set {ConnectionStringVariable} to a PostgreSQL connection string to run the database tests.";
    }
}

/// <summary>
/// A throwaway database built the way a real one is (every migration, then DbSeeder), with the production DI wiring and a
/// small business's October 2026 on top. Every SQL command is captured so the tests can show, and check, what EF sent.
/// Set ULTIMATEPOS_TEST_POSTGRES_KEEP=1 to keep the database afterwards (to point the API at it).
/// </summary>
public sealed class ReportsDatabase : IAsyncLifetime
{
    public ConcurrentQueue<string> Sql { get; } = new();
    public ServiceProvider Services { get; private set; } = null!;
    public string ConnectionString { get; private set; } = string.Empty;
    public ReportsSeed Seed { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var server = Environment.GetEnvironmentVariable(PostgresFactAttribute.ConnectionStringVariable);
        if (string.IsNullOrWhiteSpace(server))
            return; // every test in the class is skipped

        ConnectionString = new NpgsqlConnectionStringBuilder(server) { Database = $"ultimatepos_reports_{Guid.NewGuid():N}" }.ConnectionString;
        Services = BuildServices(ConnectionString, Sql);

        using (var seedScope = Services.CreateScope())
            await DbSeeder.SeedAsync(seedScope.ServiceProvider); // migrates, then permissions, Platform Admin and the admin user

        ReportsSeed.Active = true; // from here on every scope is signed in to the test business
        using var scope = Services.CreateScope();
        Seed = await ReportsSeed.WriteAsync(scope.ServiceProvider.GetRequiredService<UltimatePosDbContext>());
        Sql.Clear();
    }

    public async Task DisposeAsync()
    {
        if (Services is null)
            return;

        if (Environment.GetEnvironmentVariable("ULTIMATEPOS_TEST_POSTGRES_KEEP") != "1")
        {
            using var scope = Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<UltimatePosDbContext>().Database.EnsureDeletedAsync();
        }

        await Services.DisposeAsync();
    }

    public static ServiceProvider BuildServices(string connectionString, ConcurrentQueue<string>? sql = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = connectionString,
                ["Mpesa:BaseUrl"] = "https://localhost",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplication();
        services.AddInfrastructure(configuration);
        services.AddScoped<ICurrentUser, TestCurrentUser>();

        // Same provider and connection as production, plus a log of every command executed.
        services.RemoveAll<DbContextOptions<UltimatePosDbContext>>();
        services.AddSingleton(new DbContextOptionsBuilder<UltimatePosDbContext>()
            .UseNpgsql(connectionString)
            .LogTo(message => sql?.Enqueue(message), new[] { RelationalEventId.CommandExecuted })
            .Options);

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// A caller signed in to <see cref="ReportsSeed.BusinessId"/> with every permission — once the seed has created it. Until then
    /// (migrating, DbSeeder) no business is in scope, as for system code in production.
    /// </summary>
    private sealed class TestCurrentUser : ICurrentUser
    {
        public Guid? UserId => null;
        public bool IsAuthenticated => true;
        public Guid? BusinessId => ReportsSeed.Active ? ReportsSeed.BusinessId : null;
        public Guid? BranchId => ReportsSeed.Active ? ReportsSeed.HeadOfficeId : null;
        public IReadOnlyList<Guid> BranchIds => ReportsSeed.Active ? new[] { ReportsSeed.HeadOfficeId } : Array.Empty<Guid>();
        public bool HasPermission(string permission) => true;
    }
}

/// <summary>
/// One small flour mill's October 2026, chosen so every figure below can be worked out by hand. Times are UTC; the business
/// is on East Africa Time (UTC+3), so 21:00 UTC is already the next local day.
/// </summary>
public sealed class ReportsSeed
{
    public Guid AdminUserId, Maize, Unga, Salt, Bags, Sugar, Yeast, Kg, Bag, MillSupplies, AgroTraders, Wanjiku, Otieno, Kamau;
    public Guid SaleCredit, SaleOldCredit;

    /// <summary>The seeded business. Fixed, so the test current user can be signed in to it from any scope.</summary>
    public static readonly Guid BusinessId = Guid.Parse("7e57b00c-0000-4000-8000-000000000001");
    /// <summary>Its head office: everything in the seed happens there (the caller's active branch stamps it).</summary>
    public static readonly Guid HeadOfficeId = Guid.Parse("7e57b00c-0000-4000-8000-0000000000b0");
    public static volatile bool Active;

    public static DateTime Utc(int month, int day, int hour = 9, int minute = 0, int second = 0) =>
        new(2026, month, day, hour, minute, second, DateTimeKind.Utc);

    public static async Task<ReportsSeed> WriteAsync(UltimatePosDbContext db)
    {
        var s = new ReportsSeed { AdminUserId = (await db.Users.SingleAsync(u => u.Username == "admin")).UserId };

        var business = new BusinessProfile { BusinessId = BusinessId, BusinessName = "Test Mill", KraPin = "P000000001X" };
        db.Add(new Branch { BranchId = HeadOfficeId, BusinessId = BusinessId, BranchNumber = 0, Name = "Head Office" });
        var category = new Category { CategoryId = Guid.NewGuid(), Name = "Flour", Code = "FLR" };
        var kg = new UnitOfMeasure { UnitOfMeasureId = s.Kg = Guid.NewGuid(), Name = "Kilogram", Symbol = "kg" };
        var bag = new UnitOfMeasure { UnitOfMeasureId = s.Bag = Guid.NewGuid(), Name = "50kg bag", Symbol = "bag50" };
        db.AddRange(business, category, kg, bag);

        Product NewProduct(string sku, string name, ItemType type, decimal reorder, bool active = true) => new()
        {
            ProductId = Guid.NewGuid(), Sku = sku, Name = name, CategoryId = category.CategoryId, ItemType = type,
            BaseUnitOfMeasureId = kg.UnitOfMeasureId, ReorderLevel = reorder, IsActive = active,
        };
        var maize = NewProduct("MAZ", "Maize", ItemType.RawMaterial, 100);
        var unga = NewProduct("UNG", "Unga", ItemType.FinishedGood, 50);
        var salt = NewProduct("SLT", "Salt", ItemType.RawMaterial, 0);
        var bags = NewProduct("BAG", "Bags", ItemType.Packaging, 20);
        var sugar = NewProduct("SUG", "Sugar", ItemType.RawMaterial, 50, active: false);
        var yeast = NewProduct("YST", "Yeast", ItemType.RawMaterial, 5); // never stocked: no stock record
        (s.Maize, s.Unga, s.Salt, s.Bags, s.Sugar, s.Yeast) = (maize.ProductId, unga.ProductId, salt.ProductId, bags.ProductId, sugar.ProductId, yeast.ProductId);
        db.AddRange(maize, unga, salt, bags, sugar, yeast);

        // Now: Maize exactly at its reorder level (low), Unga at zero and Bags oversold (out), Salt fine but never costed,
        // Sugar would be low but is deactivated.
        db.StockLevels.AddRange(
            new StockLevel { ProductId = s.Maize, QuantityOnHand = 100, AverageUnitCost = 40 },
            new StockLevel { ProductId = s.Unga, QuantityOnHand = 0, AverageUnitCost = 60 },
            new StockLevel { ProductId = s.Salt, QuantityOnHand = 10, AverageUnitCost = 0 },
            new StockLevel { ProductId = s.Bags, QuantityOnHand = -2, AverageUnitCost = 15 },
            new StockLevel { ProductId = s.Sugar, QuantityOnHand = 50, AverageUnitCost = 100 });

        StockMovement Move(Guid product, StockMovementType type, decimal qty, decimal? cost, DateTime at, string? notes = null, Guid? by = null) => new()
        {
            StockMovementId = Guid.NewGuid(), ProductId = product, MovementType = type, QuantityChange = qty, UnitCost = cost,
            ReferenceType = "Test", ReferenceId = Guid.NewGuid(), BatchId = Guid.NewGuid(), Notes = notes, CreatedAt = at, CreatedBy = by,
        };
        db.StockMovements.AddRange(
            Move(s.Maize, StockMovementType.PurchaseReceipt, 55, 40, Utc(9, 30, 20, 59, 59)),   // 23:59:59 on 30 Sep local: opening
            Move(s.Maize, StockMovementType.PurchaseReceipt, 200, 40, Utc(10, 2, 7)),
            Move(s.Maize, StockMovementType.ProductionConsumption, -150, 40, Utc(10, 5, 8)),
            Move(s.Maize, StockMovementType.Adjustment, -10, 40, Utc(10, 6), "Spillage", s.AdminUserId),
            Move(s.Maize, StockMovementType.Adjustment, 5, null, Utc(10, 7), "Recount"),
            Move(s.Maize, StockMovementType.Revaluation, 0, 45, Utc(10, 8)),
            Move(s.Unga, StockMovementType.Adjustment, 30, 60, Utc(9, 30, 21)),                // midnight 1 Oct local: in October
            Move(s.Unga, StockMovementType.ProductionOutput, 100, 65, Utc(10, 5, 8)),
            Move(s.Unga, StockMovementType.Sale, -140, 62, Utc(10, 10, 10)),
            Move(s.Unga, StockMovementType.SaleVoid, 10, 62, Utc(10, 11, 10)),
            Move(s.Unga, StockMovementType.Sale, -5, 62, Utc(10, 31, 21, 30)));                // 00:30 on 1 Nov local: not October

        var wanjiku = new Customer { CustomerId = s.Wanjiku = Guid.NewGuid(), BusinessId = business.BusinessId, CustomerName = "Wanjiku", PhoneNumber = "0712000001" };
        var otieno = new Customer { CustomerId = s.Otieno = Guid.NewGuid(), BusinessId = business.BusinessId, CustomerName = "Otieno" };
        var kamau = new Customer { CustomerId = s.Kamau = Guid.NewGuid(), BusinessId = business.BusinessId, CustomerName = "Kamau" };
        db.AddRange(wanjiku, otieno, kamau);

        Sale NewSale(string number, SaleStatus status, PaymentMethod method, decimal total, DateTime at, Guid? customer = null) => new()
        {
            SaleId = Guid.NewGuid(), SaleNumber = number, Status = status, PaymentMethod = method, TotalAmount = total, CreatedAt = at, CustomerId = customer,
        };
        var s1 = NewSale("S-0001", SaleStatus.Completed, PaymentMethod.Cash, 1000, Utc(10, 3));
        var s2 = NewSale("S-0002", SaleStatus.Completed, PaymentMethod.Credit, 2000, Utc(10, 4), s.Wanjiku);
        var s3 = NewSale("S-0003", SaleStatus.Voided, PaymentMethod.MpesaTill, 500, Utc(10, 4, 10));
        var s4 = NewSale("S-0004", SaleStatus.Pending, PaymentMethod.Cash, 300, Utc(10, 5));
        var s5 = NewSale("S-0005", SaleStatus.Failed, PaymentMethod.Cash, 999, Utc(10, 5));
        var s6 = NewSale("S-0006", SaleStatus.Completed, PaymentMethod.Cash, 700, Utc(10, 31, 21, 30));   // 1 Nov local
        var s7 = NewSale("S-0007", SaleStatus.Completed, PaymentMethod.Cash, 400, Utc(9, 30, 21));        // 1 Oct local
        var s8 = NewSale("S-0008", SaleStatus.Completed, PaymentMethod.Credit, 1200, Utc(7, 1), s.Wanjiku);
        (s.SaleCredit, s.SaleOldCredit) = (s2.SaleId, s8.SaleId);
        db.Sales.AddRange(s1, s2, s3, s4, s5, s6, s7, s8);

        SaleLine Line(Sale sale, Guid product, decimal qty, decimal price, decimal? cost) => new()
        {
            SaleLineId = Guid.NewGuid(), SaleId = sale.SaleId, ProductId = product, UnitOfMeasureId = kg.UnitOfMeasureId, Quantity = qty,
            QuantityInBaseUnits = qty, UnitPrice = price, UnitCost = cost, LineTotal = qty * price, CreatedAt = sale.CreatedAt,
        };
        db.SaleLines.AddRange(Line(s1, s.Unga, 10, 100, 62), Line(s2, s.Unga, 20, 100, 62), Line(s7, s.Maize, 8, 50, null),
            Line(s6, s.Unga, 7, 100, 62), Line(s8, s.Unga, 12, 100, 62));

        CustomerLedgerEntry Entry(Guid customer, CustomerLedgerEntryType type, decimal amount, DateTime at, Guid? reference = null) => new()
        {
            CustomerLedgerEntryId = Guid.NewGuid(), CustomerId = customer, EntryType = type, Amount = amount,
            ReferenceType = type == CustomerLedgerEntryType.Invoice ? "Sale" : "Payment", ReferenceId = reference ?? Guid.NewGuid(), CreatedAt = at,
        };
        db.CustomerLedgerEntries.AddRange(
            Entry(s.Wanjiku, CustomerLedgerEntryType.Invoice, 1200, s8.CreatedAt, s8.SaleId),
            Entry(s.Wanjiku, CustomerLedgerEntryType.Invoice, 2000, s2.CreatedAt, s2.SaleId),
            Entry(s.Wanjiku, CustomerLedgerEntryType.Payment, 500, Utc(10, 20)),
            Entry(s.Otieno, CustomerLedgerEntryType.Payment, 300, Utc(10, 2)));
        db.CustomerBalances.AddRange(
            new CustomerBalance { CustomerId = s.Wanjiku, OutstandingBalance = 2700 },
            new CustomerBalance { CustomerId = s.Otieno, OutstandingBalance = -300 },
            new CustomerBalance { CustomerId = s.Kamau, OutstandingBalance = 0 });

        var formula = new Formula { FormulaId = Guid.NewGuid(), ProductId = s.Unga, VersionNumber = 1, OutputQuantity = 100, OutputUnitOfMeasureId = kg.UnitOfMeasureId };
        db.Formulas.Add(formula);

        void Run(string number, DateTime at, Guid unit, decimal planned, decimal produced, decimal maizeUsed, decimal material,
            params (ProductionCostType Type, decimal Amount)[] costs)
        {
            var additional = costs.Sum(c => c.Amount);
            var run = new ProductionRun
            {
                ProductionRunId = Guid.NewGuid(), RunNumber = number, FormulaId = formula.FormulaId, ProductId = s.Unga, UnitOfMeasureId = unit,
                QuantityPlanned = planned, QuantityProduced = produced, MaterialCost = material, AdditionalCost = additional,
                TotalCost = material + additional, UnitCost = produced == 0 ? 0 : (material + additional) / produced, CreatedAt = at,
            };
            db.ProductionRuns.Add(run);
            db.ProductionRunInputs.Add(new ProductionRunInput
            {
                ProductionRunInputId = Guid.NewGuid(), ProductionRunId = run.ProductionRunId, IngredientProductId = s.Maize, UnitOfMeasureId = kg.UnitOfMeasureId,
                QuantityConsumed = maizeUsed, QuantityConsumedInBaseUnits = maizeUsed, UnitCost = material / maizeUsed, TotalCost = material, CreatedAt = at,
            });
            db.ProductionRunCosts.AddRange(costs.Select(c => new ProductionRunCost
            {
                ProductionRunCostId = Guid.NewGuid(), ProductionRunId = run.ProductionRunId, CostType = c.Type, Amount = c.Amount, CreatedAt = at,
            }));
        }
        Run("RUN-000", Utc(9, 25), kg.UnitOfMeasureId, 10, 10, 10, 400, (ProductionCostType.Labour, 50));            // September
        Run("RUN-001", Utc(10, 5, 8), kg.UnitOfMeasureId, 100, 100, 150, 6000, (ProductionCostType.Labour, 300), (ProductionCostType.Utilities, 200));
        Run("RUN-002", Utc(10, 12, 8), bag.UnitOfMeasureId, 4, 3, 50, 2000, (ProductionCostType.Packaging, 100));

        var mill = new Supplier { SupplierId = s.MillSupplies = Guid.NewGuid(), BusinessId = business.BusinessId, SupplierName = "Mill Supplies" };
        var agro = new Supplier { SupplierId = s.AgroTraders = Guid.NewGuid(), BusinessId = business.BusinessId, SupplierName = "Agro Traders" };
        db.AddRange(mill, agro);

        void Order(string number, Supplier supplier, PurchaseOrderStatus status, DateTime orderDate, DateTime? expected, bool active,
            params (Guid Product, Guid Unit, decimal Ordered, decimal Received, decimal Cost, bool Active)[] lines)
        {
            var order = new PurchaseOrder
            {
                PurchaseOrderId = Guid.NewGuid(), OrderNumber = number, SupplierId = supplier.SupplierId, Status = status,
                OrderDate = orderDate, ExpectedDeliveryDate = expected, IsActive = active,
            };
            db.PurchaseOrders.Add(order);
            db.PurchaseOrderLines.AddRange(lines.Select(l => new PurchaseOrderLine
            {
                PurchaseOrderLineId = Guid.NewGuid(), PurchaseOrderId = order.PurchaseOrderId, ProductId = l.Product, UnitOfMeasureId = l.Unit,
                QuantityOrdered = l.Ordered, QuantityReceived = l.Received, UnitCost = l.Cost, IsActive = l.Active,
            }));
        }
        Order("PO-001", mill, PurchaseOrderStatus.Received, Utc(10, 2, 6), null, true, (s.Maize, s.Kg, 200, 200, 40, true));
        Order("PO-002", agro, PurchaseOrderStatus.PartiallyReceived, Utc(10, 10, 6), Utc(10, 12, 6), true,
            (s.Maize, s.Kg, 100, 40, 42.5m, true), (s.Salt, s.Bag, 3, 0, 333.333m, true), (s.Sugar, s.Kg, 999, 0, 1, false));
        Order("PO-003", mill, PurchaseOrderStatus.Cancelled, Utc(10, 11, 6), null, true, (s.Maize, s.Kg, 50, 0, 40, true));
        Order("PO-004", mill, PurchaseOrderStatus.Draft, Utc(10, 12, 6), null, true, (s.Maize, s.Kg, 10, 0, 40, true));
        Order("PO-005", mill, PurchaseOrderStatus.Ordered, Utc(9, 1, 6), null, true, (s.Salt, s.Bag, 2, 0, 300, true));
        Order("PO-006", agro, PurchaseOrderStatus.Ordered, Utc(10, 15, 6), null, false, (s.Maize, s.Kg, 1000, 0, 1, true)); // deleted order

        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return s;
    }
}

public class ReportsPostgresTests : IClassFixture<ReportsDatabase>
{
    private static readonly DateOnly October1 = new(2026, 10, 1);
    private static readonly DateOnly October31 = new(2026, 10, 31);
    private const int Nairobi = 180;

    private readonly ReportsDatabase _db;
    private readonly ITestOutputHelper _output;

    public ReportsPostgresTests(ReportsDatabase db, ITestOutputHelper output)
    {
        _db = db;
        _output = output;
    }

    // Runs one call in its own scope and returns the SQL it sent, after writing it to the test output.
    private async Task<(T Result, IReadOnlyList<string> Sql)> RunAsync<T>(Func<IServiceProvider, Task<T>> call)
    {
        _db.Sql.Clear();
        using var scope = _db.Services.CreateScope();
        var result = await call(scope.ServiceProvider);
        var sql = _db.Sql.ToList();
        foreach (var command in sql)
            _output.WriteLine(command + "\n");
        return (result, sql);
    }

    private static ReportsService Reports(IServiceProvider sp) => sp.GetRequiredService<ReportsService>();
    private static IReportsRepository Repository(IServiceProvider sp) => sp.GetRequiredService<IReportsRepository>();

    // ---------------------------------------------------------------- Sales

    [PostgresFact]
    public async Task Sales_report_counts_Nairobi_days_and_leaves_failed_sales_out()
    {
        var (report, _) = await RunAsync(sp => Reports(sp).GetSalesReportAsync(October1, October31, Nairobi, 10));

        Assert.Equal(3, report.SaleCount);                     // S-0001, S-0002 and S-0007 (1 Oct local); S-0006 is 1 Nov local
        Assert.Equal(3400m, report.Revenue);
        Assert.Equal(1133.33m, report.AverageSaleValue);
        Assert.Equal((1, 500m), (report.VoidedCount, report.VoidedAmount));
        Assert.Equal((1, 300m), (report.PendingCount, report.PendingAmount));
        Assert.Equal(31, report.ByDay.Count());
        Assert.Equal(400m, report.ByDay.Single(d => d.Date == October1).Revenue);
        Assert.Equal(new[] { ("Wanjiku", 2000m), ("Walk-in", 1400m) }, report.TopCustomers.Select(c => (c.CustomerName, c.Total)));

        var unga = report.TopProducts.First();
        Assert.Equal(("Unga", 30m, 3000m, 88.24m), (unga.ProductName, unga.QuantitySoldInBaseUnits, unga.Revenue, unga.PercentOfRevenue));
    }

    // ---------------------------------------------------------------- Customers

    [PostgresFact]
    public async Task Customer_ageing_settles_the_oldest_invoice_first_and_honours_the_as_of_day()
    {
        var (report, sql) = await RunAsync(sp => Reports(sp).GetCustomerAgingAsync(October31, Nairobi, null, includeInvoices: true, includeSettled: false));

        // Invoice numbers and promised payment dates are looked up in one query, with just the columns they need.
        var numbersQuery = Assert.Single(sql, c => c.Contains("= ANY"));
        Assert.Contains("SELECT s.\"SaleId\", s.\"SaleNumber\", s.\"PaymentDueDate\"\n", numbersQuery.Replace("\r\n", "\n"));

        Assert.Equal(2, report.CustomerCount);
        Assert.Equal(2700m, report.TotalOutstanding);
        Assert.Equal(300m, report.TotalUnappliedCredit);

        var wanjiku = report.Customers.Single(c => c.CustomerId == _db.Seed.Wanjiku);
        Assert.Equal((2000m, 0m, 0m, 700m), (wanjiku.Buckets.Days0To30, wanjiku.Buckets.Days31To60, wanjiku.Buckets.Days61To90, wanjiku.Buckets.Over90));
        Assert.Equal(new[] { "S-0008", "S-0002" }, wanjiku.OpenInvoices!.Select(i => i.InvoiceNumber));

        // On 10 October the payment of the 20th had not happened yet.
        var (early, _) = await RunAsync(sp => Reports(sp).GetCustomerAgingAsync(new DateOnly(2026, 10, 10), Nairobi, _db.Seed.Wanjiku, false, false));
        Assert.Equal(3200m, early.TotalOutstanding);
    }

    // ---------------------------------------------------------------- Production

    [PostgresFact]
    public async Task Production_cost_report_keeps_units_apart_and_totals_cost_types_in_SQL_rows()
    {
        var (report, _) = await RunAsync(sp => Reports(sp).GetProductionCostReportAsync(October1, October31, Nairobi, null, 10));

        Assert.Equal((2, 8000m, 600m, 8600m), (report.RunCount, report.MaterialCost, report.AdditionalCost, report.TotalCost));
        Assert.Equal(new[] { "kg", "bag50" }, report.ByProduct.Select(p => p.UnitSymbol));
        Assert.Equal(new[] { ("Labour", 300m), ("Utilities", 200m), ("Packaging", 100m) },
            report.AdditionalCostsByType.Select(t => (t.CostType, t.Amount)));
        var maize = Assert.Single(report.TopIngredients);
        Assert.Equal((200m, 8000m), (maize.QuantityInBaseUnits, maize.TotalCost));

        var (none, _) = await RunAsync(sp => Reports(sp).GetProductionCostReportAsync(October1, October31, Nairobi, _db.Seed.Maize, 10));
        Assert.Equal(0, none.RunCount);
    }

    // ---------------------------------------------------------------- Inventory

    [PostgresFact]
    public async Task Stock_status_counts_and_values_follow_the_stock_status_rules()
    {
        var (report, _) = await RunAsync(sp => Reports(sp).GetStockStatusAsync(null, null, null, false, 30, Nairobi, includeCosts: true));

        Assert.Equal(4, report.ProductCount);                  // Yeast has no stock record, Sugar is inactive
        Assert.Equal((1, 1, 2), (report.InStockCount, report.LowStockCount, report.OutOfStockCount));
        Assert.Equal(3970m, report.TotalStockValue);           // 100 x 40 + 10 x 0 + 0 x 60 + (-2) x 15
        Assert.Equal(1, report.ProductsMissingCost);           // Salt

        var (withInactive, _) = await RunAsync(sp => Reports(sp).GetStockStatusAsync(null, null, null, true, 30, Nairobi, includeCosts: false));
        Assert.Equal(5, withInactive.ProductCount);
        Assert.Null(withInactive.TotalStockValue);
        Assert.All(withInactive.Items, i => Assert.Null(i.StockValue));
    }

    [PostgresFact]
    public async Task Dashboard_alert_counts_in_SQL_match_StockStatusRules_on_the_same_products()
    {
        var ((counts, rows), _) = await RunAsync(async sp =>
            (await Repository(sp).GetStockAlertCountsAsync(), await Repository(sp).GetStockStatusRowsAsync(null, null, includeInactive: false)));

        var classified = rows.Select(r => StockStatusRules.Classify(r.QuantityOnHand, r.ReorderLevel)).ToList();
        Assert.Equal(classified.Count(c => c == StockStatus.LowStock), counts.LowStock);
        Assert.Equal(classified.Count(c => c == StockStatus.OutOfStock), counts.OutOfStock);
        Assert.Equal(new StockAlertCounts(1, 2), counts);
    }

    [PostgresFact]
    public async Task Usage_sums_sales_voids_and_consumption_in_SQL_with_the_movement_types_as_stored_strings()
    {
        var period = ReportPeriod.Create(October1, October31, Nairobi);
        var (usage, sql) = await RunAsync(sp => Repository(sp).GetStockUsageAsync(period.FromUtc, period.ToUtcExclusive));

        Assert.Equal(150m, usage.Single(u => u.ProductId == _db.Seed.Maize).QuantityUsed);
        Assert.Equal(130m, usage.Single(u => u.ProductId == _db.Seed.Unga).QuantityUsed); // 140 sold - 10 voided; the 1 Nov sale is out
        Assert.Equal(2, usage.Count);
        Assert.Contains(sql, c => c.Contains("GROUP BY") && c.Contains("sum("));
    }

    [PostgresFact]
    public async Task Movement_report_rebuilds_opening_and_closing_from_the_ledger()
    {
        var (report, sql) = await RunAsync(sp => Reports(sp).GetMovementReportAsync(October1, October31, Nairobi, null, includeCosts: true));

        var maize = report.Items.Single(i => i.ProductId == _db.Seed.Maize);
        Assert.Equal((55m, 200m, 0m, 0m, 150m, 5m, 10m, 100m, 4),
            (maize.Opening, maize.Received, maize.Produced, maize.Sold, maize.Consumed, maize.AdjustedIn, maize.AdjustedOut, maize.Closing, maize.MovementCount));
        Assert.Equal((8000m, 0m, 6000m, -400m), (maize.ReceivedValue, maize.SoldCost, maize.ConsumedCost, maize.AdjustmentNetValue));

        var unga = report.Items.Single(i => i.ProductId == _db.Seed.Unga);
        Assert.Equal((0m, 100m, 130m, 30m, 0m), (unga.Opening, unga.Produced, unga.Sold, unga.AdjustedIn, unga.Closing));
        Assert.Equal(8060m, unga.SoldCost);

        Assert.Equal((8000m, 8060m, 6000m, 1400m), (report.TotalReceivedValue, report.TotalSoldCost, report.TotalConsumedCost, report.TotalAdjustmentNetValue));
        Assert.Contains(sql, c => c.Contains("GROUP BY") && c.Contains("COALESCE"));

        var (hidden, _) = await RunAsync(sp => Reports(sp).GetMovementReportAsync(October1, October31, Nairobi, null, includeCosts: false));
        Assert.Null(hidden.TotalReceivedValue);
        Assert.All(hidden.Items, i => Assert.Null(i.SoldCost));
    }

    [PostgresFact]
    public async Task Adjustment_report_names_who_made_each_one()
    {
        var (report, sql) = await RunAsync(sp => Reports(sp).GetAdjustmentReportAsync(October1, October31, Nairobi, null, 100, includeCosts: true));

        // Only the name is read for the user, never the password hash or salt.
        var userQuery = Assert.Single(sql, c => c.Contains("FROM \"Users\""));
        Assert.DoesNotContain("PasswordHash", userQuery);
        Assert.DoesNotContain("PasswordSalt", userQuery);

        Assert.Equal((3, 2, 1), (report.AdjustmentCount, report.IncreaseCount, report.DecreaseCount));
        Assert.Equal((1800m, 400m, 1400m, 1), (report.IncreaseValue, report.DecreaseValue, report.NetValue, report.UncostedCount));
        var spillage = report.Items.Single(i => i.Reason == "Spillage");
        Assert.Equal("admin", spillage.AdjustedBy);
        Assert.Equal(new DateOnly(2026, 10, 1), report.Items.Last().Date); // 21:00 UTC on 30 Sep
    }

    // ---------------------------------------------------------------- Purchasing

    [PostgresFact]
    public async Task Purchase_summary_counts_commitments_only_rounds_per_line_and_ignores_deleted_orders()
    {
        var (report, _) = await RunAsync(sp => Reports(sp).GetPurchaseSummaryAsync(October1, October31, Nairobi, null, 10));

        Assert.Equal(4, report.OrderCount);                    // PO-001..004; PO-006 is deleted
        Assert.Equal((1, 2000m), (report.CancelledCount, report.CancelledValue));
        Assert.Equal((13250m, 9700m, 3550m), (report.OrderedValue, report.ReceivedValue, report.OutstandingValue)); // 3 x 333.333 = 1000.00
        Assert.Equal(new[] { "Draft", "PartiallyReceived", "Received", "Cancelled" }, report.ByStatus.Select(s => s.Status));
        var maize = report.TopProducts.Single(p => p.ProductId == _db.Seed.Maize);
        Assert.Equal((300m, 240m, 12250m, 40.8333m), (maize.QuantityOrdered, maize.QuantityReceived, maize.OrderedValue, maize.AverageUnitCost));
        Assert.DoesNotContain(report.TopProducts, p => p.ProductId == _db.Seed.Sugar); // inactive line
    }

    [PostgresFact]
    public async Task Open_orders_lists_what_is_still_to_arrive_however_old()
    {
        var (report, _) = await RunAsync(sp => Reports(sp).GetOpenOrdersAsync(null, Nairobi));

        Assert.Equal(new[] { "PO-002", "PO-005" }, report.Orders.Select(o => o.OrderNumber));
        Assert.Equal((4150m, 3550m, 1), (report.OutstandingValue, report.OverdueValue, report.OverdueCount));
        Assert.Equal(new[] { 60m, 3m }, report.Orders.First().Lines.Select(l => l.QuantityOutstanding));
    }

    [PostgresFact]
    public async Task Price_trends_follow_each_product_and_unit()
    {
        var (report, _) = await RunAsync(sp => Reports(sp).GetPriceTrendsAsync(October1, October31, Nairobi, null, null, 100));

        Assert.Equal(2, report.ProductCount);
        var maize = report.Items.First();
        Assert.Equal(("Maize", 40m, 42.5m, 6.25m, "Mill Supplies", "Agro Traders"),
            (maize.ProductName, maize.FirstUnitCost, maize.LatestUnitCost, maize.ChangePercent, maize.LowestSupplier, maize.HighestSupplier));
        Assert.Null(report.Items.Last().ChangePercent);

        var (one, _) = await RunAsync(sp => Reports(sp).GetPriceTrendsAsync(October1, October31, Nairobi, _db.Seed.Maize, null, 100));
        Assert.Equal(2, Assert.Single(one.Items).Points!.Count());
    }

    // ---------------------------------------------------------------- Dashboard

    [PostgresFact]
    public async Task Dashboard_summary_with_every_section()
    {
        var all = DashboardSections.Sales | DashboardSections.Profit | DashboardSections.Receivables | DashboardSections.Stock
            | DashboardSections.StockValue | DashboardSections.Production | DashboardSections.Customers;
        var (summary, _) = await RunAsync(sp => sp.GetRequiredService<DashboardService>().GetSummaryAsync(October1, October31, Nairobi, all));

        Assert.Equal((3, 3400m, 2000m, 1, 1), (summary.Sales!.SaleCount, summary.Sales.Revenue, summary.Sales.CreditSalesAmount, summary.Sales.VoidedCount, summary.Sales.PendingCount));
        Assert.Equal((3400m, 1860m, 400m, false), (summary.Profit!.Revenue, summary.Profit.CostOfGoodsSold, summary.Profit.UncostedRevenue, summary.Profit.IsComplete));
        Assert.Equal((2700m, 1, 300m, 1), (summary.Receivables!.TotalOutstanding, summary.Receivables.CustomersOwing, summary.Receivables.TotalCustomerCredit, summary.Receivables.CustomersInCredit));
        Assert.Equal((1, 2), (summary.Stock!.LowStockCount, summary.Stock.OutOfStockCount));
        // The value comes from the stock valuation, which (unlike the alert counts) includes deactivated products: + 50 x 100 for Sugar.
        Assert.Equal((8970m, 1), (summary.Stock.TotalStockValue, summary.Stock.ProductsMissingCost));
        Assert.Equal((2, 8600m), (summary.Production!.RunCount, summary.Production.TotalCost));
        Assert.Equal(October31, summary.Customers!.To); // the customer panel covers the 90 days to the end date
        Assert.Empty(summary.HiddenSections);
    }

    [PostgresFact]
    public async Task Dashboard_summary_never_queries_a_section_the_caller_may_not_see()
    {
        var (summary, sql) = await RunAsync(sp => sp.GetRequiredService<DashboardService>().GetSummaryAsync(October1, October31, Nairobi, DashboardSections.Sales));

        Assert.NotNull(summary.Sales);
        Assert.Null(summary.Profit);
        Assert.Null(summary.Stock);
        Assert.Single(sql);
        Assert.Contains("\"Sales\"", sql[0]);
        foreach (var table in new[] { "SaleLines", "Expenses", "CustomerBalances", "StockLevels", "ProductionRuns" })
            Assert.DoesNotContain($"\"{table}\"", sql[0]);

        // Stock without Costing.View: counts only, and the valuation is never loaded.
        var (stock, stockSql) = await RunAsync(sp => sp.GetRequiredService<DashboardService>().GetStockAsync(includeValue: false));
        Assert.Null(stock.TotalStockValue);
        Assert.Null(stock.ProductsMissingCost);
        Assert.DoesNotContain(stockSql, c => c.Contains("\"AverageUnitCost\""));
    }
}

/// <summary>Each test gets its own empty database, to check how permissions arrive in a database built from migrations.</summary>
public class ReportPermissionsMigrationTests
{
    private static readonly string[] ReportPermissions =
        { "Dashboard.View", "SalesReports.View", "CustomerReports.View", "ProductionReports.View", "InventoryReports.View", "PurchaseReports.View" };

    private static async Task WithFreshDatabaseAsync(Func<IServiceProvider, UltimatePosDbContext, Task> test)
    {
        var server = Environment.GetEnvironmentVariable(PostgresFactAttribute.ConnectionStringVariable)!;
        var connectionString = new NpgsqlConnectionStringBuilder(server) { Database = $"ultimatepos_perms_{Guid.NewGuid():N}" }.ConnectionString;
        await using var services = ReportsDatabase.BuildServices(connectionString);
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<UltimatePosDbContext>();
        try
        {
            await test(scope.ServiceProvider, db);
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }

    private static Task<List<string>> AdminPermissionsAsync(UltimatePosDbContext db) =>
        db.RolePermissions.IgnoreQueryFilters().Where(rp => rp.Role.RoleName == "Admin" && rp.IsActive && rp.Permission.IsActive)
            .Select(rp => rp.Permission.Name).ToListAsync();

    [PostgresFact]
    public async Task A_business_registered_on_a_new_database_gives_its_Admin_every_report_permission()
    {
        // The seeder makes the super user; registering a business creates that business's Admin with every business permission.
        await WithFreshDatabaseAsync(async (sp, db) =>
        {
            await DbSeeder.SeedAsync(sp);
            var owner = await db.Users.SingleAsync(u => u.Email == DbSeeder.DevAdminEmail);
            var platform = await db.RolePermissions.IgnoreQueryFilters()
                .Where(rp => rp.Role.BusinessId == null && rp.Role.RoleName == "Super User").Select(rp => rp.Permission.Name).ToListAsync();
            Assert.Contains("Businesses.Register", platform);
            Assert.Contains("Licenses.Manage", platform);

            var tenant = sp.GetRequiredService<ITenantContext>();
            var business = new BusinessProfile { BusinessId = Guid.NewGuid(), BusinessName = "New Pharmacy", KraPin = "P000000009Z", Kind = BusinessKind.Trader };
            using (tenant.Use(business.BusinessId))
                await sp.GetRequiredService<UltimatePos.Application.Platform.IPlatformRepository>().CreateBusinessAsync(business, owner, false, null,
                    UltimatePos.Application.Licensing.LicensePolicy.Today);

            var granted = await AdminPermissionsAsync(db);
            Assert.All(ReportPermissions, p => Assert.Contains(p, granted));
            Assert.DoesNotContain("Reports.View", granted);
            Assert.DoesNotContain("Businesses.Register", granted); // platform permissions never sit on a business role
        });
    }

    [PostgresFact]
    public async Task The_migration_grants_the_report_permissions_to_an_existing_Admin_and_removes_Reports_View()
    {
        await WithFreshDatabaseAsync(async (sp, db) =>
        {
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync("AddCostingAndExpenses");

            // A database from before this change: an Admin role holding the old single Reports.View permission. Written as SQL, since
            // today's model has columns that schema does not.
            var adminId = Guid.NewGuid();
            var dashboardId = Guid.NewGuid();
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "Roles" ("RoleId", "RoleName", "IsActive", "CreatedAt") VALUES ({adminId}, 'Admin', TRUE, now());
                INSERT INTO "Permissions" ("PermissionId", "Name", "Module", "IsActive", "CreatedAt") VALUES
                    ({Guid.NewGuid()}, 'Reports.View', 'Reports', TRUE, now()),
                    ({dashboardId}, 'Dashboard.View', 'Dashboard', TRUE, now());
                INSERT INTO "RolePermissions" ("RoleId", "PermissionId", "IsActive", "CreatedAt")
                    SELECT {adminId}, "PermissionId", TRUE, now() FROM "Permissions" WHERE "Name" = 'Reports.View';
                """);

            await migrator.MigrateAsync("AddReportPermissions");

            var granted = await AdminPermissionsAsync(db);
            Assert.Equal(ReportPermissions.OrderBy(p => p), granted.OrderBy(p => p));
            var dashboard = new Permission { PermissionId = dashboardId };
            await migrator.MigrateAsync();
            Assert.False(await db.Permissions.AnyAsync(p => p.Name == "Reports.View"));
            Assert.Equal(dashboard.PermissionId, (await db.Permissions.SingleAsync(p => p.Name == "Dashboard.View")).PermissionId);
            Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        });
    }
}
