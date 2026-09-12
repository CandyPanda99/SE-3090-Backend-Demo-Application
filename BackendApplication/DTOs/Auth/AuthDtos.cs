using System.ComponentModel.DataAnnotations;
using BackendApplication.Domain.Enums;

namespace BackendApplication.DTOs.Auth;

/// <summary>Payload for <c>POST /api/v1/auth/register</c>.</summary>
public sealed record RegisterDto
{
    /// <summary>Email address, used as the login name.</summary>
    /// <example>student@sliit.lk</example>
    [Required]
    [EmailAddress(ErrorMessage = "A valid email address is required.")]
    [StringLength(256)]
    public string Email { get; init; } = string.Empty;

    /// <summary>
    /// Plain-text password. Travels over HTTPS, is hashed immediately, and is never
    /// stored or logged.
    /// </summary>
    /// <remarks>The full rule set lives in Validation/AuthValidators.cs.</remarks>
    /// <example>Str0ng!Passw0rd</example>
    [Required]
    [StringLength(100, MinimumLength = 8, ErrorMessage = "Password must be at least 8 characters.")]
    public string Password { get; init; } = string.Empty;

    /// <summary>Must match <see cref="Password"/> exactly.</summary>
    [Required]
    [Compare(nameof(Password), ErrorMessage = "Password confirmation does not match.")]
    public string ConfirmPassword { get; init; } = string.Empty;

    /// <summary>The user's full name.</summary>
    /// <example>Lasal Hettiarachchi</example>
    [Required, StringLength(150, MinimumLength = 2)]
    public string FullName { get; init; } = string.Empty;
}

/// <summary>Payload for <c>POST /api/v1/auth/login</c>.</summary>
public sealed record LoginDto
{
    /// <example>admin@tasks.local</example>
    [Required, EmailAddress]
    public string Email { get; init; } = string.Empty;

    /// <example>Admin#12345</example>
    [Required]
    public string Password { get; init; } = string.Empty;
}

/// <summary>Payload for exchanging a refresh token for a new access token.</summary>
public sealed record RefreshTokenDto
{
    [Required]
    public string RefreshToken { get; init; } = string.Empty;
}

/// <summary>What a successful authentication returns.</summary>
/// <param name="AccessToken">The JWT. Send it as <c>Authorization: Bearer &lt;token&gt;</c>.</param>
/// <param name="RefreshToken">Opaque token used to get a new access token.</param>
/// <param name="ExpiresAtUtc">When the access token stops working.</param>
/// <param name="TokenType">Always "Bearer", per RFC 6750.</param>
/// <param name="User">Basic profile, so the client need not decode the JWT itself.</param>
public sealed record AuthResponseDto(
    string AccessToken,
    string RefreshToken,
    DateTime ExpiresAtUtc,
    string TokenType,
    UserDto User);

/// <summary>
/// Public view of a user.
/// </summary>
/// <remarks>
/// Note what is absent: PasswordHash. A DTO is the boundary that makes leaking it
/// require a deliberate edit rather than an oversight.
/// </remarks>
public sealed record UserDto(
    int Id,
    string Email,
    string FullName,
    UserRole Role);
