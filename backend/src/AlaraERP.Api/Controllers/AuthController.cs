using AlaraERP.Application.Common;
using AlaraERP.Application.Identity;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using AlaraERP.Api.Security;

namespace AlaraERP.Api.Controllers;

[Route("api/auth")]
public sealed class AuthController(
    IAuthService auth,
    ICurrentUser currentUser,
    IAntiforgery antiforgery,
    IConfiguration configuration,
    IWebHostEnvironment environment) : ApiControllerBase
{
    public sealed record CsrfTokenResponse(string Token);

    /// <summary>
    /// Issues an antiforgery token bound to the current session. Call on app start and again after login/logout,
    /// then send the value in the <c>X-XSRF-TOKEN</c> header on every POST/PUT/PATCH/DELETE.
    /// </summary>
    [HttpGet("csrf")]
    [AllowAnonymous]
    public ActionResult<CsrfTokenResponse> Csrf()
    {
        var tokens = antiforgery.GetAndStoreTokens(HttpContext);
        return new CsrfTokenResponse(tokens.RequestToken!);
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Login)]
    [ProducesResponseType<CurrentUserDto>(200)]
    [ProducesResponseType<ProblemDetails>(401)]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken ct)
    {
        var result = await auth.LoginAsync(request, ct);
        return result.Outcome switch
        {
            LoginOutcome.Succeeded => Ok(result.User),
            LoginOutcome.LockedOut => ProblemWithCode(401, "auth.locked_out", "Account temporarily locked",
                "Too many failed sign-in attempts. Try again in 15 minutes or ask an administrator to reset your password."),
            _ => ProblemWithCode(401, "auth.invalid_credentials", "Sign-in failed", "Incorrect username or password."),
        };
    }

    [HttpPost("development-login")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Login)]
    [ProducesResponseType<CurrentUserDto>(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> DevelopmentLogin(CancellationToken ct)
    {
        if (!environment.IsDevelopment() || !configuration.GetValue<bool>("Auth:PasswordlessLogin"))
            return NotFound();

        var result = await auth.DevelopmentLoginAsync(ct);
        return result.Outcome == LoginOutcome.Succeeded
            ? Ok(result.User)
            : ProblemWithCode(503, "auth.no_development_admin", "No administrator available",
                "Create an active Super Admin account before using development sign-in.");
    }

    [HttpPost("logout")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> Logout()
    {
        await auth.LogoutAsync();
        return NoContent();
    }

    [HttpGet("me")]
    public Task<CurrentUserDto> Me(CancellationToken ct) => auth.GetCurrentUserAsync(currentUser.RequiredUserId, ct);

    [HttpPost("change-password")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken ct)
    {
        await auth.ChangePasswordAsync(currentUser.RequiredUserId, request, ct);
        return NoContent();
    }
}
