using System.ComponentModel.DataAnnotations;

namespace BackendApplication.Configuration;

/// <summary>
/// Settings that control how JSON Web Tokens are issued and validated.
/// Bound from the <c>"Jwt"</c> configuration section.
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>
    /// The symmetric signing key. Must be long and secret.
    /// </summary>
    /// <remarks>
    /// Supply it via user secrets, the <c>Jwt__Key</c> environment variable, or a secret
    /// manager - never commit a real one. The key in appsettings is a labelled
    /// placeholder so the app runs out of the box; anything deployed with it is
    /// forgeable by anyone who has read the repository.
    /// </remarks>
    [Required, MinLength(32, ErrorMessage = "Jwt:Key must be at least 32 characters (256 bits) for HS256.")]
    public string Key { get; set; } = string.Empty;

    /// <summary>Who issued the token. Validated on every request ("iss" claim).</summary>
    [Required]
    public string Issuer { get; set; } = "BackendApplication.TasksApi";

    /// <summary>Who the token is for. Validated on every request ("aud" claim).</summary>
    [Required]
    public string Audience { get; set; } = "BackendApplication.TasksClients";

    /// <summary>
    /// Access-token lifetime. Short on purpose - a JWT cannot be revoked.
    /// </summary>
    [Range(1, 1440)]
    public int AccessTokenMinutes { get; set; } = 15;

    /// <summary>Refresh-token lifetime in days. Revocable, since it is stored in the database.</summary>
    [Range(1, 90)]
    public int RefreshTokenDays { get; set; } = 7;

    /// <summary>
    /// Allowed clock difference between the issuing and validating servers.
    /// </summary>
    /// <remarks>
    /// Defaults to zero. The library's own default is five minutes, which quietly keeps
    /// expired tokens working for longer than the configured lifetime suggests.
    /// </remarks>
    [Range(0, 300)]
    public int ClockSkewSeconds { get; set; }
}
