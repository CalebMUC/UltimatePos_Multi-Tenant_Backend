using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static UltimatePos.Domain.Enums.PosEnums;

namespace UltimatePos.Application.Sales.Dtos
{
    public record CreateSaleLineRequestDto(Guid ProductId, Guid UnitOfMeasureId, decimal Quantity);

    public record CreateSaleRequestDto(
        Guid? CustomerId, PaymentMethod PaymentMethod, string? Notes, IEnumerable<CreateSaleLineRequestDto> Lines,
        decimal? AmountTendered,      // Cash only
        string? MpesaPhoneNumber,     // MpesaStkPush only
        Guid? MpesaTransactionId);    // MpesaTill only — the unmatched transaction being linked

    public record SalePreviewLineDto(Guid ProductId, string ProductName, string Sku, Guid UnitOfMeasureId,
        decimal Quantity, decimal UnitPrice, decimal LineTotal, decimal AvailableInBaseUnits, decimal RequiredInBaseUnits, bool StockSufficient);
    public record SalePreviewDto(Guid? CustomerId, PriceType PriceType, decimal TotalAmount, bool AllStockSufficient, IEnumerable<SalePreviewLineDto> Lines);

    public record SaleLineDto(Guid SaleLineId, Guid ProductId, string ProductName, Guid UnitOfMeasureId, decimal Quantity, decimal UnitPrice, decimal LineTotal);
    public record SaleDto(Guid SaleId, string SaleNumber, Guid? CustomerId, PaymentMethod PaymentMethod, SaleStatus Status,
        decimal TotalAmount, decimal? AmountTendered, decimal? ChangeDue, string? Notes, DateTime CreatedAt, IEnumerable<SaleLineDto> Lines);
}
