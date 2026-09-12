namespace BackendApplication.Middleware;

/// <summary>
/// Gives every request a unique id, echoes it back in a header, and attaches it to every
/// log line produced while that request is being handled.
/// </summary>
/// <remarks>
/// This is what makes a production log searchable. Without it, a user reporting "it
/// failed at about 2pm" leaves you grepping thousands of interleaved lines; with it,
/// they quote the id from the error response and every line for that request comes back
/// together.
/// </remarks>
public sealed class CorrelationIdMiddleware
{
    /// <summary>The response header the correlation id is echoed back in.</summary>
    public const string HeaderName = "X-Correlation-ID";

    /// <summary>Key used to stash the id in <c>HttpContext.Items</c>.</summary>
    public const string ItemKey = "CorrelationId";

    private readonly RequestDelegate _next;
    private readonly ILogger<CorrelationIdMiddleware> _logger;

    public CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Honour a caller-supplied id so a trace can span several services, and fall back
        // to the framework's per-request identifier otherwise.
        var correlationId = context.Request.Headers[HeaderName].FirstOrDefault();

        if (string.IsNullOrWhiteSpace(correlationId))
        {
            correlationId = context.TraceIdentifier;
        }

        context.Items[ItemKey] = correlationId;

        // OnStarting, not a direct assignment: headers cannot be changed once the response
        // has begun, and this fires at the last moment before that happens.
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using (_logger.BeginScope(new Dictionary<string, object>
               {
                   ["CorrelationId"] = correlationId,
                   ["RequestPath"] = context.Request.Path.Value ?? string.Empty
               }))
        {
            await _next(context);
        }
    }
}
