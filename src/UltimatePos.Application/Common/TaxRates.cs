using static UltimatePos.Domain.Enums.PosEnums;

namespace UltimatePos.Application.Common
{
    /// <summary>
    /// Kenyan VAT rates by tax classification, as percentages. Applied only by VAT-registered businesses. Prices are VAT-inclusive,
    /// so the VAT in a line is LineTotal x rate / (100 + rate). Most medicines are Exempt; basic foods and farm inputs are often
    /// ZeroRated — the product's classification decides, the system does not guess.
    /// </summary>
    public static class TaxRates
    {
        public const decimal Standard = 16m;

        public static decimal For(TaxClassification classification) => classification switch
        {
            TaxClassification.Standard => Standard,
            _ => 0m,
        };
    }
}
