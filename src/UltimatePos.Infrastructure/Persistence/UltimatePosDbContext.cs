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

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<UserRole>().HasKey(ur => new { ur.UserId, ur.RoleId });
            modelBuilder.Entity<RolePermission>().HasKey(rp => new { rp.RoleId, rp.PermissionId });

            modelBuilder.Entity<User>().HasIndex(u => u.Email).IsUnique();
            modelBuilder.Entity<Role>().HasIndex(r => r.RoleName).IsUnique();
            modelBuilder.Entity<Permission>().HasIndex(p => p.Name).IsUnique();

            modelBuilder.Entity<BusinessProfile>().HasIndex(b => b.KraPin).IsUnique();
            modelBuilder.Entity<BusinessProfile>().Property(b => b.BusinessType).HasConversion<string>();

            modelBuilder.Entity<Customer>()
                .HasOne(c => c.BusinessProfile)
                .WithMany(b => b.Customers)
                .HasForeignKey(c => c.BusinessId)
                .OnDelete(DeleteBehavior.Restrict);

            base.OnModelCreating(modelBuilder);
        }
    }
}
