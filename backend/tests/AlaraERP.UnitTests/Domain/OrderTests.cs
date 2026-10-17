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
        order.ReplaceLines([Line(order, 15m, 2), Line(order, 24m, 1, 4m)], 5m, order.WarehouseId, order.Source, method);
        return order;
    }

    private static OrderItem Line(Order order, decimal price, int qty, decimal discount = 0) =>
        new(order.Id, Guid.NewGuid(), "Item", ProductType.Individual, qty, price, discount, []);

    [Fact]
    public void Totals_are_computed_from_lines_and_discount()
    {
        var order = NewOrder();
        Assert.Equal((50m, 5m, 45m), (order.Subtotal, order.DiscountTotal, order.GrandTotal));
        Assert.Equal("+96170123456", order.RecipientPhone);
    }

    [Fact]
    public void Gift_lines_are_free_but_remain_part_of_a_regular_order()
    {
        var order = new Order("AL-GIFT", OrderSource.Other, Guid.NewGuid(),
            new OrderRecipient("Diana", "70123456", null, null), PaymentMethod.CashOnDelivery,
            "USD", null, Now);
        var gift = new OrderItem(order.Id, Guid.NewGuid(), "Gift mask", ProductType.Individual, 2, 24m, 3m, [], isGift: true);
        order.ReplaceLines([Line(order, 10m, 1), gift], 0, order.WarehouseId, order.Source, order.PaymentMethod);

        Assert.True(gift.IsGift);
        Assert.Equal((0m, 0m, 0m), (gift.UnitPrice, gift.LineDiscount, gift.LineTotal));
        Assert.Equal((10m, PaymentStatus.Unpaid), (order.GrandTotal, order.PaymentStatus));
    }

    [Theory]
    [InlineData(OrderStatus.Draft, OrderStatus.Returned)]
    [InlineData(OrderStatus.Reserved, OrderStatus.Returned)]
    [InlineData(OrderStatus.Cancelled, OrderStatus.Confirmed)]
    [InlineData(OrderStatus.Returned, OrderStatus.Confirmed)]
    public void Skipping_steps_is_not_allowed(OrderStatus from, OrderStatus to)
    {
        var order = NewOrder();
        switch (from)
        {
            case OrderStatus.Reserved: order.Reserve(Now.AddDays(1), null, Now); break;
            case OrderStatus.Confirmed: order.TransitionTo(OrderStatus.Confirmed, null, Now); break;
            case OrderStatus.Cancelled: order.TransitionTo(OrderStatus.Cancelled, null, Now); break;
            case OrderStatus.Returned:
                order.TransitionTo(OrderStatus.Confirmed, null, Now);
                order.TransitionTo(OrderStatus.Returned, null, Now);
                break;
        }

        var ex = Assert.Throws<DomainException>(() => order.TransitionTo(to, null, Now));
        Assert.Equal("orders.invalid_transition", ex.Code);
    }

    [Fact]
    public void Final_states_allow_nothing_further()
    {
        foreach (var final in new[] { OrderStatus.Cancelled, OrderStatus.Returned })
            Assert.All(Enum.GetValues<OrderStatus>(), s => Assert.False(Order.CanTransition(final, s)));
    }

    [Fact]
    public void Selling_pays_the_order_at_once_and_undoing_or_cancelling_the_sale_takes_the_money_back()
    {
        var sold = NewOrder();
        sold.TransitionTo(OrderStatus.Confirmed, null, Now);
        var paidWhenSold = (sold.PaymentStatus, sold.PaidAt);

        var returned = NewOrder();
        returned.TransitionTo(OrderStatus.Confirmed, null, Now);
        returned.TransitionTo(OrderStatus.Returned, null, Now.AddDays(2));

        var backToReserved = NewOrder();
        backToReserved.TransitionTo(OrderStatus.Confirmed, null, Now);
        backToReserved.Reserve(Now.AddDays(3), null, Now);

        var cancelledSale = NewOrder();
        cancelledSale.TransitionTo(OrderStatus.Confirmed, null, Now);
        cancelledSale.TransitionTo(OrderStatus.Cancelled, null, Now);

        Assert.Equal((PaymentStatus.Paid, (DateTimeOffset?)Now), paidWhenSold);
        Assert.Equal((PaymentStatus.Void, (DateTimeOffset?)null), (returned.PaymentStatus, returned.PaidAt));
        Assert.Equal((PaymentStatus.Unpaid, (DateTimeOffset?)null), (backToReserved.PaymentStatus, backToReserved.PaidAt));
        Assert.Equal((PaymentStatus.Void, (DateTimeOffset?)null), (cancelledSale.PaymentStatus, cancelledSale.PaidAt));
    }

    [Fact]
    public void Lines_can_only_change_on_drafts_and_discounts_cannot_exceed_goods()
    {
        var order = NewOrder();
        Assert.Equal("orders.discount_exceeds_subtotal", Assert.Throws<DomainException>(() =>
            order.ReplaceLines([Line(order, 10m, 1)], 11m, order.WarehouseId, order.Source, order.PaymentMethod)).Code);

        order.TransitionTo(OrderStatus.Confirmed, null, Now);
        Assert.Equal("orders.not_editable", Assert.Throws<DomainException>(() =>
            order.ReplaceLines([Line(order, 10m, 1)], 0m, order.WarehouseId, order.Source, order.PaymentMethod)).Code);
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
            new OrderItem(order.Id, shampoo, "Shampoo", ProductType.Individual, 2, 15m, 0, []),
            new OrderItem(order.Id, Guid.NewGuid(), "Duo", ProductType.Bundle, 3, 33m, 0,
                [(shampoo, "Shampoo", 1, 15m), (mask, "Mask", 2, 24m)]),
        ], 0, order.WarehouseId, order.Source, order.PaymentMethod);

        var needs = order.StockRequirements();

        Assert.Equal(5, needs[shampoo]);
        Assert.Equal(6, needs[mask]);
        Assert.Equal(2, needs.Count);
    }

    [Fact]
    public void Bundle_revenue_is_split_across_components_by_full_price_and_adds_up_exactly()
    {
        var order = new Order("AL-3", OrderSource.Website, Guid.NewGuid(), new OrderRecipient("A", "123456", null, null),
            PaymentMethod.CashOnDelivery, "USD", null, Now);
        // Full Treatment Set: $75 for components worth 15+15+17+24+36 = $107.
        var set = new OrderItem(order.Id, Guid.NewGuid(), "Full Treatment Set", ProductType.Bundle, 2, 75m, 0,
        [
            (Guid.NewGuid(), "Shampoo", 1, 15m), (Guid.NewGuid(), "Conditioner", 1, 15m), (Guid.NewGuid(), "Leave-in", 1, 17m),
            (Guid.NewGuid(), "Mask", 1, 24m), (Guid.NewGuid(), "Pro-Edition", 1, 36m),
        ]);

        Assert.Equal(150m, set.Components.Sum(c => c.AllocatedRevenue));
        Assert.Equal(Math.Round(150m * 36 / 107, 2), set.Components.Single(c => c.ComponentName == "Pro-Edition").AllocatedRevenue);
    }
}
