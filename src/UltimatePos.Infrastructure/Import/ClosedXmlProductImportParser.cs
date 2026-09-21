using ClosedXML.Excel;
using UltimatePos.Application.Catalog;

namespace UltimatePos.Infrastructure.Import;

public class ClosedXmlProductImportParser : IProductImportParser
{
    public IEnumerable<(int RowNumber, string Name, string CategoryCode, string ItemType, string TaxClassification,
        string UnitSymbol, decimal? ReorderLevel, decimal? WholesalePrice, decimal? RetailPrice)> Parse(Stream fileStream)
    {
        using var workbook = new XLWorkbook(fileStream);
        var worksheet = workbook.Worksheet(1);

        var rowNumber = 1;
        foreach (var row in worksheet.RowsUsed().Skip(1))
        {
            rowNumber++;
            var name = row.Cell(1).GetString().Trim();
            var categoryCode = row.Cell(2).GetString().Trim().ToUpperInvariant();
            var itemType = row.Cell(3).GetString().Trim();
            var taxClassification = row.Cell(4).GetString().Trim();
            var unitSymbol = row.Cell(5).GetString().Trim();
            var reorderLevel = ParseDecimal(row.Cell(6));
            var wholesalePrice = ParseDecimal(row.Cell(7));
            var retailPrice = ParseDecimal(row.Cell(8));

            yield return (rowNumber, name, categoryCode, itemType, taxClassification, unitSymbol, reorderLevel, wholesalePrice, retailPrice);
        }
    }

    private static decimal? ParseDecimal(IXLCell cell) => cell.TryGetValue<decimal>(out var value) ? value : null;

    public byte[] GenerateTemplate()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Products");
        string[] headers = { "Name", "Category Code", "Item Type", "Tax Classification", "Unit Symbol", "Reorder Level", "Wholesale Price", "Retail Price" };
        for (var i = 0; i < headers.Length; i++)
            sheet.Cell(1, i + 1).Value = headers[i];

        sheet.Cell(2, 1).Value = "Layer Mash 50kg";
        sheet.Cell(2, 2).Value = "FEED-POU";
        sheet.Cell(2, 3).Value = "FinishedGood";
        sheet.Cell(2, 4).Value = "Standard";
        sheet.Cell(2, 5).Value = "bag";
        sheet.Cell(2, 6).Value = 20;
        sheet.Cell(2, 7).Value = 2400;
        sheet.Cell(2, 8).Value = 2650;
        sheet.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}