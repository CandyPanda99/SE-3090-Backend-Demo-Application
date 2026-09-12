namespace BackendApplication.DTOs.Common;

/// <summary>
/// A uniform success envelope: <c>{ "success": true, "data": {...}, "meta": {...} }</c>.
/// </summary>
/// <remarks>
/// Used for successful responses only; failures are returned as RFC 9457 ProblemDetails
/// by the global exception middleware. Two shapes total - one for success, one for
/// failure - is the whole contract a client has to handle.
/// </remarks>
/// <typeparam name="T">The payload type.</typeparam>
public sealed class ApiResponse<T>
{
    public bool Success { get; init; } = true;

    /// <summary>The actual resource or list the caller asked for.</summary>
    public T? Data { get; init; }

    /// <summary>Optional human-readable note, e.g. "Task created".</summary>
    public string? Message { get; init; }

    /// <summary>Diagnostics that belong to the response, not to the resource.</summary>
    public ResponseMeta Meta { get; init; } = new();

    public static ApiResponse<T> Ok(T data, string? message = null)
        => new() { Data = data, Message = message };
}

/// <summary>Per-response diagnostics.</summary>
public sealed class ResponseMeta
{
    /// <summary>Server time the response was produced (UTC, ISO-8601).</summary>
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// The correlation id, echoed from the <c>X-Correlation-ID</c> header.
    /// </summary>
    public string? CorrelationId { get; set; }

    /// <summary>Which API version served this response ("1.0").</summary>
    public string? ApiVersion { get; set; }
}
