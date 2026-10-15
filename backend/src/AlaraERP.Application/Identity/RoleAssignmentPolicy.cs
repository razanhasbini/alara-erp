using AlaraERP.Application.Common;
using AlaraERP.Application.Common.Security;

namespace AlaraERP.Application.Identity;

/// <summary>
/// Pure rules that prevent privilege escalation when one employee manages another.
/// Kept free of Identity/EF so it can be unit-tested exhaustively.
/// </summary>
public static class RoleAssignmentPolicy
{
    /// <summary>An actor may only grant roles whose permissions they already hold. Only a Super Admin can grant Super Admin.</summary>
    public static void EnsureCanGrant(
        IReadOnlySet<string> actorPermissions,
        bool actorIsSuperAdmin,
        IEnumerable<SystemRoleDefinition> rolesToGrant)
    {
        foreach (var role in rolesToGrant)
        {
            if (role.Name == SystemRoles.SuperAdmin && !actorIsSuperAdmin)
                throw new ForbiddenOperationException("users.cannot_grant_super_admin",
                    "Only a Super Admin can grant the Super Admin role.");

            var missing = role.Permissions.Except(actorPermissions).ToList();
            if (missing.Count > 0)
                throw new ForbiddenOperationException("users.role_exceeds_own_permissions",
                    $"You cannot grant the '{role.Name}' role because it includes permissions you do not have.");
        }
    }

    /// <summary>An actor may only manage users who are not more privileged than themselves.</summary>
    public static void EnsureCanManage(
        IReadOnlySet<string> actorPermissions,
        bool actorIsSuperAdmin,
        bool targetIsSuperAdmin,
        IReadOnlySet<string> targetPermissions)
    {
        if (actorIsSuperAdmin) return;

        if (targetIsSuperAdmin || !targetPermissions.IsSubsetOf(actorPermissions))
            throw new ForbiddenOperationException("users.target_more_privileged",
                "You cannot modify an employee who has permissions you do not have.");
    }

    public static void EnsureNotSelf(Guid actorId, Guid targetId, string operation)
    {
        if (actorId == targetId)
            throw new ForbiddenOperationException("users.cannot_modify_self",
                $"You cannot {operation} your own account.");
    }

    /// <summary>The system must always keep at least one active Super Admin.</summary>
    public static void EnsureSuperAdminRemains(int otherActiveSuperAdmins, bool removingSuperAdmin)
    {
        if (removingSuperAdmin && otherActiveSuperAdmins == 0)
            throw new ConflictException("users.last_super_admin",
                "This is the last active Super Admin. Create another Super Admin first.");
    }
}
