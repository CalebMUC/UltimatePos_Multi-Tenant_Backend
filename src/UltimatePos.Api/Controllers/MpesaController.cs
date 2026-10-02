using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using UltimatePos.Application.Common.Dtos;
using UltimatePos.Application.Payments;
using UltimatePos.Application.Payments.Dtos;
using UltimatePos.Infrastructure.Payments;

namespace UltimatePos.Api.Controllers
{
    [ApiController]
    [Route("api/v1/mpesa")]
    public class MpesaController : ControllerBase
    {
        private readonly MpesaService _mpesaService;
        public MpesaController(MpesaService mpesaService) => _mpesaService = mpesaService;

        // One-time setup — run once after deploying, whenever the callback URLs change.
        [Authorize(Policy = "PERMISSION:Mpesa.Configure")]
        [HttpPost("c2b/register-urls")]
        public async Task<IActionResult> RegisterC2BUrls([FromServices] IOptions<MpesaOptions> options)
        {
            await _mpesaService.RegisterC2BUrlsAsync(options.Value.C2bConfirmationUrl, options.Value.C2bValidationUrl);
            return Ok(ApiResponse<object>.Ok(new { registered = true }));
        }

        [Authorize(Policy = "PERMISSION:Mpesa.View")]
        [HttpGet("transactions/unmatched")]
        public async Task<IActionResult> GetUnmatchedTransactions()
        {
            var result = await _mpesaService.GetUnmatchedTillTransactionsAsync();
            return Ok(ApiResponse<IEnumerable<MpesaTransactionDto>>.Ok(result));
        }
    }
}
