using AlaraERP.Application.Common;
using AlaraERP.Domain.Inventory;
using Microsoft.EntityFrameworkCore;

namespace AlaraERP.Application.Inventory;

/// <summary>
/// Database-backed global inventory switches. Disabled features keep their schema and historical data,
/// while commands and queries stop applying the dormant workflow.
/// </summary>
public sealed class InventoryFeatureService(IAlaraDbContext db, ICurrentUser currentUser)
{
    private bool? _expiryDatesEnabled;

    public async Task<bool> ExpiryDatesEnabledAsync(CancellationToken ct)
    {
        if (_expiryDatesEnabled is { } cached) return cached;
        _expiryDatesEnabled = await db.InventoryFeatureSettings.AsNoTracking()
            .Where(s => s.Id == InventoryFeatureSettings.SingletonId)
            .Select(s => (bool?)s.ExpiryDatesEnabled)
            .SingleOrDefaultAsync(ct) ?? false;
        return _expiryDatesEnabled.Value;
    }

    public async Task<InventoryFeatureSettingsDto> GetAsync(CancellationToken ct)
    {
        var row = await db.InventoryFeatureSettings.AsNoTracking()
            .SingleOrDefaultAsync(s => s.Id == InventoryFeatureSettings.SingletonId, ct);
        _expiryDatesEnabled = row?.ExpiryDatesEnabled ?? false;
        return new InventoryFeatureSettingsDto(_expiryDatesEnabled.Value, row?.UpdatedAt);
    }

    public async Task<InventoryFeatureSettingsDto> SetExpiryDatesAsync(bool enabled, CancellationToken ct)
    {
        var row = await db.InventoryFeatureSettings
            .SingleOrDefaultAsync(s => s.Id == InventoryFeatureSettings.SingletonId, ct);
        if (row is null)
        {
            row = new InventoryFeatureSettings(enabled, currentUser.UserId);
            db.InventoryFeatureSettings.Add(row);
        }
        else
        {
            row.SetExpiryDates(enabled, currentUser.UserId);
        }

        await db.SaveChangesAsync(ct);
        _expiryDatesEnabled = enabled;
        return new InventoryFeatureSettingsDto(enabled, row.UpdatedAt);
    }
}
