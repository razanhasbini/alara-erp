using AlaraERP.Api.Security;
using AlaraERP.Application.Activity;
using AlaraERP.Application.Common;
using Microsoft.AspNetCore.Mvc;
using static AlaraERP.Application.Common.Security.Permissions;

namespace AlaraERP.Api.Controllers;

/// <summary>The activity log: who did what and when, in plain words (admin only to read).</summary>
[Route("api/activity")]
public sealed class ActivityController(ActivityService activity) : ApiControllerBase
{
    [HttpGet]
    [HasPermission(Activity.View)]
    public Task<PagedResult<ActivityEntryDto>> List([FromQuery] ActivityQuery query, CancellationToken ct) => activity.ListAsync(query, ct);

    /// <summary>Called by the app when someone opens a page worth noting ("checked the inventory"). Anyone signed in.</summary>
    [HttpPost("viewed")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> Viewed(ViewedRequest request, CancellationToken ct)
    {
        await activity.RecordViewAsync(request, ct);
        return NoContent();
    }
}
