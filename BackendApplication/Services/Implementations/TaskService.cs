using System.Linq.Expressions;
using BackendApplication.Data.Repositories;
using BackendApplication.Domain.Entities;
using BackendApplication.Domain.Enums;
using BackendApplication.DTOs.Common;
using BackendApplication.DTOs.Tasks;
using BackendApplication.Exceptions;
using BackendApplication.Mapping;
using BackendApplication.Services.Abstractions;

namespace BackendApplication.Services.Implementations;

/// <summary>
/// The task business logic: filter composition, safe sorting, paging maths, reference
/// allocation, the status state machine and the ownership rule.
/// </summary>
/// <remarks>
/// Controllers stay thin by design. Everything here is testable without an HTTP request,
/// and nothing here knows what a status code is.
/// </remarks>
public sealed class TaskService : ITaskService
{
    private readonly ITaskRepository _tasks;
    private readonly IRepository<AppUser> _users;
    private readonly ICurrentUserService _currentUser;
    private readonly TimeProvider _time;
    private readonly ILogger<TaskService> _logger;

    public TaskService(
        ITaskRepository tasks,
        IRepository<AppUser> users,
        ICurrentUserService currentUser,
        TimeProvider time,
        ILogger<TaskService> logger)
    {
        _tasks = tasks;
        _users = users;
        _currentUser = currentUser;
        _time = time;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<PagedResult<TaskDto>> GetPagedAsync(
        TaskQueryParameters query,
        CancellationToken ct = default)
    {
        var tasks = ApplyFilters(_tasks.Query(), query, _time.GetUtcNow().UtcDateTime);

        // Two queries, deliberately: one COUNT over the filtered set, one page of rows.
        // Loading everything to count it in memory is the mistake this avoids.
        var totalCount = await tasks.CountAsync(ct);

        var rows = await ApplySorting(tasks, query)
            .Skip(query.Skip)
            .Take(query.Take)
            .Select(TaskMappings.ToDtoProjection)
            .ToListAsync(ct);

        _logger.LogInformation(
            "Task search returned {Count} of {Total} matches (page {Page}, size {Size})",
            rows.Count, totalCount, query.PageNumber, query.PageSize);

        return PagedResult<TaskDto>.Create(rows.WithComputed(), query.PageNumber, query.PageSize, totalCount);
    }

    /// <summary>
    /// Adds a WHERE clause for each filter the client actually supplied.
    /// </summary>
    /// <remarks>
    /// Each <c>if</c> narrows the IQueryable without executing it. The whole chain becomes
    /// one SQL statement when it is finally enumerated, so an unsupplied filter costs
    /// nothing rather than filtering in memory afterwards.
    /// </remarks>
    private static IQueryable<TaskItem> ApplyFilters(
        IQueryable<TaskItem> query,
        TaskQueryParameters q,
        DateTime nowUtc)
    {
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var term = q.Search.Trim();

            // ILike is PostgreSQL's case-insensitive LIKE. Using it keeps the comparison in
            // the database; ToLower() on both sides would too, but would also defeat any
            // index on the column.
            query = query.Where(t =>
                EF.Functions.ILike(t.Reference, $"%{term}%") ||
                EF.Functions.ILike(t.Title, $"%{term}%") ||
                (t.Description != null && EF.Functions.ILike(t.Description, $"%{term}%")));
        }

        if (q.Status is { } status)
        {
            query = query.Where(t => t.Status == status);
        }

        if (q.Priority is { } priority)
        {
            query = query.Where(t => t.Priority == priority);
        }

        // AssigneeId wins over UnassignedOnly when both are sent - they contradict, and
        // silently returning nothing would look like a bug to the caller.
        if (q.AssigneeId is { } assigneeId)
        {
            query = query.Where(t => t.AssigneeId == assigneeId);
        }
        else if (q.UnassignedOnly == true)
        {
            query = query.Where(t => t.AssigneeId == null);
        }

        if (q.OpenOnly == true)
        {
            query = query.Where(t =>
                t.Status != TaskItemStatus.Done && t.Status != TaskItemStatus.Cancelled);
        }

        if (q.OverdueOnly == true)
        {
            query = query.Where(t =>
                t.DueAtUtc != null &&
                t.DueAtUtc < nowUtc &&
                t.Status != TaskItemStatus.Done &&
                t.Status != TaskItemStatus.Cancelled);
        }

        if (q.DueAfterUtc is { } after)
        {
            query = query.Where(t => t.DueAtUtc != null && t.DueAtUtc >= after);
        }

        if (q.DueBeforeUtc is { } before)
        {
            query = query.Where(t => t.DueAtUtc != null && t.DueAtUtc <= before);
        }

        return query;
    }

    /// <summary>
    /// Applies ORDER BY from a hard-coded allow-list.
    /// </summary>
    /// <remarks>
    /// The client sends a name, not an expression. Anything unrecognised falls through to
    /// the default rather than erroring, and the raw string never reaches SQL - which is
    /// what stops <c>?sortBy=1;DROP TABLE</c> from being interesting.
    /// </remarks>
    private static IQueryable<TaskItem> ApplySorting(IQueryable<TaskItem> query, TaskQueryParameters q)
    {
        // Priority is stored as text, so ordering on the column directly would sort
        // alphabetically - Critical, High, Low, Medium - which is meaningless. This maps
        // each value to a rank, and EF translates it into a SQL CASE expression.
        Expression<Func<TaskItem, object>> priorityRank = t =>
            t.Priority == TaskPriority.Critical ? 0
            : t.Priority == TaskPriority.High ? 1
            : t.Priority == TaskPriority.Medium ? 2
            : 3;

        Expression<Func<TaskItem, object>> keySelector = q.SortBy?.ToLowerInvariant() switch
        {
            "reference" => t => t.Reference,
            "title" => t => t.Title,
            "status" => t => t.Status,
            "priority" => priorityRank,

            // Null due dates sort last regardless of direction: "no deadline" is not the
            // same as "due at the beginning of time".
            "dueat" => t => t.DueAtUtc ?? DateTime.MaxValue,

            "createdat" => t => t.CreatedAtUtc,
            _ => t => t.CreatedAtUtc
        };

        var ordered = q.SortDirection == SortDirection.Desc
            ? query.OrderByDescending(keySelector)
            : query.OrderBy(keySelector);

        // A tiebreaker on the primary key. Without it, two rows with equal sort keys can
        // come back in a different order on each request, and a row can appear on both
        // page 1 and page 2 - or on neither.
        return ordered.ThenBy(t => t.Id);
    }

    /// <inheritdoc />
    public async Task<TaskDto> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var dto = await _tasks.Query()
            .Where(t => t.Id == id)
            .Select(TaskMappings.ToDtoProjection)
            .FirstOrDefaultAsync(ct);

        return dto?.WithComputed() ?? throw new NotFoundException("Task", id);
    }

    /// <inheritdoc />
    public async Task<TaskDto> CreateAsync(CreateTaskDto dto, CancellationToken ct = default)
    {
        await EnsureAssigneeExistsAsync(dto.AssigneeId, ct);

        var entity = dto.ToEntity();

        entity.Reference = await _tasks.NextReferenceAsync(ct);

        await _tasks.AddAsync(entity, ct);
        await _tasks.SaveChangesAsync(ct);

        _logger.LogInformation("Created task {TaskId} with reference {Reference}", entity.Id, entity.Reference);

        return await GetByIdAsync(entity.Id, ct);
    }

    /// <inheritdoc />
    public async Task<TaskDto> UpdateAsync(int id, UpdateTaskDto dto, CancellationToken ct = default)
    {
        var entity = await _tasks.GetByIdAsync(id, asNoTracking: false, ct)
                     ?? throw new NotFoundException("Task", id);

        EnsureCanModify(entity);

        if (!entity.IsOpen)
        {
            throw new BusinessRuleException(
                $"Task {entity.Reference} is {entity.Status} and can no longer be edited.",
                "task_is_closed");
        }

        if (dto.AssigneeId != entity.AssigneeId)
        {
            await EnsureAssigneeExistsAsync(dto.AssigneeId, ct);
        }

        dto.ApplyTo(entity);

        await _tasks.SaveChangesAsync(ct);

        _logger.LogInformation("Updated task {TaskId}", id);

        return await GetByIdAsync(id, ct);
    }

    /// <inheritdoc />
    public async Task<TaskDto> UpdateStatusAsync(int id, PatchTaskStatusDto dto, CancellationToken ct = default)
    {
        var entity = await _tasks.GetByIdAsync(id, asNoTracking: false, ct)
                     ?? throw new NotFoundException("Task", id);

        EnsureCanModify(entity);

        // Nobody works on a task that belongs to no one. Checked before the transition so
        // the caller gets the useful message rather than a generic "illegal move".
        if (dto.Status == TaskItemStatus.InProgress && entity.AssigneeId is null)
        {
            throw new BusinessRuleException(
                $"Task {entity.Reference} must be assigned to someone before it can be started.",
                "assignee_required_to_start");
        }

        if (!entity.TryTransitionTo(dto.Status, _time.GetUtcNow().UtcDateTime))
        {
            var allowed = TaskStatusTransitions.NextStatesFrom(entity.Status);

            var detail = allowed.Count == 0
                ? $"Task {entity.Reference} is {entity.Status}, which is a final state."
                : $"Cannot move task {entity.Reference} from {entity.Status} to {dto.Status}. " +
                  $"Allowed: {string.Join(", ", allowed)}.";

            throw new ConflictException(detail, "illegal_status_transition");
        }

        await _tasks.SaveChangesAsync(ct);

        _logger.LogInformation("Task {TaskId} moved to {Status}", id, dto.Status);

        return await GetByIdAsync(id, ct);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var entity = await _tasks.GetByIdAsync(id, asNoTracking: false, ct)
                     ?? throw new NotFoundException("Task", id);

        // Remove() looks like a hard delete; the auditing interceptor turns it into
        // IsDeleted = true, and the global query filter hides the row from then on.
        _tasks.Remove(entity);
        await _tasks.SaveChangesAsync(ct);

        _logger.LogWarning("Soft-deleted task {TaskId} ({Reference})", id, entity.Reference);
    }

    /// <summary>
    /// Rejects an assignee id that does not correspond to a real user.
    /// </summary>
    /// <remarks>
    /// The foreign key would catch this too, but as a 500-shaped database error rather
    /// than a 404 naming the field the caller got wrong.
    /// </remarks>
    private async Task EnsureAssigneeExistsAsync(int? assigneeId, CancellationToken ct)
    {
        if (assigneeId is not { } id)
        {
            return;
        }

        if (!await _users.ExistsAsync(u => u.Id == id, ct))
        {
            throw new NotFoundException("User", id);
        }
    }

    /// <summary>
    /// Enforces the ownership rule: a Member may only touch tasks assigned to them.
    /// </summary>
    /// <remarks>
    /// This is the check that stops the classic IDOR bug - authenticating as one user and
    /// then editing somebody else's record by changing the id in the URL. Role attributes
    /// on the controller cannot express it, because whether access is allowed depends on
    /// the row, not just on who is asking.
    /// </remarks>
    private void EnsureCanModify(TaskItem task)
    {
        if (_currentUser.Role is UserRole.Lead or UserRole.Admin)
        {
            return;
        }

        if (task.AssigneeId != _currentUser.UserId)
        {
            throw new ForbiddenException(
                $"Task {task.Reference} is not assigned to you. Ask a lead to reassign it.");
        }
    }
}
