using AlaraERP.Application.Common;
using AlaraERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AlaraERP.Infrastructure.Identity;

internal sealed class UserDirectory(AlaraDbContext db) : IUserDirectory
{
    public async Task<IReadOnlyDictionary<Guid, string>> GetDisplayNamesAsync(IEnumerable<Guid?> userIds, CancellationToken ct)
    {
        var ids = userIds.OfType<Guid>().Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<Guid, string>();
        return await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => (u.FirstName + " " + u.LastName).Trim(), ct);
    }
}
