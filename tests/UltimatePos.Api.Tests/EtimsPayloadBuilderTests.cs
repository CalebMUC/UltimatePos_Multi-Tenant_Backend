using System.Text.Json;
using UltimatePos.Application.Etims;
using static UltimatePos.Domain.Enums.PosEnums;

namespace Ultimate.Api.Tests;

/// <summary>
/// The OSCU bodies the POS builds. The figures are those of the Postman sandbox collection (Test Product A at 1,160 incl. 16% VAT,
/// Test Product B at 500 exempt, raw material C at 232), so a body here matches one KRA's sandbox has accepted.
/// </summary>
public class EtimsPayloadBuilderTests
{
    private static readonly EtimsItemRef ProductA = new("KE2NTKG0000001", "1010160100", "Test Product A", null, "NT", "KG", EtimsCodes.TaxStandard);
    private static readonly EtimsItemRef ProductB = new("KE2NTBX0000002", "1010150000", "Test Product B", null, "NT", "BX", EtimsCodes.TaxExempt);
    private static readonly EtimsItemRef RawC = new("KE1NTKG0000003", "1010160100", "Test Raw Material C", null, "NT", "KG", EtimsCodes.TaxStandard);
    private static readonly EtimsUser Admin = new("Admin", "Admin");
    private static readonly DateTime At = new(2026, 10, 27, 21, 30, 15, DateTimeKind.Utc); // 00:30:15 on the 28th in Nairobi

    private static EtimsSaleDocument CollectionSale(bool creditNote = false) => new(
        creditNote ? 2 : 1, creditNote ? 1 : 0, creditNote, "A123456789Z", "TEST CUSTOMER LIMITED", null, "01", At,
        creditNote ? EtimsCodes.RefundReasonReturn : null,
        new[] { new EtimsLine(ProductA, 2, 2320m), new EtimsLine(ProductB, 5, 2500m) },
        "Quickcrate Express Limited", "Kenya", "Thank you for shopping with us", "Welcome again", Admin);

    [Fact]
    public void A_sale_splits_VAT_inclusive_prices_by_tax_type_and_reconciles_like_the_sandbox_collection()
    {
        var body = EtimsPayloadBuilder.Sale(CollectionSale());

        Assert.Equal((2500m, 2000m, 0m, 0m, 0m), (body.TaxblAmtA, body.TaxblAmtB, body.TaxblAmtC, body.TaxblAmtD, body.TaxblAmtE));
        Assert.Equal((0m, 320m), (body.TaxAmtA, body.TaxAmtB));
        Assert.Equal((0m, 16m, 0m, 0m, 0m), (body.TaxRtA, body.TaxRtB, body.TaxRtC, body.TaxRtD, body.TaxRtE)); // E is switched off in KRA's code list
        Assert.Equal((4500m, 320m, 4820m), (body.TotTaxblAmt, body.TotTaxAmt, body.TotAmt));
        Assert.Equal(2, body.TotItemCnt);

        var a = body.ItemList[0];
        Assert.Equal((1, 1160m, 2320m, 2000m, 320m, 2320m), (a.ItemSeq, a.Prc, a.SplyAmt, a.TaxblAmt, a.TaxAmt, a.TotAmt));
        var b = body.ItemList[1];
        Assert.Equal((500m, 2500m, 2500m, 0m, 2500m), (b.Prc, b.SplyAmt, b.TaxblAmt, b.TaxAmt, b.TotAmt));

        Assert.Equal((EtimsCodes.ReceiptSale, 0L, (string?)null, (string?)null), (body.RcptTyCd, body.OrgInvcNo, body.RfdRsnCd, body.RfdDt));
        Assert.Equal(("N", "02", "N"), (body.SalesTyCd, body.SalesSttsCd, body.PrchrAcptcYn));
        Assert.Equal(1L, body.Receipt.RptNo);
        Assert.Equal("A123456789Z", body.Receipt.CustTin);
    }

    [Fact]
    public void Dates_are_East_Africa_Time()
    {
        var body = EtimsPayloadBuilder.Sale(CollectionSale());
        Assert.Equal("20261028003015", body.CfmDt);
        Assert.Equal("20261028", body.SalesDt);
        Assert.Equal(body.CfmDt, body.Receipt.RcptPbctDt);
    }

    [Fact]
    public void A_credit_note_names_the_sale_it_reverses_and_why()
    {
        var body = EtimsPayloadBuilder.Sale(CollectionSale(creditNote: true));
        Assert.Equal((EtimsCodes.ReceiptCreditNote, 2L, 1L, "06"), (body.RcptTyCd, body.InvcNo, body.OrgInvcNo, body.RfdRsnCd));
        Assert.Equal(body.CfmDt, body.RfdDt);
        Assert.Equal(4820m, body.TotAmt); // amounts stay positive, as in KRA's sample
    }

    [Fact]
    public void A_discount_comes_off_before_tax()
    {
        var a = EtimsPayloadBuilder.Amounts(new EtimsLine(ProductA, 1, 1160m, 116m));
        Assert.Equal((10m, 116m, 1044m, 144m, 900m), (a.DiscountRate, a.DiscountAmount, a.TotalAmount, a.TaxAmount, a.TaxableAmount));
    }

    [Fact]
    public void Price_is_per_quantity_unit_when_sold_by_the_pack()
    {
        // A bale of 12 packets at 1,980: reported as 12 packets.
        var a = EtimsPayloadBuilder.Amounts(new EtimsLine(ProductA with { QuantityUnitCode = "PA" }, 12, 1980m));
        Assert.Equal((165m, 1980m), (a.Price, a.SupplyAmount));
    }

    [Fact]
    public void An_invalid_customer_PIN_is_left_out_rather_than_sent()
    {
        var body = EtimsPayloadBuilder.Sale(CollectionSale() with { CustomerTin = "not-a-pin" });
        Assert.Null(body.CustTin);
        Assert.Null(body.Receipt.CustTin);
        Assert.Equal("TEST CUSTOMER LIMITED", body.CustNm);
    }

    [Fact]
    public void A_purchase_matches_the_sandbox_collection()
    {
        var body = EtimsPayloadBuilder.Purchase(new EtimsPurchaseDocument(1, "P000000000D", null, "TEST SUPPLIER", 1, "01", At,
            new[] { new EtimsLine(RawC, 50, 11600m) }, Admin));
        Assert.Equal((10000m, 1600m, 11600m), (body.TaxblAmtB, body.TaxAmtB, body.TotAmt));
        Assert.Equal(("P", "N", "02", "M"), (body.RcptTyCd, body.PchsTyCd, body.PchsSttsCd, body.RegTyCd));
        Assert.Equal(("P000000000D", "00"), (body.SpplrTin, body.SpplrBhfId));
        Assert.Equal(232m, body.ItemList.Single().Prc);
    }

    [Fact]
    public void A_purchase_from_a_supplier_without_a_PIN_names_no_PIN_or_branch()
    {
        var body = EtimsPayloadBuilder.Purchase(new EtimsPurchaseDocument(1, null, null, "Market stall", null, "01", At,
            new[] { new EtimsLine(RawC, 1, 232m) }, Admin));
        Assert.Null(body.SpplrTin);
        Assert.Null(body.SpplrBhfId);
    }

    [Fact]
    public void Opening_stock_matches_the_sandbox_collection()
    {
        var body = EtimsPayloadBuilder.StockIo(new EtimsStockDocument(1, 0, "06", At,
            new[] { new EtimsLine(ProductA, 100, 116000m), new EtimsLine(ProductB, 100, 50000m) }, Admin));
        Assert.Equal((150000m, 16000m, 166000m, 2), (body.TotTaxblAmt, body.TotTaxAmt, body.TotAmt, body.TotItemCnt));
        Assert.Equal(("06", "20261028", "M"), (body.SarTyCd, body.OcrnDt, body.RegTyCd));
    }

    [Fact]
    public void Bodies_use_KRA_field_names_and_keep_nulls()
    {
        var item = EtimsPayloadBuilder.Item(ProductA, "2", "KE", 1160m, isActive: true, Admin);
        using var json = JsonDocument.Parse(EtimsJson.Serialize(item));
        var root = json.RootElement;
        Assert.Equal("KE2NTKG0000001", root.GetProperty("itemCd").GetString());
        Assert.Equal("1010160100", root.GetProperty("itemClsCd").GetString());
        Assert.Equal("Y", root.GetProperty("useYn").GetString());
        Assert.Equal("N", root.GetProperty("isrcAplcbYn").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("btchNo").ValueKind);
        Assert.Equal(1160m, root.GetProperty("dftPrc").GetDecimal());

        using var sale = JsonDocument.Parse(EtimsJson.Serialize(EtimsPayloadBuilder.Sale(CollectionSale())));
        Assert.Equal(2500m, sale.RootElement.GetProperty("taxblAmtA").GetDecimal());
        Assert.Equal(1, sale.RootElement.GetProperty("itemList")[0].GetProperty("itemSeq").GetInt32());
        Assert.Equal("Quickcrate Express L", sale.RootElement.GetProperty("receipt").GetProperty("trdeNm").GetString());
    }

    [Fact]
    public void Every_line_is_one_package_and_receipt_texts_fit_KRA_s_20_characters()
    {
        var body = EtimsPayloadBuilder.Sale(CollectionSale());
        Assert.All(body.ItemList, i => Assert.Equal(1m, i.Pkg)); // KRA: "Invalid pkg ... Expected: 1"
        Assert.Equal((2m, 5m), (body.ItemList[0].Qty, body.ItemList[1].Qty));
        Assert.Equal("Quickcrate Express L", body.Receipt.TrdeNm);
        Assert.Equal("Thank you for shoppi", body.Receipt.TopMsg);
        Assert.Equal("Welcome again", body.Receipt.BtmMsg);
    }

    [Theory]
    [InlineData("Invalid itemCd Sequence. Expected sequence ending with: ********13 (The itemCd is either reused or not incremented properly.)", EtimsCodes.Series.Item, 13L)]
    [InlineData("Validation failed (Invalid sarNo: Expected: 2 but found: 4316026)", EtimsCodes.Series.StockAdjustment, 2L)]
    [InlineData("Invalid invcNo sequence, expected: 1 but found: 4319026 (Invc No: 4319026 is invalid, use the expected value: 1)", EtimsCodes.Series.Invoice, 1L)]
    [InlineData("Validation failed (Rule taxRtE failed: Tax rate mismatch. Expected: 0.00, Found: 8)", EtimsCodes.Series.Invoice, null)]
    [InlineData("Invalid sarNo: Expected: 2 but found: 7", EtimsCodes.Series.Invoice, null)] // about another series
    [InlineData(null, EtimsCodes.Series.Item, null)]
    public void The_number_KRA_expects_is_read_from_its_refusal(string? message, EtimsCodes.Series series, long? expected) =>
        Assert.Equal(expected, EtimsCodes.ExpectedNext(message, series));

    [Theory]
    [InlineData("KG", true)]
    [InlineData("NO", true)]
    [InlineData("LTR", false)]
    [InlineData("U", false)]
    [InlineData(null, false)]
    public void Only_two_letter_units_make_an_item_code_KRA_accepts(string? code, bool expected) =>
        Assert.Equal(expected, EtimsCodes.IsUsableQuantityUnit(code));

    [Fact]
    public void A_deactivated_item_is_registered_as_not_in_use() =>
        Assert.Equal("N", EtimsPayloadBuilder.Item(ProductA, "2", "KE", 0m, isActive: false, Admin).UseYn);

    // ---- Codes ----

    [Fact]
    public void Item_codes_are_country_type_packaging_unit_and_sequence() =>
        Assert.Equal("KE2NTBX0000002", EtimsCodes.ItemCode("KE", "2", "NT", "BX", 2));

    [Theory]
    [InlineData(TaxClassification.Standard, true, "B")]
    [InlineData(TaxClassification.ZeroRated, true, "C")]
    [InlineData(TaxClassification.Exempt, true, "A")]
    [InlineData(TaxClassification.Standard, false, "D")] // not VAT-registered: nothing carries VAT
    public void Tax_type_follows_classification_and_VAT_registration(TaxClassification classification, bool vatRegistered, string expected) =>
        Assert.Equal(expected, EtimsCodes.TaxTypeFor(classification, vatRegistered));

    [Theory]
    [InlineData("kg", null, "KG")]
    [InlineData(null, "Box", "BX")]
    [InlineData("pkt", null, "PA")]
    [InlineData("pcs", "Pieces", EtimsCodes.DefaultQuantityUnit)]
    [InlineData("L", "Litre", EtimsCodes.DefaultQuantityUnit)] // KRA refuses item codes built on LTR, L or U
    [InlineData("g", null, EtimsCodes.DefaultQuantityUnit)]
    public void Units_of_measure_map_to_KRA_quantity_units(string? symbol, string? name, string expected) =>
        Assert.Equal(expected, EtimsCodes.QuantityUnitFor(symbol, name));

    [Theory]
    [InlineData(StockMovementType.PurchaseReceipt, 10, "02")]
    [InlineData(StockMovementType.SaleReturn, 1, "03")]
    [InlineData(StockMovementType.SaleVoid, 1, "03")]
    [InlineData(StockMovementType.TransferIn, 5, "04")]
    [InlineData(StockMovementType.ProductionOutput, 5, "05")]
    [InlineData(StockMovementType.Adjustment, 5, "06")]
    [InlineData(StockMovementType.Sale, -2, "11")]
    [InlineData(StockMovementType.TransferOut, -5, "13")]
    [InlineData(StockMovementType.ProductionConsumption, -5, "14")]
    [InlineData(StockMovementType.Adjustment, -5, "16")]
    [InlineData(StockMovementType.Revaluation, 0, null)]
    public void Stock_movements_map_to_stock_in_out_types(StockMovementType type, decimal change, string? expected) =>
        Assert.Equal(expected, EtimsCodes.StockIoTypeFor(type, change));

    [Theory]
    [InlineData("P052446551J", true)]
    [InlineData("A123456789Z", true)]
    [InlineData("p052446551j", false)]
    [InlineData("P05244655J", false)]
    [InlineData(null, false)]
    public void KRA_PINs_are_recognised(string? value, bool expected) => Assert.Equal(expected, EtimsCodes.IsKraPin(value));

    [Theory]
    [InlineData(PaymentMethod.Cash, "01")]
    [InlineData(PaymentMethod.Credit, "02")]
    [InlineData(PaymentMethod.MpesaTill, "06")]
    [InlineData(PaymentMethod.MpesaStkPush, "06")]
    public void Payment_methods_map_to_KRA_payment_types(PaymentMethod method, string expected) =>
        Assert.Equal(expected, EtimsCodes.PaymentTypeFor(method));

    [Fact]
    public void Retries_back_off_to_at_most_an_hour()
    {
        Assert.Equal(TimeSpan.FromMinutes(1), EtimsSyncService.Backoff(1));
        Assert.Equal(TimeSpan.FromMinutes(8), EtimsSyncService.Backoff(4));
        Assert.Equal(TimeSpan.FromMinutes(60), EtimsSyncService.Backoff(12));
    }
}
