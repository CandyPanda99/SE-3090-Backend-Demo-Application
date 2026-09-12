using Asp.Versioning;
using BackendApplication.Configuration;
using BackendApplication.Domain.Enums;
using BackendApplication.DTOs.Common;
using BackendApplication.DTOs.Tasks;
using BackendApplication.Services.Abstractions;
using Microsoft.AspNetCore.Authorization;

namespace BackendApplication.Controllers.V1;

/// <summary>
/// Manage tasks on the board. This is the API.
/// </summary>
/// <remarks>
/// Every action is thin on purpose: bind, delegate, shape the response. There is no
/// try/catch anywhere below, because the global exception middleware turns a thrown
/// <c>NotFoundException</c> into a 404 and a <c>ConflictException</c> into a 409.
/// </remarks>
[ApiVersion(ApiVersions.V1)]
[Route("api/v{version:apiVersion}/tasks")]
[Authorize]
public sealed class TasksController : ApiControllerBase
{
    private readonly ITaskService _tasks;
    private readonly ILogger<TasksController> _logger;

    public TasksController(ITaskService tasks, ILogger<TasksController> logger)
    {
        _tasks = tasks;
        _logger = logger;
    }

    /// <summary>
    /// Returns a paged, filtered and sorted list of tasks.
    /// </summary>
    /// <remarks>
    /// Sample request:
    ///
    ///     GET /api/v1/tasks?search=pagination&amp;status=InProgress&amp;priority=High
    ///         &amp;overdueOnly=true&amp;sortBy=dueat&amp;sortDirection=asc&amp;pageNumber=1&amp;pageSize=10
    ///
    /// The response body carries paging metadata AND HATEOAS links; the same metadata is
    /// repeated in the <c>X-Pagination</c> response header.
    ///
    /// Every authenticated user can see the whole board - visibility is not restricted by
    /// role. What a Member cannot do is *change* a task that is not theirs.
    /// </remarks>
    /// <param name="query">Filtering, sorting and paging options (from the query string).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">A page of matching tasks.</response>
    /// <response code="400">A query parameter was malformed (e.g. status=purple).</response>
    [HttpGet(Name = "GetTasks")]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<TaskDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PagedResult<TaskDto>>>> GetAll(
        [FromQuery] TaskQueryParameters query,
        CancellationToken ct)
    {
        var page = await _tasks.GetPagedAsync(query, ct);

        AddPaginationLinks(page);

        return Ok(Envelope(page));
    }

    /// <summary>
    /// Returns a single task by id.
    /// </summary>
    /// <param name="id">The task id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">The task.</response>
    /// <response code="404">No task with that id exists.</response>
    [HttpGet("{id:int}", Name = "GetTaskById")]
    [ProducesResponseType(typeof(ApiResponse<TaskDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<TaskDto>>> GetById(int id, CancellationToken ct)
    {
        var task = await _tasks.GetByIdAsync(id, ct);

        return Ok(Envelope(task));
    }

    /// <summary>
    /// Creates a new task.
    /// </summary>
    /// <remarks>
    /// Requires the Lead or Admin role - members work the backlog, they do not define it.
    ///
    /// The <c>reference</c> is allocated by the server from the TSK-0001 sequence and
    /// cannot be supplied. New tasks always start in <c>Todo</c>.
    ///
    /// Sample body:
    ///
    ///     {
    ///       "title": "Add pagination to the reports screen",
    ///       "description": "It loads every row today.",
    ///       "priority": "High",
    ///       "dueAtUtc": "2026-12-31T17:00:00Z",
    ///       "estimatedHours": 8,
    ///       "assigneeId": 3
    ///     }
    /// </remarks>
    /// <response code="201">Created. The Location header points at the new resource.</response>
    /// <response code="401">No bearer token was supplied.</response>
    /// <response code="403">The caller is authenticated but is not a Lead or Admin.</response>
    /// <response code="404">The nominated assignee does not exist.</response>
    /// <response code="422">Validation failed; see the 'errors' object.</response>
    [HttpPost(Name = "CreateTask")]
    [Authorize(Roles = Roles.LeadOrAdmin)]
    [ProducesResponseType(typeof(ApiResponse<TaskDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ApiResponse<TaskDto>>> Create(
        CreateTaskDto dto,
        CancellationToken ct)
    {
        var created = await _tasks.CreateAsync(dto, ct);

        _logger.LogInformation("Task {Reference} created by {User}", created.Reference, User.Identity?.Name);

        // CreatedAtRoute rather than Created: the framework builds the Location URL from
        // the named route, so it cannot drift if the route template changes.
        return CreatedAtRoute(
            routeName: "GetTaskById",
            routeValues: new { id = created.Id, version = RequestedApiVersion },
            value: Envelope(created, "Task created successfully."));
    }

    /// <summary>
    /// Replaces the editable fields of an existing task.
    /// </summary>
    /// <remarks>
    /// A Member may only edit a task assigned to them; a Lead or Admin may edit any.
    /// Status is NOT editable here - use the PATCH endpoint, which runs the state machine.
    /// A closed task (Done or Cancelled) cannot be edited at all.
    /// </remarks>
    /// <param name="id">The task to replace.</param>
    /// <param name="dto">The complete new state of the task's editable fields.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">The updated task.</response>
    /// <response code="400">The task is closed and can no longer be edited.</response>
    /// <response code="403">The task is not assigned to you.</response>
    /// <response code="404">No task with that id exists.</response>
    /// <response code="409">Another user modified the task first (concurrency conflict).</response>
    [HttpPut("{id:int}", Name = "UpdateTask")]
    [ProducesResponseType(typeof(ApiResponse<TaskDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<TaskDto>>> Update(
        int id,
        UpdateTaskDto dto,
        CancellationToken ct)
    {
        var updated = await _tasks.UpdateAsync(id, dto, ct);

        return Ok(Envelope(updated, "Task updated successfully."));
    }

    /// <summary>
    /// Moves a task through the status state machine.
    /// </summary>
    /// <remarks>
    /// A narrow, typed PATCH rather than JSON Patch: there is exactly one thing this
    /// endpoint can change, and the request body says so.
    ///
    /// Legal moves:
    ///
    ///     Todo        -> InProgress, Cancelled
    ///     InProgress  -> Blocked, Done, Cancelled
    ///     Blocked     -> InProgress, Cancelled
    ///     Done        -> (terminal)
    ///     Cancelled   -> (terminal)
    ///
    /// An illegal move returns 409 naming the states that ARE reachable. Moving to
    /// <c>InProgress</c> additionally requires the task to have an assignee, and is
    /// rejected with 400 and the code <c>assignee_required_to_start</c> if it does not.
    ///
    /// Each response includes <c>allowedNextStatuses</c>, so a client never has to
    /// hard-code the table above.
    /// </remarks>
    /// <response code="200">The updated task.</response>
    /// <response code="400">The task has no assignee and cannot be started.</response>
    /// <response code="403">The task is not assigned to you.</response>
    /// <response code="409">The transition is not legal from the current status.</response>
    [HttpPatch("{id:int}/status", Name = "UpdateTaskStatus")]
    [ProducesResponseType(typeof(ApiResponse<TaskDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<TaskDto>>> UpdateStatus(
        int id,
        PatchTaskStatusDto dto,
        CancellationToken ct)
    {
        var updated = await _tasks.UpdateStatusAsync(id, dto, ct);

        return Ok(Envelope(updated, $"Status changed to {updated.Status}."));
    }

    /// <summary>
    /// Soft-deletes a task.
    /// </summary>
    /// <remarks>
    /// Admin only. The row is retained (<c>IsDeleted = true</c>) so the reference is never
    /// reused and any link to it still resolves to something; a global query filter hides
    /// it from every other query.
    /// </remarks>
    /// <response code="204">Deleted. No content is returned.</response>
    /// <response code="403">Only an Admin may delete tasks.</response>
    /// <response code="404">No task with that id exists.</response>
    [HttpDelete("{id:int}", Name = "DeleteTask")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await _tasks.DeleteAsync(id, ct);

        return NoContent();
    }
}
