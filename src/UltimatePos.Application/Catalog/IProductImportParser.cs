namespace UltimatePos.Application.Catalog;

public interface IProductImportParser
{
    IEnumerable<(int RowNumber, string Name, string CategoryCode, string ItemType, string TaxClassification,
        string UnitSymbol, decimal? ReorderLevel, decimal? WholesalePrice, decimal? RetailPrice, string? Description, string? Brand)> Parse(Stream fileStream);
    byte[] GenerateTemplate();
}