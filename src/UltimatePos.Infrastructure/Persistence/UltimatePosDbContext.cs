using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UltimatePos.Domain.Entities;

namespace UltimatePos.Infrastructure.Persistence
{
    public class UltimatePosDbContext : DbContext
    {
        public UltimatePosDbContext(DbContextOptions<UltimatePosDbContext> options) : base(options)
        {
        }
        public DbSet<User> Users => Set<User>();
        public DbSet<Role> Roles => Set<Role>();
        public DbSet<Permission> Permissions => Set<Permission>();
        public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
        public DbSet<UserRole> UserRoles => Set<UserRole>();
        public DbSet<UserSession> UserSessions => Set<UserSession>();

        public DbSet<BusinessProfile> Businesses => Set<BusinessProfile>();
        public DbSet<Customer> Customers => Set<Customer>();
        public DbSet<Category> Categories => Set<Category>();
        public DbSet<UnitOfMeasure> UnitsOfMeasure => Set<UnitOfMeasure>();
        public DbSet<Product> Products => Set<Product>();
        public DbSet<ProductUnitConversion> ProductUnitConversions => Set<ProductUnitConversion>();
        public DbSet<ProductPriceTier> ProductPriceTiers => Set<ProductPriceTier>();
        public DbSet<SkuSequence> SkuSequences => Set<SkuSequence>();

        public DbSet<Supplier> Suppliers => Set<Supplier>();
        public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();
        public DbSet<PurchaseOrderLine> PurchaseOrderLines => Set<PurchaseOrderLine>();
        public DbSet<StockLevel> StockLevels => Set<StockLevel>();
        public DbSet<StockMovement> StockMovements => Set<StockMovement>();

        public DbSet<Formula> Formulas => Set<Formula>();
        public DbSet<FormulaLine> FormulaLines => Set<FormulaLine>();
        public DbSet<ProductionRun> ProductionRuns => Set<ProductionRun>();
        public DbSet<ProductionRunInput> ProductionRunInputs => Set<ProductionRunInput>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<UserRole>().HasKey(ur => new { ur.UserId, ur.RoleId });
            modelBuilder.Entity<RolePermission>().HasKey(rp => new { rp.RoleId, rp.PermissionId });

            modelBuilder.Entity<User>().HasIndex(u => u.Email).IsUnique();
            modelBuilder.Entity<Role>().HasIndex(r => r.RoleName).IsUnique();
            modelBuilder.Entity<Permission>().HasIndex(p => p.Name).IsUnique();


            modelBuilder.Entity<BusinessProfile>().HasKey(b => b.BusinessId);
            modelBuilder.Entity<BusinessProfile>().HasIndex(b => b.KraPin).IsUnique();
            modelBuilder.Entity<BusinessProfile>().Property(b => b.BusinessType).HasConversion<string>();

            modelBuilder.Entity<Customer>()
                .HasOne(c => c.BusinessProfile)
                .WithMany(b => b.Customers)
                .HasForeignKey(c => c.BusinessId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<SkuSequence>().HasKey(s => s.Prefix);

            modelBuilder.Entity<Category>().HasIndex(c => c.Code).IsUnique();

            modelBuilder.Entity<UnitOfMeasure>().HasIndex(u => u.Symbol).IsUnique();
            modelBuilder.Entity<Product>().HasIndex(p => p.Sku).IsUnique();
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

            // Conversions: one per (product, unit) — a second row would make the factor ambiguous.
            modelBuilder.Entity<ProductUnitConversion>().HasIndex(c => new { c.ProductId, c.PackUnitOfMeasureId }).IsUnique();

            // Formulas: unique version per product, and at most ONE active version per product.
            modelBuilder.Entity<Formula>().HasIndex(f => new { f.ProductId, f.VersionNumber }).IsUnique();
            modelBuilder.Entity<Formula>().HasIndex(f => f.ProductId).IsUnique().HasFilter("\"IsActive\" = true");
            modelBuilder.Entity<FormulaLine>().HasIndex(l => new { l.FormulaId, l.IngredientProductId }).IsUnique();
            modelBuilder.Entity<ProductionRun>().HasIndex(r => r.RunNumber).IsUnique();

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



            base.OnModelCreating(modelBuilder);
        }
    }
}
