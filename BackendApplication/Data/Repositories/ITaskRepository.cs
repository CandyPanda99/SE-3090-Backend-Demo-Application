using BackendApplication.Domain.Entities;

namespace BackendApplication.Data.Repositories;

/// <summary>
/// Task queries that deserve a name of their own.
/// </summary>
/// <remarks>
/// Anything a single service method would otherwise express as a raw LINQ chain, but
/// which more than one caller needs, ends up here.
/// </remarks>
public interface ITaskRepository : IRepository<TaskItem>
{
    /// <summary>Loads a task together with its assignee in a single round trip.</summary>
    Task<TaskItem?> GetWithAssigneeAsync(int id, bool asNoTracking = true, CancellationToken cancellationToken = default);

    /// <summary>Checks reference uniqueness, optionally ignoring one task (used on update).</summary>
    /// <param name="reference">The reference to look for.</param>
    /// <param name="excludeTaskId">The task id to exclude from the check.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<bool> ReferenceExistsAsync(string reference, int? excludeTaskId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reserves the next reference in the <c>TSK-0001</c> sequence.
    /// </summary>
    /// <remarks>
    /// Reads the highest existing number INCLUDING soft-deleted rows, so a reference is
    /// never reused after a task is deleted - old links and chat messages keep pointing at
    /// the thing they meant.
    /// </remarks>
    Task<string> NextReferenceAsync(CancellationToken cancellationToken = default);
}
