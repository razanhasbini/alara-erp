namespace AlaraERP.Domain.Common;

/// <summary>
/// A business rule was violated. <see cref="Code"/> is a stable, machine-readable identifier
/// (e.g. "inventory.insufficient_stock") that clients can rely on.
/// </summary>
public class DomainException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
