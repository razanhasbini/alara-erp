using AlaraERP.Domain.Common;

namespace AlaraERP.Domain.Inventory;

public sealed class Warehouse : IAuditable
{
    private Warehouse() { }

    public Warehouse(string name, string code, string? address)
    {
        Id = Guid.CreateVersion7();
        IsActive = true;
        Update(name, code, address);
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = "";
    public string Code { get; private set; } = "";
    public string? Address { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public void Update(string name, string code, string? address)
    {
        Name = name.Trim();
        Code = code.Trim().ToUpperInvariant();
        Address = string.IsNullOrWhiteSpace(address) ? null : address.Trim();
    }

    public void SetActive(bool isActive) => IsActive = isActive;
}

/// <summary>Inventory policy for one product, owned by the Inventory module and set by a manager.</summary>
public sealed class ProductInventorySettings : ITracksUpdates
{
    private ProductInventorySettings() { }

    public ProductInventorySettings(Guid productId, int lowStockThreshold, bool tracksExpiry)
    {
        ProductId = productId;
        Update(lowStockThreshold, tracksExpiry, null);
    }

    public Guid ProductId { get; private set; }
    public int LowStockThreshold { get; private set; }
    public bool TracksExpiry { get; private set; }
    public Guid? UpdatedByUserId { get; private set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public void Update(int lowStockThreshold, bool tracksExpiry, Guid? userId)
    {
        if (lowStockThreshold < 0)
            throw new DomainException("inventory.invalid_threshold", "The low-stock threshold cannot be negative.");
        LowStockThreshold = lowStockThreshold;
        TracksExpiry = tracksExpiry;
        UpdatedByUserId = userId;
    }
}

/// <summary>Global inventory feature switches. This singleton row keeps dormant workflows and their data intact.</summary>
public sealed class InventoryFeatureSettings : ITracksUpdates
{
    public const int SingletonId = 1;

    private InventoryFeatureSettings() { }

    public InventoryFeatureSettings(bool expiryDatesEnabled, Guid? userId)
    {
        Id = SingletonId;
        SetExpiryDates(expiryDatesEnabled, userId);
    }

    public int Id { get; private set; }
    public bool ExpiryDatesEnabled { get; private set; }
    public Guid? UpdatedByUserId { get; private set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public void SetExpiryDates(bool enabled, Guid? userId)
    {
        ExpiryDatesEnabled = enabled;
        UpdatedByUserId = userId;
    }
}

/// <summary>A lot of one product, optionally with an expiry date. Every unit of stock belongs to a batch.</summary>
public sealed class Batch
{
    private Batch() { }

    public Batch(Guid productId, string batchNumber, DateOnly? expiryDate, DateTimeOffset createdAt)
    {
        Id = Guid.CreateVersion7();
        ProductId = productId;
        BatchNumber = batchNumber.Trim();
        ExpiryDate = expiryDate;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }
    public Guid ProductId { get; private set; }
    public string BatchNumber { get; private set; } = "";
    public DateOnly? ExpiryDate { get; private set; }
    /// <summary>Cost of one unit of this batch. Weighted average if the same batch is received more than once.</summary>
    public decimal? UnitCost { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Folds newly received units into the batch's unit cost (weighted by the units already on hand).</summary>
    public void ApplyReceivedCost(int unitsAlreadyOnHand, int unitsReceived, decimal? unitCost)
    {
        if (unitCost is not { } cost) return;
        if (cost < 0) throw new DomainException("validation.negative_amount", "Unit cost cannot be negative.");
        UnitCost = UnitCost is { } current && unitsAlreadyOnHand > 0
            ? Math.Round((current * unitsAlreadyOnHand + cost * unitsReceived) / (unitsAlreadyOnHand + unitsReceived), 4, MidpointRounding.AwayFromZero)
            : Math.Round(cost, 4, MidpointRounding.AwayFromZero);
    }

    public bool IsExpiredOn(DateOnly today) => ExpiryDate is { } expiry && expiry < today;
}
