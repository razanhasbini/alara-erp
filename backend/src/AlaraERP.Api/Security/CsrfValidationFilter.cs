using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace AlaraERP.Api.Security;

/// <summary>For a call made by another server, not a browser: it has no session to protect and must prove itself another way.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class SkipCsrfAttribute : Attribute;

/// <summary>
/// Validates the antiforgery token (cookie + <c>X-XSRF-TOKEN</c> header) on every state-changing request.
/// The SPA obtains the header value from <c>GET /api/auth/csrf</c>.
/// </summary>
public sealed class CsrfValidationFilter(IAntiforgery antiforgery) : IAsyncAuthorizationFilter
{
    private static readonly HashSet<string> SafeMethods = new(StringComparer.OrdinalIgnoreCase) { "GET", "HEAD", "OPTIONS", "TRACE" };

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        if (SafeMethods.Contains(context.HttpContext.Request.Method)) return;
        if (context.ActionDescriptor.EndpointMetadata.OfType<SkipCsrfAttribute>().Any()) return;

        try
        {
            await antiforgery.ValidateRequestAsync(context.HttpContext);
        }
        catch (AntiforgeryValidationException)
        {
            context.Result = new ObjectResult(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Invalid or missing CSRF token.",
                Detail = "Refresh the page and try again.",
                Extensions = { ["code"] = "auth.csrf_invalid" },
            }) { StatusCode = StatusCodes.Status400BadRequest };
        }
    }
}
