using System.Net;

namespace BackendApplication.Exceptions;

/// <summary>
/// Base class for every exception this application throws on purpose.
/// </summary>
/// <remarks>
/// Each exception carries its own HTTP status code, so the global handler needs no
/// per-type switch and new exception types require no middleware change. A service
/// throws what went wrong; exactly one place decides how that reaches the client.
/// </remarks>
public abstract class AppException : Exception
{
    /// <summary>The HTTP status this exception should produce.</summary>
    public abstract HttpStatusCode StatusCode { get; }

    /// <summary>
    /// Short, stable, machine-readable code, e.g. <c>"taskitem_not_found"</c>.
    /// </summary>
    /// <remarks>
    /// Clients branch on this, never on the human-readable message - the message is free
    /// to be reworded or translated without breaking anybody's error handling.
    /// </remarks>
    public abstract string ErrorCode { get; }

    /// <summary>Optional extra fields merged into the ProblemDetails response.</summary>
    public IDictionary<string, object?> Extensions { get; } = new Dictionary<string, object?>();

    protected AppException(string message) : base(message)
    {
    }

    protected AppException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
