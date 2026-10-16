namespace AlaraERP.Domain.Sales;

/// <summary>
/// Reserved: stock is held for the customer (not sold: no revenue, no cost) until <c>ReservedUntil</c>.
/// Confirmed: sold — stock deducted, counted in sales. Draft exists only for orders created before reservations.
/// </summary>
public enum OrderStatus { Draft, Confirmed, Shipped, Delivered, Cancelled, Returned, Reserved }

public enum OrderSource { WhatsApp, Instagram, Website, Other }

/// <summary>Only <see cref="CashOnDelivery"/> is offered in the UI today; the others are ready once confirmed.</summary>
public enum PaymentMethod { CashOnDelivery, Whish, Omt, BankTransfer, Cash }

/// <summary>
/// COD: Unpaid → CollectedByCourier (on delivery) → Paid (courier remits the cash).
/// Other methods: Unpaid → Paid. Cancelled/returned unpaid orders become Void.
/// </summary>
public enum PaymentStatus { Unpaid, CollectedByCourier, Paid, Void }

public enum OrderEventType { Status, Payment, Reservation }
