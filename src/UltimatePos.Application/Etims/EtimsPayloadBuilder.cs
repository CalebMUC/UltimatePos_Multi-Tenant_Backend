namespace UltimatePos.Application.Etims
{
    /// <summary>An item as registered with eTIMS: the codes every document line repeats.</summary>
    public sealed record EtimsItemRef(
        string ItemCode, string ItemClassCode, string ItemName, string? Barcode, string PackagingUnitCode, string QuantityUnitCode,
        string TaxTypeCode);

    /// <param name="Quantity">How many of the item's quantity unit (the product's base unit).</param>
    /// <param name="SupplyAmount">Quantity x price before any discount, VAT included.</param>
    /// <param name="ExpiryDate">Batch expiry, for stock and purchase lines.</param>
    public sealed record EtimsLine(EtimsItemRef Item, decimal Quantity, decimal SupplyAmount, decimal DiscountAmount = 0m, DateOnly? ExpiryDate = null);

    /// <summary>The user a document is registered under (regrId / regrNm). KRA allows 20 characters.</summary>
    public sealed record EtimsUser(string Id, string Name)
    {
        public static readonly EtimsUser System = new("system", "System");
        public static EtimsUser From(string? username) =>
            string.IsNullOrWhiteSpace(username) ? System : new(Clip(username.Trim(), 20), Clip(username.Trim(), 60));
        private static string Clip(string value, int length) => value.Length <= length ? value : value[..length];
    }

    /// <param name="OriginalInvoiceNumber">The sale a credit note reverses; 0 for a sale.</param>
    public sealed record EtimsSaleDocument(
        long InvoiceNumber, long OriginalInvoiceNumber, bool IsCreditNote, string? CustomerTin, string? CustomerName,
        string? CustomerPhone, string PaymentTypeCode, DateTime OccurredAtUtc, string? RefundReasonCode, IReadOnlyList<EtimsLine> Lines,
        string TradeName, string? Address, string? TopMessage, string? BottomMessage, EtimsUser User, string? Remark = null);

    public sealed record EtimsPurchaseDocument(
        long InvoiceNumber, string? SupplierTin, string? SupplierBhfId, string? SupplierName, long? SupplierInvoiceNumber,
        string PaymentTypeCode, DateTime OccurredAtUtc, IReadOnlyList<EtimsLine> Lines, EtimsUser User, string? Remark = null);

    public sealed record EtimsStockDocument(
        long SarNo, long OriginalSarNo, string StockIoTypeCode, DateTime OccurredAtUtc, IReadOnlyList<EtimsLine> Lines, EtimsUser User,
        string? CustomerTin = null, string? CustomerName = null, string? CustomerBhfId = null, string? Remark = null);

    /// <summary>
    /// Builds OSCU request bodies from POS documents. Prices are VAT-inclusive: a line's total is its supply amount less discount, its
    /// tax is total x rate / (100 + rate), and its taxable amount is the rest. Header figures are the sums of the lines by tax type,
    /// so every body reconciles.
    /// </summary>
    public static class EtimsPayloadBuilder
    {
        /// <summary>
        /// Every line is one package of its packaging unit: KRA refuses any other count for NT ("Invalid pkg ... Expected: 1"), and the
        /// quantity says how much.
        /// </summary>
        public const decimal Packages = 1m;

        /// <summary>eTIMS dates and times are East Africa Time (UTC+3), with no zone in the string.</summary>
        public static readonly TimeSpan KenyaOffset = TimeSpan.FromHours(3);

        public static string DateTimeString(DateTime utc) => (utc + KenyaOffset).ToString("yyyyMMddHHmmss");
        public static string DateString(DateTime utc) => (utc + KenyaOffset).ToString("yyyyMMdd");

        public sealed record LineAmounts(decimal Price, decimal SupplyAmount, decimal DiscountRate, decimal DiscountAmount,
            decimal TaxableAmount, decimal TaxAmount, decimal TotalAmount);

        public static LineAmounts Amounts(EtimsLine line)
        {
            var supply = Round(line.SupplyAmount);
            var discount = Round(line.DiscountAmount);
            var total = supply - discount;
            var rate = EtimsCodes.TaxRateFor(line.Item.TaxTypeCode);
            var tax = rate == 0 ? 0m : Round(total * rate / (100m + rate));
            var price = line.Quantity == 0 ? 0m : Round(supply / line.Quantity);
            var discountRate = supply == 0 ? 0m : Round(discount / supply * 100m);
            return new LineAmounts(price, supply, discountRate, discount, total - tax, tax, total);
        }

        private sealed record Totals(IReadOnlyDictionary<string, decimal> Taxable, IReadOnlyDictionary<string, decimal> Tax,
            decimal TotalTaxable, decimal TotalTax, decimal Total);

        private static Totals Sum(IEnumerable<(string TaxType, LineAmounts Amounts)> lines)
        {
            var list = lines.ToList();
            var taxable = EtimsCodes.TaxTypes.ToDictionary(t => t, t => list.Where(l => l.TaxType == t).Sum(l => l.Amounts.TaxableAmount));
            var tax = EtimsCodes.TaxTypes.ToDictionary(t => t, t => list.Where(l => l.TaxType == t).Sum(l => l.Amounts.TaxAmount));
            return new Totals(taxable, tax, taxable.Values.Sum(), tax.Values.Sum(), list.Sum(l => l.Amounts.TotalAmount));
        }

        public static EtimsItemRequest Item(EtimsItemRef item, string itemTypeCode, string originCountryCode, decimal defaultPrice,
            bool isActive, EtimsUser user) =>
            new(item.ItemCode, item.ItemClassCode, itemTypeCode, item.ItemName, null, originCountryCode, item.PackagingUnitCode,
                item.QuantityUnitCode, item.TaxTypeCode, null, item.Barcode, Round(defaultPrice), 0, 0, 0, 0, null, null, null, "N",
                isActive ? "Y" : "N", user.Id, user.Name, user.Id, user.Name);

        public static EtimsSalesRequest Sale(EtimsSaleDocument doc)
        {
            var items = doc.Lines.Select((line, i) =>
            {
                var a = Amounts(line);
                return new EtimsSalesItem(i + 1, line.Item.ItemCode, line.Item.ItemClassCode, line.Item.ItemName, line.Item.Barcode,
                    line.Item.PackagingUnitCode, Packages, line.Item.QuantityUnitCode, line.Quantity, a.Price, a.SupplyAmount,
                    a.DiscountRate, a.DiscountAmount, null, null, null, null, line.Item.TaxTypeCode, a.TaxableAmount, a.TaxAmount,
                    a.TotalAmount);
            }).ToList();
            var t = Sum(items.Select(i => (i.TaxTyCd, new LineAmounts(i.Prc, i.SplyAmt, i.DcRt, i.DcAmt, i.TaxblAmt, i.TaxAmt, i.TotAmt))));

            var confirmed = DateTimeString(doc.OccurredAtUtc);
            var refunded = doc.IsCreditNote ? confirmed : null;
            var customerTin = EtimsCodes.IsKraPin(doc.CustomerTin) ? doc.CustomerTin : null;
            // KRA allows 20 characters for the trade name and each message (910 "length must be between 0 and 20").
            var receipt = new EtimsReceipt(customerTin, doc.CustomerPhone, doc.InvoiceNumber, confirmed, Clip(doc.TradeName, 20), doc.Address,
                Clip(doc.TopMessage, 20), Clip(doc.BottomMessage, 20), "N");

            return new EtimsSalesRequest(doc.InvoiceNumber, doc.IsCreditNote ? doc.OriginalInvoiceNumber : 0, customerTin,
                doc.CustomerName, EtimsCodes.SalesTypeNormal, doc.IsCreditNote ? EtimsCodes.ReceiptCreditNote : EtimsCodes.ReceiptSale,
                doc.PaymentTypeCode, EtimsCodes.StatusApproved, confirmed, DateString(doc.OccurredAtUtc), confirmed, refunded, refunded,
                refunded, doc.IsCreditNote ? doc.RefundReasonCode : null, items.Count,
                t.Taxable["A"], t.Taxable["B"], t.Taxable["C"], t.Taxable["D"], t.Taxable["E"],
                Rate("A"), Rate("B"), Rate("C"), Rate("D"), Rate("E"),
                t.Tax["A"], t.Tax["B"], t.Tax["C"], t.Tax["D"], t.Tax["E"],
                t.TotalTaxable, t.TotalTax, t.Total, "N", doc.Remark,
                doc.User.Id, doc.User.Name, doc.User.Id, doc.User.Name, receipt, items);
        }

        public static EtimsPurchaseRequest Purchase(EtimsPurchaseDocument doc)
        {
            var items = doc.Lines.Select((line, i) =>
            {
                var a = Amounts(line);
                return new EtimsPurchaseItem(i + 1, line.Item.ItemCode, line.Item.ItemClassCode, line.Item.ItemName, line.Item.Barcode,
                    null, null, null, line.Item.PackagingUnitCode, Packages, line.Item.QuantityUnitCode, line.Quantity, a.Price,
                    a.SupplyAmount, a.DiscountRate, a.DiscountAmount, a.TaxableAmount, line.Item.TaxTypeCode, a.TaxAmount, a.TotalAmount,
                    line.ExpiryDate?.ToString("yyyyMMdd"));
            }).ToList();
            var t = Sum(items.Select(i => (i.TaxTyCd, new LineAmounts(i.Prc, i.SplyAmt, i.DcRt, i.DcAmt, i.TaxblAmt, i.TaxAmt, i.TotAmt))));
            var confirmed = DateTimeString(doc.OccurredAtUtc);
            var supplierTin = EtimsCodes.IsKraPin(doc.SupplierTin) ? doc.SupplierTin : null;

            return new EtimsPurchaseRequest(doc.InvoiceNumber, 0, supplierTin, supplierTin is null ? null : doc.SupplierBhfId ?? "00",
                doc.SupplierName, doc.SupplierInvoiceNumber, EtimsCodes.RegistrationManual, EtimsCodes.SalesTypeNormal,
                EtimsCodes.ReceiptPurchase, doc.PaymentTypeCode, EtimsCodes.StatusApproved, confirmed, DateString(doc.OccurredAtUtc),
                confirmed, null, null, null, items.Count,
                t.Taxable["A"], t.Taxable["B"], t.Taxable["C"], t.Taxable["D"], t.Taxable["E"],
                Rate("A"), Rate("B"), Rate("C"), Rate("D"), Rate("E"),
                t.Tax["A"], t.Tax["B"], t.Tax["C"], t.Tax["D"], t.Tax["E"],
                t.TotalTaxable, t.TotalTax, t.Total, doc.Remark, doc.User.Id, doc.User.Name, doc.User.Id, doc.User.Name, items);
        }

        public static EtimsStockIoRequest StockIo(EtimsStockDocument doc)
        {
            var items = doc.Lines.Select((line, i) =>
            {
                var a = Amounts(line);
                return new EtimsStockIoItem(i + 1, line.Item.ItemCode, line.Item.ItemClassCode, line.Item.ItemName, line.Item.Barcode,
                    line.Item.PackagingUnitCode, Packages, line.Item.QuantityUnitCode, line.Quantity,
                    line.ExpiryDate?.ToString("yyyyMMdd"), a.Price, a.SupplyAmount, a.DiscountAmount, a.TaxableAmount,
                    line.Item.TaxTypeCode, a.TaxAmount, a.TotalAmount);
            }).ToList();

            return new EtimsStockIoRequest(doc.SarNo, doc.OriginalSarNo, EtimsCodes.RegistrationManual, doc.CustomerTin, doc.CustomerName,
                doc.CustomerBhfId, doc.StockIoTypeCode, DateString(doc.OccurredAtUtc), items.Count, items.Sum(i => i.TaxblAmt),
                items.Sum(i => i.TaxAmt), items.Sum(i => i.TotAmt), doc.Remark, doc.User.Id, doc.User.Name, doc.User.Id, doc.User.Name, items);
        }

        public static EtimsStockMasterRequest StockMaster(string itemCode, decimal quantityOnHand, EtimsUser user) =>
            new(itemCode, quantityOnHand, user.Id, user.Name, user.Id, user.Name);

        private static decimal Rate(string taxType) => EtimsCodes.HeaderTaxRateFor(taxType);

        private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

        [return: System.Diagnostics.CodeAnalysis.NotNullIfNotNull(nameof(value))]
        private static string? Clip(string? value, int length) => value is null || value.Length <= length ? value : value[..length].TrimEnd();
    }
}
