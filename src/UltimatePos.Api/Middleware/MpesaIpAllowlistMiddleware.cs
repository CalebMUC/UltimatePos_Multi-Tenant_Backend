using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using System.Net;
using UltimatePos.Infrastructure.Payments;

namespace UltimatePos.Api.Middleware;

public class MpesaIpAllowlistMiddleware
{
    private readonly RequestDelegate _next;

    public MpesaIpAllowlistMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, IOptions<MpesaOptions> options)
    {
        var path = context.Request.Path;
        var isWebhook = Controllers.MpesaWebhooksController.WebhookPaths.Any(webhook => path.StartsWithSegments(webhook));

        if (!isWebhook)
        {
            await _next(context);
            return;
        }

        var allowlist = options.Value.CallbackIpAllowlist;
        if (allowlist.Length > 0)
        {
            var remoteIp = context.Connection.RemoteIpAddress;
            var allowed = remoteIp is not null && allowlist.Any(cidr => IPNetwork.Parse(cidr).Contains(remoteIp));

            if (!allowed)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }
        }

        await _next(context);
    }
}