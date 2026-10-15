using AlaraERP.Domain.Common;

namespace AlaraERP.Domain.Inventory;

/// <summary>
/// Aggregate stock of one product in one warehouse. Row-locked (SELECT … FOR UPDATE) for every change,
/// which also serialises changes to that product's <see cref="BatchBalance"/> rows in the warehouse.
/// </summary>
public sealed class StockBalance : ITracksUpdates
{
    private StockBalance() { }

    public StockBalance(Guid warehouseId, Guid productId)
    {
        WarehouseId = warehouseId;
        ProductId = productId;
    }

    public Guid WarehouseId { get; private set; }
    public Guid ProductId { get; private set; }
    public int QuantityOnHand { get; private set; }
    public int QuantityReserved { get; private set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public int Available => QuantityOnHand - QuantityReserved;

    public void Add(int quantity) => QuantityOnHand += Guard.Positive(quantity, "Quantity");

    /// <summary>Removes unreserved stock (adjustment, transfer out). Reserved units are protected.</summary>
    public void RemoveUnreserved(int quantity, string productLabel)
    {
        Guard.Positive(quantity, "Quantity");
        if (quantity > Available)
            throw new DomainException("inventory.insufficient_available",
                $"Only {Available} unreserved unit(s) of {productLabel} are available in this warehouse.");
        QuantityOnHand -= quantity;
    }

    public void Reserve(int quantity, string productLabel)
    {
        Guard.Positive(quantity, "Quantity");
        if (quantity > Available)
            throw new DomainException("inventory.insufficient_stock",
                $"Not enough stock of {productLabel}: {quantity} needed, {Available} available.");
        QuantityReserved += quantity;
    }

    public void Release(int quantity)
    {
        Guard.Positive(quantity, "Quantity");
        if (quantity > QuantityReserved)
            throw new InvalidOperationException($"Cannot release {quantity}; only {QuantityReserved} reserved.");
        QuantityReserved -= quantity;
    }

    /// <summary>Converts a reservation into a physical deduction (shipment).</summary>
    public void FulfilReserved(int quantity)
    {
        Guard.Positive(quantity, "Quantity");
        if (quantity > QuantityReserved || quantity > QuantityOnHand)
            throw new InvalidOperationException($"Cannot fulfil {quantity}; reserved {QuantityReserved}, on hand {QuantityOnHand}.");
        QuantityReserved -= quantity;
        QuantityOnHand -= quantity;
    }
}

/// <summary>Units of one batch physically present in one warehouse.</summary>
public sealed class BatchBalance : ITracksUpdates
{
    private BatchBalance() { }

    public BatchBalance(Guid warehouseId, Guid batchId)
    {
        WarehouseId = warehouseId;
        BatchId = batchId;
    }

    public Guid WarehouseId { get; private set; }
    public Guid BatchId { get; private set; }
    public Batch? Batch { get; private set; }
    public int QuantityOnHand { get; private set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public void Add(int quantity) => QuantityOnHand += Guard.Positive(quantity, "Quantity");

    public void Remove(int quantity, string batchLabel)
    {
        Guard.Positive(quantity, "Quantity");
        if (quantity > QuantityOnHand)
            throw new DomainException("inventory.insufficient_batch_stock",
                $"Batch {batchLabel} has only {QuantityOnHand} unit(s) in this warehouse.");
        QuantityOnHand -= quantity;
    }
}

public enum ReservationStatus { Active, Fulfilled, Released }

/// <summary>Stock held for one order. Product-level; batches are chosen at shipment (FEFO).</summary>
public sealed class StockReservation
{
    private StockReservation() { }

    public StockReservation(Guid orderId, Guid productId, Guid warehouseId, int quantity, DateTimeOffset createdAt)
    {
        Id = Guid.CreateVersion7();
        OrderId = orderId;
        ProductId = productId;
        WarehouseId = warehouseId;
        Quantity = Guard.Positive(quantity, "Reserved quantity");
        Status = ReservationStatus.Active;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }
    public Guid OrderId { get; private set; }
    public Guid ProductId { get; private set; }
    public Guid WarehouseId { get; private set; }
    public int Quantity { get; private set; }
    public ReservationStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ClosedAt { get; private set; }

    public void Close(ReservationStatus status, DateTimeOffset at)
    {
        if (Status != ReservationStatus.Active)
            throw new InvalidOperationException($"Reservation {Id} is already {Status}.");
        if (status == ReservationStatus.Active)
            throw new ArgumentOutOfRangeException(nameof(status));
        Status = status;
        ClosedAt = at;
    }
}
