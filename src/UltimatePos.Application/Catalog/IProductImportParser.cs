using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UltimatePos.Application.Catalog
{
    public interface IProductImportParser
    {
        IEnumerable<(int RowNumber, string Name, string CategoryCode, string ItemType, string TaxClassification,
            string UnitSymbol, decimal? ReorderLevel, decimal? WholesalePrice, decimal? RetailPrice)> Parse(Stream fileStream);
        byte[] GenerateTemplate();
    }
}
