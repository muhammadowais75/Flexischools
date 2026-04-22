using Flexischools.Api.Domain.Exceptions;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace Flexischools.Api.Application.Common;

/// <summary>
/// Converts domain exceptions into RFC 7807 Problem Details responses.
/// Keeps controllers thin — no try/catch needed there.
/// </summary>
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
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception ex)
    {
        var (statusCode, title) = ex switch
        {
            OrderCutOffException => (StatusCodes.Status422UnprocessableEntity, "Order Cut-Off Exceeded"),
            InsufficientStockException => (StatusCodes.Status422UnprocessableEntity, "Insufficient Stock"),
            InsufficientWalletBalanceException => (StatusCodes.Status422UnprocessableEntity, "Insufficient Wallet Balance"),
            AllergenConflictException => (StatusCodes.Status422UnprocessableEntity, "Allergen Conflict"),
            NotFoundException => (StatusCodes.Status404NotFound, "Resource Not Found"),
            DomainException => (StatusCodes.Status422UnprocessableEntity, "Business Rule Violation"),
            _ => (StatusCodes.Status500InternalServerError, "Internal Server Error")
        };

        if (statusCode == 500)
            _logger.LogError(ex, "Unhandled exception");
        else
            _logger.LogWarning("Domain rule violated: {Message}", ex.Message);

        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = ex.Message,
            Instance = context.Request.Path
        };

        // Include correlation id for traceability
        if (context.Request.Headers.TryGetValue("X-Correlation-Id", out var corrId))
            problem.Extensions["correlationId"] = corrId.ToString();

        context.Response.ContentType = "application/problem+json";
        context.Response.StatusCode = statusCode;

        await context.Response.WriteAsync(JsonSerializer.Serialize(problem,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
    }
}
