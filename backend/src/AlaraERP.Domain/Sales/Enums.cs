namespace AlaraERP.Domain.Sales;

/// <summary>
/// Reserved: stock is held for the customer (not sold: no revenue, no cost) until <c>ReservedUntil</c>.
/// Confirmed: sold — stock deducted, counted in sales and paid. Delivery is handled entirely by the delivery company
/// and is not tracked. A sale can be cancelled, or returned when the customer sends it back; both restore the stock.
/// Draft exists only for orders created before reservations.
/// </summary>
public enum OrderStatus { Draft, Confirmed, Cancelled, Returned, Reserved }

public enum OrderSource { WhatsApp, Instagram, Website, Other }

/// <summary>Only <see cref="CashOnDelivery"/> is offered in the UI today; the others are ready once confirmed.</summary>
public enum PaymentMethod { CashOnDelivery, Whish, Omt, BankTransfer, Cash }

/// <summary>
/// A sale is paid the moment it is made: Reserved orders are Unpaid, selling makes them Paid, and moving a sold
/// order back to Reserved makes it Unpaid again. Cancelled or returned orders are Void (the money is taken back out).
/// Delivery costs are handled separately from orders.
/// </summary>
public enum PaymentStatus { Unpaid, Paid, Void }

public enum OrderEventType { Status, Payment, Reservation }
