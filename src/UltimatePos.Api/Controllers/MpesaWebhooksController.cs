using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UltimatePos.Application.Payments;
using UltimatePos.Application.Payments.Dtos;

namespace UltimatePos.Api.Controllers
{
    [ApiController]
    [Route("api/v1/mpesa")]
    public class MpesaWebhooksController : ControllerBase
    {
        private readonly MpesaService _mpesaService;
        private readonly ILogger<MpesaWebhooksController> _logger;

        public MpesaWebhooksController(MpesaService mpesaService, ILogger<MpesaWebhooksController> logger)
        {
            _mpesaService = mpesaService;
            _logger = logger;
        }

        [AllowAnonymous]
        [HttpPost("stk/callback")]
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

            return Ok(new { ResultCode = 0, ResultDesc = "Accepted" });
        }

        [AllowAnonymous]
        [HttpPost("c2b/validation")]
        public IActionResult C2bValidation([FromBody] C2bConfirmationPayload payload) =>
            Ok(new { ResultCode = 0, ResultDesc = "Accepted" });

        [AllowAnonymous]
        [HttpPost("c2b/confirmation")]
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

            return Ok(new { ResultCode = 0, ResultDesc = "Accepted" });
        }
    }
}
