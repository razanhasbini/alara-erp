using AlaraERP.Application.Common.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace AlaraERP.Api.Security;

/// <summary>Requires the caller to hold a named permission, e.g. <c>[HasPermission(Permissions.Orders.Create)]</c>.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class HasPermissionAttribute(string permission) : AuthorizeAttribute(PermissionPolicyProvider.Prefix + permission);

public sealed class PermissionRequirement(string permission) : IAuthorizationRequirement
{
    public string Permission { get; } = permission;
}

public sealed class PermissionHandler : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (context.User.HasClaim(Permissions.ClaimType, requirement.Permission))
            context.Succeed(requirement);
        return Task.CompletedTask;
    }
}

/// <summary>Builds "perm:{name}" policies on demand instead of registering one policy per permission.</summary>
public sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : DefaultAuthorizationPolicyProvider(options)
{
    public const string Prefix = "perm:";

    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (!policyName.StartsWith(Prefix, StringComparison.Ordinal))
            return await base.GetPolicyAsync(policyName);

        var permission = policyName[Prefix.Length..];
        if (!Permissions.All.Contains(permission))
            throw new InvalidOperationException($"Unknown permission '{permission}' used in an authorization attribute.");

        return new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new PermissionRequirement(permission))
            .Build();
    }
}
