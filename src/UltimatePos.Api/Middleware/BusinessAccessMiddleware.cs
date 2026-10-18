using System.Text.Json;
using UltimatePos.Application.Common.Dtos;
using UltimatePos.Application.Common.Interfaces;
using UltimatePos.Application.Licensing;

namespace UltimatePos.Api.Middleware;

/// <summary>
/// Refuses every request made in a business that is suspended, closed or out of licence (HTTP 403 with the reason code), so access
/// stops at once — not only at the next sign-in. While a licence is about to end or in its grace period, the response carries an
/// X-License-Warning header for the frontend's banner. /auth stays open, so a user can still sign out or switch to another business.
/// </summary>
public class BusinessAccessMiddleware
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly RequestDelegate _next;

    public BusinessAccessMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, ICurrentUser currentUser, IBusinessAccessGate gate)
    {
        if (currentUser.BusinessId is { } businessId && !context.Request.Path.StartsWithSegments("/api/v1/auth"))
        {
            var access = await gate.CheckAsync(businessId);
            if (!access.Allowed)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(JsonSerializer.Serialize(ApiResponse<object>.Fail(access.Code!, access.Message!), JsonOptions));
                return;
            }
            if (access.Warning is { } warning)
                context.Response.Headers["X-License-Warning"] = Uri.EscapeDataString(warning);
        }

        await _next(context);
    }
}
