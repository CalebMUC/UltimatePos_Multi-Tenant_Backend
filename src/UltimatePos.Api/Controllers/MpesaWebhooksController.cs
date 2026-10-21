using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UltimatePos.Application.Payments;
using UltimatePos.Application.Payments.Dtos;

namespace UltimatePos.Api.Controllers
{
    /// <summary>
    /// Safaricom calls these. Each is also served under /api/v1/payments/...: Daraja refuses C2B URLs that contain "mpesa" or
    /// "safaricom", so production registers those paths. The /api/v1/mpesa/... paths stay for anything already registered.
    /// </summary>
    [ApiController]
    [Route("api/v1/mpesa")]
    public class MpesaWebhooksController : ControllerBase
    {
        /// <summary>Every path Safaricom may call, for MpesaIpAllowlistMiddleware.</summary>
        public static readonly string[] WebhookPaths =
        {
            "/api/v1/mpesa/stk/callback", "/api/v1/mpesa/c2b/confirmation", "/api/v1/mpesa/c2b/validation",
            "/api/v1/payments/stk/callback", "/api/v1/payments/c2b/confirmation", "/api/v1/payments/c2b/validation"
        };

        /// <summary>
        /// The reply Daraja documents, with its exact field names. The API's JSON settings would camel-case an object
        /// (resultCode), which Safaricom may not read, so this is written out as it is.
        /// </summary>
        private ContentResult DarajaAccepted() =>
            Content("{\"ResultCode\":0,\"ResultDesc\":\"Accepted\"}", "application/json");

        private readonly MpesaService _mpesaService;
        private readonly ILogger<MpesaWebhooksController> _logger;

        public MpesaWebhooksController(MpesaService mpesaService, ILogger<MpesaWebhooksController> logger)
        {
            _mpesaService = mpesaService;
            _logger = logger;
        }

        [AllowAnonymous]
        [HttpPost("stk/callback")]
        [HttpPost("/api/v1/payments/stk/callback")]
        public async Task<IActionResult> StkCallback([FromBody] StkCallbackPayload payload)
        {
            try
            {
                await _mpesaService.HandleStkCallbackAsync(payload.Body.StkCallback);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing STK callback for CheckoutRequestID {Id}", payload.Body.StkCallback.CheckoutRequestID);
            }

            return DarajaAccepted();
        }

        [AllowAnonymous]
        [HttpPost("c2b/validation")]
        [HttpPost("/api/v1/payments/c2b/validation")]
        public IActionResult C2bValidation([FromBody] C2bConfirmationPayload payload) =>
            DarajaAccepted();

        [AllowAnonymous]
        [HttpPost("c2b/confirmation")]
        [HttpPost("/api/v1/payments/c2b/confirmation")]
        public async Task<IActionResult> C2bConfirmation([FromBody] C2bConfirmationPayload payload)
        {
            try
            {
                await _mpesaService.HandleC2bConfirmationAsync(payload);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing C2B confirmation for TransID {Id}", payload.TransID);
            }

            return DarajaAccepted();
        }
    }
}
