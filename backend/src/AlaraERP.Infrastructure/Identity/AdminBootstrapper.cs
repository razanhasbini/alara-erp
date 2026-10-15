using AlaraERP.Application.Common.Security;
using AlaraERP.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AlaraERP.Infrastructure.Identity;

public sealed record BootstrapAdminOptions(string Email, string Password, string FirstName, string LastName);

/// <summary>Creates the very first Super Admin. Refuses to run once any active Super Admin exists.</summary>
public sealed class AdminBootstrapper(UserManager<AppUser> users, AlaraDbContext db, ILogger<AdminBootstrapper> logger)
{
    public async Task<bool> RunAsync(BootstrapAdminOptions options, CancellationToken ct)
    {
        var superAdminExists = await (from ur in db.UserRoles
                                      join r in db.Roles on ur.RoleId equals r.Id
                                      join u in db.Users on ur.UserId equals u.Id
                                      where r.Name == SystemRoles.SuperAdmin && u.IsActive
                                      select u.Id).AnyAsync(ct);
        if (superAdminExists)
        {
            logger.LogWarning("An active Super Admin already exists; bootstrap skipped.");
            return false;
        }
        if (!await db.Roles.AnyAsync(r => r.Name == SystemRoles.SuperAdmin, ct))
            throw new InvalidOperationException("Roles are missing. Run the 'migrate' command first.");

        var user = new AppUser
        {
            Email = options.Email.Trim(),
            UserName = options.Email.Trim(),
            EmailConfirmed = true,
            FirstName = options.FirstName.Trim(),
            LastName = options.LastName.Trim(),
            IsActive = true,
        };

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        (await users.CreateAsync(user, options.Password)).ThrowIfFailed();
        (await users.AddToRoleAsync(user, SystemRoles.SuperAdmin)).ThrowIfFailed();
        await tx.CommitAsync(ct);

        logger.LogInformation("Super Admin {Email} created.", user.Email);
        return true;
    }
}
