using AlaraERP.Domain.Inventory;

namespace AlaraERP.Application.Inventory;

public sealed record WarehouseDto(
    Guid Id, string Name, string Code, string? Address, bool IsActive, int UnitsOnHand, int UnitsReserved,
    int ProductsInStock, DateTimeOffset UpdatedAt);

public sealed record WarehouseRequest(string Name, string Code, string? Address, bool IsActive = true);

public sealed record InventorySettingsDto(Guid ProductId, int LowStockThreshold, bool TracksExpiry, bool IsDefault, DateTimeOffset? UpdatedAt);

public sealed record UpdateInventorySettingsRequest(int LowStockThreshold, bool TracksExpiry);

public enum StockStatus { InStock, Low, Out }

public enum StockFilter { All, Low, Out, Expiring, Expired }

public sealed record StockListQuery(
    Guid? WarehouseId, string? Search, Guid? CategoryId, StockFilter? Filter, bool IncludeInactive = false,
    string? Sort = null, int Page = 1, int PageSize = 25);

public sealed record StockRowDto(
    Guid ProductId, string Name, string Sku, string? CategoryName, bool IsActive,
    int OnHand, int Reserved, int Available, int LowStockThreshold, StockStatus Status,
    DateOnly? NearestExpiry, int ExpiringUnits, int ExpiredUnits);

public sealed record WarehouseStockDto(Guid WarehouseId, string Name, string Code, int OnHand, int Reserved, int Available);

public sealed record BatchStockDto(
    Guid BatchId, string BatchNumber, DateOnly? ExpiryDate, Guid WarehouseId, string WarehouseCode,
    int OnHand, bool IsExpired, int? DaysToExpiry);

public sealed record ProductStockDto(
    Guid ProductId, string Name, string Sku, InventorySettingsDto Settings,
    int OnHand, int Reserved, int Available, StockStatus Status,
    IReadOnlyList<WarehouseStockDto> Warehouses, IReadOnlyList<BatchStockDto> Batches);

public sealed record AvailabilityDto(Guid ProductId, int Available);

public sealed record ReceiveLineInput(Guid ProductId, int Quantity, decimal? UnitCost, string? BatchNumber, DateOnly? ExpiryDate);

public sealed record ReceiveStockRequest(
    Guid WarehouseId, string? SupplierName, string? SupplierReference, DateTimeOffset? ReceivedAt, string? Notes,
    IReadOnlyList<ReceiveLineInput> Lines);

public sealed record ReceiptLineDto(
    Guid ProductId, string ProductName, string Sku, Guid BatchId, string BatchNumber, DateOnly? ExpiryDate,
    int Quantity, decimal? UnitCost);

public sealed record ReceiptDto(
    Guid Id, string ReceiptNumber, Guid WarehouseId, string WarehouseName, string? SupplierName, string? SupplierReference,
    DateTimeOffset ReceivedAt, string? Notes, string? PerformedBy, DateTimeOffset CreatedAt,
    int TotalUnits, decimal? TotalCost, IReadOnlyList<ReceiptLineDto> Lines);

public sealed record ReceiptSummaryDto(
    Guid Id, string ReceiptNumber, string WarehouseCode, string? SupplierName, string? SupplierReference,
    DateTimeOffset ReceivedAt, int LineCount, int TotalUnits, string? PerformedBy);

public sealed record AdjustStockRequest(
    Guid WarehouseId, Guid ProductId, int QuantityChange, AdjustmentReason Reason, string? Notes,
    Guid? BatchId, string? BatchNumber, DateOnly? ExpiryDate);

public sealed record TransferLineInput(Guid ProductId, int Quantity, Guid? BatchId);

public sealed record TransferStockRequest(Guid FromWarehouseId, Guid ToWarehouseId, string? Notes, IReadOnlyList<TransferLineInput> Lines);

public sealed record TransferLineDto(Guid ProductId, string ProductName, string Sku, Guid BatchId, string BatchNumber, DateOnly? ExpiryDate, int Quantity);

public sealed record TransferDto(
    Guid Id, string TransferNumber, Guid FromWarehouseId, string FromWarehouse, Guid ToWarehouseId, string ToWarehouse,
    TransferStatus Status, string? Notes, string? PerformedBy, DateTimeOffset CreatedAt, int TotalUnits,
    IReadOnlyList<TransferLineDto> Lines);

public sealed record MovementQuery(
    Guid? WarehouseId, Guid? ProductId, MovementType? Type, DateTimeOffset? From, DateTimeOffset? To,
    Guid? ReferenceId, int Page = 1, int PageSize = 50);

public sealed record MovementDto(
    Guid Id, DateTimeOffset CreatedAt, Guid WarehouseId, string WarehouseCode, Guid ProductId, string ProductName, string Sku,
    string BatchNumber, DateOnly? ExpiryDate, MovementType Type, int QuantityChange, AdjustmentReason? Reason,
    MovementReference ReferenceType, Guid ReferenceId, string? ReferenceNumber, string? Notes, string? PerformedBy);
