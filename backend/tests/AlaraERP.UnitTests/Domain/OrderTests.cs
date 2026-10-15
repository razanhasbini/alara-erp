using AlaraERP.Domain.Catalog;
using AlaraERP.Domain.Common;
using AlaraERP.Domain.Sales;

namespace AlaraERP.UnitTests.Domain;

public sealed class OrderTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 10, 0, 0, TimeSpan.Zero);

    private static Order NewOrder(PaymentMethod method = PaymentMethod.CashOnDelivery)
    {
        var order = new Order("AL-000001", OrderSource.WhatsApp, Guid.NewGuid(),
            new OrderRecipient("Rima", "+961 70 123 456", "Beirut", null), method, "USD", null, Now);
        order.ReplaceLines([Line(order, 15m, 2), Line(order, 24m, 1, 4m)], 5m, 4m, order.WarehouseId, order.Source, method);
        return order;
    }

    private static OrderItem Line(Order order, decimal price, int qty, decimal discount = 0) =>
        new(order.Id, Guid.NewGuid(), "Item", "SKU", ProductType.Individual, qty, price, discount, []);

    [Fact]
    public void Totals_are_computed_from_lines_discount_and_delivery()
    {
        var order = NewOrder();
        Assert.Equal((50m, 5m, 4m, 49m), (order.Subtotal, order.DiscountTotal, order.ShippingTotal, order.GrandTotal));
        Assert.Equal("+96170123456", order.RecipientPhone);
    }

    [Theory]
    [InlineData(OrderStatus.Draft, OrderStatus.Shipped)]
    [InlineData(OrderStatus.Draft, OrderStatus.Delivered)]
    [InlineData(OrderStatus.Confirmed, OrderStatus.Delivered)]
    [InlineData(OrderStatus.Confirmed, OrderStatus.Returned)]
    public void Skipping_steps_is_not_allowed(OrderStatus from, OrderStatus to)
    {
        var order = NewOrder();
        if (from == OrderStatus.Confirmed) order.TransitionTo(OrderStatus.Confirmed, null, Now);

        var ex = Assert.Throws<DomainException>(() => order.TransitionTo(to, null, Now));
        Assert.Equal("orders.invalid_transition", ex.Code);
    }

    [Fact]
    public void Final_states_allow_nothing_further()
    {
        foreach (var final in new[] { OrderStatus.Delivered, OrderStatus.Cancelled, OrderStatus.Returned })
            Assert.All(Enum.GetValues<OrderStatus>(), s => Assert.False(Order.CanTransition(final, s)));
    }

    [Fact]
    public void Cod_delivered_by_a_courier_awaits_remittance_and_cancelled_orders_are_void()
    {
        var delivered = NewOrder();
        delivered.SetDelivery(Guid.NewGuid(), "WB-1", 2m);
        delivered.TransitionTo(OrderStatus.Confirmed, null, Now);
        delivered.TransitionTo(OrderStatus.Shipped, null, Now);
        delivered.TransitionTo(OrderStatus.Delivered, null, Now);

        var cancelled = NewOrder();
        cancelled.TransitionTo(OrderStatus.Cancelled, null, Now);

        Assert.Equal(PaymentStatus.CollectedByCourier, delivered.PaymentStatus);
        Assert.Equal(PaymentStatus.Void, cancelled.PaymentStatus);
    }

    [Fact]
    public void Paid_orders_cannot_be_cancelled_and_cod_cannot_be_marked_paid_directly()
    {
        var prepaid = NewOrder(PaymentMethod.Whish);
        prepaid.MarkPaid(null, Now, "Whish transfer");
        var cod = NewOrder();

        Assert.Equal("orders.paid_cannot_cancel", Assert.Throws<DomainException>(() => prepaid.TransitionTo(OrderStatus.Cancelled, null, Now)).Code);
        Assert.Equal("orders.cod_paid_via_remittance", Assert.Throws<DomainException>(() => cod.MarkPaid(null, Now, null)).Code);
    }

    [Fact]
    public void Lines_can_only_change_on_drafts_and_discounts_cannot_exceed_goods()
    {
        var order = NewOrder();
        Assert.Equal("orders.discount_exceeds_subtotal", Assert.Throws<DomainException>(() =>
            order.ReplaceLines([Line(order, 10m, 1)], 11m, 0m, order.WarehouseId, order.Source, order.PaymentMethod)).Code);

        order.TransitionTo(OrderStatus.Confirmed, null, Now);
        Assert.Equal("orders.not_editable", Assert.Throws<DomainException>(() =>
            order.ReplaceLines([Line(order, 10m, 1)], 0m, 0m, order.WarehouseId, order.Source, order.PaymentMethod)).Code);
    }

    [Fact]
    public void Stock_requirements_expand_bundles_and_merge_shared_components()
    {
        var shampoo = Guid.NewGuid();
        var mask = Guid.NewGuid();
        var order = new Order("AL-2", OrderSource.Website, Guid.NewGuid(), new OrderRecipient("A", "123456", null, null),
            PaymentMethod.CashOnDelivery, "USD", null, Now);
        order.ReplaceLines(
        [
            new OrderItem(order.Id, shampoo, "Shampoo", "S", ProductType.Individual, 2, 15m, 0, []),
            new OrderItem(order.Id, Guid.NewGuid(), "Duo", "D", ProductType.Bundle, 3, 33m, 0,
                [(shampoo, "Shampoo", "S", 1), (mask, "Mask", "M", 2)]),
        ], 0, 0, order.WarehouseId, order.Source, order.PaymentMethod);

        var needs = order.StockRequirements();

        Assert.Equal(5, needs[shampoo]);
        Assert.Equal(6, needs[mask]);
        Assert.Equal(2, needs.Count);
    }
}
