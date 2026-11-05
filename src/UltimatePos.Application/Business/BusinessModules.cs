using UltimatePos.Domain.Entities;

namespace UltimatePos.Application.Business
{
    /// <summary>
    /// Which areas of the app a business uses, worked out from its sector and kind — so every client shows the same menus without
    /// keeping its own copy of the rules. Computed, not stored: only eTIMS is switched per business (BusinessProfile.EtimsEnabled). This only decides what is SHOWN:
    /// the API still enforces permissions on every endpoint, and a hidden module is not refused by the backend.
    /// Keys are stable strings; add a key here before a client depends on it.
    /// </summary>
    public static class BusinessModules
    {
        // Every business.
        public const string Dashboard = "dashboard";
        public const string Sales = "sales";
        public const string Customers = "customers";
        public const string Catalog = "catalog";
        public const string Stock = "stock";
        public const string Purchasing = "purchasing";
        public const string Expenses = "expenses";
        public const string Reports = "reports";
        public const string Mpesa = "mpesa";
        public const string Branches = "branches";
        public const string Settings = "settings";
        /// <summary>The day's float and the end-of-day cash count (cash sessions).</summary>
        public const string CashUp = "cashUp";

        // Depending on what the business is.
        /// <summary>Formulas and production runs. Manufacturers only, whatever the sector: a trader cannot buy raw materials.</summary>
        public const string Production = "production";
        /// <summary>Batches, expiry dates and the expiry report.</summary>
        public const string Batches = "batches";
        /// <summary>Prescription-only products and the prescription register.</summary>
        public const string Prescriptions = "prescriptions";
        /// <summary>Delivery orders, the delivery board and the rider summary.</summary>
        public const string Deliveries = "deliveries";
        /// <summary>Containers sold with a pack (the "customer has own container?" toggle) and refill quick-sale buttons.</summary>
        public const string Containers = "containers";
        /// <summary>Bundles: several products sold at one price ("20L bottle + water", packs of 12), set up once and rung up as one item.</summary>
        public const string Bundles = "bundles";
        /// <summary>
        /// Age-restricted products (alcohol, tobacco): the toggle on products, the age check at the till and on delivery, the selling-hours
        /// setting and the age-check register. The API enforces a product's flag wherever the module is shown or not.
        /// </summary>
        public const string AgeRestricted = "ageRestricted";
        /// <summary>KRA eTIMS: devices, submissions, product classifications, the KRA block on receipts. Only where the business has it on.</summary>
        public const string Etims = "etims";

        public static readonly IReadOnlyList<string> Core =
            new[] { Dashboard, Sales, Customers, Catalog, Stock, Purchasing, Expenses, Reports, Mpesa, Branches, Settings, CashUp };

        /// <summary>Sector-dependent modules. General — unclassified, including every business registered before sectors — gets them all.</summary>
        private static readonly IReadOnlyDictionary<BusinessSector, string[]> BySector = new Dictionary<BusinessSector, string[]>
        {
            [BusinessSector.General] = new[] { Batches, Prescriptions, Deliveries, Containers, Bundles, AgeRestricted },
            [BusinessSector.Pharmacy] = new[] { Batches, Prescriptions },
            [BusinessSector.Agrovet] = new[] { Batches },
            [BusinessSector.WaterRefill] = new[] { Deliveries, Containers, Bundles },
            // Estate customers order by phone or WhatsApp and a boda rider takes it to the door.
            // Cigarettes, and beer where the shop holds a liquor licence, are for adults only.
            [BusinessSector.Minimart] = new[] { Batches, Deliveries, Bundles, AgeRestricted },
            [BusinessSector.Supermarket] = new[] { Batches, Bundles, AgeRestricted },
            // Cement, steel and sheets go to site by pickup or lorry; sets (a toilet with its fittings) are sold as one item.
            [BusinessSector.Hardware] = new[] { Deliveries, Bundles },
            // Contractors' orders go to site; solar systems and wiring kits are sold as one item.
            [BusinessSector.Electrical] = new[] { Deliveries, Bundles },
            // A wines and spirits shop: every bottle needs an adult buyer; crates and bottles go back for their deposit (containers);
            // orders go out by boda at night; party packs are bundles; beer, cider and ready-to-drink cans carry a best-before date.
            [BusinessSector.Liquor] = new[] { Batches, Deliveries, Containers, Bundles, AgeRestricted },
        };

        /// <summary>The modules of a business of this sector and kind: core first, then the rest in catalogue order, eTIMS last.</summary>
        public static IReadOnlyList<string> For(BusinessSector sector, BusinessKind kind, bool etimsEnabled = false)
        {
            var extra = BySector.TryGetValue(sector, out var modules) ? modules : BySector[BusinessSector.General];
            var result = Core.ToList();
            if (kind == BusinessKind.Manufacturer)
                result.Add(Production);
            result.AddRange(extra);
            if (etimsEnabled)
                result.Add(Etims);
            return result;
        }
    }
}
