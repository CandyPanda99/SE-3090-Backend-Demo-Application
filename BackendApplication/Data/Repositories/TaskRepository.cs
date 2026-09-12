using System.Globalization;
using BackendApplication.Domain.Entities;

namespace BackendApplication.Data.Repositories;

/// <summary>EF Core implementation of <see cref="ITaskRepository"/>.</summary>
public class TaskRepository : Repository<TaskItem>, ITaskRepository
{
    /// <summary>The prefix every generated reference carries.</summary>
    private const string ReferencePrefix = "TSK-";

    public TaskRepository(TasksDbContext db) : base(db)
    {
    }

    /// <inheritdoc />
    public async Task<TaskItem?> GetWithAssigneeAsync(
        int id,
        bool asNoTracking = true,
        CancellationToken cancellationToken = default)
    {
        var query = Set.Include(t => t.Assignee).AsQueryable();

        query = asNoTracking ? query.AsNoTracking() : query.AsTracking();

        return await query.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> ReferenceExistsAsync(
        string reference,
        int? excludeTaskId = null,
        CancellationToken cancellationToken = default)
    {
        // IgnoreQueryFilters so a soft-deleted task still blocks reuse of its reference -
        // the unique index covers deleted rows too, so without this the check would pass
        // and the INSERT would then fail with a confusing database error.
        var query = Set.IgnoreQueryFilters().Where(t => t.Reference == reference);

        if (excludeTaskId is { } id)
        {
            query = query.Where(t => t.Id != id);
        }

        return query.AnyAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<string> NextReferenceAsync(CancellationToken cancellationToken = default)
    {
        // Pulls the numeric tail of the highest existing reference. Done in SQL via
        // ORDER BY on the string, which works because the numbers are zero-padded to a
        // fixed width - "TSK-0009" sorts before "TSK-0010" only because of that padding.
        var latest = await Set.IgnoreQueryFilters()
            .Where(t => t.Reference.StartsWith(ReferencePrefix))
            .OrderByDescending(t => t.Reference)
            .Select(t => t.Reference)
            .FirstOrDefaultAsync(cancellationToken);

        var next = 1;

        if (latest is not null &&
            int.TryParse(latest.AsSpan(ReferencePrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var current))
        {
            next = current + 1;
        }

        return $"{ReferencePrefix}{next:D4}";
    }
}
