using UltimatePos.Domain.Entities;

namespace UltimatePos.Application.Business
{
    /// <summary>
    /// What a newly registered business starts with, so it can record expenses and set up products on day one. Everything here
    /// belongs to the new business and can be renamed or deactivated afterwards through the API.
    /// </summary>
    public static class BusinessDefaults
    {
        public static readonly IReadOnlyList<(string Code, string Name, string Description)> ExpenseCategories = new[]
        {
            ("RENT", "Rent", "Premises rent and service charge"),
            ("UTIL", "Utilities", "Electricity, water, internet, phone and airtime"),
            ("SAL", "Salaries & Wages", "Staff pay, casual labour outside production runs, allowances"),
            ("TRN", "Transport & Fuel", "Delivery, fuel, fares and vehicle running costs"),
            ("REP", "Repairs & Maintenance", "Equipment, vehicle and premises repairs"),
            ("LIC", "Licences & Permits", "County licences, permits and registrations"),
            ("MKT", "Marketing & Advertising", "Promotion, signage, SMS and print"),
            ("FIN", "Bank & M-Pesa Charges", "Transaction fees, bank charges and interest"),
            ("OFF", "Office & Stationery", "Stationery, printing, receipt rolls and small office items"),
            ("PRO", "Professional Fees", "Accounting, legal and consultancy fees"),
            ("INS", "Insurance", "Premises, stock and vehicle insurance"),
            ("SEC", "Security", "Guards, alarms and monitoring"),
            ("MISC", "Miscellaneous", "Anything that fits no other category"),
        };

        public static readonly IReadOnlyList<(string Name, string Symbol)> UnitsOfMeasure = new[]
        {
            ("Piece", "pcs"),
            ("Kilogram", "kg"),
            ("Gram", "g"),
            ("Litre", "l"),
            ("Millilitre", "ml"),
            ("Box", "box"),
            ("Packet", "pkt"),
        };

        /// <summary>
        /// What a business of one sector starts with on top of the general defaults above, and what is suggested for it at registration
        /// when the registering super user leaves it out. Starting points only — every unit and category can be renamed or deactivated,
        /// and Kind and AllowNegativeStock given explicitly always win.
        /// </summary>
        /// <param name="Kind">Suggested when registration gives no Kind. Null = no suggestion (Kind is then required).</param>
        /// <param name="AllowNegativeStock">Used when registration does not say.</param>
        public sealed record SectorProfile(
            BusinessKind? Kind,
            bool AllowNegativeStock,
            IReadOnlyList<(string Name, string Symbol)> UnitsOfMeasure,
            IReadOnlyList<(string Code, string Name, string Description)> ExpenseCategories);

        private static readonly (string, string)[] NoUnits = Array.Empty<(string, string)>();
        private static readonly (string, string, string)[] NoCategories = Array.Empty<(string, string, string)>();

        public static readonly IReadOnlyDictionary<BusinessSector, SectorProfile> Sectors = new Dictionary<BusinessSector, SectorProfile>
        {
            [BusinessSector.General] = new(null, true, NoUnits, NoCategories),
            [BusinessSector.Pharmacy] = new(BusinessKind.Trader, true,
                new[] { ("Tablet", "tab"), ("Capsule", "cap"), ("Bottle", "btl"), ("Strip", "strip"), ("Tube", "tube") },
                new[] { ("DSP", "Waste Disposal", "Fees for collecting and disposing of expired or damaged stock") }),
            // Many agrovets also make their own feeds: no Kind is suggested, so the registering super user must choose.
            [BusinessSector.Agrovet] = new(null, true,
                new[] { ("Bag", "bag"), ("Sachet", "sachet"), ("Bottle", "btl") },
                NoCategories),
            // A refill station never sells more water than it holds.
            [BusinessSector.WaterRefill] = new(BusinessKind.Trader, false,
                new[] { ("10 Litre Refill", "10L"), ("20 Litre Refill", "20L") },
                new[]
                {
                    ("FILT", "Filter Replacements", "Filter cartridges, membranes and UV lamps"),
                    ("SAN", "Cleaning & Sanitation", "Tank cleaning, sanitiser and cleaning supplies"),
                    ("WQT", "Water Testing & Inspection", "Laboratory tests and inspection fees"),
                }),
            [BusinessSector.Minimart] = new(BusinessKind.Trader, true,
                new[] { ("Dozen", "doz"), ("Crate", "crate") },
                NoCategories),
            [BusinessSector.Supermarket] = new(BusinessKind.Trader, true,
                new[] { ("Dozen", "doz"), ("Crate", "crate"), ("Carton", "ctn") },
                new[] { ("PKG", "Packaging & Carrier Bags", "Carrier bags, wrapping and packing materials used at the tills") }),
            [BusinessSector.Hardware] = new(BusinessKind.Trader, true,
                new[] { ("Metre", "m"), ("Bag", "bag"), ("Sheet", "sheet"), ("Roll", "roll") },
                NoCategories),
        };

        /// <summary>The sector's profile; an unknown sector gets the General one.</summary>
        public static SectorProfile For(BusinessSector sector) =>
            Sectors.TryGetValue(sector, out var profile) ? profile : Sectors[BusinessSector.General];

        /// <summary>Every unit a new business of <paramref name="sector"/> starts with: the general ones, then the sector's.</summary>
        public static IEnumerable<(string Name, string Symbol)> UnitsFor(BusinessSector sector) => UnitsOfMeasure.Concat(For(sector).UnitsOfMeasure);

        /// <summary>Every expense category a new business of <paramref name="sector"/> starts with: the general ones, then the sector's.</summary>
        public static IEnumerable<(string Code, string Name, string Description)> ExpenseCategoriesFor(BusinessSector sector) =>
            ExpenseCategories.Concat(For(sector).ExpenseCategories);
    }
}
