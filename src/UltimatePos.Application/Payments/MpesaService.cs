using Serilog;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using UltimatePos.Application.Payments.Dtos;
using UltimatePos.Application.Sales;
using UltimatePos.Domain.Entities;
using UltimatePos.Domain.Exceptions;
using static UltimatePos.Domain.Enums.PosEnums;

namespace UltimatePos.Application.Payments
{
    public class MpesaService
    {
        private readonly IMpesaRepository _repository;
        private readonly IMpesaClient _client;
        private readonly SalesService _salesService;
        private readonly ILogger<MpesaService> _logger;

        public MpesaService(IMpesaRepository repository, IMpesaClient client, SalesService salesService, ILogger<MpesaService> logger)
        {
            _repository = repository;
            _client = client;
            _salesService = salesService;
            _logger = logger;
        }

        // Called by the webhook controller. Deliberately swallows and logs rather than throwing — a thrown exception
        // here would mean Safaricom retries the same callback indefinitely; the reconciliation endpoint below is the
        // backstop for anything genuinely lost.
        public async Task HandleStkCallbackAsync(StkCallback callback)
        {
            var transaction = await _repository.GetByCheckoutRequestIdAsync(callback.CheckoutRequestID);
            if (transaction is null)
            {
                _logger.LogWarning("STK callback for unknown CheckoutRequestID {Id}", callback.CheckoutRequestID);
                return;
            }
            if (transaction.Status != MpesaTransactionStatus.Pending || transaction.SaleId is null)
                return; // already processed — duplicate callback, idempotent no-op

            if (callback.ResultCode == 0)
            {
                var receiptNumber = GetMetadataString(callback.CallbackMetadata, "MpesaReceiptNumber");
                var transactionDate = ParseMpesaDate(GetMetadataString(callback.CallbackMetadata, "TransactionDate"));

                await _repository.ConfirmAsync(transaction.MpesaTransactionId, receiptNumber, transactionDate);
                await _salesService.CompletePendingMpesaSaleAsync(transaction.SaleId.Value, receiptNumber);
            }
            else
            {
                await _repository.FailAsync(transaction.MpesaTransactionId);
                await _salesService.FailPendingMpesaSaleAsync(transaction.SaleId.Value, callback.ResultDesc);
            }
        }

        public async Task HandleC2bConfirmationAsync(C2bConfirmationPayload payload)
        {
            await _repository.CreateAsync(new MpesaTransaction
            {
                MpesaTransactionId = Guid.NewGuid(),
                TransactionType = MpesaTransactionType.C2B,
                Status = MpesaTransactionStatus.Received,
                MpesaReceiptNumber = payload.TransID,
                PhoneNumber = payload.MSISDN,
                Amount = decimal.Parse(payload.TransAmount, CultureInfo.InvariantCulture),
                TransactionDate = ParseMpesaDate(payload.TransTime)
            });
        }

        public Task RegisterC2BUrlsAsync(string confirmationUrl, string validationUrl) =>
            _client.RegisterC2BUrlsAsync(confirmationUrl, validationUrl);

        public async Task<IEnumerable<MpesaTransactionDto>> GetUnmatchedTillTransactionsAsync() =>
            (await _repository.GetUnmatchedAsync()).Select(ToDto);

        // On-demand reconciliation for a Pending STK sale whose callback never arrived.
        public async Task<Sales.Dtos.SaleDto> ReconcileStkPushAsync(Guid saleId)
        {
            var transaction = await _repository.GetBySaleIdAsync(saleId)
                ?? throw new NotFoundException($"No M-Pesa transaction found for sale '{saleId}'.");

            if (transaction.Status != MpesaTransactionStatus.Pending)
                return await _salesService.GetSaleByIdAsync(saleId);

            var queryResult = await _client.QueryStkStatusAsync(transaction.CheckoutRequestId!);
            if (!queryResult.Found)
                throw new InvalidAssignmentException("Safaricom has no record of this request yet — try again shortly.");

            if (queryResult.ResultCode == 0)
            {
                // The Query endpoint confirms success but doesn't reliably return the receipt number the way the
                // callback does — that's the callback's job. This just unblocks a stuck sale; the definitive
                // receipt number, if the callback does eventually arrive late, won't overwrite what's already settled
                // (HandleStkCallbackAsync's idempotency check above handles that).
                await _repository.ConfirmAsync(transaction.MpesaTransactionId, null, DateTime.UtcNow);
                return await _salesService.CompletePendingMpesaSaleAsync(saleId, null);
            }

            await _repository.FailAsync(transaction.MpesaTransactionId);
            return await _salesService.FailPendingMpesaSaleAsync(saleId, queryResult.ResultDesc ?? "Payment was not completed.");
        }

        private static string? GetMetadataString(CallbackMetadata? metadata, string name)
        {
            var item = metadata?.Item.FirstOrDefault(i => i.Name == name);
            if (item is null) return null;
            return item.Value.ValueKind == JsonValueKind.String ? item.Value.GetString() : item.Value.ToString();
        }

        private static DateTime? ParseMpesaDate(string? raw) =>
            !string.IsNullOrWhiteSpace(raw) && DateTime.TryParseExact(raw, "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)
                ? dt : null;

        private static MpesaTransactionDto ToDto(MpesaTransaction t) =>
            new(t.MpesaTransactionId, t.TransactionType, t.Status, t.MpesaReceiptNumber, t.PhoneNumber, t.Amount, t.TransactionDate, t.SaleId);
    }
}
