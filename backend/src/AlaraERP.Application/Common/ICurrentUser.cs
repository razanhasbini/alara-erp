namespace AlaraERP.Application.Common;

public interface ICurrentUser
{
    Guid? UserId { get; }
    bool IsAuthenticated { get; }
    IReadOnlySet<string> Roles { get; }
    IReadOnlySet<string> Permissions { get; }

    bool HasPermission(string permission) => Permissions.Contains(permission);

    /// <summary>The authenticated user's id; throws if called from an anonymous context.</summary>
    Guid RequiredUserId => UserId ?? throw new InvalidOperationException("No authenticated user.");
}
