using System.Linq.Expressions;
using BackendApplication.Domain.Entities;

namespace BackendApplication.Data.Repositories;

/// <summary>
/// Generic data-access contract shared by every entity.
/// </summary>
/// <typeparam name="TEntity">Any entity with an <c>Id</c>.</typeparam>
public interface IRepository<TEntity> where TEntity : BaseEntity
{
    /// <summary>Finds an entity by primary key, or returns null.</summary>
    /// <param name="id">The primary key to look up.</param>
    /// <param name="asNoTracking">
    /// <c>true</c> for read-only queries; <c>false</c> when you intend to modify and save
    /// the entity.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<TEntity?> GetByIdAsync(int id, bool asNoTracking = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a composable query. The caller adds Where/OrderBy/Select and EF Core
    /// translates the whole chain into ONE SQL statement.
    /// </summary>
    /// <remarks>
    /// Exposing IQueryable is a deliberate trade. It keeps filtering in the database
    /// instead of pulling rows into memory first, at the cost of letting callers build
    /// queries the repository never anticipated.
    /// </remarks>
    IQueryable<TEntity> Query(bool asNoTracking = true);

    /// <summary>True if any row matches - translates to SQL <c>EXISTS</c>, not a full load.</summary>
    Task<bool> ExistsAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default);

    /// <summary>Stages an INSERT. Nothing hits the database until SaveChangesAsync.</summary>
    Task AddAsync(TEntity entity, CancellationToken cancellationToken = default);

    /// <summary>Stages an UPDATE for an entity that is not currently tracked.</summary>
    void Update(TEntity entity);

    /// <summary>
    /// Stages a DELETE, which the auditing interceptor turns into a soft delete for
    /// auditable entities.
    /// </summary>
    void Remove(TEntity entity);

    /// <summary>
    /// Commits every pending change and returns the number of rows affected.
    /// </summary>
    /// <remarks>
    /// Every repository in a request shares one scoped <see cref="TasksDbContext"/>, so
    /// this saves the whole change tracker - not just this entity type. Calling it on any
    /// repository therefore commits the request's work atomically, which is the job a
    /// separate <c>IUnitOfWork</c> would otherwise do.
    /// </remarks>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
