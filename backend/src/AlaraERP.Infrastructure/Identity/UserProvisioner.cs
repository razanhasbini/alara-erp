using AlaraERP.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AlaraERP.Infrastructure.Identity;

public sealed record ProvisionUserOptions(
    string UserName, string Email, string Password, string FirstName, string LastName, string Role);

/// <summary>Idempotently creates or updates a local employee account without storing its password in source control.</summary>
public sealed class UserProvisioner(
    UserManager<AppUser> users,
    AlaraDbContext db,
    ILogger<UserProvisioner> logger)
{
    public async Task RunAsync(ProvisionUserOptions options, CancellationToken ct)
    {
        var role = options.Role.Trim();
        if (!await db.Roles.AnyAsync(r => r.Name == role, ct))
            throw new InvalidOperationException($"Role '{role}' does not exist. Run the 'migrate' command first.");

        var userName = options.UserName.Trim();
        var email = options.Email.Trim();
        var user = await users.FindByNameAsync(userName) ?? await users.FindByEmailAsync(email);

        if (user is null)
        {
            user = new AppUser
            {
                UserName = userName,
                Email = email,
                EmailConfirmed = true,
                FirstName = options.FirstName.Trim(),
                LastName = options.LastName.Trim(),
                IsActive = true,
                MustChangePassword = false,
            };
            (await users.CreateAsync(user, options.Password)).ThrowIfFailed();
        }
        else
        {
            user.UserName = userName;
            user.Email = email;
            user.EmailConfirmed = true;
            user.FirstName = options.FirstName.Trim();
            user.LastName = options.LastName.Trim();
            user.IsActive = true;
            user.MustChangePassword = false;
            user.LockoutEnd = null;
            user.AccessFailedCount = 0;
            (await users.UpdateAsync(user)).ThrowIfFailed();
            var token = await users.GeneratePasswordResetTokenAsync(user);
            (await users.ResetPasswordAsync(user, token, options.Password)).ThrowIfFailed();
        }

        var existingRoles = await users.GetRolesAsync(user);
        if (existingRoles.Count > 0)
            (await users.RemoveFromRolesAsync(user, existingRoles)).ThrowIfFailed();
        (await users.AddToRoleAsync(user, role)).ThrowIfFailed();
        logger.LogInformation("Employee account {UserName} provisioned with role {Role}.", userName, role);
    }
}
