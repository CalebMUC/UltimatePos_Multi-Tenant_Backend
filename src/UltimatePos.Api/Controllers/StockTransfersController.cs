using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UltimatePos.Application.Common.Dtos;
using UltimatePos.Application.Inventory;
using UltimatePos.Domain.Entities;

namespace UltimatePos.Api.Controllers;

/// <summary>Stock moving between branches: create (Draft) → dispatch (stock leaves the source) → receive (stock arrives).</summary>
[ApiController]
[Route("api/v1/stock-transfers")]
public class StockTransfersController : ControllerBase
{
    private readonly StockTransferService _transfers;
    public StockTransfersController(StockTransferService transfers) => _transfers = transfers;

    /// <summary>Transfers from or to the branches the caller can see. branchId narrows to one branch (either end).</summary>
    [Authorize(Policy = "PERMISSION:StockTransfers.View")]
    [HttpGet]
    public async Task<IActionResult> GetTransfers([FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        [FromQuery] StockTransferStatus? status = null, [FromQuery] Guid? branchId = null)
    {
        var paged = await _transfers.GetTransfersAsync(page, pageSize, status, branchId);
        var meta = new PagingMeta(paged.Page, paged.PageSize, paged.TotalCount, paged.TotalPages);
        return Ok(ApiResponse<IEnumerable<StockTransferDto>>.Ok(paged.Items, meta));
    }

    [Authorize(Policy = "PERMISSION:StockTransfers.View")]
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetTransfer(Guid id)
    {
        var result = await _transfers.GetAsync(id);
        return Ok(ApiResponse<StockTransferDto>.Ok(result));
    }

    [Authorize(Policy = "PERMISSION:StockTransfers.Create")]
    [HttpPost]
    public async Task<IActionResult> CreateTransfer(CreateStockTransferRequestDto request)
    {
        var result = await _transfers.CreateAsync(request);
        return CreatedAtAction(nameof(GetTransfer), new { id = result.StockTransferId }, ApiResponse<StockTransferDto>.Ok(result));
    }

    /// <summary>Takes the stock out of the source branch at its average cost. Staff of the source branch only.</summary>
    [Authorize(Policy = "PERMISSION:StockTransfers.Dispatch")]
    [HttpPost("{id:guid}/dispatch")]
    public async Task<IActionResult> Dispatch(Guid id)
    {
        var result = await _transfers.DispatchAsync(id);
        return Ok(ApiResponse<StockTransferDto>.Ok(result));
    }

    /// <summary>Records what arrived (everything, unless lines say otherwise). Staff of the destination branch only.</summary>
    [Authorize(Policy = "PERMISSION:StockTransfers.Receive")]
    [HttpPost("{id:guid}/receive")]
    public async Task<IActionResult> Receive(Guid id, ReceiveStockTransferRequestDto request)
    {
        var result = await _transfers.ReceiveAsync(id, request);
        return Ok(ApiResponse<StockTransferDto>.Ok(result));
    }

    /// <summary>Cancels a draft. A dispatched transfer can't be cancelled — receive it (with what arrived).</summary>
    [Authorize(Policy = "PERMISSION:StockTransfers.Create")]
    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id)
    {
        var result = await _transfers.CancelAsync(id);
        return Ok(ApiResponse<StockTransferDto>.Ok(result));
    }
}
