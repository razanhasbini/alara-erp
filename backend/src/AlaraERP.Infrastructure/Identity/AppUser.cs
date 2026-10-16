using AlaraERP.Domain.Common;
using Microsoft.AspNetCore.Identity;

namespace AlaraERP.Infrastructure.Identity;

/// <summary>An ERP employee account. Employees may sign in with their username or email address.</summary>
public class AppUser : IdentityUser<Guid>, IAuditable
{
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public bool IsActive { get; set; } = true;

    /// <summary>Set when an administrator issues a temporary password; cleared when the employee changes it.</summary>
    public bool MustChangePassword { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }
}

public class AppRole : IdentityRole<Guid>
{
    public string Description { get; set; } = "";

    /// <summary>System roles are defined in code and cannot be deleted or have their permissions edited.</summary>
    public bool IsSystem { get; set; }
}
