using Asp.Versioning;
using BackendApplication.Configuration;
using BackendApplication.DTOs.Auth;
using BackendApplication.DTOs.Common;
using BackendApplication.Services.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;

namespace BackendApplication.Controllers.V1;

/// <summary>
/// Registration, sign-in, token refresh and sign-out.
/// </summary>
/// <remarks>
/// Supporting infrastructure, not the subject of this API. It exists so the Tasks
/// endpoints have an identity to authorise against.
/// </remarks>
[ApiVersion(ApiVersions.V1)]
[Route("api/v{version:apiVersion}/auth")]
public sealed class AuthController : ApiControllerBase
{
    private readonly IAuthService _auth;

    public AuthController(IAuthService auth) => _auth = auth;

    /// <summary>
    /// Creates a new member account and returns a token pair.
    /// </summary>
    /// <remarks>
    /// New accounts always get the <c>Member</c> role - the client cannot request one.
    /// Password rules live in <c>Validation/AuthValidators.cs</c>.
    /// </remarks>
    /// <response code="201">Account created; tokens returned.</response>
    /// <response code="409">That email is already registered.</response>
    /// <response code="422">Validation failed (weak password, mismatch, bad email).</response>
    [HttpPost("register", Name = "Register")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(typeof(ApiResponse<AuthResponseDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ApiResponse<AuthResponseDto>>> Register(
        RegisterDto dto,
        CancellationToken ct)
    {
        var result = await _auth.RegisterAsync(dto, ct);

        return StatusCode(StatusCodes.Status201Created, Envelope(result, "Account created successfully."));
    }

    /// <summary>
    /// Exchanges email and password for a token pair.
    /// </summary>
    /// <remarks>
    /// Seeded demo accounts (all created by <c>DbSeeder</c>):
    ///
    ///     admin@tasks.local   / Admin#12345    -> Admin
    ///     lead@tasks.local    / Lead#12345     -> Lead
    ///     member@tasks.local  / Member#12345   -> Member
    ///
    /// Rate limited to 10 attempts per minute per caller.
    /// </remarks>
    /// <response code="200">Signed in; tokens returned.</response>
    /// <response code="401">Invalid email or password.</response>
    /// <response code="429">Too many attempts. Slow down.</response>
    [HttpPost("login", Name = "Login")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(typeof(ApiResponse<AuthResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<ApiResponse<AuthResponseDto>>> Login(LoginDto dto, CancellationToken ct)
        => Ok(Envelope(await _auth.LoginAsync(dto, ct), "Signed in successfully."));

    /// <summary>
    /// Exchanges a valid refresh token for a brand-new token pair.
    /// </summary>
    /// <remarks>
    /// Rotation: the supplied refresh token is revoked and replaced. Presenting an
    /// already-used token returns 401 - which is also how token theft shows up in the log.
    /// </remarks>
    /// <response code="200">New tokens issued.</response>
    /// <response code="401">The refresh token is unknown, expired or already used.</response>
    [HttpPost("refresh", Name = "RefreshToken")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(typeof(ApiResponse<AuthResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<AuthResponseDto>>> Refresh(
        RefreshTokenDto dto,
        CancellationToken ct)
        => Ok(Envelope(await _auth.RefreshAsync(dto, ct), "Token refreshed."));

    /// <summary>
    /// Revokes a refresh token. This is "logout".
    /// </summary>
    /// <remarks>
    /// This does not invalidate the access token, which stays valid until it expires -
    /// the unavoidable trade-off of stateless JWTs, and the reason they are short-lived.
    /// </remarks>
    /// <response code="204">Token revoked (or was already invalid - this is idempotent).</response>
    [HttpPost("revoke", Name = "RevokeToken")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Revoke(RefreshTokenDto dto, CancellationToken ct)
    {
        await _auth.RevokeAsync(dto.RefreshToken, ct);

        return NoContent();
    }

    /// <summary>
    /// Returns the profile of the currently authenticated user.
    /// </summary>
    /// <remarks>Useful for confirming which role a token actually carries.</remarks>
    /// <response code="200">The current user.</response>
    /// <response code="401">No valid bearer token was supplied.</response>
    [HttpGet("me", Name = "GetCurrentUser")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse<UserDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<UserDto>>> Me(CancellationToken ct)
        => Ok(Envelope(await _auth.GetCurrentUserAsync(ct)));
}
