using System.Net;
using AlaraERP.Application.Common;
using AlaraERP.Application.Common.Security;
using AlaraERP.Application.Identity;
using AlaraERP.IntegrationTests.Infrastructure;

namespace AlaraERP.IntegrationTests.Identity;

public sealed class UserAdministrationTests(AlaraApiFactory factory) : ApiTestBase(factory)
{
    private static CreateUserRequest NewUser(string email, params string[] roles) =>
        new(email, "Lea", "Khoury", "Temporary-Pass-1", roles);

    [Fact]
    public async Task Super_admin_creates_an_employee_with_roles()
    {
        using var admin = await SignedInAsAsync(SystemRoles.SuperAdmin);

        var response = await admin.PostAsync("/api/users", NewUser("lea@alaraline.test", SystemRoles.InventoryManager));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var user = await response.ReadJsonAsync<UserSummaryDto>();
        Assert.Equal([SystemRoles.InventoryManager], user.Roles);
        Assert.True(user.IsActive);

        var list = await admin.GetJsonAsync<PagedResult<UserSummaryDto>>("/api/users?search=lea@");
        Assert.Single(list.Items);
    }

    [Fact]
    public async Task Duplicate_email_is_rejected_with_409()
    {
        using var admin = await SignedInAsAsync(SystemRoles.SuperAdmin);
        (await admin.PostAsync("/api/users", NewUser("dup@alaraline.test", SystemRoles.Viewer))).EnsureSuccessStatusCode();

        var response = await admin.PostAsync("/api/users", NewUser("DUP@alaraline.test", SystemRoles.Viewer));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("users.duplicate_email", await response.ProblemCodeAsync());
    }

    [Fact]
    public async Task Weak_password_and_unknown_role_are_reported_per_field()
    {
        using var admin = await SignedInAsAsync(SystemRoles.SuperAdmin);

        var response = await admin.PostAsync("/api/users",
            new CreateUserRequest("weak@alaraline.test", "A", "B", "short", ["Wizard"]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("temporaryPassword", body);
        Assert.Contains("roles", body);
    }

    [Theory]
    [InlineData(SystemRoles.Manager)]
    [InlineData(SystemRoles.InventoryManager)]
    [InlineData(SystemRoles.SalesEmployee)]
    [InlineData(SystemRoles.Viewer)]
    public async Task Only_super_admins_can_create_employees(string role)
    {
        using var client = await SignedInAsAsync(role);

        var response = await client.PostAsync("/api/users", NewUser($"x-{Guid.NewGuid():N}@alaraline.test", SystemRoles.Viewer));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Manager_can_view_employees_but_sales_cannot()
    {
        using var manager = await SignedInAsAsync(SystemRoles.Manager);
        using var sales = await SignedInAsAsync(SystemRoles.SalesEmployee);

        Assert.Equal(HttpStatusCode.OK, (await manager.GetAsync("/api/users")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await manager.GetAsync("/api/roles")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await sales.GetAsync("/api/users")).StatusCode);
    }

    [Fact]
    public async Task Employees_cannot_deactivate_or_re_role_themselves()
    {
        var self = await CreateEmployeeAsync(SystemRoles.SuperAdmin);
        using var client = await SignedInAsync(self);

        var deactivate = await client.PostAsync($"/api/users/{self.Id}/deactivate");
        var roles = await client.PutAsync($"/api/users/{self.Id}/roles", new AssignRolesRequest([SystemRoles.Viewer]));

        Assert.Equal(HttpStatusCode.Forbidden, deactivate.StatusCode);
        Assert.Equal("users.cannot_modify_self", await deactivate.ProblemCodeAsync());
        Assert.Equal(HttpStatusCode.Forbidden, roles.StatusCode);
    }

    [Fact]
    public async Task Role_changes_take_effect_on_the_next_request()
    {
        using var admin = await SignedInAsAsync(SystemRoles.SuperAdmin);
        var employee = await CreateEmployeeAsync(SystemRoles.Manager);
        using var employeeClient = await SignedInAsync(employee);
        Assert.Equal(HttpStatusCode.OK, (await employeeClient.GetAsync("/api/users")).StatusCode);

        (await admin.PutAsync($"/api/users/{employee.Id}/roles", new AssignRolesRequest([SystemRoles.SalesEmployee])))
            .EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Forbidden, (await employeeClient.GetAsync("/api/users")).StatusCode);
    }

    [Fact]
    public async Task Reset_password_issues_a_temporary_password_and_unlocks_the_account()
    {
        using var admin = await SignedInAsAsync(SystemRoles.SuperAdmin);
        var employee = await CreateEmployeeAsync(SystemRoles.Viewer);
        using var locked = NewClient();
        for (var i = 0; i < 5; i++) await locked.LoginAsync(employee.Email, "Wrong-Password-1");

        var reset = await admin.PostAsync($"/api/users/{employee.Id}/reset-password", new ResetPasswordRequest("Fresh-Temp-Pass-3"));

        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
        using var client = NewClient();
        var login = await client.LoginAsync(employee.Email, "Fresh-Temp-Pass-3");
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.True((await login.ReadJsonAsync<CurrentUserDto>()).MustChangePassword);
    }

    [Fact]
    public async Task Roles_endpoint_lists_system_roles_with_their_permissions()
    {
        using var admin = await SignedInAsAsync(SystemRoles.SuperAdmin);

        var roles = await admin.GetJsonAsync<List<RoleDto>>("/api/roles");

        Assert.Equal(SystemRoles.All.Select(r => r.Name), roles.Select(r => r.Name));
        Assert.Equal(Permissions.All, roles.Single(r => r.Name == SystemRoles.SuperAdmin).Permissions);
        Assert.All(roles, r => Assert.True(r.IsSystem));
    }
}
