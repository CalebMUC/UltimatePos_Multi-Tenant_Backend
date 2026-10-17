using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using UltimatePos.Application.Common.Interfaces;
using UltimatePos.Domain.Entities;

namespace UltimatePos.Infrastructure.Persistence;

public static class DbSeeder
{
    private static readonly (string Name, string Module)[] RequiredPermissions =
 {
    ("Users.Create", "Users"), ("Users.View", "Users"), ("Users.AssignRoles", "Users"),
    ("Users.Deactivate", "Users"), ("Users.Lock", "Users"),
    ("Roles.View", "Roles"), ("Roles.Create", "Roles"), ("Roles.Update", "Roles"),
    ("Roles.Deactivate", "Roles"), ("Roles.AssignPermissions", "Roles"),
    ("Permissions.View", "Permissions"),

    ("Businesses.Register", "Businesses"), ("Businesses.View", "Businesses"),
    ("Businesses.Update", "Businesses"), ("Businesses.Deactivate", "Businesses"),
    ("Customers.Register", "Customers"), ("Customers.View", "Customers"),
    ("Customers.Update", "Customers"), ("Customers.Deactivate", "Customers"),
    ("Customers.ViewLedger", "Customers"), ("Customers.RecordPayment", "Customers"),

    ("Categories.View", "Categories"), ("Categories.Create", "Categories"), ("Categories.BulkImport", "Categories"),
    ("Units.View", "Units"), ("Units.Create", "Units"),
    ("Products.View", "Products"), ("Products.Create", "Products"), ("Products.Update", "Products"),
    ("Products.ManagePricing", "Products"), ("Products.BulkImport", "Products"), ("Products.ManageUnitConversions", "Products"),

    ("Suppliers.Register", "Suppliers"), ("Suppliers.View", "Suppliers"),
    ("Suppliers.Update", "Suppliers"), ("Suppliers.Deactivate", "Suppliers"),
    ("PurchaseOrders.Create", "PurchaseOrders"), ("PurchaseOrders.View", "PurchaseOrders"),
    ("PurchaseOrders.Receive", "PurchaseOrders"), ("PurchaseOrders.Cancel", "PurchaseOrders"),
    ("Stock.View", "Stock"), ("Stock.Adjust", "Stock"),

    ("Formulas.Create", "Formulas"), ("Formulas.View", "Formulas"), ("Formulas.Deactivate", "Formulas"),
    ("Production.Run", "Production"), ("Production.View", "Production"),

    ("Sales.Create", "Sales"), ("Sales.View", "Sales"), ("Sales.Void", "Sales"),
    ("Mpesa.Configure", "Mpesa"), ("Mpesa.View", "Mpesa"),

    ("Costing.View", "Costing"), ("Costing.Revalue", "Costing"),


    ("ExpenseCategories.Create", "Expenses"), ("ExpenseCategories.View", "Expenses"),
    ("ExpenseCategories.Update", "Expenses"), ("ExpenseCategories.Deactivate", "Expenses"),
    ("Expenses.Create", "Expenses"), ("Expenses.View", "Expenses"),
    ("Expenses.Update", "Expenses"), ("Expenses.Void", "Expenses"),

    // Reports and dashboard. Each is its own permission so a role gets only the information it needs. Costs inside the inventory
    // reports and the dashboard's stock value stay behind Costing.View on top of these.
    ("Dashboard.View", "Dashboard"),
    ("SalesReports.View", "Reports"), ("CustomerReports.View", "Reports"), ("ProductionReports.View", "Reports"),
    ("InventoryReports.View", "Reports"), ("PurchaseReports.View", "Reports")


};

    // Starting set so expenses can be recorded on day one. Seeded only while the table is empty; everything here can be
    // renamed or deactivated afterwards through the API.
    private static readonly (string Code, string Name, string Description)[] DefaultExpenseCategories =
    {
    ("RENT", "Rent", "Premises rent and service charge"),
    ("UTIL", "Utilities", "Electricity, water, internet, phone and airtime"),
    ("SAL", "Salaries & Wages", "Staff pay, casual labour outside production runs, allowances"),
    ("TRN", "Transport & Fuel", "Delivery, fuel, fares and vehicle running costs"),
    ("REP", "Repairs & Maintenance", "Equipment, mill, vehicle and premises repairs"),
    ("LIC", "Licences & Permits", "County licences, permits and registrations"),
    ("MKT", "Marketing & Advertising", "Promotion, signage, SMS and print"),
    ("FIN", "Bank & M-Pesa Charges", "Transaction fees, bank charges and interest"),
    ("OFF", "Office & Stationery", "Stationery, printing, receipt rolls and small office items"),
    ("PRO", "Professional Fees", "Accounting, legal, veterinary and consultancy fees"),
    ("INS", "Insurance", "Premises, stock and vehicle insurance"),
    ("SEC", "Security", "Guards, alarms and monitoring"),
    ("MISC", "Miscellaneous", "Anything that fits no other category")
};

    public static async Task SeedAsync(IServiceProvider services)
    {
        var context = services.GetRequiredService<UltimatePosDbContext>();
        var hasher = services.GetRequiredService<IPasswordHasher>();
        //var logger = services.GetRequiredService<ILogger>();

        await context.Database.MigrateAsync();

        var adminRole = await context.Roles.FirstOrDefaultAsync(r => r.RoleName == "Admin");
        var adminRoleCreated = adminRole is null;
        if (adminRole is null)
        {
            adminRole = new Role { RoleName = "Admin" };
            context.Roles.Add(adminRole);
            await context.SaveChangesAsync();
            //logger.LogInformation("DbSeeder: created Admin role.");
        }

        var existingNames = await context.Permissions.Select(p => p.Name).ToListAsync();
        var missing = RequiredPermissions.Where(p => !existingNames.Contains(p.Name)).ToList();

        var newPermissions = missing.Select(p => new Permission { Name = p.Name, Module = p.Module }).ToList();
        if (newPermissions.Count > 0)
        {
            context.Permissions.AddRange(newPermissions);
            await context.SaveChangesAsync();
        }

        // A new Admin role gets every permission, including those a migration added before the role existed. An existing one only
        // gets the permissions created just now, so a permission deliberately taken away from Admin stays taken away.
        var toGrant = newPermissions;
        if (adminRoleCreated)
        {
            var requiredNames = RequiredPermissions.Select(p => p.Name).ToList();
            toGrant = await context.Permissions.Where(p => requiredNames.Contains(p.Name)).ToListAsync();
        }

        if (toGrant.Count > 0)
        {
            foreach (var permission in toGrant)
                context.RolePermissions.Add(new RolePermission { RoleId = adminRole.RoleId, PermissionId = permission.PermissionId });

            await context.SaveChangesAsync();
            //logger.LogInformation("DbSeeder: added {Count} missing permission(s) to Admin role: {Names}",
            //    toGrant.Count, string.Join(", ", toGrant.Select(p => p.Name)));
        }

        if (!await context.ExpenseCategories.AnyAsync())
        {
            context.ExpenseCategories.AddRange(DefaultExpenseCategories.Select(c => new ExpenseCategory
            {
                Code = c.Code,
                Name = c.Name,
                Description = c.Description
            }));
            await context.SaveChangesAsync();
        }

        if (!await context.Users.AnyAsync(u => u.Email == "admin@ultimatepos.local"))
        {
            var (hash, salt) = hasher.HashPassword("Admin@12345");
            var adminUser = new User
            {
                Email = "admin@ultimatepos.local",
                Username = "admin",
                PasswordHash = hash,
                PasswordSalt = salt
            };
            context.Users.Add(adminUser);
            await context.SaveChangesAsync();

            context.UserRoles.Add(new UserRole { UserId = adminUser.UserId, RoleId = adminRole.RoleId });
            await context.SaveChangesAsync();
            //logger.LogInformation("DbSeeder: created default admin user.");
        }
    }
}