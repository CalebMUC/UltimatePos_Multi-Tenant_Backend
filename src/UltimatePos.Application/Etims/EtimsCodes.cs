using System.Text.RegularExpressions;
using static UltimatePos.Domain.Enums.PosEnums;

namespace UltimatePos.Application.Etims
{
    /// <summary>
    /// KRA eTIMS codes the POS sends, and how its own values map onto them. The code list a device downloads (selectCodeList) is the
    /// authority; these are the values confirmed against the sandbox code list for P052446551J.
    /// </summary>
    public static class EtimsCodes
    {
        /// <summary>resultCd: accepted.</summary>
        public const string Success = "000";
        /// <summary>resultCd on a read: nothing new since lastReqDt. Not an error.</summary>
        public const string NoSearchResult = "001";
        /// <summary>resultCd: KRA already holds an invoice with this number.</summary>
        public const string InvoiceAlreadyExists = "924";

        // Tax types (code class 04). Prices are VAT-inclusive, so a line's tax is total x rate / (100 + rate).
        public const string TaxExempt = "A";
        public const string TaxStandard = "B";
        public const string TaxZeroRated = "C";
        public const string TaxNonVat = "D";
        public const string TaxReduced = "E";
        public static readonly IReadOnlyList<string> TaxTypes = new[] { TaxExempt, TaxStandard, TaxZeroRated, TaxNonVat, TaxReduced };

        public static decimal TaxRateFor(string taxTypeCode) => taxTypeCode switch
        {
            TaxStandard => 16m,
            TaxReduced => 8m,
            _ => 0m,
        };

        /// <summary>
        /// The rate a document's header states for a tax type (taxRtA..E). KRA's code list has E (8%) switched off (useYn N) and refuses
        /// any header that states 8 for it ("Rule taxRtE failed: Expected 0.00").
        /// </summary>
        public static decimal HeaderTaxRateFor(string taxTypeCode) => taxTypeCode == TaxReduced ? 0m : TaxRateFor(taxTypeCode);

        /// <summary>A business that is not VAT-registered reports every item as non-VAT (D), whatever its classification.</summary>
        public static string TaxTypeFor(TaxClassification classification, bool isVatRegistered) =>
            !isVatRegistered ? TaxNonVat : classification switch
            {
                TaxClassification.Standard => TaxStandard,
                TaxClassification.ZeroRated => TaxZeroRated,
                _ => TaxExempt,
            };

        /// <summary>Item type (code class 24): 1 raw material, 2 finished product, 3 service. Packaging is bought in as a raw material.</summary>
        public static string ItemTypeFor(ItemType itemType) => itemType == ItemType.FinishedGood ? "2" : "1";

        /// <summary>Payment type (code class 07): 01 cash, 02 credit, 06 mobile money.</summary>
        public static string PaymentTypeFor(PaymentMethod method) => method switch
        {
            PaymentMethod.Cash => "01",
            PaymentMethod.Credit => "02",
            _ => "06",
        };

        public const string ReceiptSale = "S";
        public const string ReceiptCreditNote = "R";
        public const string ReceiptPurchase = "P";
        public const string SalesTypeNormal = "N";
        /// <summary>Transaction progress (code class 11): approved.</summary>
        public const string StatusApproved = "02";
        public const string RegistrationManual = "M";

        /// <summary>Refund reason (code class 32) for goods brought back.</summary>
        public const string RefundReasonReturn = "06";
        /// <summary>Refund reason for a sale cancelled after KRA had it (voided at the till).</summary>
        public const string RefundReasonVoid = "06";

        /// <summary>
        /// Stock in/out type (code class 12) for a stock movement. Incoming: 02 purchase, 03 return, 04 transfer in, 05 production,
        /// 06 adjustment. Outgoing: 11 sale, 13 transfer out, 14 used in production, 16 adjustment. Null: not reported (revaluation
        /// moves value, not stock).
        /// </summary>
        public static string? StockIoTypeFor(StockMovementType type, decimal quantityChange) => type switch
        {
            StockMovementType.PurchaseReceipt => "02",
            StockMovementType.SaleReturn or StockMovementType.SaleVoid => "03",
            StockMovementType.TransferIn => "04",
            StockMovementType.ProductionOutput => "05",
            StockMovementType.Adjustment => quantityChange >= 0 ? "06" : "16",
            StockMovementType.Sale => "11",
            StockMovementType.TransferOut => "13",
            StockMovementType.ProductionConsumption => "14",
            _ => null,
        };

        /// <summary>Packaging unit (code class 17) used when nothing else is set: NT, "net" (not packaged).</summary>
        public const string DefaultPackagingUnit = "NT";

        /// <summary>
        /// Quantity unit (code class 10) for pieces and for anything without a two-letter code. KRA's gateway accepts an item code only
        /// when its quantity unit has two letters ("Incorrect QtyUnitCd Prefix" for U, L, LTR, GRM ... however the code is padded), so
        /// pieces, litres, grams and metres are all reported as NO (number). A product can be given another two-letter unit.
        /// </summary>
        public const string DefaultQuantityUnit = "NO";

        private static readonly Dictionary<string, string> QuantityUnits = new(StringComparer.OrdinalIgnoreCase)
        {
            ["kg"] = "KG", ["kgs"] = "KG", ["kilogram"] = "KG", ["kilograms"] = "KG",
            ["box"] = "BX", ["bx"] = "BX", ["boxes"] = "BX",
            ["bag"] = "BG", ["bags"] = "BG",
            ["pkt"] = "PA", ["packet"] = "PA", ["pack"] = "PA", ["packets"] = "PA",
            ["dozen"] = "DZ", ["dz"] = "DZ", ["doz"] = "DZ",
            ["pair"] = "PR", ["pairs"] = "PR",
            ["roll"] = "RO", ["rolls"] = "RO",
            ["can"] = "CA", ["cans"] = "CA",
            ["tube"] = "TU", ["tubes"] = "TU",
            ["bale"] = "BE", ["bundle"] = "BE",
            ["block"] = "BL", ["drum"] = "DR", ["reel"] = "RL", ["sheet"] = "ST", ["barrel"] = "BA",
        };

        /// <summary>The quantity unit for a unit of measure, by its symbol and then its name.</summary>
        public static string QuantityUnitFor(string? symbol, string? name) =>
            symbol is not null && QuantityUnits.TryGetValue(symbol.Trim(), out var bySymbol) ? bySymbol
            : name is not null && QuantityUnits.TryGetValue(name.Trim(), out var byName) ? byName
            : DefaultQuantityUnit;

        /// <summary>Only two-letter units fit an item code KRA accepts.</summary>
        public static bool IsUsableQuantityUnit(string? code) => code is { Length: 2 } && code.All(char.IsAsciiLetterOrDigit);

        /// <summary>Stock in/out types 01-06 bring stock in; 11-16 take it out.</summary>
        public static bool IsIncoming(string stockIoTypeCode) => string.CompareOrdinal(stockIoTypeCode, "10") < 0;

        /// <summary>The KRA series a refusal is about, and the number KRA said it expects next.</summary>
        public enum Series { Item, Invoice, StockAdjustment }

        private static readonly Regex ExpectedNumber = new(@"expected\D*?(\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>
        /// The next number KRA asks for when it refuses one out of sequence, e.g. "Invalid itemCd Sequence. Expected sequence ending
        /// with: ********13", "Invalid sarNo: Expected: 2 but found: 7", "Invalid invcNo sequence, expected: 1 but found: 2". Null for
        /// any other refusal (including other "expected" messages, such as a tax rate mismatch).
        /// </summary>
        public static long? ExpectedNext(string? message, Series series)
        {
            if (string.IsNullOrEmpty(message)) return null;
            var about = series switch
            {
                Series.Item => message.Contains("itemCd Sequence", StringComparison.OrdinalIgnoreCase),
                Series.Invoice => message.Contains("invcNo", StringComparison.OrdinalIgnoreCase),
                _ => message.Contains("sarNo", StringComparison.OrdinalIgnoreCase),
            };
            return about && ExpectedNumber.Match(message) is { Success: true } m && long.TryParse(m.Groups[1].Value, out var n) ? n : null;
        }

        /// <summary>
        /// Item code: origin country (2) + item type (1) + packaging unit + quantity unit + a 7-digit sequence, e.g. KE2NTBX0000002.
        /// </summary>
        public static string ItemCode(string originCountry, string itemTypeCode, string packagingUnit, string quantityUnit, int sequence) =>
            $"{originCountry}{itemTypeCode}{packagingUnit}{quantityUnit}{sequence:D7}";

        /// <summary>KRA PINs: a letter, nine digits, a letter (P052446551J). Anything else is sent as no PIN.</summary>
        public static bool IsKraPin(string? value) =>
            value is { Length: 11 } && char.IsAsciiLetterUpper(value[0]) && char.IsAsciiLetterUpper(value[10])
            && value[1..10].All(char.IsAsciiDigit);
    }
}
