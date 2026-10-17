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
    }
}
