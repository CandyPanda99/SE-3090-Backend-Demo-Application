using System.ComponentModel.DataAnnotations;
using BackendApplication.Domain.Enums;

namespace BackendApplication.DTOs.Tasks;

/// <summary>
/// A task as returned by the API.
/// </summary>
/// <remarks>
/// Deliberately not the entity. Returning <c>TaskItem</c> directly would expose
/// <c>IsDeleted</c> and the concurrency token, and would mean any rename in the domain
/// silently became a breaking change for every client.
/// </remarks>
/// <param name="Id">Unique identifier.</param>
/// <param name="Reference">Human-facing business identifier, e.g. TSK-0042.</param>
/// <param name="Title">Short summary of the work.</param>
/// <param name="Description">Long-form detail.</param>
/// <param name="Status">Lifecycle status, serialised as a string.</param>
/// <param name="Priority">Urgency, serialised as a string.</param>
/// <param name="DueAtUtc">When the task is due, or null.</param>
/// <param name="CompletedAtUtc">When the task reached Done, or null.</param>
/// <param name="EstimatedHours">Rough size in hours, or null if unestimated.</param>
/// <param name="AssigneeId">Owning user id, or null while unassigned.</param>
/// <param name="AssigneeName">Owning user's name, flattened for convenience.</param>
/// <param name="CreatedAtUtc">When the task was created.</param>
/// <param name="UpdatedAtUtc">When the task was last modified.</param>
public sealed record TaskDto(
    int Id,
    string Reference,
    string Title,
    string? Description,
    TaskItemStatus Status,
    TaskPriority Priority,
    DateTime? DueAtUtc,
    DateTime? CompletedAtUtc,
    decimal? EstimatedHours,
    int? AssigneeId,
    string? AssigneeName,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc)
{
    /// <summary>Computed: past its due date and not yet finished.</summary>
    /// <remarks>
    /// Not part of the positional constructor on purpose. Everything above maps to a
    /// column and so can be built inside a SQL projection; this and
    /// <see cref="AllowedNextStatuses"/> cannot, because one compares against the current
    /// time and the other reads a C# dictionary. They are filled in after materialisation
    /// with a <c>with</c> expression - see <c>Mapping/TaskMappings.cs</c>.
    /// </remarks>
    public bool IsOverdue { get; init; }

    /// <summary>
    /// The statuses this task may legally move to right now.
    /// </summary>
    /// <remarks>
    /// Saves the client from hard-coding the state machine. An empty list means the task
    /// has reached a terminal state.
    /// </remarks>
    public IReadOnlyList<TaskItemStatus> AllowedNextStatuses { get; init; } = [];
}

/// <summary>
/// Payload for <c>POST /api/v1/tasks</c>.
/// </summary>
/// <remarks>
/// A separate type from <see cref="TaskDto"/> because the fields a client may *send*
/// are not the fields it *receives*: Id, Reference and the audit stamps are assigned by
/// the server, and accepting them would let a caller forge them.
/// </remarks>
public sealed record CreateTaskDto
{
    /// <summary>Short summary of the work.</summary>
    /// <example>Add pagination to the reports screen</example>
    [Required(ErrorMessage = "Title is required.")]
    [StringLength(200, MinimumLength = 3)]
    public string Title { get; init; } = string.Empty;

    /// <summary>Optional long-form detail.</summary>
    /// <example>The screen currently loads every row. Add offset paging with a page size of 20.</example>
    [StringLength(4000)]
    public string? Description { get; init; }

    /// <summary>Urgency. Defaults to Medium.</summary>
    /// <example>High</example>
    [EnumDataType(typeof(TaskPriority), ErrorMessage = "Priority must be Low, Medium, High or Critical.")]
    public TaskPriority Priority { get; init; } = TaskPriority.Medium;

    /// <summary>When the task is due (UTC). Must be in the future.</summary>
    /// <example>2026-12-31T17:00:00Z</example>
    public DateTime? DueAtUtc { get; init; }

    /// <summary>Rough size in hours.</summary>
    /// <example>6.5</example>
    [Range(typeof(decimal), "0.25", "9999.99", ErrorMessage = "Estimated hours must be between 0.25 and 9999.99.")]
    public decimal? EstimatedHours { get; init; }

    /// <summary>The user to assign it to. Must exist. Omit to leave it unassigned.</summary>
    /// <example>3</example>
    public int? AssigneeId { get; init; }
}

/// <summary>
/// Payload for <c>PUT /api/v1/tasks/{id}</c> - a full replacement of the editable fields.
/// </summary>
/// <remarks>
/// Status is absent on purpose. It changes only through the dedicated PATCH endpoint,
/// because a status change has to run the state machine; allowing it here would let a
/// caller move a task straight from Todo to Done by sending the whole object back.
/// </remarks>
public sealed record UpdateTaskDto
{
    [Required, StringLength(200, MinimumLength = 3)]
    public string Title { get; init; } = string.Empty;

    [StringLength(4000)]
    public string? Description { get; init; }

    [EnumDataType(typeof(TaskPriority))]
    public TaskPriority Priority { get; init; }

    public DateTime? DueAtUtc { get; init; }

    [Range(typeof(decimal), "0.25", "9999.99")]
    public decimal? EstimatedHours { get; init; }

    /// <summary>The assignee, or null to unassign.</summary>
    public int? AssigneeId { get; init; }
}

/// <summary>Payload for the narrow "change status only" endpoint.</summary>
public sealed record PatchTaskStatusDto
{
    /// <summary>The status to move to. Must be a legal transition from the current one.</summary>
    /// <example>InProgress</example>
    [Required]
    [EnumDataType(typeof(TaskItemStatus))]
    public TaskItemStatus Status { get; init; }
}
