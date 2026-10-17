using AlaraERP.Domain.Catalog;
using AlaraERP.Domain.Sales;

namespace AlaraERP.Application.Sales;

public sealed record RecipientInput(string Name, string Phone, string? City, string? Address);

public sealed record CustomerRequest(
    string Name, string Phone, string? Email, string? City, string? Address, string? Notes, bool IsActive = true);

public sealed record CustomerListQuery(string? Search, bool? IsActive, int Page = 1, int PageSize = 25);

public sealed record CustomerDto(
    Guid Id, string Name, string Phone, string? Email, string? City, string? Address, string? Notes, bool IsActive,
    int OrderCount, decimal TotalSpent, decimal TotalPaid, decimal AverageOrderValue,
    DateTimeOffset? FirstOrderAt, DateTimeOffset? LastOrderAt, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record OrderLineInput(Guid ProductId, int Quantity, decimal LineDiscount = 0, bool IsGift = false);

public sealed record CreateOrderRequest(
    OrderSource Source, Guid WarehouseId, PaymentMethod PaymentMethod, RecipientInput Recipient,
    decimal DiscountTotal, string? Notes,
    IReadOnlyList<OrderLineInput> Items,
    /// <summary>Sell now: deducts stock and counts as a sale.</summary>
    bool Confirm = false,
    Guid? CustomerId = null,
    /// <summary>Reserve instead: holds the stock until this time (no sale, no cost yet). Exactly one of Confirm / ReserveUntil.</summary>
    DateTimeOffset? ReserveUntil = null);

public sealed record ReserveOrderRequest(DateTimeOffset ReservedUntil, string? Notes);

/// <summary>Who is holding stock of a product: reserved (not yet sold) orders.</summary>
public sealed record ReservedStockOrderDto(Guid OrderId, string OrderNumber, string RecipientName, int Quantity, DateTimeOffset? ReservedUntil);

public sealed record ReservedStockDto(Guid ProductId, int ReservedQuantity, IReadOnlyList<ReservedStockOrderDto> Orders);

public sealed record UpdateOrderRequest(
    OrderSource Source, Guid WarehouseId, PaymentMethod PaymentMethod, RecipientInput Recipient,
    decimal DiscountTotal, string? Notes,
    IReadOnlyList<OrderLineInput> Items,
    Guid? CustomerId = null);

public sealed record UpdateRecipientRequest(RecipientInput Recipient, string? Notes);

public sealed record TransitionRequest(string? Notes);

public sealed record OrderListQuery(
    string? Search, OrderStatus? Status, PaymentStatus? PaymentStatus, OrderSource? Source, Guid? CustomerId,
    DateOnly? From, DateOnly? To, string? Sort, int Page = 1, int PageSize = 25,
    /// <summary>Any of these statuses (e.g. Reserved and Confirmed). Combined with <c>Status</c> if both set.</summary>
    OrderStatus[]? Statuses = null);

public sealed record OrderListItemDto(
    Guid Id, string OrderNumber, OrderStatus Status, PaymentStatus PaymentStatus, PaymentMethod PaymentMethod, OrderSource Source,
    string RecipientName, string RecipientPhone, string? RecipientCity, int ItemCount, decimal GrandTotal, string CurrencyCode,
    DateTimeOffset CreatedAt, string? CreatedBy,
    DateTimeOffset? ReservedUntil = null);

public sealed record OrderItemComponentDto(Guid ProductId, string Name, int QuantityPerBundle, int TotalQuantity);

public sealed record OrderItemDto(
    Guid Id, Guid ProductId, string ProductName, ProductType ProductType, bool IsGift, int Quantity,
    decimal UnitPrice, decimal LineDiscount, decimal LineTotal, IReadOnlyList<OrderItemComponentDto> Components);

public sealed record OrderHistoryDto(OrderEventType EventType, string? OldValue, string NewValue, string? Notes, DateTimeOffset ChangedAt, string? ChangedBy);

public sealed record OrderDetailDto(
    Guid Id, string OrderNumber, OrderStatus Status, PaymentStatus PaymentStatus, PaymentMethod PaymentMethod, OrderSource Source,
    Guid WarehouseId, string WarehouseName, Guid? CustomerId,
    string RecipientName, string RecipientPhone, string? RecipientCity, string? RecipientAddress,
    decimal Subtotal, decimal DiscountTotal, decimal GrandTotal, string CurrencyCode,
    string? Notes,
    Guid? CreatedByUserId, string? CreatedBy, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    DateTimeOffset? ConfirmedAt, DateTimeOffset? CancelledAt,
    DateTimeOffset? ReturnedAt, DateTimeOffset? PaidAt,
    IReadOnlyList<OrderStatus> AllowedTransitions,
    IReadOnlyList<OrderItemDto> Items, IReadOnlyList<OrderHistoryDto> History,
    DateTimeOffset? ReservedAt = null, DateTimeOffset? ReservedUntil = null);

