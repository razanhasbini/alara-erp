using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using AlaraERP.Api.Cli;
using AlaraERP.Api.Errors;
using AlaraERP.Api.Security;
using AlaraERP.Application;
using AlaraERP.Application.Common;
using AlaraERP.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

if (!builder.Environment.IsDevelopment())
    builder.Logging.ClearProviders().AddJsonConsole();

builder.Services.AddApplication();
builder.Services.AddInfrastructure(config);

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();

builder.Services
    .AddControllers(o =>
    {
        o.Filters.Add<CsrfValidationFilter>();
        o.Filters.Add<ValidationFilter>();
    })
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddOpenApi();

// ---- Authentication: Identity cookie, HttpOnly, SameSite=Strict -------------------------------
builder.Services
    .AddAuthentication(IdentityConstants.ApplicationScheme)
    .AddIdentityCookies();
builder.Services.ConfigureApplicationCookie(o =>
{
    o.Cookie.Name = "alara.session";
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Strict;
    o.Cookie.SecurePolicy = CookieSecurePolicyFrom(config);
    o.ExpireTimeSpan = TimeSpan.FromHours(config.GetValue("Auth:SessionHours", 8));
    o.SlidingExpiration = true;
    // An API never redirects to a login page.
    o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
    o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
});
// How quickly deactivation, password resets and role changes reach existing sessions.
builder.Services.Configure<SecurityStampValidatorOptions>(o =>
    o.ValidationInterval = config.GetValue("Auth:SessionRevalidationInterval", TimeSpan.FromMinutes(1)));

builder.Services.AddAntiforgery(o =>
{
    o.HeaderName = "X-XSRF-TOKEN";
    o.Cookie.Name = "alara.csrf";
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Strict;
    o.Cookie.SecurePolicy = CookieSecurePolicyFrom(config);
});

// ---- Authorization: everything requires a signed-in employee unless marked [AllowAnonymous] ----
builder.Services.AddAuthorization(o =>
    o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
builder.Services.AddSingleton<IAuthorizationHandler, PermissionHandler>();

// ---- Brute-force protection (in addition to Identity lockout) ----------------------------------
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy(RateLimitPolicies.Login, ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = config.GetValue("Auth:LoginAttemptsPerMinute", 10),
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        }));
});

var app = builder.Build();

if (await CliCommands.TryRunAsync(app, args) is { } exitCode)
    return exitCode;

app.UseExceptionHandler();
app.UseStatusCodePages();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

if (app.Environment.IsDevelopment())
    app.MapOpenApi().AllowAnonymous();

app.MapControllers();

await app.RunAsync();
return 0;

// Settings are read inside option callbacks (not up-front) so environment/test overrides apply.
static CookieSecurePolicy CookieSecurePolicyFrom(IConfiguration config) =>
    config.GetValue("Auth:CookieSecurePolicy", CookieSecurePolicy.Always);

public partial class Program;
