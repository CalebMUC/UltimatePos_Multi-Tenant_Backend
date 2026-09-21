using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UltimatePos.Application.Catalog
{
    public interface ICategoryImportParser
    {
        IEnumerable<(int RowNumber, string Name, string Code, string? ParentCode)> Parse(Stream fileStream);
        byte[] GenerateTemplate();
    }
}
