using System.Security.Claims;
using BackendApplication.Domain.Entities;
using BackendApplication.DTOs.Auth;

namespace BackendApplication.Services.Abstractions;

/// <summary>Registration, login and refresh.</summary>
/// <remarks>
/// Authentication is supporting infrastructure here, not the subject of the API. It
/// exists so the single Tasks resource has something to authorise against.
/// </remarks>
public interface IAuthService
{
    Task<AuthResponseDto> RegisterAsync(RegisterDto dto, CancellationToken ct = default);

    Task<AuthResponseDto> LoginAsync(LoginDto dto, CancellationToken ct = default);

    /// <summary>Exchanges a valid refresh token for a new token pair (with rotation).</summary>
    Task<AuthResponseDto> RefreshAsync(RefreshTokenDto dto, CancellationToken ct = default);

    /// <summary>Revokes a refresh token. This is "logout".</summary>
    Task RevokeAsync(string refreshToken, CancellationToken ct = default);

    /// <summary>The profile of the currently authenticated user.</summary>
    Task<UserDto> GetCurrentUserAsync(CancellationToken ct = default);
}

/// <summary>Creates JWTs and refresh tokens.</summary>
public interface ITokenService
{
    /// <summary>Builds and signs a JWT access token for the given user.</summary>
    /// <returns>The compact token string and its expiry.</returns>
    (string Token, DateTime ExpiresAtUtc) CreateAccessToken(AppUser user);

    /// <summary>Generates a cryptographically random opaque refresh token.</summary>
    RefreshToken CreateRefreshToken(int userId);

    /// <summary>Reads the claims out of an expired token, verifying only the signature.</summary>
    ClaimsPrincipal? GetPrincipalFromExpiredToken(string token);
}
