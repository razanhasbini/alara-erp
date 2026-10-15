using System.Net;
using AlaraERP.Application.Common.Security;
using AlaraERP.Application.Identity;
using AlaraERP.IntegrationTests.Infrastructure;

namespace AlaraERP.IntegrationTests.Identity;

public sealed class AuthTests(AlaraApiFactory factory) : ApiTestBase(factory)
{
    [Fact]
    public async Task Login_with_correct_password_returns_the_employee_with_role_permissions()
    {
        var employee = await CreateEmployeeAsync(SystemRoles.SalesEmployee);
        using var client = NewClient();

        var response = await client.LoginAsync(employee.Email, employee.Password);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var me = await response.ReadJsonAsync<CurrentUserDto>();
        Assert.Equal(employee.Email, me.Email);
        Assert.Equal([SystemRoles.SalesEmployee], me.Roles);
        Assert.Contains(Permissions.Orders.Create, me.Permissions);
        Assert.DoesNotContain(Permissions.Users.View, me.Permissions);

        var cookie = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("alara.session="));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Session_survives_across_requests()
    {
        using var client = await SignedInAsAsync(SystemRoles.Viewer);

        var me = await client.GetJsonAsync<CurrentUserDto>("/api/auth/me");

        Assert.Equal([SystemRoles.Viewer], me.Roles);
    }

    [Fact]
    public async Task Login_with_wrong_password_is_rejected()
    {
        var employee = await CreateEmployeeAsync(SystemRoles.Viewer);
        using var client = NewClient();

        var response = await client.LoginAsync(employee.Email, "Wrong-Password-1");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("auth.invalid_credentials", await response.ProblemCodeAsync());
    }

    [Fact]
    public async Task Login_with_unknown_email_gives_the_same_answer_as_a_wrong_password()
    {
        using var client = NewClient();

        var response = await client.LoginAsync("nobody@alaraline.test", "Whatever-Password-1");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("auth.invalid_credentials", await response.ProblemCodeAsync());
    }

    [Fact]
    public async Task Five_failed_attempts_lock_the_account_even_for_the_correct_password()
    {
        var employee = await CreateEmployeeAsync(SystemRoles.Viewer);
        using var client = NewClient();

        for (var i = 0; i < 5; i++)
            await client.LoginAsync(employee.Email, "Wrong-Password-1");
        var response = await client.LoginAsync(employee.Email, employee.Password);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("auth.locked_out", await response.ProblemCodeAsync());
    }

    [Fact]
    public async Task Deactivated_employee_cannot_sign_in()
    {
        var employee = await TestEmployees.CreateAsync(Factory.Services, [SystemRoles.Viewer], isActive: false);
        using var client = NewClient();

        var response = await client.LoginAsync(employee.Email, employee.Password);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("auth.invalid_credentials", await response.ProblemCodeAsync());
    }

    [Fact]
    public async Task Deactivating_an_employee_ends_their_open_session()
    {
        using var admin = await SignedInAsAsync(SystemRoles.SuperAdmin);
        var employee = await CreateEmployeeAsync(SystemRoles.SalesEmployee);
        using var employeeClient = await SignedInAsync(employee);
        Assert.Equal(HttpStatusCode.OK, (await employeeClient.GetAsync("/api/auth/me")).StatusCode);

        (await admin.PostAsync($"/api/users/{employee.Id}/deactivate")).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Unauthorized, (await employeeClient.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Logout_ends_the_session()
    {
        using var client = await SignedInAsAsync(SystemRoles.Viewer);

        (await client.PostAsync("/api/auth/logout")).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Anonymous_requests_to_protected_endpoints_get_401()
    {
        using var client = NewClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/users")).StatusCode);
    }

    [Fact]
    public async Task State_changing_requests_without_a_csrf_token_are_rejected()
    {
        using var client = await SignedInAsAsync(SystemRoles.Viewer);
        client.Http.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");

        var response = await client.PostAsync("/api/auth/logout");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("auth.csrf_invalid", await response.ProblemCodeAsync());
    }

    [Fact]
    public async Task Changing_password_clears_the_temporary_flag_and_retires_the_old_password()
    {
        using var admin = await SignedInAsAsync(SystemRoles.SuperAdmin);
        var created = await (await admin.PostAsync("/api/users", new CreateUserRequest(
            "new.hire@alaraline.test", "Maya", "Haddad", "Temporary-Pass-1", [SystemRoles.SalesEmployee])))
            .ReadJsonAsync<UserSummaryDto>();

        using var employee = NewClient();
        var login = await employee.LoginAsync(created.Email, "Temporary-Pass-1");
        Assert.True((await login.ReadJsonAsync<CurrentUserDto>()).MustChangePassword);

        var change = await employee.PostAsync("/api/auth/change-password",
            new ChangePasswordRequest("Temporary-Pass-1", "My-Own-Secret-77"));
        Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);

        // The session that changed the password stays valid.
        Assert.False((await employee.GetJsonAsync<CurrentUserDto>("/api/auth/me")).MustChangePassword);

        using var other = NewClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await other.LoginAsync(created.Email, "Temporary-Pass-1")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await other.LoginAsync(created.Email, "My-Own-Secret-77")).StatusCode);
    }

    [Fact]
    public async Task Changing_password_with_a_wrong_current_password_fails_validation()
    {
        using var client = await SignedInAsAsync(SystemRoles.Viewer);

        var response = await client.PostAsync("/api/auth/change-password",
            new ChangePasswordRequest("Not-My-Password-1", "Brand-New-Pass-9"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("currentPassword", await response.Content.ReadAsStringAsync());
    }
}
