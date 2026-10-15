using AlaraERP.Domain.Common;

namespace AlaraERP.Application.Common;

public sealed class NotFoundException(string entity, object key)
    : Exception($"{entity} '{key}' was not found.")
{
    public string Entity { get; } = entity;
}

/// <summary>The request conflicts with current state (duplicate SKU, stale version, ...).</summary>
public sealed class ConflictException(string code, string message) : DomainException(code, message);

/// <summary>The caller is authenticated and has the permission, but this specific operation is not allowed for them.</summary>
public sealed class ForbiddenOperationException(string code, string message) : DomainException(code, message);
