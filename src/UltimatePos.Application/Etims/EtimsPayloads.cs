using System.Text.Encodings.Web;
using System.Text.Json;

namespace UltimatePos.Application.Etims
{
    /// <summary>
    /// Request bodies of the KRA eTIMS OSCU API. Property names are KRA's field names in PascalCase; <see cref="EtimsJson.Options"/>
    /// writes them camelCase (taxblAmtA, dvcSrlNo ...). Nulls are written, as in KRA's samples.
    /// </summary>
    public static class EtimsJson
    {
        public static readonly JsonSerializerOptions Options = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        public static string Serialize<T>(T body) => JsonSerializer.Serialize(body, Options);
    }

    public sealed record EtimsInitializeRequest(string Tin, string BhfId, string DvcSrlNo);

    /// <summary>Body of every "select" read: what changed since lastReqDt (yyyyMMddHHmmss).</summary>
    public sealed record EtimsLastRequest(string Tin, string BhfId, string LastReqDt);

    /// <summary>KRA's spec shows only custmTin; the gateway also wants tin, bhfId and lastReqDt.</summary>
    public sealed record EtimsCustomerPinRequest(string Tin, string BhfId, string LastReqDt, string CustmTin);

    public sealed record EtimsInvoiceDetailRequest(string Tin, string BhfId, string InvcNo);

    public sealed record EtimsItemRequest(
        string ItemCd, string ItemClsCd, string ItemTyCd, string ItemNm, string? ItemStdNm, string OrgnNatCd, string PkgUnitCd,
        string QtyUnitCd, string TaxTyCd, string? BtchNo, string? Bcd, decimal DftPrc, decimal? GrpPrcL1, decimal? GrpPrcL2,
        decimal? GrpPrcL3, decimal? GrpPrcL4, decimal? GrpPrcL5, string? AddInfo, decimal? SftyQty, string IsrcAplcbYn, string UseYn,
        string RegrId, string RegrNm, string ModrId, string ModrNm);

    public sealed record EtimsItemCompositionRequest(string ItemCd, string CpstItemCd, decimal CpstQty, string RegrId, string RegrNm);

    public sealed record EtimsBranchUserRequest(
        string UserId, string UserNm, string Pwd, string? Adrs, string? Cntc, string? AuthCd, string? Remark, string UseYn,
        string RegrId, string RegrNm, string ModrId, string ModrNm);

    public sealed record EtimsBranchCustomerRequest(
        string CustNo, string CustTin, string CustNm, string? Adrs, string? TelNo, string? Email, string? FaxNo, string UseYn,
        string? Remark, string RegrId, string RegrNm, string ModrId, string ModrNm);

    public sealed record EtimsBranchInsuranceRequest(
        string IsrccCd, string IsrccNm, decimal IsrcRt, string UseYn, string RegrId, string RegrNm, string ModrId, string ModrNm);

    public sealed record EtimsImportedItemConvertedRequest(
        string TaskCd, string DclDe, int ItemSeq, string HsCd, string ItemClsCd, string ItemCd, string ImptItemSttsCd, string? Remark,
        string ModrId, string ModrNm);

    public sealed record EtimsStockMasterRequest(string ItemCd, decimal RsdQty, string RegrId, string RegrNm, string ModrId, string ModrNm);

    public sealed record EtimsSalesRequest(
        long InvcNo, long OrgInvcNo, string? CustTin, string? CustNm, string SalesTyCd, string RcptTyCd, string PmtTyCd,
        string SalesSttsCd, string CfmDt, string SalesDt, string? StockRlsDt, string? CnclReqDt, string? CnclDt, string? RfdDt,
        string? RfdRsnCd, int TotItemCnt,
        decimal TaxblAmtA, decimal TaxblAmtB, decimal TaxblAmtC, decimal TaxblAmtD, decimal TaxblAmtE,
        decimal TaxRtA, decimal TaxRtB, decimal TaxRtC, decimal TaxRtD, decimal TaxRtE,
        decimal TaxAmtA, decimal TaxAmtB, decimal TaxAmtC, decimal TaxAmtD, decimal TaxAmtE,
        decimal TotTaxblAmt, decimal TotTaxAmt, decimal TotAmt, string PrchrAcptcYn, string? Remark,
        string RegrId, string RegrNm, string ModrId, string ModrNm, EtimsReceipt Receipt, IReadOnlyList<EtimsSalesItem> ItemList);

    public sealed record EtimsReceipt(
        string? CustTin, string? CustMblNo, long RptNo, string RcptPbctDt, string TrdeNm, string? Adrs, string? TopMsg, string? BtmMsg,
        string PrchrAcptcYn);

    public sealed record EtimsSalesItem(
        int ItemSeq, string ItemCd, string ItemClsCd, string ItemNm, string? Bcd, string PkgUnitCd, decimal Pkg, string QtyUnitCd,
        decimal Qty, decimal Prc, decimal SplyAmt, decimal DcRt, decimal DcAmt, string? IsrccCd, string? IsrccNm, decimal? IsrcRt,
        decimal? IsrcAmt, string TaxTyCd, decimal TaxblAmt, decimal TaxAmt, decimal TotAmt);

    public sealed record EtimsPurchaseRequest(
        long InvcNo, long OrgInvcNo, string? SpplrTin, string? SpplrBhfId, string? SpplrNm, long? SpplrInvcNo, string RegTyCd,
        string PchsTyCd, string RcptTyCd, string PmtTyCd, string PchsSttsCd, string CfmDt, string PchsDt, string? WrhsDt,
        string? CnclReqDt, string? CnclDt, string? RfdDt, int TotItemCnt,
        decimal TaxblAmtA, decimal TaxblAmtB, decimal TaxblAmtC, decimal TaxblAmtD, decimal TaxblAmtE,
        decimal TaxRtA, decimal TaxRtB, decimal TaxRtC, decimal TaxRtD, decimal TaxRtE,
        decimal TaxAmtA, decimal TaxAmtB, decimal TaxAmtC, decimal TaxAmtD, decimal TaxAmtE,
        decimal TotTaxblAmt, decimal TotTaxAmt, decimal TotAmt, string? Remark,
        string RegrId, string RegrNm, string ModrId, string ModrNm, IReadOnlyList<EtimsPurchaseItem> ItemList);

    public sealed record EtimsPurchaseItem(
        int ItemSeq, string ItemCd, string ItemClsCd, string ItemNm, string? Bcd, string? SpplrItemClsCd, string? SpplrItemCd,
        string? SpplrItemNm, string PkgUnitCd, decimal Pkg, string QtyUnitCd, decimal Qty, decimal Prc, decimal SplyAmt, decimal DcRt,
        decimal DcAmt, decimal TaxblAmt, string TaxTyCd, decimal TaxAmt, decimal TotAmt, string? ItemExprDt);

    public sealed record EtimsStockIoRequest(
        long SarNo, long OrgSarNo, string RegTyCd, string? CustTin, string? CustNm, string? CustBhfId, string SarTyCd, string OcrnDt,
        int TotItemCnt, decimal TotTaxblAmt, decimal TotTaxAmt, decimal TotAmt, string? Remark,
        string RegrId, string RegrNm, string ModrId, string ModrNm, IReadOnlyList<EtimsStockIoItem> ItemList);

    public sealed record EtimsStockIoItem(
        int ItemSeq, string ItemCd, string ItemClsCd, string ItemNm, string? Bcd, string PkgUnitCd, decimal Pkg, string QtyUnitCd,
        decimal Qty, string? ItemExprDt, decimal Prc, decimal SplyAmt, decimal TotDcAmt, decimal TaxblAmt, string TaxTyCd,
        decimal TaxAmt, decimal TotAmt);
}
