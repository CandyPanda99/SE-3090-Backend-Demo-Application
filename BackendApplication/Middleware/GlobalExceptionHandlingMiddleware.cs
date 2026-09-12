using System.Text.Json;
using BackendApplication.Exceptions;
using ValidationException = BackendApplication.Exceptions.ValidationException;

namespace BackendApplication.Middleware;

/// <summary>
/// Catches every unhandled exception and converts it into an RFC 9457
/// <c>application/problem+json</c> response.
/// </summary>
/// <remarks>
/// Registered first in the pipeline so it wraps everything below it. One place decides
/// how an exception becomes a status code, which is why no controller in this project
/// contains a try/catch.
/// </remarks>
public sealed class GlobalExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionHandlingMiddleware> _logger;
    private readonly IHostEnvironment _environment;

    public GlobalExceptionHandlingMiddleware(
        RequestDelegate next,
        ILogger<GlobalExceptionHandlingMiddleware> logger,
        IHostEnvironment environment)
    {
        _next = next;
        _logger = logger;
        _environment = environment;
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

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        // Once bytes are on the wire the status code and headers are fixed; the best that
        // can be done is record what happened.
        if (context.Response.HasStarted)
        {
            _logger.LogError(exception,
                "Exception thrown after the response had started; cannot write a ProblemDetails body.");
            return;
        }

        var correlationId = context.Items[CorrelationIdMiddleware.ItemKey]?.ToString()
                            ?? context.TraceIdentifier;

        var problem = BuildProblemDetails(context, exception, correlationId);

        // 5xx is our bug and gets a stack trace; 4xx is the caller's mistake and would
        // just be noise at Error level.
        if (problem.Status >= 500)
        {
            _logger.LogError(exception,
                "Unhandled exception on {Method} {Path}. CorrelationId={CorrelationId}",
                context.Request.Method, context.Request.Path, correlationId);
        }
        else
        {
            _logger.LogWarning(
                "Request failed with {StatusCode} on {Method} {Path}: {Message}. CorrelationId={CorrelationId}",
                problem.Status, context.Request.Method, context.Request.Path, exception.Message, correlationId);
        }

        context.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;

        context.Response.ContentType = "application/problem+json";

        await context.Response.WriteAsync(JsonSerializer.Serialize(problem, JsonOptions));
    }

    /// <summary>Maps an exception type onto a ProblemDetails body.</summary>
    private ProblemDetails BuildProblemDetails(HttpContext context, Exception exception, string correlationId)
    {
        var problem = new ProblemDetails
        {
            Instance = $"{context.Request.Method} {context.Request.Path}"
        };

        switch (exception)
        {
            case ValidationException validation:
                problem.Status = StatusCodes.Status422UnprocessableEntity;
                problem.Title = "Validation failed";
                problem.Detail = "One or more fields are invalid. See 'errors' for details.";
                problem.Extensions["errors"] = validation.Errors;
                problem.Extensions["errorCode"] = validation.ErrorCode;
                break;

            case FluentValidation.ValidationException fluent:
                problem.Status = StatusCodes.Status422UnprocessableEntity;
                problem.Title = "Validation failed";
                problem.Detail = "One or more fields are invalid. See 'errors' for details.";
                problem.Extensions["errors"] = fluent.Errors
                    .GroupBy(e => e.PropertyName)
                    .ToDictionary(g => Camelize(g.Key), g => g.Select(e => e.ErrorMessage).ToArray());
                problem.Extensions["errorCode"] = "validation_failed";
                break;

            // Every deliberate exception carries its own status and code, so this one arm
            // handles all of them and new exception types need no change here.
            case AppException app:
                problem.Status = (int)app.StatusCode;
                problem.Title = TitleFor(app.StatusCode);
                problem.Detail = app.Message;
                problem.Extensions["errorCode"] = app.ErrorCode;

                foreach (var (key, value) in app.Extensions)
                {
                    problem.Extensions[key] = value;
                }
                break;

            // Optimistic concurrency: someone else saved this row first.
            case DbUpdateConcurrencyException:
                problem.Status = StatusCodes.Status409Conflict;
                problem.Title = "Conflict";
                problem.Detail = "This record was modified by someone else while you were editing it. " +
                                 "Reload and try again.";
                problem.Extensions["errorCode"] = "concurrency_conflict";
                break;

            // Translating PostgreSQL's SQLSTATE codes turns an opaque 500 into an
            // actionable 4xx - a unique-index violation really is the caller's problem.
            case DbUpdateException dbUpdate when dbUpdate.InnerException is Npgsql.PostgresException pg:
                (problem.Status, problem.Title, problem.Detail, problem.Extensions["errorCode"]) = pg.SqlState switch
                {
                    "23505" => (StatusCodes.Status409Conflict, "Conflict",
                                "A record with these values already exists.", (object?)"duplicate_key"),
                    "23503" => (StatusCodes.Status409Conflict, "Conflict",
                                "This operation references a record that does not exist, or is referenced by another record.",
                                "foreign_key_violation"),
                    "23514" => (StatusCodes.Status400BadRequest, "Bad request",
                                "A value violates a database rule.",
                                "check_constraint_violation"),
                    _ => (StatusCodes.Status500InternalServerError, "Database error",
                          "A database error occurred.", "database_error")
                };
                break;

            // The client hung up. Not an error on our side, and 499 keeps it out of the
            // 5xx error rate.
            case OperationCanceledException or TaskCanceledException:
                problem.Status = 499;
                problem.Title = "Client closed request";
                problem.Detail = "The request was cancelled by the client.";
                problem.Extensions["errorCode"] = "request_cancelled";
                break;

            default:
                problem.Status = StatusCodes.Status500InternalServerError;
                problem.Title = "An unexpected error occurred";

                // Detail differs by environment on purpose: an exception message can name
                // a table, a column or a file path, none of which a stranger should see.
                problem.Detail = _environment.IsDevelopment()
                    ? exception.Message
                    : "An unexpected error occurred. Please contact support with the correlation id.";

                if (_environment.IsDevelopment())
                {
                    problem.Extensions["exceptionType"] = exception.GetType().FullName;
                    problem.Extensions["stackTrace"] = exception.StackTrace;
                }

                problem.Extensions["errorCode"] = "internal_error";
                break;
        }

        problem.Type = $"https://httpstatuses.io/{problem.Status}";

        problem.Extensions["correlationId"] = correlationId;
        problem.Extensions["timestamp"] = DateTimeOffset.UtcNow;

        return problem;
    }

    private static string TitleFor(System.Net.HttpStatusCode status) => status switch
    {
        System.Net.HttpStatusCode.NotFound => "Resource not found",
        System.Net.HttpStatusCode.Conflict => "Conflict",
        System.Net.HttpStatusCode.BadRequest => "Bad request",
        System.Net.HttpStatusCode.Unauthorized => "Unauthorized",
        System.Net.HttpStatusCode.Forbidden => "Forbidden",
        System.Net.HttpStatusCode.UnprocessableContent => "Validation failed",
        _ => "Request failed"
    };

    private static string Camelize(string value)
        => string.IsNullOrEmpty(value) ? value : char.ToLowerInvariant(value[0]) + value[1..];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };
}
