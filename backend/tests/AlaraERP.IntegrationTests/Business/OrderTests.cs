using System.Net;
using AlaraERP.Application.Catalog;
using AlaraERP.Application.Common;
using AlaraERP.Application.Common.Security;
using AlaraERP.Application.Inventory;
using AlaraERP.Application.Sales;
using AlaraERP.Domain.Inventory;
using AlaraERP.Domain.Sales;
using AlaraERP.IntegrationTests.Infrastructure;

namespace AlaraERP.IntegrationTests.Business;

public sealed class OrderTests(AlaraApiFactory factory) : ApiTestBase(factory)
{
    private record Shop(ErpClient Client, Scenario S, WarehouseDto Warehouse, ProductDetailDto Shampoo, ProductDetailDto Mask, ProductDetailDto Duo);

    /// <summary>Shampoo $15 ×20, Mask $24 ×10, Deep Repair Duo (shampoo + mask) $33.</summary>
    private async Task<Shop> OpenShopAsync()
    {
        var (client, s) = await ManagerAsync();
        var warehouse = await s.WarehouseAsync();
        var shampoo = await s.ProductAsync("ALR-HC-SHAM-OH", 15m, name: "Intensive Repair Shampoo – Oh Honey");
        var mask = await s.ProductAsync("ALR-HC-MASK", 24m, name: "Miracle Mask");
        await s.ReceiveAsync(warehouse.Id, shampoo.Id, 20);
        await s.ReceiveAsync(warehouse.Id, mask.Id, 10);
        var duo = await s.BundleAsync("ALR-SET-DUO", 33m, (shampoo.Id, 1), (mask.Id, 1));
        return new Shop(client, s, warehouse, shampoo, mask, duo);
    }

    [Fact]
    public async Task Totals_include_line_discounts_order_discount_and_default_delivery_fee()
    {
        var shop = await OpenShopAsync();

        var response = await shop.Client.PostAsync("/api/orders", new CreateOrderRequest(
            OrderSource.Instagram, shop.Warehouse.Id, PaymentMethod.CashOnDelivery,
            new RecipientInput("Nour Haddad", "+961 3 111 222", "Saida", null), null, 5m, null, "Gift wrap",
            [new OrderLineInput(shop.Shampoo.Id, 2, 1.50m), new OrderLineInput(shop.Duo.Id, 1)]));

        var order = await response.OkJsonAsync<OrderDetailDto>();
        Assert.StartsWith("AL-", order.OrderNumber);
        Assert.Equal(OrderStatus.Draft, order.Status);
        Assert.Equal(61.50m, order.Subtotal);   // 2×15 − 1.50 + 33
        Assert.Equal(5m, order.DiscountTotal);
        Assert.Equal(4.00m, order.ShippingTotal); // configured default
        Assert.Equal(60.50m, order.GrandTotal);
        Assert.Equal("USD", order.CurrencyCode);
        var bundleLine = order.Items.Single(i => i.ProductId == shop.Duo.Id);
        Assert.Equal(2, bundleLine.Components.Count);
        Assert.Equal(0, (await shop.S.StockAsync(shop.Shampoo.Id)).Reserved); // drafts reserve nothing
    }

    [Fact]
    public async Task Discounts_larger_than_the_goods_are_rejected()
    {
        var shop = await OpenShopAsync();

        var response = await shop.S.CreateOrderAsync(shop.Warehouse.Id, confirm: false, discount: 100m, lines: (shop.Mask.Id, 1));

        Assert.Equal("orders.discount_exceeds_subtotal", await response.ProblemCodeAsync());
    }

    [Fact]
    public async Task Confirming_reserves_individual_and_bundle_component_stock()
    {
        var shop = await OpenShopAsync();

        var order = await shop.S.OrderAsync(shop.Warehouse.Id, confirm: true, (shop.Shampoo.Id, 2), (shop.Duo.Id, 3));

        Assert.Equal(OrderStatus.Confirmed, order.Status);
        var shampoo = await shop.S.StockAsync(shop.Shampoo.Id);
        var mask = await shop.S.StockAsync(shop.Mask.Id);
        Assert.Equal((20, 5, 15), (shampoo.OnHand, shampoo.Reserved, shampoo.Available));
        Assert.Equal((10, 3, 7), (mask.OnHand, mask.Reserved, mask.Available));
        Assert.Equal(7, await shop.S.AvailableAsync(shop.Duo.Id));
    }

    [Fact]
    public async Task Insufficient_stock_blocks_confirmation_and_reserves_nothing()
    {
        var shop = await OpenShopAsync();
        var draft = await shop.S.OrderAsync(shop.Warehouse.Id, confirm: false, (shop.Shampoo.Id, 5), (shop.Mask.Id, 11));

        var response = await shop.Client.PostAsync($"/api/orders/{draft.Id}/confirm", new TransitionRequest(null));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("inventory.insufficient_stock", await response.ProblemCodeAsync());
        Assert.Equal(OrderStatus.Draft, (await shop.Client.GetJsonAsync<OrderDetailDto>($"/api/orders/{draft.Id}")).Status);
        Assert.Equal(0, (await shop.S.StockAsync(shop.Shampoo.Id)).Reserved); // shampoo line was rolled back too
    }

    [Fact]
    public async Task Order_keeps_historical_prices_names_and_bundle_contents()
    {
        var shop = await OpenShopAsync();
        var order = await shop.S.OrderAsync(shop.Warehouse.Id, confirm: true, (shop.Duo.Id, 1));

        // Later the catalogue changes: new price, new name, and the bundle gains a component.
        (await shop.Client.PutAsync($"/api/products/{shop.Duo.Id}", new UpdateProductRequest("Deep Repair Duo (new)", "ALR-SET-DUO",
            null, null, null, null, null, null, null, null, null, null, 99m, null))).EnsureSuccessStatusCode();
        var extra = await shop.S.ProductAsync("ALR-HC-LEAVEIN", 17m);
        await shop.S.ReceiveAsync(shop.Warehouse.Id, extra.Id, 5);
        (await shop.Client.PutAsync($"/api/products/{shop.Duo.Id}/bundle-items", new ReplaceBundleItemsRequest(
            [new BundleItemInput(shop.Shampoo.Id, 1), new BundleItemInput(shop.Mask.Id, 1), new BundleItemInput(extra.Id, 1)])))
            .EnsureSuccessStatusCode();

        var after = await shop.Client.GetJsonAsync<OrderDetailDto>($"/api/orders/{order.Id}");
        var line = after.Items.Single();
        Assert.Equal(("Bundle ALR-SET-DUO", 33m, 2), (line.ProductName, line.UnitPrice, line.Components.Count));

        // Shipping uses the snapshot: the leave-in is untouched.
        (await shop.Client.PostAsync($"/api/orders/{order.Id}/ship", new ShipOrderRequest(null, "TRK-1", null, null))).EnsureSuccessStatusCode();
        Assert.Equal(5, (await shop.S.StockAsync(extra.Id)).OnHand);
        Assert.Equal(19, (await shop.S.StockAsync(shop.Shampoo.Id)).OnHand);
    }

    [Fact]
    public async Task Shipping_deducts_stock_exactly_once_from_the_earliest_expiring_batch()
    {
        var shop = await OpenShopAsync();
        var soon = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(2);
        await shop.S.ReceiveAsync(shop.Warehouse.Id, shop.Mask.Id, 2, soon, "SOON");
        var order = await shop.S.OrderAsync(shop.Warehouse.Id, confirm: true, (shop.Mask.Id, 3));

        var shipped = await (await shop.Client.PostAsync($"/api/orders/{order.Id}/ship", new ShipOrderRequest(null, null, null, null)))
            .OkJsonAsync<OrderDetailDto>();
        var again = await shop.Client.PostAsync($"/api/orders/{order.Id}/ship", new ShipOrderRequest(null, null, null, null));
        var delivered = await shop.Client.PostAsync($"/api/orders/{order.Id}/deliver", new TransitionRequest(null));

        Assert.Equal(OrderStatus.Shipped, shipped.Status);
        Assert.Equal("orders.invalid_transition", await again.ProblemCodeAsync());
        delivered.EnsureSuccessStatusCode();
        var mask = await shop.S.StockAsync(shop.Mask.Id);
        Assert.Equal((9, 0), (mask.OnHand, mask.Reserved)); // 12 − 3, once
        var shipments = (await shop.Client.GetJsonAsync<PagedResult<MovementDto>>($"/api/inventory/movements?referenceId={order.Id}")).Items;
        Assert.Equal([("SOON", -2), (shipments.Single(m => m.BatchNumber != "SOON").BatchNumber, -1)],
            shipments.OrderBy(m => m.QuantityChange).Select(m => (m.BatchNumber, m.QuantityChange)));
    }

    [Fact]
    public async Task Expired_batches_are_never_shipped()
    {
        var shop = await OpenShopAsync();
        var maskBatch = (await shop.S.StockAsync(shop.Mask.Id)).Batches.Single();
        var order = await shop.S.OrderAsync(shop.Warehouse.Id, confirm: true, (shop.Mask.Id, 1));
        await shop.S.SetBatchExpiryAsync(maskBatch.BatchId, new DateOnly(2020, 1, 1));

        var response = await shop.Client.PostAsync($"/api/orders/{order.Id}/ship", new ShipOrderRequest(null, null, null, null));

        Assert.Equal("inventory.only_expired_stock", await response.ProblemCodeAsync());
        Assert.Equal(10, (await shop.S.StockAsync(shop.Mask.Id)).OnHand);
    }

    [Fact]
    public async Task Cancelling_releases_only_what_was_reserved()
    {
        var shop = await OpenShopAsync();
        var draft = await shop.S.OrderAsync(shop.Warehouse.Id, confirm: false, (shop.Mask.Id, 2));
        var confirmed = await shop.S.OrderAsync(shop.Warehouse.Id, confirm: true, (shop.Mask.Id, 4));

        (await shop.Client.PostAsync($"/api/orders/{draft.Id}/cancel", new TransitionRequest("Customer changed mind"))).EnsureSuccessStatusCode();
        Assert.Equal(4, (await shop.S.StockAsync(shop.Mask.Id)).Reserved);
        var cancelled = await (await shop.Client.PostAsync($"/api/orders/{confirmed.Id}/cancel", new TransitionRequest(null)))
            .OkJsonAsync<OrderDetailDto>();

        var mask = await shop.S.StockAsync(shop.Mask.Id);
        Assert.Equal((10, 0), (mask.OnHand, mask.Reserved));
        Assert.Equal(PaymentStatus.Void, cancelled.PaymentStatus);
        Assert.Equal(0, await shop.S.CountAsync("SELECT count(*) FROM inventory.stock_movements WHERE type = 'Shipment'"));
    }

    [Fact]
    public async Task Shipped_orders_cannot_be_cancelled_but_a_refused_delivery_returns_stock_to_its_batch()
    {
        var shop = await OpenShopAsync();
        var order = await shop.S.OrderAsync(shop.Warehouse.Id, confirm: true, (shop.Duo.Id, 2));
        (await shop.Client.PostAsync($"/api/orders/{order.Id}/ship", new ShipOrderRequest(null, null, null, null))).EnsureSuccessStatusCode();

        var cancel = await shop.Client.PostAsync($"/api/orders/{order.Id}/cancel", new TransitionRequest(null));
        var returned = await (await shop.Client.PostAsync($"/api/orders/{order.Id}/return", new TransitionRequest("Customer refused")))
            .OkJsonAsync<OrderDetailDto>();

        Assert.Equal("orders.invalid_transition", await cancel.ProblemCodeAsync());
        Assert.Equal((OrderStatus.Returned, PaymentStatus.Void), (returned.Status, returned.PaymentStatus));
        Assert.Equal((20, 0), ((await shop.S.StockAsync(shop.Shampoo.Id)).OnHand, (await shop.S.StockAsync(shop.Shampoo.Id)).Reserved));
        Assert.Equal(10, (await shop.S.StockAsync(shop.Mask.Id)).Batches.Single().OnHand);
    }

    [Fact]
    public async Task Reopening_a_confirmed_order_releases_stock_and_allows_editing()
    {
        var shop = await OpenShopAsync();
        var order = await shop.S.OrderAsync(shop.Warehouse.Id, confirm: true, (shop.Mask.Id, 4));

        var editWhileConfirmed = await shop.Client.PutAsync($"/api/orders/{order.Id}", Update(shop, (shop.Mask.Id, 1)));
        (await shop.Client.PostAsync($"/api/orders/{order.Id}/reopen", new TransitionRequest(null))).EnsureSuccessStatusCode();
        var edited = await (await shop.Client.PutAsync($"/api/orders/{order.Id}", Update(shop, (shop.Mask.Id, 1))))
            .OkJsonAsync<OrderDetailDto>();

        Assert.Equal("orders.not_editable", await editWhileConfirmed.ProblemCodeAsync());
        Assert.Equal((OrderStatus.Draft, 28.00m), (edited.Status, edited.GrandTotal));
        Assert.Equal(0, (await shop.S.StockAsync(shop.Mask.Id)).Reserved);
    }

    [Fact]
    public async Task Cash_on_delivery_flows_from_courier_to_remittance()
    {
        var shop = await OpenShopAsync();
        var courier = await (await shop.Client.PostAsync("/api/delivery-companies", new DeliveryCompanyRequest("Fast Lebanon", "+961 1 000 000")))
            .OkJsonAsync<DeliveryCompanyDto>();
        var a = await (await shop.S.CreateOrderAsync(shop.Warehouse.Id, true, deliveryCompanyId: courier.Id, lines: (shop.Mask.Id, 1))).OkJsonAsync<OrderDetailDto>();
        var b = await (await shop.S.CreateOrderAsync(shop.Warehouse.Id, true, deliveryCompanyId: courier.Id, lines: (shop.Shampoo.Id, 2))).OkJsonAsync<OrderDetailDto>();
        foreach (var o in new[] { a, b })
        {
            (await shop.Client.PostAsync($"/api/orders/{o.Id}/ship", new ShipOrderRequest(null, $"WB-{o.OrderNumber}", 2.5m, null))).EnsureSuccessStatusCode();
            var delivered = await (await shop.Client.PostAsync($"/api/orders/{o.Id}/deliver", new TransitionRequest(null))).OkJsonAsync<OrderDetailDto>();
            Assert.Equal(PaymentStatus.CollectedByCourier, delivered.PaymentStatus);
        }
        var awaiting = await shop.Client.GetJsonAsync<List<RemittanceOrderDto>>($"/api/delivery-companies/{courier.Id}/awaiting-remittance");
        Assert.Equal(2, awaiting.Count);

        var remittance = await (await shop.Client.PostAsync("/api/courier-remittances",
            new CreateRemittanceRequest(courier.Id, [a.Id, b.Id], null, "Weekly hand-over"))).OkJsonAsync<RemittanceDto>();
        var twice = await shop.Client.PostAsync("/api/courier-remittances", new CreateRemittanceRequest(courier.Id, [a.Id], null, null));

        Assert.Equal((62.00m, 5.00m, 57.00m, 2), (remittance.AmountCollected, remittance.CourierFees, remittance.NetAmount, remittance.OrderCount));
        Assert.Equal("remittances.order_not_collectable", await twice.ProblemCodeAsync());
        var paid = await shop.Client.GetJsonAsync<OrderDetailDto>($"/api/orders/{a.Id}");
        Assert.Equal((PaymentStatus.Paid, remittance.RemittanceNumber), (paid.PaymentStatus, paid.CourierRemittanceNumber));
    }

    [Fact]
    public async Task Delivery_by_own_staff_marks_cash_on_delivery_paid_immediately()
    {
        var shop = await OpenShopAsync();
        var order = await shop.S.OrderAsync(shop.Warehouse.Id, confirm: true, (shop.Mask.Id, 1));
        (await shop.Client.PostAsync($"/api/orders/{order.Id}/ship", new ShipOrderRequest(null, null, null, null))).EnsureSuccessStatusCode();

        var delivered = await (await shop.Client.PostAsync($"/api/orders/{order.Id}/deliver", new TransitionRequest(null))).OkJsonAsync<OrderDetailDto>();

        Assert.Equal(PaymentStatus.Paid, delivered.PaymentStatus);
        Assert.NotNull(delivered.PaidAt);
        Assert.Equal(["Draft", "Confirmed", "Shipped", "Delivered"],
            delivered.History.Where(h => h.EventType == OrderEventType.Status).Select(h => h.NewValue));
        Assert.All(delivered.History, h => Assert.NotNull(h.ChangedBy));
    }

    [Fact]
    public async Task Concurrent_orders_cannot_oversell_the_last_units()
    {
        var shop = await OpenShopAsync();
        var drafts = new List<OrderDetailDto>();
        for (var i = 0; i < 6; i++)
            drafts.Add(await shop.S.OrderAsync(shop.Warehouse.Id, confirm: false, (shop.Mask.Id, 4))); // 6 × 4 wanted, 10 in stock

        var clients = new List<ErpClient>();
        for (var i = 0; i < drafts.Count; i++) clients.Add(await SignedInAsAsync(SystemRoles.SalesEmployee));
        var results = await Task.WhenAll(drafts.Select((d, i) => clients[i].PostAsync($"/api/orders/{d.Id}/confirm", new TransitionRequest(null))));

        Assert.Equal(2, results.Count(r => r.IsSuccessStatusCode));
        Assert.All(results.Where(r => !r.IsSuccessStatusCode), r => Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode));
        var mask = await shop.S.StockAsync(shop.Mask.Id);
        Assert.Equal((10, 8, 2), (mask.OnHand, mask.Reserved, mask.Available));
        Assert.Equal(2, await shop.S.CountAsync("SELECT count(*) FROM inventory.stock_reservations WHERE status = 'Active'"));
    }

    [Fact]
    public async Task Confirming_the_same_order_twice_at_once_reserves_once()
    {
        var shop = await OpenShopAsync();
        var draft = await shop.S.OrderAsync(shop.Warehouse.Id, confirm: false, (shop.Mask.Id, 3));
        using var other = await SignedInAsAsync(SystemRoles.SalesEmployee);

        var results = await Task.WhenAll(
            shop.Client.PostAsync($"/api/orders/{draft.Id}/confirm", new TransitionRequest(null)),
            other.PostAsync($"/api/orders/{draft.Id}/confirm", new TransitionRequest(null)));

        Assert.Single(results, r => r.IsSuccessStatusCode);
        Assert.Equal(3, (await shop.S.StockAsync(shop.Mask.Id)).Reserved);
    }

    [Fact]
    public async Task Search_and_filters_find_orders_by_phone_number_and_status()
    {
        var shop = await OpenShopAsync();
        await shop.S.OrderAsync(shop.Warehouse.Id, confirm: true, (shop.Mask.Id, 1));
        await shop.S.OrderAsync(shop.Warehouse.Id, confirm: false, (shop.Mask.Id, 1));

        var byPhone = await shop.Client.GetJsonAsync<PagedResult<OrderListItemDto>>("/api/orders?search=70123456");
        var confirmed = await shop.Client.GetJsonAsync<PagedResult<OrderListItemDto>>("/api/orders?status=Confirmed");

        var open = await shop.Client.GetJsonAsync<PagedResult<OrderListItemDto>>("/api/orders?statuses=Draft&statuses=Confirmed&statuses=Shipped");

        Assert.Equal(2, byPhone.TotalCount);
        Assert.Single(confirmed.Items);
        Assert.Equal(2, open.TotalCount);
        Assert.All(byPhone.Items, o => Assert.NotNull(o.CreatedBy));
    }

    [Fact]
    public async Task Role_boundaries_for_orders_are_enforced_by_the_api()
    {
        var shop = await OpenShopAsync();
        var order = await shop.S.OrderAsync(shop.Warehouse.Id, confirm: false, (shop.Mask.Id, 1));
        using var stockKeeper = await SignedInAsAsync(SystemRoles.InventoryManager);
        using var viewer = await SignedInAsAsync(SystemRoles.Viewer);
        using var sales = await SignedInAsAsync(SystemRoles.SalesEmployee);

        Assert.Equal(HttpStatusCode.Forbidden, (await stockKeeper.GetAsync("/api/orders")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsync($"/api/orders/{order.Id}/confirm", new TransitionRequest(null))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await sales.PostAsync($"/api/orders/{order.Id}/mark-paid", new TransitionRequest(null))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await sales.PostAsync($"/api/orders/{order.Id}/confirm", new TransitionRequest(null))).StatusCode);
    }

    private static UpdateOrderRequest Update(Shop shop, params (Guid ProductId, int Quantity)[] lines) => new(
        OrderSource.WhatsApp, shop.Warehouse.Id, PaymentMethod.CashOnDelivery,
        new RecipientInput("Rima Saad", "+961 70 123 456", "Beirut", null), null, 0m, 4m, null,
        lines.Select(l => new OrderLineInput(l.ProductId, l.Quantity)).ToList());
}
