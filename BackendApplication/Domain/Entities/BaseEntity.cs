using System.ComponentModel.DataAnnotations;

namespace BackendApplication.Domain.Entities;

/// <summary>
/// The smallest thing every stored object shares: a primary key.
/// </summary>
public abstract class BaseEntity
{
    /// <summary>Primary key. PostgreSQL generates it via an identity column.</summary>
    /// <remarks>
    /// [Key] replaces the <c>builder.HasKey(x =&gt; x.Id)</c> line every configuration would
    /// otherwise repeat. Declaring it once here covers every entity, since they all derive
    /// from this class.
    /// </remarks>
    [Key]
    public int Id { get; set; }
}

/// <summary>
/// Adds "who touched this row and when" columns plus soft-delete support.
/// </summary>
/// <remarks>
/// These properties are stamped automatically by
/// <c>Data/Interceptors/AuditingSaveChangesInterceptor.cs</c>. No service ever sets
/// them by hand, which is the point: an audit trail that depends on every developer
/// remembering to fill it in is an audit trail with holes.
/// </remarks>
public abstract class AuditableEntity : BaseEntity
{
    /// <summary>UTC timestamp of INSERT.</summary>
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>UTC timestamp of the most recent UPDATE, or null if never modified.</summary>
    public DateTime? UpdatedAtUtc { get; set; }

    /// <summary>Email of the authenticated user who created the row (null for the seeder).</summary>
    [MaxLength(256)]
    public string? CreatedBy { get; set; }

    /// <summary>Email of the authenticated user who last modified the row.</summary>
    [MaxLength(256)]
    public string? UpdatedBy { get; set; }

    /// <summary>Soft-delete marker. True rows are filtered out of every query.</summary>
    /// <remarks>
    /// The <c>DEFAULT false</c> on this column has no attribute equivalent, so every
    /// configuration still carries one <c>HasDefaultValue(false)</c> line for it.
    /// </remarks>
    public bool IsDeleted { get; set; }
}
