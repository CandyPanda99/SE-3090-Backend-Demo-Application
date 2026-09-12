using BackendApplication.Domain.Entities;
using BackendApplication.Services.Abstractions;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace BackendApplication.Data.Interceptors;

/// <summary>
/// Fills in <c>CreatedAtUtc</c>/<c>UpdatedAtUtc</c>/<c>CreatedBy</c>/<c>UpdatedBy</c> and
/// converts hard DELETEs into soft deletes - automatically, for every entity.
/// </summary>
/// <remarks>
/// The alternative is four assignments at the top of every service method that saves
/// anything, which works until the day somebody adds a fifth method. Doing it here means
/// the audit trail cannot be forgotten.
///
/// Must be registered as a singleton: a pooled DbContext shares one DbContextOptions
/// instance, so EF Core resolves interceptors from the root service provider.
/// </remarks>
public sealed class AuditingSaveChangesInterceptor : SaveChangesInterceptor
{
    private readonly ICurrentUserService _currentUser;
    private readonly TimeProvider _timeProvider;

    /// <param name="currentUser">Reads the authenticated user's email from the JWT claims.</param>
    /// <param name="timeProvider">Injected clock, so audit stamps are testable.</param>
    public AuditingSaveChangesInterceptor(ICurrentUserService currentUser, TimeProvider timeProvider)
    {
        _currentUser = currentUser;
        _timeProvider = timeProvider;
    }

    /// <summary>Synchronous SaveChanges path.</summary>
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        ApplyAuditInformation(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    /// <summary>Asynchronous SaveChangesAsync path.</summary>
    /// <remarks>Both paths exist because EF Core calls only the one that matches the caller.</remarks>
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ApplyAuditInformation(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void ApplyAuditInformation(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var user = _currentUser.Email ?? "system";

        foreach (EntityEntry<AuditableEntity> entry in context.ChangeTracker.Entries<AuditableEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAtUtc = nowUtc;
                    entry.Entity.CreatedBy = user;
                    break;

                case EntityState.Modified:
                    entry.Entity.UpdatedAtUtc = nowUtc;
                    entry.Entity.UpdatedBy = user;

                    // Without these two lines an update would rewrite the creation stamps
                    // with the current values, quietly erasing who actually created the row.
                    entry.Property(e => e.CreatedAtUtc).IsModified = false;
                    entry.Property(e => e.CreatedBy).IsModified = false;
                    break;

                case EntityState.Deleted:
                    // Turn the DELETE into an UPDATE. Everything downstream - the global query
                    // filter, foreign keys, history - behaves as if the row were gone.
                    entry.State = EntityState.Modified;
                    entry.Entity.IsDeleted = true;
                    entry.Entity.UpdatedAtUtc = nowUtc;
                    entry.Entity.UpdatedBy = user;
                    break;

                case EntityState.Detached:
                case EntityState.Unchanged:
                default:
                    break;
            }
        }
    }
}
