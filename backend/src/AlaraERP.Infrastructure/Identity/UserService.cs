using AlaraERP.Application.Common;
using AlaraERP.Application.Common.Security;
using AlaraERP.Application.Identity;
using AlaraERP.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AlaraERP.Infrastructure.Identity;

internal sealed class UserService(
    UserManager<AppUser> users,
    AlaraDbContext db,
    ICurrentUser actor,
    IActivityLog activity,
    TimeProvider clock) : IUserService
{
    public async Task<PagedResult<UserSummaryDto>> ListAsync(UserListQuery query, CancellationToken ct)
    {
        var page = new PageRequest { Page = query.Page, PageSize = query.PageSize };
        var q = db.Users.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = $"%{query.Search.Trim()}%";
            q = q.Where(u => EF.Functions.ILike(u.Email!, term)
                          || EF.Functions.ILike(u.FirstName, term)
                          || EF.Functions.ILike(u.LastName, term));
        }
        if (query.IsActive is { } active)
            q = q.Where(u => u.IsActive == active);
        if (!string.IsNullOrWhiteSpace(query.Role))
            q = q.Where(u => db.UserRoles.Any(ur => ur.UserId == u.Id
                                                 && db.Roles.Any(r => r.Id == ur.RoleId && r.Name == query.Role)));

        var total = await q.CountAsync(ct);
        var items = await q.OrderBy(u => u.FirstName).ThenBy(u => u.LastName).ThenBy(u => u.Id)
            .Skip(page.Skip).Take(page.SafePageSize).ToListAsync(ct);

        var roles = await RolesByUserAsync(items.Select(u => u.Id).ToList(), ct);
        return new PagedResult<UserSummaryDto>(
            items.Select(u => ToDto(u, roles.GetValueOrDefault(u.Id, []))).ToList(),
            page.SafePage, page.SafePageSize, total);
    }

    public async Task<UserSummaryDto> GetAsync(Guid id, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, ct)
                   ?? throw new NotFoundException("User", id);
        return ToDto(user, await RoleNamesAsync(id, ct));
    }

    public async Task<UserSummaryDto> CreateAsync(CreateUserRequest request, CancellationToken ct)
    {
        var roles = Resolve(request.Roles);
        RoleAssignmentPolicy.EnsureCanGrant(actor.Permissions, ActorIsSuperAdmin, roles);

        var email = request.Email.Trim();
        var user = new AppUser
        {
            Email = email,
            UserName = email,
            EmailConfirmed = true,
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            IsActive = true,
            MustChangePassword = true,
        };

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        (await users.CreateAsync(user, request.TemporaryPassword)).ThrowIfFailed("temporaryPassword");
        (await users.AddToRolesAsync(user, roles.Select(r => r.Name))).ThrowIfFailed();
        Log($"added employee {Name(user)} ({email}) as {RoleList(roles.Select(r => r.Name))}");
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return await GetAsync(user.Id, ct);
    }

    public async Task<UserSummaryDto> UpdateAsync(Guid id, UpdateUserRequest request, CancellationToken ct)
    {
        var user = await FindAsync(id);
        await EnsureCanManageAsync(user, ct);

        var before = Name(user);
        user.FirstName = request.FirstName.Trim();
        user.LastName = request.LastName.Trim();
        var email = request.Email.Trim();
        if (!string.Equals(user.Email, email, StringComparison.OrdinalIgnoreCase))
        {
            user.Email = email;
            user.UserName = email;
            user.EmailConfirmed = true;
        }
        Log(before == Name(user) ? $"updated the details of employee {before}" : $"renamed employee {before} to {Name(user)}");
        (await users.UpdateAsync(user)).ThrowIfFailed();
        return await GetAsync(id, ct);
    }

    public async Task<UserSummaryDto> SetActiveAsync(Guid id, bool isActive, CancellationToken ct)
    {
        RoleAssignmentPolicy.EnsureNotSelf(actor.RequiredUserId, id, isActive ? "activate" : "deactivate");
        var user = await FindAsync(id);
        var targetRoles = await RoleNamesAsync(id, ct);
        await EnsureCanManageAsync(user, ct, targetRoles);

        if (user.IsActive == isActive)
            return ToDto(user, targetRoles);

        if (!isActive && targetRoles.Contains(SystemRoles.SuperAdmin))
            RoleAssignmentPolicy.EnsureSuperAdminRemains(await OtherActiveSuperAdminsAsync(id, ct), removingSuperAdmin: true);

        user.IsActive = isActive;
        Log(isActive ? $"let {Name(user)} sign in again" : $"blocked {Name(user)} from signing in");
        (await users.UpdateAsync(user)).ThrowIfFailed();
        if (!isActive)
            await users.UpdateSecurityStampAsync(user); // ends existing sessions at the next validation

        return ToDto(user, targetRoles);
    }

    public async Task<UserSummaryDto> AssignRolesAsync(Guid id, AssignRolesRequest request, CancellationToken ct)
    {
        RoleAssignmentPolicy.EnsureNotSelf(actor.RequiredUserId, id, "change the roles of");
        var user = await FindAsync(id);
        var current = await RoleNamesAsync(id, ct);
        await EnsureCanManageAsync(user, ct, current);

        var wanted = Resolve(request.Roles);
        RoleAssignmentPolicy.EnsureCanGrant(actor.Permissions, ActorIsSuperAdmin, wanted);

        var wantedNames = wanted.Select(r => r.Name).ToList();
        var removingSuperAdmin = current.Contains(SystemRoles.SuperAdmin) && !wantedNames.Contains(SystemRoles.SuperAdmin);
        if (removingSuperAdmin && user.IsActive)
            RoleAssignmentPolicy.EnsureSuperAdminRemains(await OtherActiveSuperAdminsAsync(id, ct), removingSuperAdmin: true);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var toRemove = current.Except(wantedNames).ToList();
        var toAdd = wantedNames.Except(current).ToList();
        if (toRemove.Count > 0) (await users.RemoveFromRolesAsync(user, toRemove)).ThrowIfFailed();
        if (toAdd.Count > 0) (await users.AddToRolesAsync(user, toAdd)).ThrowIfFailed();
        if (toRemove.Count > 0 || toAdd.Count > 0)
        {
            Log($"changed what {Name(user)} can do: now {RoleList(wantedNames)}");
            await db.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct);

        return await GetAsync(id, ct);
    }

    public async Task ResetPasswordAsync(Guid id, ResetPasswordRequest request, CancellationToken ct)
    {
        RoleAssignmentPolicy.EnsureNotSelf(actor.RequiredUserId, id, "reset the password of");
        var user = await FindAsync(id);
        await EnsureCanManageAsync(user, ct);

        var token = await users.GeneratePasswordResetTokenAsync(user);
        (await users.ResetPasswordAsync(user, token, request.TemporaryPassword)).ThrowIfFailed("temporaryPassword");

        user.MustChangePassword = true;
        user.LockoutEnd = null;
        user.AccessFailedCount = 0;
        Log($"gave {Name(user)} a new temporary password");
        (await users.UpdateAsync(user)).ThrowIfFailed();
    }

    // ---- helpers -------------------------------------------------------------------------

    private bool ActorIsSuperAdmin => actor.Roles.Contains(SystemRoles.SuperAdmin);

    /// <summary>Staged with the user change; UserManager's save (same context) writes both.</summary>
    private void Log(string summary) => activity.Record(Domain.Activity.ActivityArea.Employees, summary, "/employees");

    private static string Name(AppUser u) => $"{u.FirstName} {u.LastName}".Trim();

    private static string RoleList(IEnumerable<string> roles) => roles.Any() ? string.Join(" and ", roles) : "no role";

    private async Task<AppUser> FindAsync(Guid id) =>
        await users.FindByIdAsync(id.ToString()) ?? throw new NotFoundException("User", id);

    private static List<SystemRoleDefinition> Resolve(IEnumerable<string> names) =>
        names.Select(n => SystemRoles.Find(n) ?? throw new ConflictException("users.unknown_role", $"Unknown role '{n}'."))
             .DistinctBy(r => r.Name)
             .ToList();

    private async Task EnsureCanManageAsync(AppUser target, CancellationToken ct, IReadOnlyList<string>? targetRoles = null)
    {
        targetRoles ??= await RoleNamesAsync(target.Id, ct);
        var targetPermissions = targetRoles
            .Select(SystemRoles.Find).OfType<SystemRoleDefinition>()
            .SelectMany(r => r.Permissions)
            .ToHashSet();
        RoleAssignmentPolicy.EnsureCanManage(actor.Permissions, ActorIsSuperAdmin,
            targetRoles.Contains(SystemRoles.SuperAdmin), targetPermissions);
    }

    private Task<int> OtherActiveSuperAdminsAsync(Guid excludeUserId, CancellationToken ct) =>
        (from ur in db.UserRoles
         join r in db.Roles on ur.RoleId equals r.Id
         join u in db.Users on ur.UserId equals u.Id
         where r.Name == SystemRoles.SuperAdmin && u.IsActive && u.Id != excludeUserId
         select u.Id).CountAsync(ct);

    private async Task<List<string>> RoleNamesAsync(Guid userId, CancellationToken ct) =>
        await (from ur in db.UserRoles
               join r in db.Roles on ur.RoleId equals r.Id
               where ur.UserId == userId
               orderby r.Name
               select r.Name!).ToListAsync(ct);

    private async Task<Dictionary<Guid, List<string>>> RolesByUserAsync(List<Guid> userIds, CancellationToken ct)
    {
        var rows = await (from ur in db.UserRoles
                          join r in db.Roles on ur.RoleId equals r.Id
                          where userIds.Contains(ur.UserId)
                          select new { ur.UserId, r.Name }).ToListAsync(ct);
        return rows.GroupBy(x => x.UserId).ToDictionary(g => g.Key, g => g.Select(x => x.Name!).OrderBy(n => n).ToList());
    }

    private UserSummaryDto ToDto(AppUser u, IReadOnlyList<string> roles) => new(
        u.Id, u.Email!, u.FirstName, u.LastName, u.IsActive,
        u.LockoutEnd is { } end && end > clock.GetUtcNow(),
        roles, u.CreatedAt, u.LastLoginAt);
}
