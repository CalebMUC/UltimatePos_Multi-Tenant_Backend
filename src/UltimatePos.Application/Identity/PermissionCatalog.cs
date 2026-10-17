namespace UltimatePos.Application.Identity
{
    /// <summary>
    /// Every permission the API checks. Keep in step with the controller [Authorize] attributes; DbSeeder and the
    /// multi-business migration insert from this list.
    ///
    /// Platform permissions are about the platform itself (which businesses exist). They can only sit on a platform role
    /// (Role.BusinessId = null) and apply whichever business the user is signed in to. Every other permission is a business
    /// permission: granted through a role that belongs to one business, and meaningful only inside it.
    /// </summary>
    public static class PermissionCatalog
    {
        public const string PlatformModule = "Platform";

        public static readonly IReadOnlyList<(string Name, string Module)> Platform = new[]
        {
            ("Businesses.Register", "Businesses"),
            ("Businesses.ViewAll", "Businesses"),
            ("Businesses.Deactivate", "Businesses"),
        };

        public static readonly IReadOnlyList<(string Name, string Module)> Business = new[]
        {
            ("Users.Create", "Users"), ("Users.View", "Users"), ("Users.Update", "Users"), ("Users.AssignRoles", "Users"),
            ("Users.Deactivate", "Users"), ("Users.Lock", "Users"), ("Users.ResetPassword", "Users"),
            ("Roles.View", "Roles"), ("Roles.Create", "Roles"), ("Roles.Update", "Roles"),
            ("Roles.Deactivate", "Roles"), ("Roles.AssignPermissions", "Roles"),
            ("Permissions.View", "Permissions"),

            ("Businesses.View", "Businesses"), ("Businesses.Update", "Businesses"),
            ("Customers.Register", "Customers"), ("Customers.View", "Customers"),
            ("Customers.Update", "Customers"), ("Customers.Deactivate", "Customers"),
            ("Customers.ViewLedger", "Customers"), ("Customers.RecordPayment", "Customers"),

            ("Categories.View", "Categories"), ("Categories.Create", "Categories"), ("Categories.Update", "Categories"),
            ("Categories.Deactivate", "Categories"), ("Categories.BulkImport", "Categories"),
            ("Units.View", "Units"), ("Units.Create", "Units"), ("Units.Update", "Units"), ("Units.Deactivate", "Units"),
            ("Products.View", "Products"), ("Products.Create", "Products"), ("Products.Update", "Products"),
            ("Products.Deactivate", "Products"), ("Products.ManagePricing", "Products"), ("Products.BulkImport", "Products"),
            ("Products.ManageUnitConversions", "Products"),

            ("Suppliers.Register", "Suppliers"), ("Suppliers.View", "Suppliers"),
            ("Suppliers.Update", "Suppliers"), ("Suppliers.Deactivate", "Suppliers"),
            ("Suppliers.ViewLedger", "Suppliers"), ("Suppliers.RecordPayment", "Suppliers"),
            ("PurchaseOrders.Create", "PurchaseOrders"), ("PurchaseOrders.View", "PurchaseOrders"),
            ("PurchaseOrders.Receive", "PurchaseOrders"), ("PurchaseOrders.Cancel", "PurchaseOrders"),
            ("Stock.View", "Stock"), ("Stock.Adjust", "Stock"),

            ("Formulas.Create", "Formulas"), ("Formulas.View", "Formulas"), ("Formulas.Deactivate", "Formulas"),
            ("Production.Run", "Production"), ("Production.View", "Production"),

            ("Sales.Create", "Sales"), ("Sales.View", "Sales"), ("Sales.Void", "Sales"),
            ("Sales.Return", "Sales"), ("Sales.ApplyDiscount", "Sales"), ("Sales.OverrideCreditLimit", "Sales"),
            ("Prescriptions.Dispense", "Prescriptions"), ("Prescriptions.View", "Prescriptions"),
            ("Mpesa.Configure", "Mpesa"), ("Mpesa.View", "Mpesa"),

            ("Costing.View", "Costing"), ("Costing.Revalue", "Costing"),

            ("ExpenseCategories.Create", "Expenses"), ("ExpenseCategories.View", "Expenses"),
            ("ExpenseCategories.Update", "Expenses"), ("ExpenseCategories.Deactivate", "Expenses"),
            ("Expenses.Create", "Expenses"), ("Expenses.View", "Expenses"),
            ("Expenses.Update", "Expenses"), ("Expenses.Void", "Expenses"),

            ("Dashboard.View", "Dashboard"),
            ("SalesReports.View", "Reports"), ("CustomerReports.View", "Reports"), ("ProductionReports.View", "Reports"),
            ("InventoryReports.View", "Reports"), ("PurchaseReports.View", "Reports"),
        };

        public static IEnumerable<(string Name, string Module)> All => Platform.Concat(Business);

        private static readonly HashSet<string> PlatformNames = Platform.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);

        public static bool IsPlatform(string permissionName) => PlatformNames.Contains(permissionName);

        /// <summary>Name of the role a newly registered business's creator gets: every business permission.</summary>
        public const string BusinessAdminRole = "Admin";

        /// <summary>Name of the platform role that may register and manage businesses.</summary>
        public const string PlatformAdminRole = "Platform Admin";
    }
}
