using System.Security.Claims;
using AlaraERP.Application.Common;
using AlaraERP.Application.Common.Security;

namespace AlaraERP.Api.Security;

internal sealed class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public Guid? UserId =>
        IsAuthenticated && Guid.TryParse(Principal!.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    public IReadOnlySet<string> Roles => Values(ClaimTypes.Role);

    public IReadOnlySet<string> Permissions => Values(Application.Common.Security.Permissions.ClaimType);

    private HashSet<string> Values(string type) =>
        Principal?.FindAll(type).Select(c => c.Value).ToHashSet(StringComparer.Ordinal) ?? [];
}
