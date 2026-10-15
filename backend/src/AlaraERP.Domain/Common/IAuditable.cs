namespace AlaraERP.Domain.Common;

/// <summary>Entities with creation/update timestamps maintained by the persistence layer.</summary>
public interface IAuditable
{
    DateTimeOffset CreatedAt { get; set; }
    DateTimeOffset UpdatedAt { get; set; }
}
