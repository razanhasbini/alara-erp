namespace AlaraERP.Application.Identity;

public sealed record LoginRequest(string Email, string Password);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public sealed record CurrentUserDto(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    bool MustChangePassword,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions);

public sealed record UserSummaryDto(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    bool IsActive,
    bool IsLockedOut,
    IReadOnlyList<string> Roles,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt);

public sealed record UserListQuery(string? Search, bool? IsActive, string? Role, int Page = 1, int PageSize = 25);

public sealed record CreateUserRequest(
    string Email,
    string FirstName,
    string LastName,
    string TemporaryPassword,
    IReadOnlyList<string> Roles);

public sealed record UpdateUserRequest(string Email, string FirstName, string LastName);

public sealed record AssignRolesRequest(IReadOnlyList<string> Roles);

public sealed record ResetPasswordRequest(string TemporaryPassword);

public sealed record RoleDto(string Name, string Description, bool IsSystem, IReadOnlyList<string> Permissions, int UserCount);
