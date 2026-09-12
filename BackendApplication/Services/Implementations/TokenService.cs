using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using BackendApplication.Configuration;
using BackendApplication.Domain.Entities;
using BackendApplication.Services.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace BackendApplication.Services.Implementations;

/// <summary>
/// Builds and signs JSON Web Tokens.
/// </summary>
public sealed class TokenService : ITokenService
{
    private readonly JwtOptions _options;
    private readonly TimeProvider _time;
    private readonly SymmetricSecurityKey _signingKey;

    /// <param name="options">JWT configuration.</param>
    /// <param name="time">Injected clock, so token expiry is testable.</param>
    public TokenService(IOptions<JwtOptions> options, TimeProvider time)
    {
        _options = options.Value;
        _time = time;

        _signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Key));
    }

    /// <inheritdoc />
    public (string Token, DateTime ExpiresAtUtc) CreateAccessToken(AppUser user)
    {
        var nowUtc = _time.GetUtcNow().UtcDateTime;
        var expiresUtc = nowUtc.AddMinutes(_options.AccessTokenMinutes);

        // A JWT is signed, not encrypted. Anyone holding it can read these claims, so
        // nothing sensitive goes in - an id and a role, never a password or an email
        // the user has not already given us.
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),

            // A unique id per token, so an individual token can be denylisted if it ever
            // needs to be revoked before expiry.
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),

            new(JwtRegisteredClaimNames.Email, user.Email),
            new(JwtRegisteredClaimNames.Name, user.FullName),

            new(JwtRegisteredClaimNames.Iat,
                new DateTimeOffset(nowUtc).ToUnixTimeSeconds().ToString(),
                ClaimValueTypes.Integer64),

            new(ClaimTypes.Role, user.Role.ToString()),

            // Both spellings: ClaimTypes.Role is the long schema URI ASP.NET Core's
            // [Authorize(Roles=...)] looks for, while "role" is what most non-.NET clients
            // expect to find when they decode the token.
            new("role", user.Role.ToString())
        };

        var credentials = new SigningCredentials(_signingKey, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: nowUtc,
            expires: expiresUtc,
            signingCredentials: credentials);

        return (new JwtSecurityTokenHandler().WriteToken(token), expiresUtc);
    }

    /// <inheritdoc />
    /// <remarks>
    /// 64 random bytes from a cryptographic RNG, not a Guid. A Guid is unique but not
    /// unpredictable, and this value is a credential.
    /// </remarks>
    public RefreshToken CreateRefreshToken(int userId) => new()
    {
        Token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)),
        UserId = userId,
        CreatedAtUtc = _time.GetUtcNow().UtcDateTime,
        ExpiresAtUtc = _time.GetUtcNow().UtcDateTime.AddDays(_options.RefreshTokenDays)
    };

    /// <inheritdoc />
    /// <remarks>Validates everything except the expiry.</remarks>
    public ClaimsPrincipal? GetPrincipalFromExpiredToken(string token)
    {
        var parameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = _options.Issuer,
            ValidateAudience = true,
            ValidAudience = _options.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = _signingKey,
            ValidateLifetime = false,
            ClockSkew = TimeSpan.Zero
        };

        try
        {
            var principal = new JwtSecurityTokenHandler()
                .ValidateToken(token, parameters, out var securityToken);

            // Pin the algorithm. Without this check a token signed with "alg": "none",
            // or with a weaker algorithm, could be accepted.
            if (securityToken is not JwtSecurityToken jwt ||
                !jwt.Header.Alg.Equals(SecurityAlgorithms.HmacSha256, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return principal;
        }
        catch (SecurityTokenException)
        {
            return null;
        }
    }
}
