using System.Net;
using System.Text.Json;
using UltimatePos.Application.Common.Dtos;
using UltimatePos.Domain.Exceptions;

namespace UltimatePos.Api.Middleware;

public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception");

            var (status, code, message) = ex switch
            {
                InvalidCredentialsException => (HttpStatusCode.Unauthorized, "INVALID_CREDENTIALS", ex.Message),
                UserAlreadyExistsException => (HttpStatusCode.Conflict, "USER_EXISTS", ex.Message),
                _ => (HttpStatusCode.InternalServerError, "INTERNAL_ERROR", "An unexpected error occurred.")
                // Deliberately not ex.Message on the fallback branch — the original
                // system leaked internal exception details to clients on Register.
            };

            var response = ApiResponse<object>.Fail(code, message);
            context.Response.ContentType = "application/json";
            context.Response.StatusCode = (int)status;
            await context.Response.WriteAsync(JsonSerializer.Serialize(response));
        }
    }
}