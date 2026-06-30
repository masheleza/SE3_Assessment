using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace FinancialStatements.DocumentApi.Filters;

public sealed class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) => _logger = logger;

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var (status, title) = exception switch
        {
            InvalidOperationException => (StatusCodes.Status400BadRequest, "Invalid operation"),
            UnauthorizedAccessException => (StatusCodes.Status401Unauthorized, "Unauthorized"),
            KeyNotFoundException => (StatusCodes.Status404NotFound, "Resource not found"),
            _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred")
        };

        _logger.LogError(
            exception,
            "Unhandled {ExceptionType} on {Method} {Path} → {Status}",
            exception.GetType().Name,
            httpContext.Request.Method,
            httpContext.Request.Path,
            status);

        httpContext.Response.StatusCode = status;

        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = exception.Message,
            Instance = httpContext.Request.Path,
            Extensions = { ["traceId"] = httpContext.TraceIdentifier }
        };

        await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken);
        return true;
    }
}
