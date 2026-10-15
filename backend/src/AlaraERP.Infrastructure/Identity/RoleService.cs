using AlaraERP.Application.Common.Security;
using AlaraERP.Application.Identity;
using AlaraERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AlaraERP.Infrastructure.Identity;

internal sealed class RoleService(AlaraDbContext db) : IRoleService
{
    public async Task<IReadOnlyList<RoleDto>> ListAsync(CancellationToken ct)
    {
        var roles = await db.Roles.AsNoTracking().ToListAsync(ct);
        var claims = await db.RoleClaims.AsNoTracking()
            .Where(c => c.ClaimType == Permissions.ClaimType)
            .Select(c => new { c.RoleId, c.ClaimValue })
            .ToListAsync(ct);
        var counts = await (from ur in db.UserRoles
                            join u in db.Users on ur.UserId equals u.Id
                            where u.IsActive
                            group ur by ur.RoleId into g
                            select new { RoleId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.RoleId, x => x.Count, ct);

        // Order as defined in SystemRoles (most to least privileged), custom roles last.
        var order = SystemRoles.All.Select((r, i) => (r.Name, i)).ToDictionary(x => x.Name, x => x.i);
        return roles
            .OrderBy(r => order.GetValueOrDefault(r.Name!, int.MaxValue)).ThenBy(r => r.Name)
            .Select(r => new RoleDto(
                r.Name!, r.Description, r.IsSystem,
                claims.Where(c => c.RoleId == r.Id).Select(c => c.ClaimValue!).OrderBy(p => p).ToList(),
                counts.GetValueOrDefault(r.Id)))
            .ToList();
    }
}
