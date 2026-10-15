using AlaraERP.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AlaraERP.Infrastructure.Persistence;

/// <summary>Stamps <see cref="IAuditable"/> entities so no service has to remember to.</summary>
public sealed class AuditInterceptor(TimeProvider clock) : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Stamp(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Stamp(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    private void Stamp(DbContext? context)
    {
        if (context is null) return;
        var now = clock.GetUtcNow();
        foreach (var entry in context.ChangeTracker.Entries<ITracksUpdates>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified)) continue;
            entry.Entity.UpdatedAt = now;
            if (entry.Entity is not IAuditable auditable) continue;

            if (entry.State == EntityState.Added)
            {
                // Keep a creation time the domain set explicitly (e.g. orders), otherwise stamp it.
                if (auditable.CreatedAt == default) auditable.CreatedAt = now;
            }
            else
            {
                entry.Property(nameof(IAuditable.CreatedAt)).IsModified = false;
            }
        }
    }
}
