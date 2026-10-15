namespace AlaraERP.Domain.Common;

/// <summary>Entities whose <see cref="UpdatedAt"/> is maintained by the persistence layer.</summary>
public interface ITracksUpdates
{
    DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Entities with creation/update timestamps maintained by the persistence layer.</summary>
public interface IAuditable : ITracksUpdates
{
    DateTimeOffset CreatedAt { get; set; }
}
