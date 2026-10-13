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
        public enum StockMovementType { PurchaseReceipt, Sale, Adjustment, ProductionConsumption, ProductionOutput, SaleVoid, Revaluation }
        // Non-inventory costs added to a production run. Packaging that is tracked as a stock item belongs in the formula
        // (it is costed automatically as a consumed ingredient); use Packaging here only for packaging bought outside stock.
        public enum ProductionCostType { Labour, Packaging, Utilities, Transport, Overhead, Other }

        public enum PurchaseOrderStatus { Draft, Ordered, PartiallyReceived, Received, Cancelled }

        public enum SaleStatus { Pending, Completed, Voided, Failed }

        public enum CustomerLedgerEntryType { Invoice, Payment, Reversal }

        public enum PaymentMethod { Cash, Credit, MpesaTill, MpesaStkPush }
        public enum MpesaTransactionType { C2B, StkPush }
        public enum MpesaTransactionStatus { Received, Matched, Pending, Confirmed, Failed }
    }
}
