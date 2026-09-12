using BackendApplication.DTOs.Common;
using BackendApplication.DTOs.Tasks;

namespace BackendApplication.Services.Abstractions;

/// <summary>
/// Business operations on tasks - the one resource this API exposes.
/// </summary>
public interface ITaskService
{
    /// <summary>Paged, filtered and sorted tasks.</summary>
    Task<PagedResult<TaskDto>> GetPagedAsync(TaskQueryParameters query, CancellationToken ct = default);

    /// <summary>One task by id.</summary>
    /// <exception cref="Exceptions.NotFoundException">No task with that id exists.</exception>
    Task<TaskDto> GetByIdAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Creates a task, allocating the next reference in the TSK-0001 sequence.
    /// </summary>
    Task<TaskDto> CreateAsync(CreateTaskDto dto, CancellationToken ct = default);

    /// <summary>Replaces the editable fields of an existing task.</summary>
    /// <remarks>Status is not editable here - see <see cref="UpdateStatusAsync"/>.</remarks>
    Task<TaskDto> UpdateAsync(int id, UpdateTaskDto dto, CancellationToken ct = default);

    /// <summary>
    /// Moves a task through the status state machine.
    /// </summary>
    /// <exception cref="Exceptions.ConflictException">The transition is not legal.</exception>
    Task<TaskDto> UpdateStatusAsync(int id, PatchTaskStatusDto dto, CancellationToken ct = default);

    /// <summary>Soft-deletes a task.</summary>
    Task DeleteAsync(int id, CancellationToken ct = default);
}
