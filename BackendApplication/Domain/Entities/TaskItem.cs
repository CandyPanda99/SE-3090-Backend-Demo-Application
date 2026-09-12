using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using BackendApplication.Domain.Enums;

namespace BackendApplication.Domain.Entities;

/// <summary>
/// A unit of work on the board. The central entity of this application.
/// </summary>
/// <remarks>
/// Named <c>TaskItem</c>, not <c>Task</c>: <see cref="System.Threading.Tasks.Task"/> is in
/// scope in every file thanks to implicit usings, and every <c>async</c> method here
/// returns one. A domain type sharing that name would mean writing
/// <c>Domain.Entities.Task</c> or <c>System.Threading.Tasks.Task</c> forever.
///
/// Three of this entity's indexes are declared here as attributes; the filtered one
/// (IX_Tasks_Title_Active) has to stay in <c>TaskItemConfiguration</c>, because
/// [Index] cannot express a partial index.
/// </remarks>
[Table("Tasks")]
[Index(nameof(Reference), IsUnique = true, Name = "IX_Tasks_Reference")]
[Index(nameof(AssigneeId), Name = "IX_Tasks_AssigneeId")]
[Index(nameof(Status), nameof(DueAtUtc), Name = "IX_Tasks_Status_DueAt")]
public class TaskItem : AuditableEntity
{
    /// <summary>
    /// The human-facing business identifier, e.g. "TSK-0042".
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="BaseEntity.Id"/> on purpose. Id is a database detail;
    /// this is what people type into chat messages and commit summaries, so it has to
    /// stay stable and readable even if rows are ever migrated between databases.
    /// </remarks>
    [Required, MaxLength(30)]
    public string Reference { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(4000)]
    public string? Description { get; set; }

    /// <summary>Lifecycle state. Stored as text in the database for readability.</summary>
    /// <remarks>Value conversion plus a DEFAULT - neither has an attribute. Stays fluent.</remarks>
    public TaskItemStatus Status { get; set; } = TaskItemStatus.Todo;

    /// <summary>How urgent this task is. Stored as text, same reasoning as Status.</summary>
    public TaskPriority Priority { get; set; } = TaskPriority.Medium;

    /// <summary>
    /// When the task is due, or null for "no deadline".
    /// </summary>
    /// <remarks>
    /// Always UTC. Storing local times is the classic way to produce a system that works
    /// until the clocks change; the column type is <c>timestamp with time zone</c>, set
    /// once as a convention in <c>TasksDbContext.ConfigureConventions</c>.
    /// </remarks>
    public DateTime? DueAtUtc { get; set; }

    /// <summary>When the task actually reached <see cref="TaskItemStatus.Done"/>.</summary>
    public DateTime? CompletedAtUtc { get; set; }

    /// <summary>Rough size in hours. Null while the task is still unestimated.</summary>
    [Column(TypeName = "numeric(6,2)")]
    public decimal? EstimatedHours { get; set; }

    /// <summary>The user responsible for the task, or null while it sits unassigned.</summary>
    public int? AssigneeId { get; set; }

    /// <summary>Navigation property for the assignee.</summary>
    public AppUser? Assignee { get; set; }

    /// <summary>Optimistic concurrency token, mapped onto PostgreSQL's <c>xmin</c> system column.</summary>
    /// <remarks>
    /// Costs nothing: xmin already exists on every row. Two users editing the same task
    /// now produces a 409 rather than one silently overwriting the other.
    /// </remarks>
    [Timestamp]
    public uint Version { get; set; }

    /// <summary>True when the task is past its due date and not yet finished.</summary>
    /// <remarks>
    /// Computed in C#, not mapped to a column. Anything comparing against "now" cannot be
    /// stored, because it would be stale the moment it was written.
    /// </remarks>
    public bool IsOverdue =>
        DueAtUtc is { } due &&
        Status is not (TaskItemStatus.Done or TaskItemStatus.Cancelled) &&
        due < DateTime.UtcNow;

    /// <summary>True when the task can still change state.</summary>
    public bool IsOpen => !TaskStatusTransitions.IsTerminal(Status);

    /// <summary>
    /// Moves the task to <paramref name="next"/> if the state machine allows it.
    /// </summary>
    /// <remarks>
    /// The entity owns this rather than the service, so the invariant travels with the
    /// data: there is no way to reach an illegal state by going around the service layer.
    /// Stamping <see cref="CompletedAtUtc"/> here is the same idea - it cannot drift out
    /// of step with Status if only one method sets both.
    /// </remarks>
    /// <param name="next">The status to move to.</param>
    /// <param name="nowUtc">The current time, injected so the behaviour is testable.</param>
    /// <returns><c>true</c> if the move was legal and applied; otherwise <c>false</c>.</returns>
    public bool TryTransitionTo(TaskItemStatus next, DateTime nowUtc)
    {
        if (!TaskStatusTransitions.CanMove(Status, next))
        {
            return false;
        }

        Status = next;

        CompletedAtUtc = next == TaskItemStatus.Done ? nowUtc : null;

        return true;
    }

    /// <summary>Assigns the task to a user, or clears the assignment when null.</summary>
    public void AssignTo(int? userId) => AssigneeId = userId;
}
