using System.Net;

namespace BackendApplication.Exceptions;

/// <summary>
/// 404 - the requested resource does not exist.
/// </summary>
public sealed class NotFoundException : AppException
{
    public override HttpStatusCode StatusCode => HttpStatusCode.NotFound;

    public override string ErrorCode { get; }

    /// <summary>Creates a message like <c>Task with id '999' was not found.</c></summary>
    public NotFoundException(string resourceName, object key)
        : base($"{resourceName} with id '{key}' was not found.")
    {
        ErrorCode = $"{resourceName.ToLowerInvariant()}_not_found";
        Extensions["resource"] = resourceName;
        Extensions["key"] = key.ToString();
    }

    public NotFoundException(string message) : base(message) => ErrorCode = "not_found";
}

/// <summary>
/// 409 - the request is well-formed but conflicts with the current state.
/// </summary>
/// <remarks>
/// Used for a duplicate reference, an already-registered email, a concurrency
/// collision, or an illegal status transition.
/// </remarks>
public sealed class ConflictException : AppException
{
    public override HttpStatusCode StatusCode => HttpStatusCode.Conflict;

    public override string ErrorCode { get; }

    public ConflictException(string message, string errorCode = "conflict") : base(message)
        => ErrorCode = errorCode;
}

/// <summary>
/// 400 - a business rule was broken (as opposed to a malformed field, which the
/// validation filter catches before the controller even runs).
/// </summary>
public sealed class BusinessRuleException : AppException
{
    public override HttpStatusCode StatusCode => HttpStatusCode.BadRequest;

    public override string ErrorCode { get; }

    public BusinessRuleException(string message, string errorCode = "business_rule_violation")
        : base(message) => ErrorCode = errorCode;
}

/// <summary>
/// 422 - the payload parsed fine but failed validation. Carries per-field errors.
/// </summary>
public sealed class ValidationException : AppException
{
    public override HttpStatusCode StatusCode => HttpStatusCode.UnprocessableContent;

    public override string ErrorCode => "validation_failed";

    /// <summary>Field name -&gt; list of problems with that field.</summary>
    public IReadOnlyDictionary<string, string[]> Errors { get; }

    public ValidationException(IReadOnlyDictionary<string, string[]> errors)
        : base("One or more validation errors occurred.")
        => Errors = errors;

    public ValidationException(string field, string error)
        : this(new Dictionary<string, string[]> { [field] = [error] })
    {
    }
}

/// <summary>401 - no credentials, or credentials that do not check out.</summary>
public sealed class UnauthorizedException : AppException
{
    public override HttpStatusCode StatusCode => HttpStatusCode.Unauthorized;

    public override string ErrorCode => "unauthorized";

    public UnauthorizedException(string message = "Invalid credentials.") : base(message)
    {
    }
}

/// <summary>403 - authenticated, but this user is not allowed to do this.</summary>
public sealed class ForbiddenException : AppException
{
    public override HttpStatusCode StatusCode => HttpStatusCode.Forbidden;

    public override string ErrorCode => "forbidden";

    public ForbiddenException(string message = "You do not have permission to perform this action.")
        : base(message)
    {
    }
}
