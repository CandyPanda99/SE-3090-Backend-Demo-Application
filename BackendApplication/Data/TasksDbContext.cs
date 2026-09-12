using System.Linq.Expressions;
using BackendApplication.Domain.Entities;

namespace BackendApplication.Data;

/// <summary>
/// The Entity Framework Core <c>DbContext</c> - our gateway to PostgreSQL.
/// </summary>
public class TasksDbContext : DbContext
{
    public TasksDbContext(DbContextOptions<TasksDbContext> options) : base(options)
    {
    }

    public DbSet<TaskItem> Tasks => Set<TaskItem>();
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    /// <summary>
    /// Builds the model: which classes map to which tables, keys, indexes, filters.
    /// </summary>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Picks up every IEntityTypeConfiguration in this assembly, so adding a new
        // configuration class needs no line here.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TasksDbContext).Assembly);

        // A global query filter hiding soft-deleted rows, applied to every AuditableEntity.
        //
        // Written with the Expression API rather than a generic helper because the filter has
        // to be built per entity type at runtime, and HasQueryFilter needs a LambdaExpression
        // typed to that entity. The alternative is one hand-written line per entity, which
        // is exactly the kind of thing somebody forgets when adding the fifth table.
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(AuditableEntity).IsAssignableFrom(entityType.ClrType))
            {
                continue;
            }

            var parameter = Expression.Parameter(entityType.ClrType, "e");
            var property = Expression.Property(parameter, nameof(AuditableEntity.IsDeleted));
            var filter = Expression.Lambda(Expression.Not(property), parameter);

            modelBuilder.Entity(entityType.ClrType).HasQueryFilter(filter);
        }

        // Any string column that nobody gave a length becomes varchar(256) rather than
        // PostgreSQL's unbounded text. A forgotten [MaxLength] should not silently allow
        // a megabyte of input into a column meant for a name.
        foreach (var property in modelBuilder.Model.GetEntityTypes()
                     .SelectMany(t => t.GetProperties())
                     .Where(p => p.ClrType == typeof(string) && p.GetMaxLength() is null))
        {
            property.SetMaxLength(256);
        }
    }

    /// <summary>
    /// Extra model-building conventions applied before <see cref="OnModelCreating"/>.
    /// </summary>
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<decimal>().HavePrecision(18, 2);

        // Every DateTime becomes timestamptz. Set once here so no entity can accidentally
        // store a naive local timestamp.
        configurationBuilder.Properties<DateTime>().HaveColumnType("timestamp with time zone");
    }
}
