using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BackendApplication.Domain.Entities;

/// <summary>
/// A long-lived, single-use token that buys a fresh short-lived access token.
/// </summary>
/// <remarks>
/// Access tokens are JWTs: self-contained, signed, and impossible to revoke before
/// they expire. That is why they are short-lived and why this exists - the refresh
/// token is a row in a table, so revoking it is an UPDATE.
/// </remarks>
[Table("RefreshTokens")]
[Index(nameof(Token), IsUnique = true, Name = "IX_RefreshTokens_Token")]
public class RefreshToken : BaseEntity
{
    /// <summary>Cryptographically random opaque string (not a JWT - it carries no data).</summary>
    [Required, MaxLength(200)]
    public string Token { get; set; } = string.Empty;

    public DateTime ExpiresAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    /// <summary>Set when the token is used or explicitly logged out. Null = still live.</summary>
    public DateTime? RevokedAtUtc { get; set; }

    /// <summary>The token issued in place of this one, forming an audit chain.</summary>
    [MaxLength(200)]
    public string? ReplacedByToken { get; set; }

    public int UserId { get; set; }

    public AppUser User { get; set; } = null!;

    public bool IsExpired => DateTime.UtcNow >= ExpiresAtUtc;

    /// <summary>Usable only if it has neither expired nor been revoked.</summary>
    public bool IsActive => RevokedAtUtc is null && !IsExpired;
}
