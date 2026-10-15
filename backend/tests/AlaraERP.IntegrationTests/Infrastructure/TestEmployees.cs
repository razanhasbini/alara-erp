using AlaraERP.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace AlaraERP.IntegrationTests.Infrastructure;

public sealed record TestEmployee(Guid Id, string Email, string Password);

public static class TestEmployees
{
    public const string DefaultPassword = "Correct-Horse-42";

    public static async Task<TestEmployee> CreateAsync(IServiceProvider services, string[] roles, bool isActive = true)
    {
        await using var scope = services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var email = $"emp-{Guid.NewGuid():N}@alaraline.test";
        var user = new AppUser
        {
            Email = email, UserName = email, EmailConfirmed = true,
            FirstName = "Test", LastName = string.Join("+", roles), IsActive = isActive,
        };
        Check(await users.CreateAsync(user, DefaultPassword));
        if (roles.Length > 0) Check(await users.AddToRolesAsync(user, roles));
        return new TestEmployee(user.Id, email, DefaultPassword);
    }

    private static void Check(IdentityResult result)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join("; ", result.Errors.Select(e => e.Description)));
    }
}
