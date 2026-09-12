using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using BackendApplication.Domain.Enums;

namespace BackendApplication.Domain.Entities;

/// <summary>
/// A person who can log in: a team member, a lead, or an administrator.
/// </summary>
[Table("Users")]
[Index(nameof(Email), IsUnique = true, Name = "IX_Users_Email")]
public class AppUser : AuditableEntity
{
    /// <summary>Login identifier. Stored lower-cased and uniquely indexed.</summary>
    [Required, MaxLength(256)]
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// The BCrypt hash of the password - never the password itself.
    /// </summary>
    /// <remarks>
    /// BCrypt embeds its salt and work factor in the hash string, which is why one
    /// column is enough and no separate salt column exists.
    /// </remarks>
    [Required, MaxLength(256)]
    public string PasswordHash { get; set; } = string.Empty;

    [Required, MaxLength(150)]
    public string FullName { get; set; } = string.Empty;

    /// <summary>Determines what the user is allowed to do. Written into the JWT.</summary>
    /// <remarks>
    /// Deliberately left bare: this column is stored as text rather than a number, and
    /// value conversion has no attribute. See <c>AppUserConfiguration</c>.
    /// </remarks>
    public UserRole Role { get; set; } = UserRole.Member;

    /// <summary>Set false to block sign-in without deleting history.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Long-lived tokens used to obtain new access tokens.</summary>
    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();

    /// <summary>Tasks currently assigned to this user.</summary>
    public ICollection<TaskItem> AssignedTasks { get; set; } = new List<TaskItem>();
}
