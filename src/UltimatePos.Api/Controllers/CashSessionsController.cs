using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UltimatePos.Application.CashSessions;
using UltimatePos.Application.Common.Dtos;
using UltimatePos.Application.Reports;
using static UltimatePos.Domain.Enums.PosEnums;

namespace UltimatePos.Api.Controllers;

/// <summary>
/// The day's float and the end-of-day cash count, per branch: open with the float allocated for change, record cash paid in or out
/// during the day, close with the amount counted. The close shows what was expected and the difference.
/// </summary>
[ApiController]
[Route("api/v1/cash-sessions")]
public class CashSessionsController : ControllerBase
{
    private readonly CashSessionService _sessions;
    public CashSessionsController(CashSessionService sessions) => _sessions = sessions;

    /// <summary>Sessions at the branches the caller can see, newest first. from/to are the local days they were opened.</summary>
    [Authorize(Policy = "PERMISSION:CashSessions.View")]
    [HttpGet]
    public async Task<IActionResult> GetSessions([FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        [FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null, [FromQuery] CashSessionStatus? status = null,
        [FromQuery] int utcOffsetMinutes = ReportPeriod.DefaultUtcOffsetMinutes)
    {
        var paged = await _sessions.GetSessionsAsync(page, pageSize, new CashSessionQuery(from, to, status, utcOffsetMinutes));
        var meta = new PagingMeta(paged.Page, paged.PageSize, paged.TotalCount, paged.TotalPages);
        return Ok(ApiResponse<IEnumerable<CashSessionDto>>.Ok(paged.Items, meta));
    }

    /// <summary>The open session at the branch signed in to, with its running figures; data is null when the day has not been opened.</summary>
    [Authorize(Policy = "PERMISSION:CashSessions.View")]
    [HttpGet("current")]
    public async Task<IActionResult> GetCurrent()
    {
        var result = await _sessions.GetCurrentAsync();
        return Ok(ApiResponse<CashSessionDto?>.Ok(result));
    }

    [Authorize(Policy = "PERMISSION:CashSessions.View")]
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetSession(Guid id)
    {
        var result = await _sessions.GetAsync(id);
        return Ok(ApiResponse<CashSessionDto>.Ok(result));
    }

    /// <summary>Opens the day at the branch signed in to with the float. One open session per branch.</summary>
    [Authorize(Policy = "PERMISSION:CashSessions.Open")]
    [HttpPost("open")]
    public async Task<IActionResult> Open(OpenCashSessionRequestDto request)
    {
        var result = await _sessions.OpenAsync(request);
        return CreatedAtAction(nameof(GetSession), new { id = result.CashSessionId }, ApiResponse<CashSessionDto>.Ok(result));
    }

    [Authorize(Policy = "PERMISSION:CashSessions.RecordMovement")]
    [HttpPost("{id:guid}/movements")]
    public async Task<IActionResult> RecordMovement(Guid id, RecordCashMovementRequestDto request)
    {
        var result = await _sessions.RecordMovementAsync(id, request);
        return Ok(ApiResponse<CashSessionDto>.Ok(result));
    }

    /// <summary>Counts out the day: fixes what was expected, what was counted and the difference.</summary>
    [Authorize(Policy = "PERMISSION:CashSessions.Close")]
    [HttpPost("{id:guid}/close")]
    public async Task<IActionResult> Close(Guid id, CloseCashSessionRequestDto request)
    {
        var result = await _sessions.CloseAsync(id, request);
        return Ok(ApiResponse<CashSessionDto>.Ok(result));
    }
}
