using AlaraERP.Domain.Inventory;

namespace AlaraERP.Application.Inventory;

public sealed record WarehouseDto(
    Guid Id, string Name, string Code, string? Address, bool IsActive, int UnitsOnHand, int UnitsReserved,
    int ProductsInStock, DateTimeOffset UpdatedAt);

public sealed record WarehouseRequest(string Name, string Code, string? Address, bool IsActive = true);

public sealed record InventorySettingsDto(Guid ProductId, int LowStockThreshold, bool TracksExpiry, bool IsDefault, DateTimeOffset? UpdatedAt);

public sealed record UpdateInventorySettingsRequest(int LowStockThreshold, bool TracksExpiry);

public sealed record InventoryFeatureSettingsDto(bool ExpiryDatesEnabled, DateTimeOffset? UpdatedAt);

public sealed record UpdateExpiryDatesRequest(bool Enabled);

public enum StockStatus { InStock, Low, Out }

public enum StockFilter { All, Low, Out, Expiring, Expired }

public sealed record StockListQuery(
    Guid? WarehouseId, string? Search, Guid? CategoryId, StockFilter? Filter, bool IncludeInactive = false,
    string? Sort = null, int Page = 1, int PageSize = 25);

public sealed record StockRowDto(
    Guid ProductId, string Name, string? CategoryName, bool IsActive,
    int OnHand, int Reserved, int Available, int LowStockThreshold, StockStatus Status,
    DateOnly? NearestExpiry, int ExpiringUnits, int ExpiredUnits, Guid? PrimaryImageId = null);

public sealed record WarehouseStockDto(Guid WarehouseId, string Name, string Code, int OnHand, int Reserved, int Available);

public sealed record BatchStockDto(
    Guid BatchId, string BatchNumber, DateOnly? ExpiryDate, Guid WarehouseId, string WarehouseCode,
    int OnHand, bool IsExpired, int? DaysToExpiry, decimal? UnitCost, DateTimeOffset? ReceivedAt = null);

public sealed record ProductStockDto(
    Guid ProductId, string Name, InventorySettingsDto Settings,
    int OnHand, int Reserved, int Available, StockStatus Status,
    IReadOnlyList<WarehouseStockDto> Warehouses, IReadOnlyList<BatchStockDto> Batches,
    /// <summary>Cost fields are only filled for employees with costs.view.</summary>
    ProductCostDto? Cost, Guid? PrimaryImageId = null);

/// <summary>
/// AverageCost: on-hand units valued at their batch costs (falls back to the standard cost).
/// AverageCost and StockValue are null if any on-hand unit has no cost. LastReceiptCost is the latest invoice cost.
/// </summary>
public sealed record ProductCostDto(decimal? StandardCost, decimal? AverageCost, decimal? LastReceiptCost, DateTimeOffset? LastReceivedAt,
    decimal? StockValue, int UncostedUnits);

public sealed record AvailabilityDto(Guid ProductId, int Available);

public sealed record ReceiveLineInput(Guid ProductId, int Quantity, decimal? UnitCost, string? BatchNumber, DateOnly? ExpiryDate);

public sealed record ReceiveStockRequest(
    Guid WarehouseId, string? SupplierName, string? SupplierReference, DateTimeOffset? ReceivedAt, string? Notes,
    IReadOnlyList<ReceiveLineInput> Lines);

public sealed record ReceiptLineDto(
    Guid ProductId, string ProductName, Guid BatchId, string BatchNumber, DateOnly? ExpiryDate,
    int Quantity, decimal? UnitCost,
    /// <summary>True when no cost was recorded on the receipt line and UnitCost is the stored cost of the stock
    /// (the delivery's batch cost, or the product's saved cost).</summary>
    bool CostFromSaved = false);

public sealed record ReceiptDto(
    Guid Id, string ReceiptNumber, Guid WarehouseId, string WarehouseName, string? SupplierName, string? SupplierReference,
    DateTimeOffset ReceivedAt, string? Notes, string? PerformedBy, DateTimeOffset CreatedAt,
    int TotalUnits, decimal? TotalCost, IReadOnlyList<ReceiptLineDto> Lines);

public sealed record ReceiptSummaryDto(
    Guid Id, string ReceiptNumber, string WarehouseCode, string? SupplierName, string? SupplierReference,
    DateTimeOffset ReceivedAt, int LineCount, int TotalUnits, string? PerformedBy);

public sealed record AdjustStockRequest(
    Guid WarehouseId, Guid ProductId, int QuantityChange, AdjustmentReason Reason, string? Notes,
    Guid? BatchId, string? BatchNumber, DateOnly? ExpiryDate, decimal? UnitCost = null);

/// <summary>
/// Sets the units on hand to what was actually counted: for the whole product in a warehouse, or for one delivery
/// (<paramref name="BatchId"/>). <paramref name="ExpiryDate"/> is only used when the product has no delivery yet.
/// </summary>
public sealed record CorrectCountRequest(
    Guid WarehouseId, Guid ProductId, int CountedQuantity, Guid? BatchId, string? Notes, DateOnly? ExpiryDate = null);

public sealed record WithdrawBundleRequest(Guid WarehouseId, Guid BundleProductId, int Quantity, AdjustmentReason Reason, string? Notes);

public sealed record TransferLineInput(Guid ProductId, int Quantity, Guid? BatchId);

public sealed record TransferStockRequest(Guid FromWarehouseId, Guid ToWarehouseId, string? Notes, IReadOnlyList<TransferLineInput> Lines);

public sealed record TransferLineDto(Guid ProductId, string ProductName, Guid BatchId, string BatchNumber, DateOnly? ExpiryDate, int Quantity);

public sealed record TransferDto(
    Guid Id, string TransferNumber, Guid FromWarehouseId, string FromWarehouse, Guid ToWarehouseId, string ToWarehouse,
    TransferStatus Status, string? Notes, string? PerformedBy, DateTimeOffset CreatedAt, int TotalUnits,
    IReadOnlyList<TransferLineDto> Lines);

public sealed record MovementQuery(
    Guid? WarehouseId, Guid? ProductId, MovementType? Type, DateTimeOffset? From, DateTimeOffset? To,
    Guid? ReferenceId, int Page = 1, int PageSize = 50);

public sealed record MovementDto(
    Guid Id, DateTimeOffset CreatedAt, Guid WarehouseId, string WarehouseCode, Guid ProductId, string ProductName,
    string BatchNumber, DateOnly? ExpiryDate, MovementType Type, int QuantityChange, AdjustmentReason? Reason,
    MovementReference ReferenceType, Guid ReferenceId, string? ReferenceNumber, string? Notes, string? PerformedBy,
    /// <summary>Cost per unit moved; only for employees with costs.view.</summary>
    decimal? UnitCost = null);

public sealed record BoxContentInput(Guid ProductId, int Quantity);

/// <summary><paramref name="InitialCount"/> is only used when the box is created; later changes go through the count actions.</summary>
public sealed record SaveBoxRequest(
    string Name, string? Notes, decimal? Price, int LowStockThreshold, IReadOnlyList<BoxContentInput> Contents, int? InitialCount = null);

/// <summary>Add: boxes delivered. Remove: used, damaged or given away. Count: set the count to what was counted.</summary>
public enum BoxCountAction { Add, Remove, Count }

public sealed record ChangeBoxCountRequest(BoxCountAction Action, int Quantity, string? Notes);

public sealed record SetBoxActiveRequest(bool IsActive);

public sealed record BoxContentDto(Guid ProductId, string ProductName, Domain.Catalog.ProductType ProductType, int Quantity);

public sealed record BoxDto(
    Guid Id, string Name, string? Notes, decimal? Price, int LowStockThreshold, int OnHand, StockStatus Status, bool IsActive,
    IReadOnlyList<BoxContentDto> Contents, DateTimeOffset UpdatedAt);

public sealed record BoxMovementDto(
    Guid Id, BoxMovementType Type, int QuantityChange, int QuantityAfter, string? Notes, string? PerformedBy, DateTimeOffset CreatedAt);
