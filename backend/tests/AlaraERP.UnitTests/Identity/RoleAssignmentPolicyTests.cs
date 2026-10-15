using AlaraERP.Application.Common;
using AlaraERP.Application.Common.Security;
using AlaraERP.Application.Identity;

namespace AlaraERP.UnitTests.Identity;

public sealed class RoleAssignmentPolicyTests
{
    private static SystemRoleDefinition Role(string name) => SystemRoles.Find(name)!;
    private static IReadOnlySet<string> PermissionsOf(string name) => Role(name).Permissions;

    [Fact]
    public void Super_admin_can_grant_any_role()
    {
        RoleAssignmentPolicy.EnsureCanGrant(PermissionsOf(SystemRoles.SuperAdmin), actorIsSuperAdmin: true,
            SystemRoles.All);
    }

    [Fact]
    public void Nobody_else_can_grant_super_admin_even_with_every_permission()
    {
        var ex = Assert.Throws<ForbiddenOperationException>(() => RoleAssignmentPolicy.EnsureCanGrant(
            Permissions.All.ToHashSet(), actorIsSuperAdmin: false, [Role(SystemRoles.SuperAdmin)]));
        Assert.Equal("users.cannot_grant_super_admin", ex.Code);
    }

    [Fact]
    public void A_role_with_permissions_the_actor_lacks_cannot_be_granted()
    {
        var ex = Assert.Throws<ForbiddenOperationException>(() => RoleAssignmentPolicy.EnsureCanGrant(
            PermissionsOf(SystemRoles.InventoryManager), actorIsSuperAdmin: false, [Role(SystemRoles.SalesEmployee)]));
        Assert.Equal("users.role_exceeds_own_permissions", ex.Code);
    }

    [Fact]
    public void A_subset_role_can_be_granted()
    {
        var actor = PermissionsOf(SystemRoles.Manager).Append(Permissions.Users.AssignRoles).ToHashSet();
        RoleAssignmentPolicy.EnsureCanGrant(actor, actorIsSuperAdmin: false,
            [Role(SystemRoles.SalesEmployee), Role(SystemRoles.Viewer), Role(SystemRoles.InventoryManager)]);
    }

    [Fact]
    public void Non_super_admins_cannot_manage_super_admins_or_more_privileged_users()
    {
        var manager = PermissionsOf(SystemRoles.Manager);

        Assert.Throws<ForbiddenOperationException>(() => RoleAssignmentPolicy.EnsureCanManage(
            manager, false, targetIsSuperAdmin: true, PermissionsOf(SystemRoles.SuperAdmin)));
        Assert.Throws<ForbiddenOperationException>(() => RoleAssignmentPolicy.EnsureCanManage(
            PermissionsOf(SystemRoles.SalesEmployee), false, false, manager));

        RoleAssignmentPolicy.EnsureCanManage(manager, false, false, PermissionsOf(SystemRoles.SalesEmployee));
    }

    [Fact]
    public void The_last_active_super_admin_cannot_be_removed()
    {
        var ex = Assert.Throws<ConflictException>(() =>
            RoleAssignmentPolicy.EnsureSuperAdminRemains(otherActiveSuperAdmins: 0, removingSuperAdmin: true));
        Assert.Equal("users.last_super_admin", ex.Code);

        RoleAssignmentPolicy.EnsureSuperAdminRemains(otherActiveSuperAdmins: 1, removingSuperAdmin: true);
    }

    [Fact]
    public void Self_modification_is_blocked()
    {
        var id = Guid.NewGuid();
        Assert.Throws<ForbiddenOperationException>(() => RoleAssignmentPolicy.EnsureNotSelf(id, id, "deactivate"));
        RoleAssignmentPolicy.EnsureNotSelf(id, Guid.NewGuid(), "deactivate");
    }
}

public sealed class PermissionCatalogTests
{
    [Fact]
    public void Permissions_are_unique_and_follow_module_dot_action_naming()
    {
        Assert.Equal(Permissions.All.Count, Permissions.All.Distinct().Count());
        Assert.All(Permissions.All, p => Assert.Matches("^[a-z_]+\\.[a-z_]+$", p));
    }

    [Fact]
    public void Super_admin_holds_every_permission_and_roles_only_use_known_permissions()
    {
        Assert.Equal(Permissions.All.ToHashSet(), SystemRoles.Find(SystemRoles.SuperAdmin)!.Permissions);
        Assert.All(SystemRoles.All, r => Assert.Subset(Permissions.All.ToHashSet(), r.Permissions.ToHashSet()));
    }

    [Fact]
    public void Only_super_admin_can_manage_employees()
    {
        foreach (var role in SystemRoles.All.Where(r => r.Name != SystemRoles.SuperAdmin))
        {
            Assert.DoesNotContain(Permissions.Users.Create, role.Permissions);
            Assert.DoesNotContain(Permissions.Users.AssignRoles, role.Permissions);
        }
    }
}
