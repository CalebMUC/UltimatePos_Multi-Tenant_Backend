using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UltimatePos.Application.Branches;
using UltimatePos.Application.Common.Dtos;

namespace UltimatePos.Api.Controllers;

/// <summary>The branches of the business signed in to. Numbers follow KRA eTIMS: 00 is the head office, then 01, 02 ...</summary>
[ApiController]
[Route("api/v1/branches")]
public class BranchesController : ControllerBase
{
    private readonly BranchService _branches;
    public BranchesController(BranchService branches) => _branches = branches;

    /// <summary>The branches the caller can see (every branch with Branches.AccessAll, otherwise their own). Any signed-in member.</summary>
    [Authorize]
    [HttpGet]
    public async Task<IActionResult> GetBranches([FromQuery] bool includeInactive = false)
    {
        var result = await _branches.GetBranchesAsync(includeInactive);
        return Ok(ApiResponse<IEnumerable<BranchDto>>.Ok(result));
    }

    [Authorize]
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetBranch(Guid id)
    {
        var result = await _branches.GetBranchAsync(id);
        return Ok(ApiResponse<BranchDto>.Ok(result));
    }

    /// <summary>Opens a branch. Leave branchNumber out to take the next free one. Refused with BRANCH_LIMIT_REACHED past the licence's limit.</summary>
    [Authorize(Policy = "PERMISSION:Branches.Manage")]
    [HttpPost]
    public async Task<IActionResult> CreateBranch(CreateBranchRequestDto request)
    {
        var result = await _branches.CreateBranchAsync(request);
        return CreatedAtAction(nameof(GetBranch), new { id = result.BranchId }, ApiResponse<BranchDto>.Ok(result));
    }

    [Authorize(Policy = "PERMISSION:Branches.Manage")]
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateBranch(Guid id, UpdateBranchRequestDto request)
    {
        var result = await _branches.UpdateBranchAsync(id, request);
        return Ok(ApiResponse<BranchDto>.Ok(result));
    }

    /// <summary>Reopens a closed branch (counts against the licence's branch limit again).</summary>
    [Authorize(Policy = "PERMISSION:Branches.Manage")]
    [HttpPost("{id:guid}/activate")]
    public async Task<IActionResult> Activate(Guid id)
    {
        var result = await _branches.SetBranchActiveAsync(id, true);
        return Ok(ApiResponse<BranchDto>.Ok(result));
    }

    /// <summary>Closes a branch. The head office can't be closed, nor a branch still holding stock.</summary>
    [Authorize(Policy = "PERMISSION:Branches.Manage")]
    [HttpPost("{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id)
    {
        var result = await _branches.SetBranchActiveAsync(id, false);
        return Ok(ApiResponse<BranchDto>.Ok(result));
    }
}
