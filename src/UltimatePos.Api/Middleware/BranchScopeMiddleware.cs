using System.Text.Json;
using UltimatePos.Application.Common.Dtos;
using UltimatePos.Application.Common.Interfaces;

namespace UltimatePos.Api.Middleware;

/// <summary>
/// Any GET may add <c>?branchId=</c> to see one branch only — reports, the dashboard, stock lists, sales. The read is then also
/// "at" that branch (its stock level, its average costs). A branch the caller can't see is refused (403 BRANCH_FORBIDDEN), not
/// silently emptied. Writes always happen at the branch signed in to; switch branch to work somewhere else.
/// </summary>
public class BranchScopeMiddleware
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly RequestDelegate _next;

    public BranchScopeMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, ICurrentUser currentUser, IBranchContext branches)
    {
        if (HttpMethods.IsGet(context.Request.Method) && currentUser.BusinessId is not null
            && context.Request.Query.TryGetValue("branchId", out var value) && !string.IsNullOrWhiteSpace(value))
        {
            if (!Guid.TryParse(value, out var branchId) || branchId == Guid.Empty)
            {
                await RefuseAsync(context, StatusCodes.Status400BadRequest, "VALIDATION_FAILED", "branchId must be a branch id.");
                return;
            }
            if (!branches.CanSee(branchId))
            {
                await RefuseAsync(context, StatusCodes.Status403Forbidden, "BRANCH_FORBIDDEN", "You don't have access to that branch.");
                return;
            }

            using (branches.Narrow(branchId))
                await _next(context);
            return;
        }

        await _next(context);
    }

    private static async Task RefuseAsync(HttpContext context, int status, string code, string message)
    {
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(JsonSerializer.Serialize(ApiResponse<object>.Fail(code, message), JsonOptions));
    }
}
