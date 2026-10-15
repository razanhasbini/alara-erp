using AlaraERP.Api.Security;
using AlaraERP.Application.Common;
using AlaraERP.Application.Identity;
using Microsoft.AspNetCore.Mvc;
using static AlaraERP.Application.Common.Security.Permissions;

namespace AlaraERP.Api.Controllers;

[Route("api/users")]
public sealed class UsersController(IUserService users) : ApiControllerBase
{
    [HttpGet]
    [HasPermission(Users.View)]
    public Task<PagedResult<UserSummaryDto>> List([FromQuery] UserListQuery query, CancellationToken ct) =>
        users.ListAsync(query, ct);

    [HttpGet("{id:guid}")]
    [HasPermission(Users.View)]
    public Task<UserSummaryDto> Get(Guid id, CancellationToken ct) => users.GetAsync(id, ct);

    [HttpPost]
    [HasPermission(Users.Create)]
    [ProducesResponseType<UserSummaryDto>(201)]
    public async Task<IActionResult> Create(CreateUserRequest request, CancellationToken ct)
    {
        var user = await users.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = user.Id }, user);
    }

    [HttpPut("{id:guid}")]
    [HasPermission(Users.Update)]
    public Task<UserSummaryDto> Update(Guid id, UpdateUserRequest request, CancellationToken ct) =>
        users.UpdateAsync(id, request, ct);

    [HttpPost("{id:guid}/activate")]
    [HasPermission(Users.Update)]
    public Task<UserSummaryDto> Activate(Guid id, CancellationToken ct) => users.SetActiveAsync(id, true, ct);

    [HttpPost("{id:guid}/deactivate")]
    [HasPermission(Users.Update)]
    public Task<UserSummaryDto> Deactivate(Guid id, CancellationToken ct) => users.SetActiveAsync(id, false, ct);

    [HttpPut("{id:guid}/roles")]
    [HasPermission(Users.AssignRoles)]
    public Task<UserSummaryDto> AssignRoles(Guid id, AssignRolesRequest request, CancellationToken ct) =>
        users.AssignRolesAsync(id, request, ct);

    [HttpPost("{id:guid}/reset-password")]
    [HasPermission(Users.Update)]
    [ProducesResponseType(204)]
    public async Task<IActionResult> ResetPassword(Guid id, ResetPasswordRequest request, CancellationToken ct)
    {
        await users.ResetPasswordAsync(id, request, ct);
        return NoContent();
    }
}

[Route("api/roles")]
public sealed class RolesController(IRoleService roles) : ApiControllerBase
{
    [HttpGet]
    [HasPermission(Users.View)]
    public Task<IReadOnlyList<RoleDto>> List(CancellationToken ct) => roles.ListAsync(ct);

    [HttpGet("permissions")]
    [HasPermission(Users.View)]
    public IReadOnlyList<string> Permissions() => All;
}
