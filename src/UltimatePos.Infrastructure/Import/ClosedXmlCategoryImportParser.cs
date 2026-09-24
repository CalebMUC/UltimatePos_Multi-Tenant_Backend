using ClosedXML.Excel;
using UltimatePos.Application.Catalog;

namespace UltimatePos.Infrastructure.Import;

public class ClosedXmlCategoryImportParser : ICategoryImportParser
{
    public IEnumerable<(int RowNumber, string Name, string Code, string? ParentCode)> Parse(Stream fileStream)
    {
        using var workbook = new XLWorkbook(fileStream);
        var worksheet = workbook.Worksheet(1);

        var rowNumber = 1;
        foreach (var row in worksheet.RowsUsed().Skip(1))
        {
            rowNumber++;
            var name = row.Cell(1).GetString().Trim();
            var code = row.Cell(2).GetString().Trim().ToUpperInvariant();
            var parentCodeRaw = row.Cell(3).GetString().Trim();
            var parentCode = string.IsNullOrWhiteSpace(parentCodeRaw) ? null : parentCodeRaw.ToUpperInvariant();
            yield return (rowNumber, name, code, parentCode);
        }
    }

    public byte[] GenerateTemplate()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Categories");
        sheet.Cell(1, 1).Value = "Name";
        sheet.Cell(1, 2).Value = "Code";
        sheet.Cell(1, 3).Value = "Parent Code";
        sheet.Cell(2, 1).Value = "Animal Feeds";
        sheet.Cell(2, 2).Value = "FEED";
        sheet.Cell(3, 1).Value = "Poultry Feeds";
        sheet.Cell(3, 2).Value = "FEED-POU";
        sheet.Cell(3, 3).Value = "FEED";
        sheet.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}