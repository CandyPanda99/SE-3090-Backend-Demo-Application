using System.Diagnostics;

namespace BackendApplication.Middleware;

/// <summary>
/// Logs one structured line per request with method, path, status and duration.
/// </summary>
/// <remarks>
/// The duration it measures includes every middleware and the endpoint below it in the
/// pipeline, which is what a user actually waited for - not just the time spent in the
/// controller.
/// </remarks>
public sealed class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Stopwatch.GetTimestamp rather than DateTime.UtcNow: it is monotonic, so an NTP
        // correction mid-request cannot produce a negative duration.
        var startTimestamp = Stopwatch.GetTimestamp();

        try
        {
            await _next(context);
        }
        finally
        {
            // finally, so a request that throws is still logged with its real duration.
            var elapsed = Stopwatch.GetElapsedTime(startTimestamp);
            var statusCode = context.Response.StatusCode;

            var level = statusCode switch
            {
                >= 500 => LogLevel.Error,
                >= 400 => LogLevel.Warning,
                _ when elapsed.TotalMilliseconds > 1000 => LogLevel.Warning,
                _ => LogLevel.Information
            };

            _logger.Log(level,
                "HTTP {Method} {Path}{QueryString} responded {StatusCode} in {ElapsedMs:0.00}ms",
                context.Request.Method,
                context.Request.Path.Value,
                context.Request.QueryString.HasValue ? context.Request.QueryString.Value : string.Empty,
                statusCode,
                elapsed.TotalMilliseconds);
        }
    }
}
