using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static UltimatePos.Domain.Enums.PosEnums;

namespace UltimatePos.Domain.Entities
{
    public class MpesaTransaction
    {
        public Guid MpesaTransactionId { get; set; }
        public MpesaTransactionType TransactionType { get; set; }
        public MpesaTransactionStatus Status { get; set; }
        public string? MpesaReceiptNumber { get; set; }
        public string PhoneNumber { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public DateTime? TransactionDate { get; set; }
        public string? MerchantRequestId { get; set; }
        public string? CheckoutRequestId { get; set; }
        public Guid? SaleId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public Guid? CreatedBy { get; set; }
        public DateTime? LastUpdatedAt { get; set; }

        public Sale? Sale { get; set; }
    }
}
