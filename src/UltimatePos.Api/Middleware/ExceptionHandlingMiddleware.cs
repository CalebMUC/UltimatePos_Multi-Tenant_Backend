using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using UltimatePos.Application.Common.Dtos;
using UltimatePos.Domain.Exceptions;

namespace UltimatePos.Api.Middleware;

public class ExceptionHandlingMiddleware
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

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
            var (status, code, message) = ex switch
            {
                InvalidCredentialsException => (HttpStatusCode.Unauthorized, "INVALID_CREDENTIALS", ex.Message),
                InvalidTokenException => (HttpStatusCode.Unauthorized, "INVALID_TOKEN", ex.Message),
                ForbiddenException => (HttpStatusCode.Forbidden, "FORBIDDEN", ex.Message),
                BusinessAccessDeniedException denied => (HttpStatusCode.Forbidden, denied.Code, ex.Message),
                ValidationException => (HttpStatusCode.BadRequest, "VALIDATION_FAILED", ex.Message),
                UserAlreadyExistsException => (HttpStatusCode.Conflict, "USER_EXISTS", ex.Message),
                NotFoundException => (HttpStatusCode.NotFound, "NOT_FOUND", ex.Message),
                InvalidAssignmentException => (HttpStatusCode.BadRequest, "INVALID_ASSIGNMENT", ex.Message),
                DuplicateBusinessException => (HttpStatusCode.Conflict, "BUSINESS_EXISTS", ex.Message),
                InsufficientStockException => (HttpStatusCode.Conflict, "INSUFFICIENT_STOCK", ex.Message),
                CreditLimitExceededException => (HttpStatusCode.Conflict, "CREDIT_LIMIT_EXCEEDED", ex.Message),
                DuplicateReferenceException => (HttpStatusCode.Conflict, "DUPLICATE_REFERENCE", ex.Message),
                PurchaseNotAllowedException => (HttpStatusCode.UnprocessableEntity, "PURCHASE_NOT_ALLOWED", ex.Message),
                PrescriptionRequiredException => (HttpStatusCode.UnprocessableEntity, "PRESCRIPTION_REQUIRED", ex.Message),
                // A unique index caught a duplicate that a check above it missed (usually two requests racing).
                DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } } =>
                    (HttpStatusCode.Conflict, "DUPLICATE", "That record already exists — it may have just been created by someone else."),
                _ => (HttpStatusCode.InternalServerError, "INTERNAL_ERROR", "An unexpected error occurred.")
                // Deliberately not ex.Message on the fallback branch — the original
                // system leaked internal exception details to clients on Register.
            };

            if (status == HttpStatusCode.InternalServerError)
                _logger.LogError(ex, "Unhandled exception");
            else
                _logger.LogInformation("{Code}: {Message}", code, ex.Message);

            var response = ApiResponse<object>.Fail(code, message, (ex as ValidationException)?.Errors);
            context.Response.ContentType = "application/json";
            context.Response.StatusCode = (int)status;
            await context.Response.WriteAsync(JsonSerializer.Serialize(response, JsonOptions));
        }
    }
}
