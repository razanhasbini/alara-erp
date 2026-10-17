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
    public async Task Totals_include_line_discounts_and_order_discount_with_no_delivery_fee()
    {
        var shop = await OpenShopAsync();

        var response = await shop.Client.PostAsync("/api/orders", new CreateOrderRequest(
            OrderSource.Instagram, shop.Warehouse.Id, PaymentMethod.CashOnDelivery,
            new RecipientInput("Nour Haddad", "+961 3 111 222", "Saida", null), 5m, "Gift wrap",
            [new OrderLineInput(shop.Shampoo.Id, 2, 1.50m), new OrderLineInput(shop.Duo.Id, 1)], ReserveUntil: Scenario.Tomorrow));

        var order = await response.OkJsonAsync<OrderDetailDto>();
        Assert.StartsWith("AL-", order.OrderNumber);
        Assert.Equal(OrderStatus.Reserved, order.Status);
        Assert.Equal(61.50m, order.Subtotal);   // 2×15 − 1.50 + 33
        Assert.Equal(5m, order.DiscountTotal);
        Assert.Equal(56.50m, order.GrandTotal); // items − discount; delivery is the courier's business
        Assert.Equal("USD", order.CurrencyCode);
        var bundleLine = order.Items.Single(i => i.ProductId == shop.Duo.Id);
        Assert.Equal(2, bundleLine.Components.Count);
        var shampoo = await shop.S.StockAsync(shop.Shampoo.Id);
        Assert.Equal((20, 3), (shampoo.OnHand, shampoo.Reserved)); // held (2 + 1 in the duo), not deducted
    }

    [Fact]
    public async Task Discounts_larger_than_the_goods_are_rejected()
    {
        var shop = await OpenShopAsync();

        var response = await shop.S.CreateOrderAsync(shop.Warehouse.Id, confirm: false, discount: 100m, lines: (shop.Mask.Id, 1));

        Assert.Equal("orders.discount_exceeds_subtotal", await response.ProblemCodeAsync());
    }

    [Fact]
    public async Task Confirming_immediately_deducts_individual_and_bundle_component_stock()
    {
        var shop = await OpenShopAsync();

        var order = await shop.S.OrderAsync(shop.Warehouse.Id, confirm: true, (shop.Shampoo.Id, 2), (shop.Duo.Id, 3));

        Assert.Equal(OrderStatus.Confirmed, order.Status);
        var shampoo = await shop.S.StockAsync(shop.Shampoo.Id);
        var mask = await shop.S.StockAsync(shop.Mask.Id);
        Assert.Equal((15, 0, 15), (shampoo.OnHand, shampoo.Reserved, shampoo.Available));
        Assert.Equal((7, 0, 7), (mask.OnHand, mask.Reserved, mask.Available));
        Assert.Equal(7, await shop.S.AvailableAsync(shop.Duo.Id));
    }

    [Fact]
    public async Task Confirmed_order_can_include_free_gifts_and_deducts_all_stock()
    {
        var shop = await OpenShopAsync();

        var response = await shop.Client.PostAsync("/api/orders", new CreateOrderRequest(
            OrderSource.Other, shop.Warehouse.Id, PaymentMethod.CashOnDelivery,
            new RecipientInput("Gift Recipient", "+961 70 444 555", "Beirut", null),
            5m, "Includes a complimentary gift",
            [new OrderLineInput(shop.Shampoo.Id, 1), new OrderLineInput(shop.Shampoo.Id, 2, IsGift: true), new OrderLineInput(shop.Duo.Id, 1, IsGift: true)],
            Confirm: true));

        var order = await response.OkJsonAsync<OrderDetailDto>();
        Assert.Equal((OrderStatus.Confirmed, PaymentStatus.Paid), (order.Status, order.PaymentStatus));
        Assert.Equal((15m, 5m, 10m), (order.Subtotal, order.DiscountTotal, order.GrandTotal));
        Assert.All(order.Items.Where(line => line.IsGift), line => Assert.Equal((0m, 0m, 0m), (line.UnitPrice, line.LineDiscount, line.LineTotal)));
        Assert.Equal(16, (await shop.S.StockAsync(shop.Shampoo.Id)).OnHand);
        Assert.Equal(9, (await shop.S.StockAsync(shop.Mask.Id)).OnHand);
    }

    [Fact]
    public async Task Reserving_more_than_is_available_is_refused_and_holds_nothing()
    {
        var shop = await OpenShopAsync();

        var response = await shop.S.CreateOrderAsync(shop.Warehouse.Id, confirm: false, lines: [(shop.Shampoo.Id, 5), (shop.Mask.Id, 11)]);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("inventory.insufficient_stock", await response.ProblemCodeAsync());
        Assert.Equal(0, (await shop.S.StockAsync(shop.Shampoo.Id)).Reserved); // the shampoo line was rolled back too
        Assert.Equal(0, await shop.S.CountAsync("SELECT count(*) FROM sales.orders"));
    }

    [Fact]
    public async Task Order_keeps_historical_prices_names_and_bundle_contents()
    {
        var shop = await OpenShopAsync();
        var order = await shop.S.OrderAsync(shop.Warehouse.Id, confirm: true, (shop.Duo.Id, 1));

        // Later the catalogue changes: new price, new name, and the bundle gains a component.
        (await shop.Client.PutAsync($"/api/products/{shop.Duo.Id}", new UpdateProductRequest("Deep Repair Duo (new)",
            null, null, null, null, null, null, null, null, null, 99m, null))).EnsureSuccessStatusCode();
        var extra = await shop.S.ProductAsync("ALR-HC-LEAVEIN", 17m);
        await shop.S.ReceiveAsync(shop.Warehouse.Id, extra.Id, 5);
        (await shop.Client.PutAsync($"/api/products/{shop.Duo.Id}/bundle-items", new ReplaceBundleItemsRequest(
            [new BundleItemInput(shop.Shampoo.Id, 1), new BundleItemInput(shop.Mask.Id, 1), new BundleItemInput(extra.Id, 1)])))
            .EnsureSuccessStatusCode();

        var after = await shop.Client.GetJsonAsync<OrderDetailDto>($"/api/orders/{order.Id}");
        var line = after.Items.Single();
        Assert.Equal(("Bundle ALR-SET-DUO", 33m, 2), (line.ProductName, line.UnitPrice, line.Components.Count));

        // Selling used the snapshot: the leave-in is untouched.
        Assert.Equal(5, (await shop.S.StockAsync(extra.Id)).OnHand);
        Assert.Equal(19, (await shop.S.StockAsync(shop.Shampoo.Id)).OnHand);
    }

    [Fact]
    public async Task Selling_deducts_stock_once_from_the_earliest_expiring_batch()
    {
        var shop = await OpenShopAsync();
        var soon = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(2);
        await shop.S.ReceiveAsync(shop.Warehouse.Id, shop.Mask.Id, 2, soon, "SOON");
        var order = await shop.S.OrderAsync(shop.Warehouse.Id, confirm: true, (shop.Mask.Id, 3));
        Assert.Equal((9, 0), ((await shop.S.StockAsync(shop.Mask.Id)).OnHand, (await shop.S.StockAsync(shop.Mask.Id)).Reserved));

        var again = await shop.Client.PostAsync($"/api/orders/{order.Id}/confirm", new TransitionRequest(null));

        Assert.Equal("orders.invalid_transition", await again.ProblemCodeAsync());
        var mask = await shop.S.StockAsync(shop.Mask.Id);
        Assert.Equal((9, 0), (mask.OnHand, mask.Reserved)); // 12 − 3, once
        var shipments = (await shop.Client.GetJsonAsync<PagedResult<MovementDto>>($"/api/inventory/movements?referenceId={order.Id}")).Items;
        Assert.Equal([("SOON", -2), (shipments.Single(m => m.BatchNumber != "SOON").BatchNumber, -1)],
            shipments.OrderBy(m => m.QuantityChange).Select(m => (m.BatchNumber, m.QuantityChange)));
    }

    [Fact]
    public async Task Expired_batches_block_confirmation()
    {
        var shop = await OpenShopAsync();
        var maskBatch = (await shop.S.StockAsync(shop.Mask.Id)).Batches.Single();
        await shop.S.SetBatchExpiryAsync(maskBatch.BatchId, new DateOnly(2020, 1, 1));
        var order = await shop.S.OrderAsync(shop.Warehouse.Id, confirm: false, (shop.Mask.Id, 1));

        var response = await shop.Client.PostAsync($"/api/orders/{order.Id}/confirm", new TransitionRequest(null));

        Assert.Equal("inventory.only_expired_stock", await response.ProblemCodeAsync());
        Assert.Equal(10, (await shop.S.StockAsync(shop.Mask.Id)).OnHand);
    }

    [Fact]
    public async Task Cancelling_a_confirmed_order_returns_the_deducted_stock()
    {
        var shop = await OpenShopAsync();
        var draft = await shop.S.OrderAsync(shop.Warehouse.Id, confirm: false, (shop.Mask.Id, 2));
        var confirmed = await shop.S.OrderAsync(shop.Warehouse.Id, confirm: true, (shop.Mask.Id, 4));

        (await shop.Client.PostAsync($"/api/orders/{draft.Id}/cancel", new TransitionRequest("Customer changed mind"))).EnsureSuccessStatusCode();
        Assert.Equal((6, 0), ((await shop.S.StockAsync(shop.Mask.Id)).OnHand, (await shop.S.StockAsync(shop.Mask.Id)).Reserved));
        var cancelled = await (await shop.Client.PostAsync($"/api/orders/{confirmed.Id}/cancel", new TransitionRequest(null)))
            .OkJsonAsync<OrderDetailDto>();

        var mask = await shop.S.StockAsync(shop.Mask.Id);
        Assert.Equal((10, 0), (mask.OnHand, mask.Reserved));
        Assert.Equal(PaymentStatus.Void, cancelled.PaymentStatus);
        Assert.Equal(1, await shop.S.CountAsync("SELECT count(*) FROM inventory.stock_movements WHERE type = 'Shipment'"));
        Assert.Equal(1, await shop.S.CountAsync("SELECT count(*) FROM inventory.stock_movements WHERE type = 'Return'"));
    }

    [Fact]
    public async Task A_returned_bundle_puts_its_products_back_into_their_batches_and_cannot_be_cancelled_after()
    {
        var shop = await OpenShopAsync();
        var order = await shop.S.OrderAsync(shop.Warehouse.Id, confirm: true, (shop.Duo.Id, 2));

        var returned = await (await shop.Client.PostAsync($"/api/orders/{order.Id}/return", new TransitionRequest("Customer refused")))
            .OkJsonAsync<OrderDetailDto>();
        var cancel = await shop.Client.PostAsync($"/api/orders/{order.Id}/cancel", new TransitionRequest(null));

        Assert.Equal("orders.invalid_transition", await cancel.ProblemCodeAsync());
        Assert.Equal((OrderStatus.Returned, PaymentStatus.Void), (returned.Status, returned.PaymentStatus));
        Assert.Equal((20, 0), ((await shop.S.StockAsync(shop.Shampoo.Id)).OnHand, (await shop.S.StockAsync(shop.Shampoo.Id)).Reserved));
        Assert.Equal(10, (await shop.S.StockAsync(shop.Mask.Id)).Batches.Single().OnHand);
    }

    [Fact]
    public async Task A_sold_order_can_be_edited_and_its_stock_and_profit_follow_the_new_items()
    {
        var shop = await OpenShopAsync();
        var order = await shop.S.OrderAsync(shop.Warehouse.Id, confirm: true, (shop.Mask.Id, 4));

        var edited = await (await shop.Client.PutAsync($"/api/orders/{order.Id}", Update(shop, (shop.Mask.Id, 1), (shop.Shampoo.Id, 2))))
            .OkJsonAsync<OrderDetailDto>();
        var mask = await shop.S.StockAsync(shop.Mask.Id);
        var shampoo = await shop.S.StockAsync(shop.Shampoo.Id);
        using var admin = await SignedInAsAsync(SystemRoles.SuperAdmin); // costs are admin-only
        var analytics = await admin.GetJsonAsync<AlaraERP.Application.Analytics.AnalyticsSummaryDto>("/api/analytics/summary");
        (await shop.Client.PostAsync($"/api/orders/{order.Id}/cancel", new TransitionRequest(null))).EnsureSuccessStatusCode();

        Assert.Equal((OrderStatus.Confirmed, PaymentStatus.Paid, 54m), (edited.Status, edited.PaymentStatus, edited.GrandTotal)); // 24 + 2 × 15
        Assert.Equal(order.ConfirmedAt, edited.ConfirmedAt);                 // still sold on the original day
        Assert.Equal((9, 0), (mask.OnHand, mask.Reserved));                  // 3 masks back on the shelf
        Assert.Equal((18, 0), (shampoo.OnHand, shampoo.Reserved));
        Assert.Equal((1, 3, 54m), (analytics.Totals.Orders, analytics.Totals.UnitsSold, analytics.Totals.NetSales));
        Assert.Equal(4m * 1 + 4m * 2, analytics.Totals.CostOfGoods);        // the new items' cost ($4 each), not the old ones
        Assert.Equal(10, (await shop.S.StockAsync(shop.Mask.Id)).OnHand);    // cancelling afterwards returns exactly once
        Assert.Equal(20, (await shop.S.StockAsync(shop.Shampoo.Id)).OnHand);
    }

    [Fact]
    public async Task A_sold_order_moved_back_to_reserved_returns_its_stock_to_a_hold_and_can_be_edited()
    {
        var shop = await OpenShopAsync();
        var order = await shop.S.OrderAsync(shop.Warehouse.Id, confirm: true, (shop.Mask.Id, 4));

        var reserved = await (await shop.Client.PostAsync($"/api/orders/{order.Id}/reserve", new ReserveOrderRequest(Scenario.Tomorrow, "Customer will pay Friday")))
            .OkJsonAsync<OrderDetailDto>();
        var mask = await shop.S.StockAsync(shop.Mask.Id);
        var edited = await (await shop.Client.PutAsync($"/api/orders/{order.Id}", Update(shop, (shop.Mask.Id, 1)))).OkJsonAsync<OrderDetailDto>();
        var afterEdit = await shop.S.StockAsync(shop.Mask.Id);

        Assert.Equal(OrderStatus.Reserved, reserved.Status);
        Assert.Null(reserved.ConfirmedAt);                                  // no longer a sale
        Assert.Equal((PaymentStatus.Paid, PaymentStatus.Unpaid), (order.PaymentStatus, reserved.PaymentStatus)); // the money is taken back out
        Assert.Equal((10, 4), (mask.OnHand, mask.Reserved));                // back on the shelf, held for the customer
        Assert.Equal((OrderStatus.Reserved, 24.00m), (edited.Status, edited.GrandTotal));
        Assert.Equal((10, 1), (afterEdit.OnHand, afterEdit.Reserved));      // the hold follows the edited lines
    }

    [Fact]
    public async Task Reserved_stock_is_held_without_selling_and_marking_sold_deducts_it_once()
    {
        var shop = await OpenShopAsync();
        var order = await shop.S.OrderAsync(shop.Warehouse.Id, confirm: false, (shop.Mask.Id, 3));
        var held = await shop.S.StockAsync(shop.Mask.Id);
        var analyticsBefore = await shop.Client.GetJsonAsync<AlaraERP.Application.Analytics.AnalyticsSummaryDto>("/api/analytics/summary");

        var sold = await (await shop.Client.PostAsync($"/api/orders/{order.Id}/confirm", new TransitionRequest(null))).OkJsonAsync<OrderDetailDto>();
        var after = await shop.S.StockAsync(shop.Mask.Id);
        var analyticsAfter = await shop.Client.GetJsonAsync<AlaraERP.Application.Analytics.AnalyticsSummaryDto>("/api/analytics/summary");

        Assert.Equal((OrderStatus.Reserved, 10, 3, 7), (order.Status, held.OnHand, held.Reserved, held.Available));
        Assert.Equal(0, analyticsBefore.Totals.Orders);                       // reserved is not a sale
        Assert.Equal(PaymentStatus.Unpaid, order.PaymentStatus);
        Assert.Equal((OrderStatus.Confirmed, PaymentStatus.Paid), (sold.Status, sold.PaymentStatus)); // selling = paid
        Assert.Equal(72m, analyticsAfter.Totals.CashReceived);              // paid at sale: 3 × $24
        Assert.Equal((7, 0, 7), (after.OnHand, after.Reserved, after.Available)); // hold became a deduction, once
        Assert.Equal((1, 72m), (analyticsAfter.Totals.Orders, analyticsAfter.Totals.NetSales));
    }

    [Fact]
    public async Task Cancelling_a_reservation_frees_the_stock()
    {
        var shop = await OpenShopAsync();
        var order = await shop.S.OrderAsync(shop.Warehouse.Id, confirm: false, (shop.Duo.Id, 2));

        (await shop.Client.PostAsync($"/api/orders/{order.Id}/cancel", new TransitionRequest("Customer changed mind"))).EnsureSuccessStatusCode();

        Assert.Equal((10, 0), ((await shop.S.StockAsync(shop.Mask.Id)).OnHand, (await shop.S.StockAsync(shop.Mask.Id)).Reserved));
        Assert.Equal((20, 0), ((await shop.S.StockAsync(shop.Shampoo.Id)).OnHand, (await shop.S.StockAsync(shop.Shampoo.Id)).Reserved));
    }

    [Fact]
    public async Task Short_stock_names_the_reserved_orders_holding_it()
    {
        var shop = await OpenShopAsync();
        var holder = await shop.S.OrderAsync(shop.Warehouse.Id, confirm: false, (shop.Mask.Id, 8));

        var holds = await shop.Client.GetJsonAsync<List<ReservedStockDto>>($"/api/orders/reserved-stock?productIds={shop.Mask.Id}&productIds={shop.Shampoo.Id}");
        var refused = await shop.S.CreateOrderAsync(shop.Warehouse.Id, confirm: true, lines: (shop.Duo.Id, 3)); // needs 3 masks, 2 free

        var mask = holds.Single(h => h.ProductId == shop.Mask.Id);
        Assert.Equal((8, holder.OrderNumber), (mask.ReservedQuantity, mask.Orders.Single().OrderNumber));
        Assert.Equal(0, holds.Single(h => h.ProductId == shop.Shampoo.Id).ReservedQuantity);
        var problem = await refused.Content.ReadAsStringAsync();
        Assert.Contains("2 available", problem);
        Assert.Contains($"8 more are reserved in {holder.OrderNumber}", problem);
    }

    [Fact]
    public async Task Reservation_times_must_be_in_the_future_and_can_be_extended()
    {
        var shop = await OpenShopAsync();
        var past = await shop.Client.PostAsync("/api/orders", new CreateOrderRequest(OrderSource.WhatsApp, shop.Warehouse.Id, PaymentMethod.CashOnDelivery,
            new RecipientInput("A", "+961 70 000 000", null, null), 0, null, [new OrderLineInput(shop.Mask.Id, 1)],
            ReserveUntil: DateTimeOffset.UtcNow.AddMinutes(-5)));
        var both = await shop.Client.PostAsync("/api/orders", new CreateOrderRequest(OrderSource.WhatsApp, shop.Warehouse.Id, PaymentMethod.CashOnDelivery,
            new RecipientInput("A", "+961 70 000 000", null, null), 0, null, [new OrderLineInput(shop.Mask.Id, 1)],
            Confirm: true, ReserveUntil: Scenario.Tomorrow));
        var order = await shop.S.OrderAsync(shop.Warehouse.Id, confirm: false, (shop.Mask.Id, 1));
        var later = DateTimeOffset.UtcNow.AddDays(3);

        var extended = await (await shop.Client.PutAsync($"/api/orders/{order.Id}/reservation", new ReserveOrderRequest(later, "Asked for 3 days")))
            .OkJsonAsync<OrderDetailDto>();

        Assert.Equal("orders.reservation_in_past", await past.ProblemCodeAsync());
        Assert.Equal(HttpStatusCode.BadRequest, both.StatusCode);
        Assert.Equal(later.ToUnixTimeSeconds(), extended.ReservedUntil!.Value.ToUnixTimeSeconds());
        Assert.Contains(extended.History, h => h.EventType == OrderEventType.Reservation && h.Notes == "Asked for 3 days");
    }

    [Fact]
    public async Task A_sold_order_is_paid_at_once_and_a_return_puts_the_stock_back_and_voids_the_payment()
    {
        var shop = await OpenShopAsync();
        var order = await shop.S.OrderAsync(shop.Warehouse.Id, confirm: true, (shop.Mask.Id, 2));
        var sold = await shop.S.StockAsync(shop.Mask.Id);

        var returned = await (await shop.Client.PostAsync($"/api/orders/{order.Id}/return", new TransitionRequest("Customer refused")))
            .OkJsonAsync<OrderDetailDto>();
        var after = await shop.S.StockAsync(shop.Mask.Id);
        var analytics = await shop.Client.GetJsonAsync<AlaraERP.Application.Analytics.AnalyticsSummaryDto>("/api/analytics/summary");

        Assert.Equal((OrderStatus.Confirmed, PaymentStatus.Paid, 48m), (order.Status, order.PaymentStatus, order.GrandTotal));
        Assert.Equal(8, sold.OnHand);
        Assert.Equal((OrderStatus.Returned, PaymentStatus.Void), (returned.Status, returned.PaymentStatus));
        Assert.Equal(10, after.OnHand);
        Assert.Equal((0, 0m), (analytics.Totals.Orders, analytics.Totals.CashReceived));
        Assert.Equal(["Draft", "Confirmed", "Returned"], returned.History.Where(h => h.EventType == OrderEventType.Status).Select(h => h.NewValue));
        Assert.All(returned.History, h => Assert.NotNull(h.ChangedBy));
    }

    [Fact]
    public async Task Concurrent_reservations_cannot_hold_more_than_is_in_stock()
    {
        var shop = await OpenShopAsync();
        var clients = new List<ErpClient>();
        for (var i = 0; i < 6; i++) clients.Add(await SignedInAsAsync(SystemRoles.SalesEmployee));

        // 6 employees reserve 4 masks each at the same moment; only 10 exist.
        var results = await Task.WhenAll(clients.Select(c => new Scenario(c, Factory)
            .CreateOrderAsync(shop.Warehouse.Id, confirm: false, lines: (shop.Mask.Id, 4))));

        Assert.Equal(2, results.Count(r => r.IsSuccessStatusCode));
        Assert.All(results.Where(r => !r.IsSuccessStatusCode), r => Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode));
        var mask = await shop.S.StockAsync(shop.Mask.Id);
        Assert.Equal((10, 8, 2), (mask.OnHand, mask.Reserved, mask.Available));
        Assert.Equal(2, await shop.S.CountAsync("SELECT count(*) FROM inventory.stock_reservations WHERE status = 'Active'"));
    }

    [Fact]
    public async Task Confirming_the_same_order_twice_at_once_deducts_once()
    {
        var shop = await OpenShopAsync();
        var draft = await shop.S.OrderAsync(shop.Warehouse.Id, confirm: false, (shop.Mask.Id, 3));
        using var other = await SignedInAsAsync(SystemRoles.SalesEmployee);

        var results = await Task.WhenAll(
            shop.Client.PostAsync($"/api/orders/{draft.Id}/confirm", new TransitionRequest(null)),
            other.PostAsync($"/api/orders/{draft.Id}/confirm", new TransitionRequest(null)));

        Assert.Single(results, r => r.IsSuccessStatusCode);
        Assert.Equal((7, 0), ((await shop.S.StockAsync(shop.Mask.Id)).OnHand, (await shop.S.StockAsync(shop.Mask.Id)).Reserved));
    }

    [Fact]
    public async Task Search_and_filters_find_orders_by_phone_number_and_status()
    {
        var shop = await OpenShopAsync();
        await shop.S.OrderAsync(shop.Warehouse.Id, confirm: true, (shop.Mask.Id, 1));
        await shop.S.OrderAsync(shop.Warehouse.Id, confirm: false, (shop.Mask.Id, 1));

        var byPhone = await shop.Client.GetJsonAsync<PagedResult<OrderListItemDto>>("/api/orders?search=70123456");
        var confirmed = await shop.Client.GetJsonAsync<PagedResult<OrderListItemDto>>("/api/orders?status=Confirmed");

        var open = await shop.Client.GetJsonAsync<PagedResult<OrderListItemDto>>("/api/orders?statuses=Reserved&statuses=Confirmed");
        var reserved = await shop.Client.GetJsonAsync<PagedResult<OrderListItemDto>>("/api/orders?status=Reserved");
        Assert.NotNull(Assert.Single(reserved.Items).ReservedUntil);

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
        Assert.Equal(HttpStatusCode.OK, (await sales.PostAsync($"/api/orders/{order.Id}/confirm", new TransitionRequest(null))).StatusCode);
    }

    [Fact]
    public async Task Super_admin_can_delete_test_orders_and_restore_reserved_or_deducted_stock()
    {
        var shop = await OpenShopAsync();
        var sold = await shop.S.OrderAsync(shop.Warehouse.Id, confirm: true, (shop.Mask.Id, 3));
        var reserved = await shop.S.OrderAsync(shop.Warehouse.Id, confirm: false, (shop.Mask.Id, 2));
        using var sales = await SignedInAsAsync(SystemRoles.SalesEmployee);
        using var admin = await SignedInAsAsync(SystemRoles.SuperAdmin);

        Assert.Equal(HttpStatusCode.Forbidden, (await sales.DeleteAsync($"/api/orders/{sold.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/orders/{sold.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/orders/{reserved.Id}")).StatusCode);

        var stock = await shop.S.StockAsync(shop.Mask.Id);
        Assert.Equal((10, 0, 10), (stock.OnHand, stock.Reserved, stock.Available));
        Assert.Equal(0, await shop.S.CountAsync("SELECT count(*) FROM sales.orders"));
        Assert.Equal(0, await shop.S.CountAsync("SELECT count(*) FROM inventory.stock_reservations"));
        Assert.Equal(0, await shop.S.CountAsync("SELECT count(*) FROM inventory.stock_movements WHERE reference_type = 'Order'"));
        var analytics = await shop.Client.GetJsonAsync<AlaraERP.Application.Analytics.AnalyticsSummaryDto>("/api/analytics/summary");
        Assert.Equal((0, 0m), (analytics.Totals.Orders, analytics.Totals.NetSales));
    }

    private static UpdateOrderRequest Update(Shop shop, params (Guid ProductId, int Quantity)[] lines) => new(
        OrderSource.WhatsApp, shop.Warehouse.Id, PaymentMethod.CashOnDelivery,
        new RecipientInput("Rima Saad", "+961 70 123 456", "Beirut", null), 0m, null,
        lines.Select(l => new OrderLineInput(l.ProductId, l.Quantity)).ToList());
}
