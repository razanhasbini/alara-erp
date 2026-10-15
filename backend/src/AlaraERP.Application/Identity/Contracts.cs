using AlaraERP.Application.Common;

namespace AlaraERP.Application.Identity;

public enum LoginOutcome { Succeeded, InvalidCredentials, LockedOut }

public sealed record LoginResult(LoginOutcome Outcome, CurrentUserDto? User = null);

public interface IAuthService
{
    Task<LoginResult> LoginAsync(LoginRequest request, CancellationToken ct);
    Task LogoutAsync();
    Task<CurrentUserDto> GetCurrentUserAsync(Guid userId, CancellationToken ct);
    Task ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken ct);
}

public interface IUserService
{
    Task<PagedResult<UserSummaryDto>> ListAsync(UserListQuery query, CancellationToken ct);
    Task<UserSummaryDto> GetAsync(Guid id, CancellationToken ct);
    Task<UserSummaryDto> CreateAsync(CreateUserRequest request, CancellationToken ct);
    Task<UserSummaryDto> UpdateAsync(Guid id, UpdateUserRequest request, CancellationToken ct);
    Task<UserSummaryDto> SetActiveAsync(Guid id, bool isActive, CancellationToken ct);
    Task<UserSummaryDto> AssignRolesAsync(Guid id, AssignRolesRequest request, CancellationToken ct);
    Task ResetPasswordAsync(Guid id, ResetPasswordRequest request, CancellationToken ct);
}

public interface IRoleService
{
    Task<IReadOnlyList<RoleDto>> ListAsync(CancellationToken ct);
}
