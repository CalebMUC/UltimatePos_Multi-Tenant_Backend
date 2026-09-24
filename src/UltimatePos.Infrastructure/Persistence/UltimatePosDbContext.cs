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



            base.OnModelCreating(modelBuilder);
        }
    }
}
