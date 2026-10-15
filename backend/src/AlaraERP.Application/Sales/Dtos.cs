using AlaraERP.Domain.Catalog;
using AlaraERP.Domain.Sales;

namespace AlaraERP.Application.Sales;

public sealed record RecipientInput(string Name, string Phone, string? City, string? Address);

public sealed record OrderLineInput(Guid ProductId, int Quantity, decimal LineDiscount = 0);

public sealed record CreateOrderRequest(
    OrderSource Source, Guid WarehouseId, PaymentMethod PaymentMethod, RecipientInput Recipient,
    Guid? DeliveryCompanyId, decimal DiscountTotal, decimal? ShippingTotal, string? Notes,
    IReadOnlyList<OrderLineInput> Items,
    /// <summary>Confirm (and reserve stock) immediately instead of saving a draft.</summary>
    bool Confirm = false);

public sealed record UpdateOrderRequest(
    OrderSource Source, Guid WarehouseId, PaymentMethod PaymentMethod, RecipientInput Recipient,
    Guid? DeliveryCompanyId, decimal DiscountTotal, decimal ShippingTotal, string? Notes,
    IReadOnlyList<OrderLineInput> Items);

public sealed record UpdateDeliveryRequest(RecipientInput Recipient, Guid? DeliveryCompanyId, string? TrackingNumber, decimal? CourierFee, string? Notes);

public sealed record TransitionRequest(string? Notes);

public sealed record ShipOrderRequest(Guid? DeliveryCompanyId, string? TrackingNumber, decimal? CourierFee, string? Notes);

public sealed record OrderListQuery(
    string? Search, OrderStatus? Status, PaymentStatus? PaymentStatus, OrderSource? Source, Guid? DeliveryCompanyId,
    DateOnly? From, DateOnly? To, string? Sort, int Page = 1, int PageSize = 25,
    /// <summary>Any of these statuses (e.g. the "open" view: Draft, Confirmed, Shipped). Combined with <c>Status</c> if both set.</summary>
    OrderStatus[]? Statuses = null);

public sealed record OrderListItemDto(
    Guid Id, string OrderNumber, OrderStatus Status, PaymentStatus PaymentStatus, PaymentMethod PaymentMethod, OrderSource Source,
    string RecipientName, string RecipientPhone, string? RecipientCity, int ItemCount, decimal GrandTotal, string CurrencyCode,
    string? DeliveryCompanyName, string? TrackingNumber, DateTimeOffset CreatedAt, string? CreatedBy);

public sealed record OrderItemComponentDto(Guid ProductId, string Name, string Sku, int QuantityPerBundle, int TotalQuantity);

public sealed record OrderItemDto(
    Guid Id, Guid ProductId, string ProductName, string Sku, ProductType ProductType, int Quantity,
    decimal UnitPrice, decimal LineDiscount, decimal LineTotal, IReadOnlyList<OrderItemComponentDto> Components);

public sealed record OrderHistoryDto(OrderEventType EventType, string? OldValue, string NewValue, string? Notes, DateTimeOffset ChangedAt, string? ChangedBy);

public sealed record OrderDetailDto(
    Guid Id, string OrderNumber, OrderStatus Status, PaymentStatus PaymentStatus, PaymentMethod PaymentMethod, OrderSource Source,
    Guid WarehouseId, string WarehouseName,
    string RecipientName, string RecipientPhone, string? RecipientCity, string? RecipientAddress,
    Guid? DeliveryCompanyId, string? DeliveryCompanyName, string? TrackingNumber,
    decimal Subtotal, decimal DiscountTotal, decimal ShippingTotal, decimal? CourierFee, decimal GrandTotal, string CurrencyCode,
    string? CourierRemittanceNumber, string? Notes,
    Guid? CreatedByUserId, string? CreatedBy, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    DateTimeOffset? ConfirmedAt, DateTimeOffset? ShippedAt, DateTimeOffset? DeliveredAt, DateTimeOffset? CancelledAt,
    DateTimeOffset? ReturnedAt, DateTimeOffset? PaidAt,
    IReadOnlyList<OrderStatus> AllowedTransitions,
    IReadOnlyList<OrderItemDto> Items, IReadOnlyList<OrderHistoryDto> History);

public sealed record DeliveryCompanyDto(
    Guid Id, string Name, string? Phone, bool IsActive, int AwaitingRemittanceOrders, decimal AwaitingRemittanceAmount,
    decimal AwaitingRemittanceFees);

public sealed record DeliveryCompanyRequest(string Name, string? Phone, bool IsActive = true);

public sealed record CreateRemittanceRequest(Guid DeliveryCompanyId, IReadOnlyList<Guid> OrderIds, DateTimeOffset? ReceivedAt, string? Notes);

public sealed record RemittanceOrderDto(Guid Id, string OrderNumber, string RecipientName, DateTimeOffset? DeliveredAt, decimal GrandTotal, decimal? CourierFee);

public sealed record RemittanceDto(
    Guid Id, string RemittanceNumber, Guid DeliveryCompanyId, string DeliveryCompanyName,
    decimal AmountCollected, decimal CourierFees, decimal NetAmount, int OrderCount,
    DateTimeOffset ReceivedAt, string? Notes, string? RecordedBy, DateTimeOffset CreatedAt,
    IReadOnlyList<RemittanceOrderDto> Orders);
