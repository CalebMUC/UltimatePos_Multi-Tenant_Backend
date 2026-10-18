using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UltimatePos.Application.Common.Interfaces;
using UltimatePos.Domain.Entities;
using UltimatePos.Domain.Exceptions;

namespace UltimatePos.Infrastructure.Persistence
{
    public class UltimatePosDbContext : DbContext
    {
        private readonly ITenantContext _tenant;

        public UltimatePosDbContext(DbContextOptions<UltimatePosDbContext> options, ITenantContext tenant) : base(options)
        {
            _tenant = tenant;
        }

        /// <summary>
        /// Read by the query filters at query time (EF parameterises a DbContext member), so every query sees only the business
        /// in scope. Null means no business: business-owned rows match nothing.
        /// </summary>
        private Guid? CurrentBusinessId => _tenant.BusinessId;

        public DbSet<User> Users => Set<User>();
        public DbSet<Role> Roles => Set<Role>();
        public DbSet<Permission> Permissions => Set<Permission>();
        public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
        public DbSet<UserRole> UserRoles => Set<UserRole>();
        public DbSet<UserSession> UserSessions => Set<UserSession>();
        public DbSet<UserBusiness> UserBusinesses => Set<UserBusiness>();

        public DbSet<BusinessProfile> Businesses => Set<BusinessProfile>();
        public DbSet<Customer> Customers => Set<Customer>();
        public DbSet<Category> Categories => Set<Category>();
        public DbSet<UnitOfMeasure> UnitsOfMeasure => Set<UnitOfMeasure>();
        public DbSet<Product> Products => Set<Product>();
        public DbSet<ProductUnitConversion> ProductUnitConversions => Set<ProductUnitConversion>();
        public DbSet<ProductPriceTier> ProductPriceTiers => Set<ProductPriceTier>();
        public DbSet<SkuSequence> SkuSequences => Set<SkuSequence>();

        public DbSet<Supplier> Suppliers => Set<Supplier>();
        public DbSet<SupplierLedgerEntry> SupplierLedgerEntries => Set<SupplierLedgerEntry>();
        public DbSet<SupplierBalance> SupplierBalances => Set<SupplierBalance>();
        public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();
        public DbSet<PurchaseOrderLine> PurchaseOrderLines => Set<PurchaseOrderLine>();
        public DbSet<StockLevel> StockLevels => Set<StockLevel>();
        public DbSet<StockMovement> StockMovements => Set<StockMovement>();
        public DbSet<StockBatch> StockBatches => Set<StockBatch>();

        public DbSet<Formula> Formulas => Set<Formula>();
        public DbSet<FormulaLine> FormulaLines => Set<FormulaLine>();
        public DbSet<ProductionRun> ProductionRuns => Set<ProductionRun>();
        public DbSet<ProductionRunInput> ProductionRunInputs => Set<ProductionRunInput>();

        public DbSet<ProductionRunCost> ProductionRunCosts => Set<ProductionRunCost>();

        public DbSet<ExpenseCategory> ExpenseCategories => Set<ExpenseCategory>();
        public DbSet<Expense> Expenses => Set<Expense>();

        public DbSet<MpesaTransaction> MpesaTransactions => Set<MpesaTransaction>();

        public DbSet<Sale> Sales => Set<Sale>();
        public DbSet<SaleLine> SaleLines => Set<SaleLine>();
        public DbSet<SaleLineBatchAllocation> SaleLineBatchAllocations => Set<SaleLineBatchAllocation>();
        public DbSet<SalePrescription> SalePrescriptions => Set<SalePrescription>();
        public DbSet<SaleReturn> SaleReturns => Set<SaleReturn>();
        public DbSet<SaleReturnLine> SaleReturnLines => Set<SaleReturnLine>();
        public DbSet<CustomerLedgerEntry> CustomerLedgerEntries => Set<CustomerLedgerEntry>();
        public DbSet<CustomerBalance> CustomerBalances => Set<CustomerBalance>();

        public DbSet<SystemEvent> SystemEvents => Set<SystemEvent>();
        public DbSet<Notification> Notifications => Set<Notification>();
        public DbSet<NotificationDelivery> NotificationDeliveries => Set<NotificationDelivery>();
        public DbSet<AlertState> AlertStates => Set<AlertState>();
        public DbSet<BusinessLicense> BusinessLicenses => Set<BusinessLicense>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<UserRole>().HasKey(ur => new { ur.UserId, ur.RoleId });
            modelBuilder.Entity<RolePermission>().HasKey(rp => new { rp.RoleId, rp.PermissionId });

            modelBuilder.Entity<User>().HasIndex(u => u.Email).IsUnique();
            // Role names are unique within a business, and among platform roles (BusinessId null — Postgres treats NULLs as distinct,
            // so platform roles need an index of their own).
            modelBuilder.Entity<Role>().HasIndex(r => new { r.BusinessId, r.RoleName }).IsUnique();
            modelBuilder.Entity<Role>().HasIndex(r => r.RoleName).IsUnique().HasFilter("\"BusinessId\" IS NULL")
                .HasDatabaseName("IX_Roles_RoleName_Platform");
            modelBuilder.Entity<Role>()
                .HasOne<BusinessProfile>().WithMany()
                .HasForeignKey(r => r.BusinessId).OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<Permission>().HasIndex(p => p.Name).IsUnique();

            modelBuilder.Entity<UserBusiness>().HasKey(ub => new { ub.UserId, ub.BusinessId });
            modelBuilder.Entity<UserBusiness>()
                .HasOne(ub => ub.User).WithMany()
                .HasForeignKey(ub => ub.UserId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<UserBusiness>()
                .HasOne(ub => ub.Business).WithMany()
                .HasForeignKey(ub => ub.BusinessId).OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<UserSession>().HasIndex(s => s.SessionToken).IsUnique();
            modelBuilder.Entity<UserSession>().HasIndex(s => s.UserId);

            modelBuilder.Entity<BusinessProfile>().HasKey(b => b.BusinessId);
            modelBuilder.Entity<BusinessProfile>().HasIndex(b => b.KraPin).IsUnique();
            modelBuilder.Entity<BusinessProfile>().HasIndex(b => b.MpesaShortCode).IsUnique().HasFilter("\"MpesaShortCode\" IS NOT NULL");
            modelBuilder.Entity<BusinessProfile>().Property(b => b.BusinessType).HasConversion<string>();
            modelBuilder.Entity<BusinessProfile>().Property(b => b.Kind).HasConversion<string>();


            modelBuilder.Entity<Customer>()
                .HasOne(c => c.BusinessProfile)
                .WithMany(b => b.Customers)
                .HasForeignKey(c => c.BusinessId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<SkuSequence>().HasKey(s => new { s.BusinessId, s.Prefix });

            modelBuilder.Entity<Category>().HasIndex(c => new { c.BusinessId, c.Code }).IsUnique();

            modelBuilder.Entity<UnitOfMeasure>().HasIndex(u => new { u.BusinessId, u.Symbol }).IsUnique();
            modelBuilder.Entity<Product>().HasIndex(p => new { p.BusinessId, p.Sku }).IsUnique();
            modelBuilder.Entity<Product>().HasIndex(p => new { p.BusinessId, p.Barcode }).IsUnique().HasFilter("\"Barcode\" IS NOT NULL");
            modelBuilder.Entity<Product>().Property(p => p.ItemType).HasConversion<string>();
            modelBuilder.Entity<Product>().Property(p => p.TaxClassification).HasConversion<string>();
            modelBuilder.Entity<ProductPriceTier>().Property(t => t.PriceType).HasConversion<string>();

            modelBuilder.Entity<Category>()
           .HasOne(c => c.ParentCategory).WithMany(c => c.Subcategories)
           .HasForeignKey(c => c.ParentCategoryId).OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Product>()
                .HasOne(p => p.Category).WithMany(c => c.Products)
                .HasForeignKey(p => p.CategoryId).OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Product>()
                .HasOne(p => p.BaseUnitOfMeasure).WithMany()
                .HasForeignKey(p => p.BaseUnitOfMeasureId).OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<ProductUnitConversion>()
                .HasOne(c => c.Product).WithMany(p => p.UnitConversions)
                .HasForeignKey(c => c.ProductId).OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<ProductUnitConversion>()
                .HasOne(c => c.PackUnitOfMeasure).WithMany()
                .HasForeignKey(c => c.PackUnitOfMeasureId).OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<ProductPriceTier>()
                .HasOne(t => t.Product).WithMany(p => p.PriceTiers)
                .HasForeignKey(t => t.ProductId).OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<ProductPriceTier>()
                .HasOne(t => t.UnitOfMeasure).WithMany()
                .HasForeignKey(t => t.UnitOfMeasureId).OnDelete(DeleteBehavior.Restrict);


            modelBuilder.Entity<StockLevel>().HasKey(s => s.ProductId);

            modelBuilder.Entity<PurchaseOrder>().Property(o => o.Status).HasConversion<string>();
            modelBuilder.Entity<PurchaseOrder>().HasIndex(o => new { o.BusinessId, o.OrderNumber }).IsUnique();
            modelBuilder.Entity<StockMovement>().Property(m => m.MovementType).HasConversion<string>();

            modelBuilder.Entity<Supplier>()
                .HasOne(s => s.Business).WithMany()
                .HasForeignKey(s => s.BusinessId).OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<PurchaseOrder>()
                .HasOne(o => o.Supplier).WithMany(s => s.PurchaseOrders)
                .HasForeignKey(o => o.SupplierId).OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<PurchaseOrderLine>()
                .HasOne(l => l.PurchaseOrder).WithMany(o => o.Lines)
                .HasForeignKey(l => l.PurchaseOrderId).OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<PurchaseOrderLine>()
                .HasOne(l => l.Product).WithMany()
                .HasForeignKey(l => l.ProductId).OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<PurchaseOrderLine>()
                .HasOne(l => l.UnitOfMeasure).WithMany()
                .HasForeignKey(l => l.UnitOfMeasureId).OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<StockLevel>()
                .HasOne(s => s.Product).WithOne()
                .HasForeignKey<StockLevel>(s => s.ProductId).OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<StockMovement>()
                .HasOne(m => m.Product).WithMany()
                .HasForeignKey(m => m.ProductId).OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<StockMovement>()
                .HasOne<StockBatch>().WithMany()
                .HasForeignKey(m => m.StockBatchId).OnDelete(DeleteBehavior.Restrict);

            // Batches: one row per (product, lot number); FEFO reads them by expiry.
            modelBuilder.Entity<StockBatch>().HasIndex(b => new { b.ProductId, b.BatchNumber }).IsUnique();
            modelBuilder.Entity<StockBatch>().HasIndex(b => new { b.ProductId, b.ExpiryDate });
            modelBuilder.Entity<StockBatch>()
                .HasOne(b => b.Product).WithMany()
                .HasForeignKey(b => b.ProductId).OnDelete(DeleteBehavior.Restrict);

            // Conversions: one per (product, unit) — a second row would make the factor ambiguous.
            modelBuilder.Entity<ProductUnitConversion>().HasIndex(c => new { c.ProductId, c.PackUnitOfMeasureId }).IsUnique();

            // Formulas: unique version per product, and at most ONE active version per product.
            modelBuilder.Entity<Formula>().HasIndex(f => new { f.ProductId, f.VersionNumber }).IsUnique();
            modelBuilder.Entity<Formula>().HasIndex(f => f.ProductId).IsUnique().HasFilter("\"IsActive\" = true");
            modelBuilder.Entity<FormulaLine>().HasIndex(l => new { l.FormulaId, l.IngredientProductId }).IsUnique();
            modelBuilder.Entity<ProductionRun>().HasIndex(r => new { r.BusinessId, r.RunNumber }).IsUnique();

            modelBuilder.Entity<Formula>()
                .HasOne(f => f.Product).WithMany()
                .HasForeignKey(f => f.ProductId).OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<Formula>()
                .HasOne(f => f.OutputUnitOfMeasure).WithMany()
                .HasForeignKey(f => f.OutputUnitOfMeasureId).OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<FormulaLine>()
                .HasOne(l => l.Formula).WithMany(f => f.Lines)
                .HasForeignKey(l => l.FormulaId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<FormulaLine>()
                .HasOne(l => l.IngredientProduct).WithMany()
                .HasForeignKey(l => l.IngredientProductId).OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<FormulaLine>()
                .HasOne(l => l.UnitOfMeasure).WithMany()
                .HasForeignKey(l => l.UnitOfMeasureId).OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<ProductionRun>()
                .HasOne(r => r.Formula).WithMany()
                .HasForeignKey(r => r.FormulaId).OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<ProductionRun>()
                .HasOne(r => r.Product).WithMany()
                .HasForeignKey(r => r.ProductId).OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<ProductionRun>()
                .HasOne(r => r.UnitOfMeasure).WithMany()
                .HasForeignKey(r => r.UnitOfMeasureId).OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<ProductionRunInput>()
                .HasOne(i => i.ProductionRun).WithMany(r => r.Inputs)
                .HasForeignKey(i => i.ProductionRunId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<ProductionRunInput>()
                .HasOne(i => i.IngredientProduct).WithMany()
                .HasForeignKey(i => i.IngredientProductId).OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<ProductionRunInput>()
                .HasOne(i => i.UnitOfMeasure).WithMany()
                .HasForeignKey(i => i.UnitOfMeasureId).OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<ProductionRunCost>().Property(c => c.CostType).HasConversion<string>();
            modelBuilder.Entity<ProductionRunCost>()
                .HasOne(c => c.ProductionRun).WithMany(r => r.AdditionalCosts)
                .HasForeignKey(c => c.ProductionRunId).OnDelete(DeleteBehavior.Cascade);


            modelBuilder.Entity<ExpenseCategory>().HasIndex(c => new { c.BusinessId, c.Code }).IsUnique();
            modelBuilder.Entity<ExpenseCategory>().HasIndex(c => new { c.BusinessId, c.Name }).IsUnique();

            modelBuilder.Entity<Expense>().Property(e => e.PaymentMethod).HasConversion<string>();
            modelBuilder.Entity<Expense>().Property(e => e.Status).HasConversion<string>();
            modelBuilder.Entity<Expense>().HasIndex(e => new { e.BusinessId, e.ExpenseNumber }).IsUnique();
            modelBuilder.Entity<Expense>().HasIndex(e => e.ExpenseDate);
            modelBuilder.Entity<Expense>()
                .HasOne(e => e.Category).WithMany()
                .HasForeignKey(e => e.ExpenseCategoryId).OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<Expense>()
                .HasOne(e => e.Supplier).WithMany()
                .HasForeignKey(e => e.SupplierId).OnDelete(DeleteBehavior.Restrict);


            modelBuilder.Entity<MpesaTransaction>().Property(t => t.TransactionType).HasConversion<string>();
            modelBuilder.Entity<MpesaTransaction>().Property(t => t.Status).HasConversion<string>();
            modelBuilder.Entity<MpesaTransaction>().HasIndex(t => t.CheckoutRequestId);

            modelBuilder.Entity<MpesaTransaction>()
                .HasOne(t => t.Sale).WithMany()
                .HasForeignKey(t => t.SaleId).OnDelete(DeleteBehavior.Restrict);


            modelBuilder.Entity<CustomerBalance>().HasKey(b => b.CustomerId);

            modelBuilder.Entity<Sale>().HasIndex(s => new { s.BusinessId, s.SaleNumber }).IsUnique();
            modelBuilder.Entity<Sale>().HasIndex(s => new { s.BusinessId, s.CreatedAt });
            modelBuilder.Entity<Sale>().Property(s => s.PaymentMethod).HasConversion<string>();
            modelBuilder.Entity<Sale>().Property(s => s.Status).HasConversion<string>();
            modelBuilder.Entity<CustomerLedgerEntry>().Property(e => e.EntryType).HasConversion<string>();
            modelBuilder.Entity<CustomerLedgerEntry>().Property(e => e.PaymentMethod).HasConversion<string>();

            modelBuilder.Entity<Sale>()
                .HasOne(s => s.Customer).WithMany()
                .HasForeignKey(s => s.CustomerId).OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<SaleLine>()
                .HasOne(l => l.Sale).WithMany(s => s.Lines)
                .HasForeignKey(l => l.SaleId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<SaleLine>()
                .HasOne(l => l.Product).WithMany()
                .HasForeignKey(l => l.ProductId).OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<SaleLine>()
                .HasOne(l => l.UnitOfMeasure).WithMany()
                .HasForeignKey(l => l.UnitOfMeasureId).OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<SaleLineBatchAllocation>()
                .HasOne(a => a.SaleLine).WithMany(l => l.BatchAllocations)
                .HasForeignKey(a => a.SaleLineId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<SaleLineBatchAllocation>()
                .HasOne(a => a.StockBatch).WithMany()
                .HasForeignKey(a => a.StockBatchId).OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<SalePrescription>().HasKey(p => p.SaleId);
            modelBuilder.Entity<SalePrescription>()
                .HasOne(p => p.Sale).WithOne(s => s.Prescription)
                .HasForeignKey<SalePrescription>(p => p.SaleId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<SalePrescription>().HasIndex(p => p.PrescriptionDate);

            modelBuilder.Entity<SaleReturn>().Property(r => r.RefundMethod).HasConversion<string>();
            modelBuilder.Entity<SaleReturn>().HasIndex(r => new { r.BusinessId, r.ReturnNumber }).IsUnique();
            modelBuilder.Entity<SaleReturn>()
                .HasOne(r => r.Sale).WithMany()
                .HasForeignKey(r => r.SaleId).OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<SaleReturnLine>()
                .HasOne(l => l.SaleReturn).WithMany(r => r.Lines)
                .HasForeignKey(l => l.SaleReturnId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<SaleReturnLine>()
                .HasOne(l => l.SaleLine).WithMany()
                .HasForeignKey(l => l.SaleLineId).OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<SaleReturnLine>()
                .HasOne(l => l.Product).WithMany()
                .HasForeignKey(l => l.ProductId).OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<CustomerLedgerEntry>()
                .HasOne(e => e.Customer).WithMany()
                .HasForeignKey(e => e.CustomerId).OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<CustomerBalance>()
                .HasOne(b => b.Customer).WithOne()
                .HasForeignKey<CustomerBalance>(b => b.CustomerId).OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<SupplierBalance>().HasKey(b => b.SupplierId);
            modelBuilder.Entity<SupplierBalance>()
                .HasOne(b => b.Supplier).WithOne()
                .HasForeignKey<SupplierBalance>(b => b.SupplierId).OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<SupplierLedgerEntry>().Property(e => e.EntryType).HasConversion<string>();
            modelBuilder.Entity<SupplierLedgerEntry>().Property(e => e.PaymentMethod).HasConversion<string>();
            modelBuilder.Entity<SupplierLedgerEntry>().HasIndex(e => new { e.SupplierId, e.CreatedAt });
            modelBuilder.Entity<SupplierLedgerEntry>()
                .HasOne(e => e.Supplier).WithMany()
                .HasForeignKey(e => e.SupplierId).OnDelete(DeleteBehavior.Restrict);

            // Every business-owned table belongs to the business by FK, so a row can never point at a business that is not there.
            foreach (var entityType in new[] { typeof(Category), typeof(UnitOfMeasure), typeof(Product), typeof(PurchaseOrder), typeof(Formula),
                         typeof(ProductionRun), typeof(Sale), typeof(Expense), typeof(ExpenseCategory), typeof(StockBatch), typeof(SaleReturn) })
            {
                modelBuilder.Entity(entityType)
                    .HasOne(typeof(BusinessProfile)).WithMany()
                    .HasForeignKey(nameof(ITenantEntity.BusinessId)).OnDelete(DeleteBehavior.Restrict);
            }
            modelBuilder.Entity<MpesaTransaction>()
                .HasOne<BusinessProfile>().WithMany()
                .HasForeignKey(t => t.BusinessId).OnDelete(DeleteBehavior.Restrict);

            // ---- Events, notifications, deliveries, alerts ----
            modelBuilder.Entity<SystemEvent>().Property(e => e.Severity).HasConversion<string>();
            modelBuilder.Entity<SystemEvent>().Property(e => e.EventType).HasMaxLength(100);
            modelBuilder.Entity<SystemEvent>().Property(e => e.Data).HasColumnType("jsonb");
            modelBuilder.Entity<SystemEvent>().HasIndex(e => new { e.BusinessId, e.OccurredAt });
            modelBuilder.Entity<SystemEvent>().HasIndex(e => new { e.BusinessId, e.EventType, e.OccurredAt });
            modelBuilder.Entity<SystemEvent>().HasIndex(e => new { e.EntityType, e.EntityId });
            // The dispatcher's queue: only unprocessed rows, oldest first.
            modelBuilder.Entity<SystemEvent>().HasIndex(e => e.OccurredAt).HasFilter("\"ProcessedAt\" IS NULL")
                .HasDatabaseName("IX_SystemEvents_Unprocessed");

            modelBuilder.Entity<Notification>().Property(n => n.Severity).HasConversion<string>();
            modelBuilder.Entity<Notification>().HasIndex(n => new { n.UserId, n.IsRead, n.CreatedAt });
            modelBuilder.Entity<Notification>()
                .HasOne<SystemEvent>().WithMany()
                .HasForeignKey(n => n.SystemEventId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<Notification>()
                .HasOne<User>().WithMany()
                .HasForeignKey(n => n.UserId).OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<NotificationDelivery>().Property(d => d.Channel).HasConversion<string>();
            modelBuilder.Entity<NotificationDelivery>().Property(d => d.Status).HasConversion<string>();
            modelBuilder.Entity<NotificationDelivery>().HasIndex(d => new { d.Status, d.NextAttemptAt });
            modelBuilder.Entity<NotificationDelivery>()
                .HasOne<SystemEvent>().WithMany()
                .HasForeignKey(d => d.SystemEventId).OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<AlertState>().HasIndex(a => new { a.BusinessId, a.AlertKey }).IsUnique();
            modelBuilder.Entity<AlertState>()
                .HasOne<BusinessProfile>().WithMany()
                .HasForeignKey(a => a.BusinessId).OnDelete(DeleteBehavior.Restrict);

            // ---- Licences ----
            modelBuilder.Entity<BusinessLicense>().Property(l => l.Kind).HasConversion<string>();
            modelBuilder.Entity<BusinessLicense>().Property(l => l.State).HasConversion<string>();
            modelBuilder.Entity<BusinessLicense>().Property(l => l.PaymentMethod).HasConversion<string>();
            modelBuilder.Entity<BusinessLicense>().HasIndex(l => new { l.BusinessId, l.LicenseNumber }).IsUnique();
            modelBuilder.Entity<BusinessLicense>().HasIndex(l => new { l.BusinessId, l.EndsOn });
            modelBuilder.Entity<BusinessLicense>()
                .HasOne<BusinessProfile>().WithMany()
                .HasForeignKey(l => l.BusinessId).OnDelete(DeleteBehavior.Restrict);

            ConfigureTenantFilters(modelBuilder);

            base.OnModelCreating(modelBuilder);
        }

        /// <summary>
        /// Business isolation. Owned rows filter on their own BusinessId; child rows (lines, balances, movements) filter through
        /// their parent, so no query — LINQ written today or later — can read across businesses. Raw SQL is not filtered: the
        /// ledgers only touch rows whose ids were first loaded through a filtered query.
        /// </summary>
        private void ConfigureTenantFilters(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Category>().HasQueryFilter(e => e.BusinessId == CurrentBusinessId);
            modelBuilder.Entity<UnitOfMeasure>().HasQueryFilter(e => e.BusinessId == CurrentBusinessId);
            modelBuilder.Entity<Product>().HasQueryFilter(e => e.BusinessId == CurrentBusinessId);
            modelBuilder.Entity<Customer>().HasQueryFilter(e => e.BusinessId == CurrentBusinessId);
            modelBuilder.Entity<Supplier>().HasQueryFilter(e => e.BusinessId == CurrentBusinessId);
            modelBuilder.Entity<PurchaseOrder>().HasQueryFilter(e => e.BusinessId == CurrentBusinessId);
            modelBuilder.Entity<Formula>().HasQueryFilter(e => e.BusinessId == CurrentBusinessId);
            modelBuilder.Entity<ProductionRun>().HasQueryFilter(e => e.BusinessId == CurrentBusinessId);
            modelBuilder.Entity<Sale>().HasQueryFilter(e => e.BusinessId == CurrentBusinessId);
            modelBuilder.Entity<Expense>().HasQueryFilter(e => e.BusinessId == CurrentBusinessId);
            modelBuilder.Entity<ExpenseCategory>().HasQueryFilter(e => e.BusinessId == CurrentBusinessId);
            modelBuilder.Entity<StockBatch>().HasQueryFilter(e => e.BusinessId == CurrentBusinessId);
            modelBuilder.Entity<SaleReturn>().HasQueryFilter(e => e.BusinessId == CurrentBusinessId);
            modelBuilder.Entity<MpesaTransaction>().HasQueryFilter(e => e.BusinessId == CurrentBusinessId);
            modelBuilder.Entity<AlertState>().HasQueryFilter(e => e.BusinessId == CurrentBusinessId);
            modelBuilder.Entity<BusinessLicense>().HasQueryFilter(e => e.BusinessId == CurrentBusinessId);
            // Events and deliveries: the business's own, or platform-level ones when no business is in scope. The dispatcher reads
            // across businesses with IgnoreQueryFilters.
            modelBuilder.Entity<SystemEvent>().HasQueryFilter(e => e.BusinessId == CurrentBusinessId);
            modelBuilder.Entity<NotificationDelivery>().HasQueryFilter(e => e.BusinessId == CurrentBusinessId);
            // Notifications: the business's own plus platform notifications, which show in whichever business the user is in.
            modelBuilder.Entity<Notification>().HasQueryFilter(e => e.BusinessId == CurrentBusinessId || e.BusinessId == null);
            // Nullable on both sides with C# semantics: in a business, its own roles; with no business in scope, platform roles.
            modelBuilder.Entity<Role>().HasQueryFilter(r => r.BusinessId == CurrentBusinessId);
            // Role assignments and grants follow their role. Sign-in reads them across businesses with IgnoreQueryFilters.
            modelBuilder.Entity<UserRole>().HasQueryFilter(ur => ur.Role.BusinessId == CurrentBusinessId);
            modelBuilder.Entity<RolePermission>().HasQueryFilter(rp => rp.Role.BusinessId == CurrentBusinessId);

            modelBuilder.Entity<StockLevel>().HasQueryFilter(e => e.Product.BusinessId == CurrentBusinessId);
            modelBuilder.Entity<StockMovement>().HasQueryFilter(e => e.Product.BusinessId == CurrentBusinessId);
            modelBuilder.Entity<ProductPriceTier>().HasQueryFilter(e => e.Product.BusinessId == CurrentBusinessId);
            modelBuilder.Entity<ProductUnitConversion>().HasQueryFilter(e => e.Product.BusinessId == CurrentBusinessId);
            modelBuilder.Entity<PurchaseOrderLine>().HasQueryFilter(e => e.PurchaseOrder.BusinessId == CurrentBusinessId);
            modelBuilder.Entity<FormulaLine>().HasQueryFilter(e => e.Formula.BusinessId == CurrentBusinessId);
            modelBuilder.Entity<ProductionRunInput>().HasQueryFilter(e => e.ProductionRun.BusinessId == CurrentBusinessId);
            modelBuilder.Entity<ProductionRunCost>().HasQueryFilter(e => e.ProductionRun.BusinessId == CurrentBusinessId);
            modelBuilder.Entity<SaleLine>().HasQueryFilter(e => e.Sale.BusinessId == CurrentBusinessId);
            modelBuilder.Entity<SaleLineBatchAllocation>().HasQueryFilter(e => e.StockBatch.BusinessId == CurrentBusinessId);
            modelBuilder.Entity<SalePrescription>().HasQueryFilter(e => e.Sale.BusinessId == CurrentBusinessId);
            modelBuilder.Entity<SaleReturnLine>().HasQueryFilter(e => e.SaleReturn.BusinessId == CurrentBusinessId);
            modelBuilder.Entity<CustomerLedgerEntry>().HasQueryFilter(e => e.Customer.BusinessId == CurrentBusinessId);
            modelBuilder.Entity<CustomerBalance>().HasQueryFilter(e => e.Customer.BusinessId == CurrentBusinessId);
            modelBuilder.Entity<SupplierLedgerEntry>().HasQueryFilter(e => e.Supplier.BusinessId == CurrentBusinessId);
            modelBuilder.Entity<SupplierBalance>().HasQueryFilter(e => e.Supplier.BusinessId == CurrentBusinessId);
        }

        /// <summary>
        /// Next number in a per-business document sequence (SKUs, INV, PO, PR, EXP, RET). Atomic, so concurrent callers never get the
        /// same number; each business counts from 1.
        /// </summary>
        public async Task<int> NextSequenceNumberAsync(string prefix)
        {
            var businessId = _tenant.RequireBusinessId();
            var results = await Database.SqlQuery<int>($"""
                INSERT INTO "SkuSequences" ("BusinessId", "Prefix", "LastNumber") VALUES ({businessId}, {prefix}, 1)
                ON CONFLICT ("BusinessId", "Prefix") DO UPDATE SET "LastNumber" = "SkuSequences"."LastNumber" + 1
                RETURNING "LastNumber" AS "Value"
                """).ToListAsync();
            return results.Single();
        }

        public override int SaveChanges(bool acceptAllChangesOnSuccess)
        {
            StampBusiness();
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }

        public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
        {
            StampBusiness();
            return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }

        /// <summary>
        /// New business-owned rows get the business in scope; rows can never be written into, or moved to, another business.
        /// </summary>
        private void StampBusiness()
        {
            var businessId = CurrentBusinessId;

            foreach (var entry in ChangeTracker.Entries<ITenantEntity>())
            {
                if (entry.State == EntityState.Added)
                {
                    if (entry.Entity.BusinessId == Guid.Empty)
                        entry.Entity.BusinessId = businessId
                            ?? throw new ForbiddenException("No business is selected — business data cannot be saved without one.");
                    else if (businessId.HasValue && entry.Entity.BusinessId != businessId.Value)
                        throw new ForbiddenException("A record cannot be created in a business other than the one signed in to.");
                }
                else if (entry.State is EntityState.Modified or EntityState.Deleted)
                {
                    GuardOwnership(entry.Property(nameof(ITenantEntity.BusinessId)), businessId);
                }
            }

            foreach (var entry in ChangeTracker.Entries<Role>())
            {
                if (entry.State == EntityState.Added && entry.Entity.BusinessId is null && businessId.HasValue)
                    entry.Entity.BusinessId = businessId; // a role created inside a business belongs to it
                else if (entry.State is EntityState.Modified or EntityState.Deleted
                         && entry.Property(r => r.BusinessId).IsModified)
                    throw new ForbiddenException("A role cannot be moved between businesses.");
            }
        }

        private static void GuardOwnership(PropertyEntry property, Guid? businessId)
        {
            var original = (Guid)property.OriginalValue!;
            if (property.IsModified && original != (Guid)property.CurrentValue!)
                throw new ForbiddenException("A record cannot be moved between businesses.");
            if (businessId.HasValue && original != businessId.Value)
                throw new ForbiddenException("A record belonging to another business cannot be changed.");
        }
    }
}
