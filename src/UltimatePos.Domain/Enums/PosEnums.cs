using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UltimatePos.Domain.Enums
{
    public class PosEnums
    {
        public enum ItemType { RawMaterial, Packaging, FinishedGood }
        public enum TaxClassification { Standard, ZeroRated, Exempt }
        public enum PriceType { Wholesale, Retail }
        public enum StockMovementType { PurchaseReceipt, Sale, Adjustment, ProductionConsumption, ProductionOutput }
        public enum PurchaseOrderStatus { Draft, Ordered, PartiallyReceived, Received, Cancelled }
    }
}
