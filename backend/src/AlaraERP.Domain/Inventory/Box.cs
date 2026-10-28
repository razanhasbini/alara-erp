using AlaraERP.Domain.Common;

namespace AlaraERP.Domain.Inventory;

/// <summary>
/// A packaging box (e.g. the Curls On shampoo box, the 4-mists box, the PR box), counted on its own.
/// Its contents say which products it is made for. Selling does not take boxes out: staff record boxes received,
/// used and counted themselves, and are warned when a box runs low.
/// </summary>
public sealed class Box : IAuditable
{
    public const int DefaultLowStockThreshold = 500;

    private readonly List<BoxContent> _contents = [];

    private Box() { }

    public Box(string name, string? notes, decimal? price, int lowStockThreshold)
    {
        Id = Guid.CreateVersion7();
        IsActive = true;
        Update(name, notes, price, lowStockThreshold);
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = "";
    public string? Notes { get; private set; }
    /// <summary>What a customer pays for the box when they choose to buy it (e.g. the PR box). Informational only.</summary>
    public decimal? Price { get; private set; }
    public int LowStockThreshold { get; private set; }
    public int QuantityOnHand { get; private set; }
    public bool IsActive { get; private set; }
    /// <summary>Set when the "low on boxes" alert was raised; cleared once the count is back above the alert level.</summary>
    public DateTimeOffset? LowStockAlertedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    /// <summary>Optimistic concurrency token (PostgreSQL xmin).</summary>
    public uint Version { get; private set; }

    public IReadOnlyList<BoxContent> Contents => _contents;

    public bool IsLow => QuantityOnHand <= LowStockThreshold;

    public void Update(string name, string? notes, decimal? price, int lowStockThreshold)
    {
        Name = string.IsNullOrWhiteSpace(name) ? throw new DomainException("validation.required", "Box name is required.") : name.Trim();
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        Price = price is { } p ? Guard.Money(Guard.NotNegative(p, "Box price")) : null;
        LowStockThreshold = lowStockThreshold >= 0 ? lowStockThreshold
            : throw new DomainException("validation.negative", "The alert level cannot be negative.");
    }

    public void ReplaceContents(IEnumerable<(Guid ProductId, int Quantity)> contents)
    {
        var list = contents.ToList();
        if (list.GroupBy(c => c.ProductId).Any(g => g.Count() > 1))
            throw new DomainException("boxes.duplicate_content", "A product is listed twice in this box. Combine the lines.");
        _contents.Clear();
        _contents.AddRange(list.Select(c => new BoxContent(Id, c.ProductId, Guard.Positive(c.Quantity, "Quantity in the box"))));
    }

    public void SetActive(bool isActive) => IsActive = isActive;

    /// <summary>Changes the count and returns the history entry to store.</summary>
    public BoxMovement Add(int quantity, string? notes, Guid? userId, DateTimeOffset at)
    {
        QuantityOnHand += Guard.Positive(quantity, "Boxes added");
        return new BoxMovement(Id, BoxMovementType.Added, quantity, QuantityOnHand, notes, userId, at);
    }

    public BoxMovement Remove(int quantity, string? notes, Guid? userId, DateTimeOffset at)
    {
        Guard.Positive(quantity, "Boxes removed");
        if (quantity > QuantityOnHand)
            throw new DomainException("boxes.insufficient", $"Only {QuantityOnHand} {Name} left.");
        QuantityOnHand -= quantity;
        return new BoxMovement(Id, BoxMovementType.Removed, -quantity, QuantityOnHand, notes, userId, at);
    }

    /// <summary>Sets the count to what was counted (a miscount fix).</summary>
    public BoxMovement SetCount(int counted, string? notes, Guid? userId, DateTimeOffset at)
    {
        if (counted < 0) throw new DomainException("validation.negative", "The count cannot be negative.");
        if (counted == QuantityOnHand)
            throw new DomainException("boxes.count_unchanged", $"{Name} already shows {counted}. Nothing to correct.");
        var change = counted - QuantityOnHand;
        QuantityOnHand = counted;
        return new BoxMovement(Id, BoxMovementType.Counted, change, QuantityOnHand, notes, userId, at);
    }

    public void MarkLowStockAlerted(DateTimeOffset at) => LowStockAlertedAt = at;
    public void ClearLowStockAlert() => LowStockAlertedAt = null;
}

/// <summary>One product (or bundle) a box is made for, and how many of it go in.</summary>
public sealed class BoxContent
{
    private BoxContent() { }

    internal BoxContent(Guid boxId, Guid productId, int quantity)
    {
        Id = Guid.CreateVersion7();
        BoxId = boxId;
        ProductId = productId;
        Quantity = quantity;
    }

    public Guid Id { get; private set; }
    public Guid BoxId { get; private set; }
    public Guid ProductId { get; private set; }
    public int Quantity { get; private set; }
}

/// <summary>Added: boxes delivered. Removed: used, damaged or given away. Counted: a miscount fixed.</summary>
public enum BoxMovementType { Added, Removed, Counted }

/// <summary>Append-only history of every change to a box count.</summary>
public sealed class BoxMovement
{
    private BoxMovement() { }

    internal BoxMovement(Guid boxId, BoxMovementType type, int quantityChange, int quantityAfter, string? notes,
        Guid? performedByUserId, DateTimeOffset createdAt)
    {
        Id = Guid.CreateVersion7();
        BoxId = boxId;
        Type = type;
        QuantityChange = quantityChange;
        QuantityAfter = quantityAfter;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        PerformedByUserId = performedByUserId;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }
    public Guid BoxId { get; private set; }
    public BoxMovementType Type { get; private set; }
    public int QuantityChange { get; private set; }
    public int QuantityAfter { get; private set; }
    public string? Notes { get; private set; }
    public Guid? PerformedByUserId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
