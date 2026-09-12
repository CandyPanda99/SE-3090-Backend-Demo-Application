namespace BackendApplication.Domain.Enums;

/// <summary>
/// Authorisation roles, written into the JWT as a <c>ClaimTypes.Role</c> claim.
/// </summary>
public enum UserRole
{
    /// <summary>An ordinary team member. Can see every task and manage the ones assigned to them.</summary>
    Member = 0,

    /// <summary>Runs a team. Can create, reassign and close any task.</summary>
    Lead = 1,

    /// <summary>Full control, including deleting tasks.</summary>
    Admin = 2
}

/// <summary>
/// String constants matching <see cref="UserRole"/>, for use in
/// <c>[Authorize(Roles = ...)]</c> attributes, which only accept strings.
/// </summary>
/// <remarks>
/// <c>nameof</c> keeps these in step with the enum: rename a member and this file stops
/// compiling, rather than silently authorising nobody.
/// </remarks>
public static class Roles
{
    public const string Member = nameof(UserRole.Member);
    public const string Lead = nameof(UserRole.Lead);
    public const string Admin = nameof(UserRole.Admin);

    /// <summary>Anyone who can administer the backlog.</summary>
    public const string LeadOrAdmin = $"{Lead},{Admin}";
}
