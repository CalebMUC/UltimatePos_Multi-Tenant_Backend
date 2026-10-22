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
        public enum StockMovementType { PurchaseReceipt, Sale, Adjustment, ProductionConsumption, ProductionOutput, SaleVoid, Revaluation, SaleReturn, TransferOut, TransferIn }
        // Non-inventory costs added to a production run. Packaging that is tracked as a stock item belongs in the formula
        // (it is costed automatically as a consumed ingredient); use Packaging here only for packaging bought outside stock.
        public enum ProductionCostType { Labour, Packaging, Utilities, Transport, Overhead, Other }

        public enum PurchaseOrderStatus { Draft, Ordered, PartiallyReceived, Received, Cancelled }

        public enum SaleStatus { Pending, Completed, Voided, Failed }

        // Return = a credit note for goods a credit customer brought back. Only Invoice increases what the customer owes.
        public enum CustomerLedgerEntryType { Invoice, Payment, Reversal, Return }

        // MpesaReference: paid to the business's M-Pesa outside the POS; the cashier types the code from the customer's SMS. Not checked
        // with Safaricom (until till payments reach the POS through C2B).
        public enum PaymentMethod { Cash, Credit, MpesaTill, MpesaStkPush, MpesaReference }
        public enum MpesaTransactionType { C2B, StkPush }
        public enum MpesaTransactionStatus { Received, Matched, Pending, Confirmed, Failed }


        // How an operating expense was PAID. Expenses are recorded when paid (cash basis); unpaid bills / payables are not modelled yet.
        public enum ExpensePaymentMethod { Cash, Mpesa, BankTransfer, Cheque }
        public enum ExpenseStatus { Posted, Voided }
        // How money changed hands when a customer settled their account, or the business paid a supplier.
        public enum SettlementMethod { Cash, Mpesa, BankTransfer, Cheque }
        // Bill = goods received on account (raises what the business owes); Payment and Reversal reduce it.
        public enum SupplierLedgerEntryType { Bill, Payment, Reversal }
        // How the customer was paid back for returned goods. CreditNote credits a registered customer's account instead of paying out.
        public enum RefundMethod { Cash, Mpesa, CreditNote }

        // How the goods reached the customer. Rider: rung up at the till as a delivery order, and a sale only once the rider is back with
        // the money (or the M-Pesa code).
        public enum DeliveryMode { Counter, Rider }

        // A till's day: Open from the float being counted in until the cash is counted out.
        public enum CashSessionStatus { Open, Closed }
        // Cash put into (float top-up, rider handing back change) or taken out of (banking, change given to a rider) the drawer during the day.
        public enum CashMovementType { PayIn, PayOut }
    }
}
