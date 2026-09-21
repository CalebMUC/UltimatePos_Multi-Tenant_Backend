using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UltimatePos.Domain.Entities
{
    public class SkuSequence
    {
        public string Prefix { get; set; } = string.Empty;
        public int LastNumber { get; set; }
    }
}
