namespace AlaraERP.Domain.Inventory;

/// <summary>
/// Marks that a product's "low on stock" alert has been raised, so it is raised once per drop rather than on every
/// check. Removed when the product is restocked above its alert level, so the next drop alerts again.
/// </summary>
public sealed class LowStockAlert
{
    private LowStockAlert() { }

    public LowStockAlert(Guid productId, int available, int threshold, DateTimeOffset alertedAt)
    {
        ProductId = productId;
        Available = available;
        Threshold = threshold;
        AlertedAt = alertedAt;
    }

    public Guid ProductId { get; private set; }
    /// <summary>Units in stock when the alert was raised.</summary>
    public int Available { get; private set; }
    public int Threshold { get; private set; }
    public DateTimeOffset AlertedAt { get; private set; }
}
