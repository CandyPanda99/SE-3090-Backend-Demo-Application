using System.Security.Claims;
using BackendApplication.Domain.Enums;
using BackendApplication.Services.Abstractions;

namespace BackendApplication.Services.Implementations;

/// <summary>
/// Reads the current user out of the JWT claims attached to the request.
/// </summary>
/// <remarks>
/// Registered as a singleton. It is stateless: every property reads through
/// <see cref="IHttpContextAccessor"/>, which keeps the current HttpContext in an
/// <c>AsyncLocal</c>. Caching a value in a field here would leak one request's user
/// into the next.
/// </remarks>
public sealed class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _accessor;

    public CurrentUserService(IHttpContextAccessor accessor) => _accessor = accessor;

    /// <summary>The claims principal for this request, or null outside a request.</summary>
    private ClaimsPrincipal? User => _accessor.HttpContext?.User;

    /// <inheritdoc />
    public int? UserId
    {
        get
        {
            var raw = User?.FindFirstValue(ClaimTypes.NameIdentifier)
                      ?? User?.FindFirstValue("sub");

            return int.TryParse(raw, out var id) ? id : null;
        }
    }

    /// <inheritdoc />
    public string? Email =>
        User?.FindFirstValue(ClaimTypes.Email) ?? User?.FindFirstValue("email");

    /// <inheritdoc />
    public UserRole? Role
    {
        get
        {
            var raw = User?.FindFirstValue(ClaimTypes.Role) ?? User?.FindFirstValue("role");
            return Enum.TryParse<UserRole>(raw, out var role) ? role : null;
        }
    }

    /// <inheritdoc />
    public bool IsAuthenticated => User?.Identity?.IsAuthenticated ?? false;

    /// <inheritdoc />
    public bool IsInRole(UserRole role) => Role == role;
}
