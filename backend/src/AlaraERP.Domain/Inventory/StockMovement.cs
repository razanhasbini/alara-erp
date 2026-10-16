using AlaraERP.Domain.Common;

namespace AlaraERP.Domain.Inventory;

public enum MovementType { Receipt, Shipment, Return, AdjustmentIncrease, AdjustmentDecrease, TransferOut, TransferIn }

public enum AdjustmentReason { Damaged, Expired, CountCorrection, Lost, SampleOrGift, Other }

public enum MovementReference { Receipt, Transfer, Order, Adjustment }

/// <summary>Immutable ledger entry. Every change to on-hand stock writes exactly one movement per batch touched.</summary>
public sealed class StockMovement
{
    private StockMovement() { }

    public StockMovement(
        Guid warehouseId, Guid productId, Guid batchId, MovementType type, int quantityChange,
        MovementReference referenceType, Guid referenceId, string? referenceNumber, Guid? performedByUserId, DateTimeOffset createdAt,
        AdjustmentReason? reason = null, string? notes = null, decimal? unitCost = null)
    {
        if (quantityChange == 0)
            throw new DomainException("inventory.zero_movement", "A stock movement cannot have a zero quantity.");
        var mustBePositive = type is MovementType.Receipt or MovementType.Return or MovementType.AdjustmentIncrease or MovementType.TransferIn;
        if (mustBePositive != quantityChange > 0)
            throw new InvalidOperationException($"{type} movements must be {(mustBePositive ? "positive" : "negative")}.");

        Id = Guid.CreateVersion7();
        WarehouseId = warehouseId;
        ProductId = productId;
        BatchId = batchId;
        Type = type;
        QuantityChange = quantityChange;
        ReferenceType = referenceType;
        ReferenceId = referenceId;
        ReferenceNumber = referenceNumber;
        PerformedByUserId = performedByUserId;
        CreatedAt = createdAt;
        Reason = reason;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        UnitCost = unitCost;
    }

    public Guid Id { get; private set; }
    public Guid WarehouseId { get; private set; }
    public Guid ProductId { get; private set; }
    public Guid BatchId { get; private set; }
    public MovementType Type { get; private set; }
    public int QuantityChange { get; private set; }
    /// <summary>Cost per unit of the units moved (from their batch) — the basis of cost of goods sold.</summary>
    public decimal? UnitCost { get; private set; }
    public AdjustmentReason? Reason { get; private set; }
    public MovementReference ReferenceType { get; private set; }
    public Guid ReferenceId { get; private set; }
    /// <summary>Human-readable number of the source document (receipt, transfer or order), denormalised for the ledger.</summary>
    public string? ReferenceNumber { get; private set; }
    public string? Notes { get; private set; }
    public Guid? PerformedByUserId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}

public sealed class StockReceipt
{
    private readonly List<StockReceiptLine> _lines = [];

    private StockReceipt() { }

    public StockReceipt(string receiptNumber, Guid warehouseId, string? supplierName, string? supplierReference,
        DateTimeOffset receivedAt, string? notes, Guid? performedByUserId, DateTimeOffset createdAt)
    {
        Id = Guid.CreateVersion7();
        ReceiptNumber = receiptNumber;
        WarehouseId = warehouseId;
        SupplierName = Clean(supplierName);
        SupplierReference = Clean(supplierReference);
        ReceivedAt = receivedAt;
        Notes = Clean(notes);
        PerformedByUserId = performedByUserId;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }
    public string ReceiptNumber { get; private set; } = "";
    public Guid WarehouseId { get; private set; }
    public string? SupplierName { get; private set; }
    public string? SupplierReference { get; private set; }
    public DateTimeOffset ReceivedAt { get; private set; }
    public string? Notes { get; private set; }
    public Guid? PerformedByUserId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public IReadOnlyList<StockReceiptLine> Lines => _lines;

    public StockReceiptLine AddLine(Guid productId, Guid batchId, int quantity, decimal? unitCost)
    {
        if (unitCost is < 0)
            throw new DomainException("validation.negative_amount", "Unit cost cannot be negative.");
        var line = new StockReceiptLine(Id, productId, batchId, Guard.Positive(quantity, "Received quantity"),
            unitCost is { } c ? Math.Round(c, 4, MidpointRounding.AwayFromZero) : null);
        _lines.Add(line);
        return line;
    }

    private static string? Clean(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
}

public sealed class StockReceiptLine
{
    private StockReceiptLine() { }

    internal StockReceiptLine(Guid receiptId, Guid productId, Guid batchId, int quantity, decimal? unitCost)
    {
        Id = Guid.CreateVersion7();
        ReceiptId = receiptId;
        ProductId = productId;
        BatchId = batchId;
        Quantity = quantity;
        UnitCost = unitCost;
    }

    public Guid Id { get; private set; }
    public Guid ReceiptId { get; private set; }
    public Guid ProductId { get; private set; }
    public Guid BatchId { get; private set; }
    public int Quantity { get; private set; }
    public decimal? UnitCost { get; private set; }
}

public enum TransferStatus { Completed }

public sealed class StockTransfer
{
    private readonly List<StockTransferLine> _lines = [];

    private StockTransfer() { }

    public StockTransfer(string transferNumber, Guid fromWarehouseId, Guid toWarehouseId, string? notes,
        Guid? performedByUserId, DateTimeOffset createdAt)
    {
        if (fromWarehouseId == toWarehouseId)
            throw new DomainException("transfers.same_warehouse", "Source and destination warehouses must differ.");
        Id = Guid.CreateVersion7();
        TransferNumber = transferNumber;
        FromWarehouseId = fromWarehouseId;
        ToWarehouseId = toWarehouseId;
        Status = TransferStatus.Completed;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        PerformedByUserId = performedByUserId;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }
    public string TransferNumber { get; private set; } = "";
    public Guid FromWarehouseId { get; private set; }
    public Guid ToWarehouseId { get; private set; }
    public TransferStatus Status { get; private set; }
    public string? Notes { get; private set; }
    public Guid? PerformedByUserId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public IReadOnlyList<StockTransferLine> Lines => _lines;

    public void AddLine(Guid productId, Guid batchId, int quantity) =>
        _lines.Add(new StockTransferLine(Id, productId, batchId, Guard.Positive(quantity, "Transferred quantity")));
}

public sealed class StockTransferLine
{
    private StockTransferLine() { }

    internal StockTransferLine(Guid transferId, Guid productId, Guid batchId, int quantity)
    {
        Id = Guid.CreateVersion7();
        TransferId = transferId;
        ProductId = productId;
        BatchId = batchId;
        Quantity = quantity;
    }

    public Guid Id { get; private set; }
    public Guid TransferId { get; private set; }
    public Guid ProductId { get; private set; }
    public Guid BatchId { get; private set; }
    public int Quantity { get; private set; }
}
