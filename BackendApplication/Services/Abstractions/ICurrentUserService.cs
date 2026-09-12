using BackendApplication.Domain.Enums;

namespace BackendApplication.Services.Abstractions;

/// <summary>
/// Answers "who is making this request?" without dragging <c>HttpContext</c> through the
/// whole application.
/// </summary>
/// <remarks>
/// Services depend on this instead of IHttpContextAccessor, which keeps them testable:
/// a unit test supplies a stub, rather than constructing a fake HTTP request.
/// </remarks>
public interface ICurrentUserService
{
    /// <summary>The authenticated user's database id, or null if anonymous.</summary>
    int? UserId { get; }

    /// <summary>The authenticated user's email, or null if anonymous.</summary>
    string? Email { get; }

    /// <summary>The authenticated user's role, or null if anonymous.</summary>
    UserRole? Role { get; }

    /// <summary>True when the request carries a valid token.</summary>
    bool IsAuthenticated { get; }

    /// <summary>True when the user holds the given role.</summary>
    bool IsInRole(UserRole role);
}
