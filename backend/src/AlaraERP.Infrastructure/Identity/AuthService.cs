using AlaraERP.Application.Common;
using AlaraERP.Application.Common.Security;
using AlaraERP.Application.Identity;
using AlaraERP.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace AlaraERP.Infrastructure.Identity;

internal sealed class AuthService(
    UserManager<AppUser> users,
    SignInManager<AppUser> signIn,
    AlaraDbContext db,
    TimeProvider clock,
    IConfiguration configuration,
    IHostEnvironment environment) : IAuthService
{
    private bool DevelopmentLoginEnabled =>
        environment.IsDevelopment() && configuration.GetValue<bool>("Auth:PasswordlessLogin");

    public async Task<LoginResult> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        var identifier = request.Email.Trim();
        var user = await users.FindByNameAsync(identifier) ?? await users.FindByEmailAsync(identifier);
        if (user is null)
            return new LoginResult(LoginOutcome.InvalidCredentials);

        var result = await signIn.PasswordSignInAsync(user, request.Password, isPersistent: false, lockoutOnFailure: true);
        if (result.IsLockedOut)
            return new LoginResult(LoginOutcome.LockedOut);
        if (!result.Succeeded)
            return new LoginResult(LoginOutcome.InvalidCredentials); // wrong password or deactivated: same answer

        user.LastLoginAt = clock.GetUtcNow();
        await users.UpdateAsync(user);
        return new LoginResult(LoginOutcome.Succeeded, await BuildCurrentUserAsync(user, ct));
    }

    public async Task<LoginResult> DevelopmentLoginAsync(CancellationToken ct)
    {
        if (!DevelopmentLoginEnabled)
            return new LoginResult(LoginOutcome.InvalidCredentials);

        // Temporary local setup: use the first active Super Admin so account creation remains available.
        // This path is unreachable outside Development and does not alter the user's stored password.
        var user = await (from u in db.Users
                          join ur in db.UserRoles on u.Id equals ur.UserId
                          join r in db.Roles on ur.RoleId equals r.Id
                          where u.IsActive && r.Name == SystemRoles.SuperAdmin
                          orderby u.CreatedAt, u.Id
                          select u).FirstOrDefaultAsync(ct);
        if (user is null)
            return new LoginResult(LoginOutcome.InvalidCredentials);

        await signIn.SignInAsync(user, isPersistent: false);
        user.LastLoginAt = clock.GetUtcNow();
        await users.UpdateAsync(user);
        return new LoginResult(LoginOutcome.Succeeded, await BuildCurrentUserAsync(user, ct));
    }

    public Task LogoutAsync() => signIn.SignOutAsync();

    public async Task<CurrentUserDto> GetCurrentUserAsync(Guid userId, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(userId.ToString()) ?? throw new NotFoundException("User", userId);
        return await BuildCurrentUserAsync(user, ct);
    }

    public async Task ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(userId.ToString()) ?? throw new NotFoundException("User", userId);
        (await users.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword)).ThrowIfFailed("newPassword");

        user.MustChangePassword = false;
        (await users.UpdateAsync(user)).ThrowIfFailed();
        await signIn.RefreshSignInAsync(user); // security stamp changed; keep this session, others are invalidated
    }

    private async Task<CurrentUserDto> BuildCurrentUserAsync(AppUser user, CancellationToken ct)
    {
        var roles = await (from ur in db.UserRoles
                           join r in db.Roles on ur.RoleId equals r.Id
                           where ur.UserId == user.Id
                           select r).ToListAsync(ct);
        var roleIds = roles.Select(r => r.Id).ToList();
        var permissions = await db.RoleClaims
            .Where(c => roleIds.Contains(c.RoleId) && c.ClaimType == Permissions.ClaimType)
            .Select(c => c.ClaimValue!)
            .Distinct()
            .OrderBy(p => p)
            .ToListAsync(ct);

        return new CurrentUserDto(user.Id, user.Email!, user.FirstName, user.LastName,
            DevelopmentLoginEnabled ? false : user.MustChangePassword,
            roles.Select(r => r.Name!).OrderBy(n => n).ToList(), permissions);
    }
}
