using System.Linq.Expressions;
using BackendApplication.Domain.Entities;

namespace BackendApplication.Data.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IRepository{TEntity}"/>.
/// </summary>
/// <remarks>
/// The Add/Update/Remove methods only stage work on the change tracker; nothing reaches
/// the database until <see cref="SaveChangesAsync"/> is called.
/// </remarks>
/// <typeparam name="TEntity">The entity type this repository serves.</typeparam>
public class Repository<TEntity> : IRepository<TEntity> where TEntity : BaseEntity
{
    /// <summary>Protected so derived repositories (e.g. TaskRepository) can reuse it.</summary>
    protected readonly TasksDbContext Db;

    /// <summary>The table this repository wraps.</summary>
    protected readonly DbSet<TEntity> Set;

    public Repository(TasksDbContext db)
    {
        Db = db;
        Set = db.Set<TEntity>();
    }

    /// <inheritdoc />
    public virtual async Task<TEntity?> GetByIdAsync(
        int id,
        bool asNoTracking = false,
        CancellationToken cancellationToken = default)
    {
        var query = asNoTracking ? Set.AsNoTracking() : Set.AsTracking();

        return await query.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
    }

    /// <inheritdoc />
    public virtual IQueryable<TEntity> Query(bool asNoTracking = true)
        => asNoTracking ? Set.AsNoTracking() : Set.AsTracking();

    /// <inheritdoc />
    public virtual Task<bool> ExistsAsync(
        Expression<Func<TEntity, bool>> predicate,
        CancellationToken cancellationToken = default)
        => Set.AnyAsync(predicate, cancellationToken);

    /// <inheritdoc />
    public virtual async Task AddAsync(TEntity entity, CancellationToken cancellationToken = default)
        => await Set.AddAsync(entity, cancellationToken);

    /// <inheritdoc />
    public virtual void Update(TEntity entity) => Set.Update(entity);

    /// <inheritdoc />
    public virtual void Remove(TEntity entity) => Set.Remove(entity);

    /// <inheritdoc />
    public virtual Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => Db.SaveChangesAsync(cancellationToken);
}
