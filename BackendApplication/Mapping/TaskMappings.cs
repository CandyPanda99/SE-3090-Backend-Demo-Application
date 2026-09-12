using System.Linq.Expressions;
using BackendApplication.Domain.Entities;
using BackendApplication.Domain.Enums;
using BackendApplication.DTOs.Tasks;

namespace BackendApplication.Mapping;

/// <summary>
/// Entity &lt;-&gt; DTO translation for tasks.
/// </summary>
/// <remarks>
/// Hand-written rather than reflection-based. It is more lines, but the compiler checks
/// every one of them: rename a property and this file fails the build, instead of
/// silently producing nulls at runtime the way a convention-based mapper would.
/// </remarks>
public static class TaskMappings
{
    /// <summary>
    /// Projection used with <c>IQueryable.Select()</c> so EF Core builds an efficient
    /// SELECT with only the needed columns and an automatic LEFT JOIN to the assignee.
    /// </summary>
    /// <remarks>
    /// Every expression here has to be translatable to SQL, which is why the two computed
    /// members of <see cref="TaskDto"/> are left at their defaults and filled in by
    /// <see cref="WithComputed(TaskDto)"/> once the rows are in memory. Calling
    /// <c>TaskStatusTransitions.NextStatesFrom</c> inside this expression would compile
    /// happily and then throw at runtime, because EF has no way to turn a dictionary
    /// lookup into SQL.
    /// </remarks>
    public static Expression<Func<TaskItem, TaskDto>> ToDtoProjection =>
        t => new TaskDto(
            t.Id,
            t.Reference,
            t.Title,
            t.Description,
            t.Status,
            t.Priority,
            t.DueAtUtc,
            t.CompletedAtUtc,
            t.EstimatedHours,
            t.AssigneeId,
            t.Assignee != null ? t.Assignee.FullName : null,
            t.CreatedAtUtc,
            t.UpdatedAtUtc);

    /// <summary>
    /// Fills in the members that could not be computed in SQL.
    /// </summary>
    /// <remarks>
    /// Uses a <c>with</c> expression, which copies the record and overwrites only the
    /// named properties - the rest are carried across without being retyped.
    /// </remarks>
    public static TaskDto WithComputed(this TaskDto dto) => dto with
    {
        IsOverdue =
            dto.DueAtUtc is { } due &&
            dto.Status is not (TaskItemStatus.Done or TaskItemStatus.Cancelled) &&
            due < DateTime.UtcNow,

        AllowedNextStatuses = TaskStatusTransitions.NextStatesFrom(dto.Status)
    };

    /// <summary>Applies <see cref="WithComputed(TaskDto)"/> across a materialised page.</summary>
    public static List<TaskDto> WithComputed(this IEnumerable<TaskDto> items)
        => items.Select(WithComputed).ToList();

    /// <summary>In-memory mapping for an entity that is already loaded.</summary>
    public static TaskDto ToDto(this TaskItem t) =>
        new TaskDto(
            t.Id,
            t.Reference,
            t.Title,
            t.Description,
            t.Status,
            t.Priority,
            t.DueAtUtc,
            t.CompletedAtUtc,
            t.EstimatedHours,
            t.AssigneeId,
            t.Assignee?.FullName,
            t.CreatedAtUtc,
            t.UpdatedAtUtc).WithComputed();

    /// <summary>
    /// Builds a new entity from a create request.
    /// </summary>
    /// <remarks>
    /// <c>Reference</c> is absent: the service allocates it from the sequence, because a
    /// client-supplied identifier is a client-supplied collision.
    /// </remarks>
    public static TaskItem ToEntity(this CreateTaskDto dto) =>
        new()
        {
            Title = dto.Title.Trim(),
            Description = dto.Description?.Trim(),
            Priority = dto.Priority,
            DueAtUtc = NormaliseToUtc(dto.DueAtUtc),
            EstimatedHours = dto.EstimatedHours,
            AssigneeId = dto.AssigneeId,

            // New tasks always start in the backlog. Letting the client choose would be a
            // way around the state machine before the task even exists.
            Status = TaskItemStatus.Todo
        };

    /// <summary>
    /// Copies an update request onto an entity that EF Core is already tracking.
    /// </summary>
    /// <remarks>Status is deliberately not copied - it moves only through the PATCH endpoint.</remarks>
    public static void ApplyTo(this UpdateTaskDto dto, TaskItem entity)
    {
        entity.Title = dto.Title.Trim();
        entity.Description = dto.Description?.Trim();
        entity.Priority = dto.Priority;
        entity.DueAtUtc = NormaliseToUtc(dto.DueAtUtc);
        entity.EstimatedHours = dto.EstimatedHours;
        entity.AssigneeId = dto.AssigneeId;
    }

    /// <summary>
    /// Forces an incoming timestamp to UTC before it reaches the database.
    /// </summary>
    /// <remarks>
    /// The columns are <c>timestamp with time zone</c>, and Npgsql refuses to write a
    /// DateTime whose Kind is Local or Unspecified to one - it throws rather than guess
    /// which zone was meant. A client sending <c>2026-12-31T17:00:00</c> with no offset
    /// parses as Unspecified, so without this every such request would fail with an
    /// unhelpful 500. Unspecified is read as "already UTC"; Local is converted.
    /// </remarks>
    private static DateTime? NormaliseToUtc(DateTime? value) => value switch
    {
        null => null,
        { Kind: DateTimeKind.Utc } utc => utc,
        { Kind: DateTimeKind.Local } local => local.ToUniversalTime(),
        var unspecified => DateTime.SpecifyKind(unspecified.Value, DateTimeKind.Utc)
    };
}

/// <summary>Entity -&gt; DTO translation for users.</summary>
public static class UserMappings
{
    /// <summary>Public projection of a user. Never includes the password hash.</summary>
    public static DTOs.Auth.UserDto ToDto(this AppUser user)
        => new(user.Id, user.Email, user.FullName, user.Role);
}
