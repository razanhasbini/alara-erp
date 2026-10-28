using System.Net;
using AlaraERP.Application.Common;
using AlaraERP.Application.Common.Security;
using AlaraERP.Application.Inventory;
using AlaraERP.Domain.Inventory;
using AlaraERP.IntegrationTests.Infrastructure;

namespace AlaraERP.IntegrationTests.Business;

public sealed class InventoryTests(AlaraApiFactory factory) : ApiTestBase(factory)
{
    [Fact]
    public async Task Super_admin_can_deactivate_and_reactivate_expiry_dates_without_losing_the_workflow()
    {
        var (manager, s) = await ManagerAsync();
        var warehouse = await s.WarehouseAsync();
        var first = await s.ProductAsync("ALR-NO-EXPIRY", 18m);
        var second = await s.ProductAsync("ALR-NEEDS-EXPIRY", 19m);
        using var superAdmin = await SignedInAsAsync(SystemRoles.SuperAdmin);

        Assert.Equal(HttpStatusCode.Forbidden, (await manager.GetAsync("/api/settings/features")).StatusCode);
        var disabled = await (await superAdmin.PutAsync("/api/settings/features/expiry-dates", new UpdateExpiryDatesRequest(false)))
            .OkJsonAsync<InventoryFeatureSettingsDto>();
        var received = await manager.PostAsync("/api/inventory/receipts", new ReceiveStockRequest(warehouse.Id, null, null, null, null,
            [new ReceiveLineInput(first.Id, 3, null, null, null)]));

        Assert.False(disabled.ExpiryDatesEnabled);
        Assert.Equal(HttpStatusCode.Created, received.StatusCode);
        Assert.Null(Assert.Single((await s.StockAsync(first.Id)).Batches).ExpiryDate);

        var enabled = await (await superAdmin.PutAsync("/api/settings/features/expiry-dates", new UpdateExpiryDatesRequest(true)))
            .OkJsonAsync<InventoryFeatureSettingsDto>();
        var missing = await manager.PostAsync("/api/inventory/receipts", new ReceiveStockRequest(warehouse.Id, null, null, null, null,
            [new ReceiveLineInput(second.Id, 3, null, null, null)]));

        Assert.True(enabled.ExpiryDatesEnabled);
        Assert.Equal("inventory.expiry_required", await missing.ProblemCodeAsync());
    }

    [Fact]
    public async Task Receiving_stock_creates_batch_balance_and_receipt_movement()
    {
        var (client, s) = await AdminAsync();
        var warehouse = await s.WarehouseAsync();
        var mask = await s.ProductAsync("ALR-HC-MASK", 24m);
        var expiry = Scenario.InAYear;

        var receipt = await s.ReceiveAsync(warehouse.Id, mask.Id, 40, expiry, "LOT-2026-07", unitCost: 6.25m);

        Assert.StartsWith("RC-", receipt.ReceiptNumber);
        Assert.Equal(40, receipt.TotalUnits);
        Assert.Equal(250.00m, receipt.TotalCost);
        var stock = await s.StockAsync(mask.Id);
        Assert.Equal(40, stock.OnHand);
        Assert.Equal(0, stock.Reserved);
        var batch = Assert.Single(stock.Batches);
        Assert.Equal(("LOT-2026-07", expiry, 40), (batch.BatchNumber, batch.ExpiryDate, batch.OnHand));
        var movement = Assert.Single((await client.GetJsonAsync<PagedResult<MovementDto>>($"/api/inventory/movements?productId={mask.Id}")).Items);
        Assert.Equal((MovementType.Receipt, 40, receipt.ReceiptNumber), (movement.Type, movement.QuantityChange, movement.ReferenceNumber));
    }

    [Fact]
    public async Task Expiry_is_required_for_tracked_products_and_cannot_be_in_the_past()
    {
        var (client, s) = await ManagerAsync();
        var warehouse = await s.WarehouseAsync();
        var serum = await s.ProductAsync("ALR-HC-SERUM", 22m);
        var brush = await s.ProductAsync("ALR-ACC-BRUSH", 13m, tracksExpiry: false);

        var missing = await client.PostAsync("/api/inventory/receipts", new ReceiveStockRequest(warehouse.Id, null, null, null, null,
            [new ReceiveLineInput(serum.Id, 5, null, null, null)]));
        var past = await client.PostAsync("/api/inventory/receipts", new ReceiveStockRequest(warehouse.Id, null, null, null, null,
            [new ReceiveLineInput(serum.Id, 5, null, null, new DateOnly(2020, 1, 1))]));
        var accessory = await client.PostAsync("/api/inventory/receipts", new ReceiveStockRequest(warehouse.Id, null, null, null, null,
            [new ReceiveLineInput(brush.Id, 5, null, null, null)]));

        Assert.Equal("inventory.expiry_required", await missing.ProblemCodeAsync());
        Assert.Equal("inventory.already_expired", await past.ProblemCodeAsync());
        Assert.Equal(HttpStatusCode.Created, accessory.StatusCode);
    }

    [Fact]
    public async Task Bundles_cannot_be_stocked_directly()
    {
        var (client, s) = await ManagerAsync();
        var warehouse = await s.WarehouseAsync();
        var shampoo = await s.ProductAsync("ALR-SH", 15m);
        var set = await s.BundleAsync("ALR-SET", 20m, (shampoo.Id, 1));

        var response = await client.PostAsync("/api/inventory/receipts", new ReceiveStockRequest(warehouse.Id, null, null, null, null,
            [new ReceiveLineInput(set.Id, 5, null, null, Scenario.InAYear)]));

        Assert.Equal("inventory.bundle_not_stocked", await response.ProblemCodeAsync());
    }

    [Fact]
    public async Task Adjustments_need_a_reason_and_cannot_remove_more_than_remains_after_confirmed_sales()
    {
        var (client, s) = await ManagerAsync();
        var warehouse = await s.WarehouseAsync();
        var oil = await s.ProductAsync("ALR-BC-TANOIL", 16m);
        await s.ReceiveAsync(warehouse.Id, oil.Id, 10);
        var batchId = (await s.StockAsync(oil.Id)).Batches.Single().BatchId;
        await s.OrderAsync(warehouse.Id, confirm: true, (oil.Id, 7)); // 7 deducted, 3 remain

        var noReason = await client.PostAsync("/api/inventory/adjustments",
            new AdjustStockRequest(warehouse.Id, oil.Id, -1, AdjustmentReason.Other, null, batchId, null, null));
        var tooMuch = await client.PostAsync("/api/inventory/adjustments",
            new AdjustStockRequest(warehouse.Id, oil.Id, -4, AdjustmentReason.Damaged, "Broken bottles", batchId, null, null));
        var ok = await client.PostAsync("/api/inventory/adjustments",
            new AdjustStockRequest(warehouse.Id, oil.Id, -3, AdjustmentReason.Damaged, "Broken bottles", batchId, null, null));

        Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);
        Assert.Equal("inventory.insufficient_batch_stock", await tooMuch.ProblemCodeAsync());
        var movement = await ok.OkJsonAsync<MovementDto>();
        Assert.Equal((MovementType.AdjustmentDecrease, -3, AdjustmentReason.Damaged), (movement.Type, movement.QuantityChange, movement.Reason));
        var stock = await s.StockAsync(oil.Id);
        Assert.Equal((0, 0, 0), (stock.OnHand, stock.Reserved, stock.Available));
    }

    [Fact]
    public async Task Correcting_a_count_sets_stock_to_the_counted_number_and_logs_the_difference()
    {
        var (client, s) = await ManagerAsync();
        var warehouse = await s.WarehouseAsync();
        var oil = await s.ProductAsync("ALR-BC-TANOIL", 16m);
        await s.ReceiveAsync(warehouse.Id, oil.Id, 10, batch: "OLD");
        await s.ReceiveAsync(warehouse.Id, oil.Id, 5, batch: "NEW");

        var down = await (await client.PostAsync("/api/inventory/counts",
            new CorrectCountRequest(warehouse.Id, oil.Id, 8, null, "Shelf count"))).OkJsonAsync<ProductStockDto>();
        var unchanged = await client.PostAsync("/api/inventory/counts", new CorrectCountRequest(warehouse.Id, oil.Id, 8, null, null));
        var up = await (await client.PostAsync("/api/inventory/counts",
            new CorrectCountRequest(warehouse.Id, oil.Id, 11, null, null))).OkJsonAsync<ProductStockDto>();

        // 7 missing: all 5 of the newest delivery, then 2 of the older one. 3 found: back into the newest delivery.
        Assert.Equal(8, down.OnHand);
        Assert.Equal((8, 0), (down.Batches.Single(b => b.BatchNumber == "OLD").OnHand, down.Batches.SingleOrDefault(b => b.BatchNumber == "NEW")?.OnHand ?? 0));
        Assert.Equal("inventory.count_unchanged", await unchanged.ProblemCodeAsync());
        Assert.Equal(11, up.OnHand);
        Assert.Equal(3, up.Batches.Single(b => b.BatchNumber == "NEW").OnHand);
        var corrections = (await client.GetJsonAsync<PagedResult<MovementDto>>($"/api/inventory/movements?productId={oil.Id}")).Items
            .Where(m => m.Reason == AdjustmentReason.CountCorrection).ToList();
        Assert.Equal([-5, -2, 3], corrections.Select(m => m.QuantityChange).Order());
        Assert.Contains(corrections, m => m.Notes == "Counted 8, was 15. Shelf count");
    }

    [Fact]
    public async Task A_count_can_be_corrected_for_one_delivery_or_for_a_product_never_stocked()
    {
        var (client, s) = await ManagerAsync();
        var warehouse = await s.WarehouseAsync();
        var oil = await s.ProductAsync("ALR-BC-TANOIL", 16m);
        var mask = await s.ProductAsync("ALR-HC-MASK", 24m, tracksExpiry: false);
        await s.ReceiveAsync(warehouse.Id, oil.Id, 10, batch: "OLD");
        await s.ReceiveAsync(warehouse.Id, oil.Id, 5, batch: "NEW");
        var old = (await s.StockAsync(oil.Id)).Batches.Single(b => b.BatchNumber == "OLD");

        var oneDelivery = await (await client.PostAsync("/api/inventory/counts",
            new CorrectCountRequest(warehouse.Id, oil.Id, 12, old.BatchId, null))).OkJsonAsync<ProductStockDto>();
        var neverStocked = await (await client.PostAsync("/api/inventory/counts",
            new CorrectCountRequest(warehouse.Id, mask.Id, 6, null, null))).OkJsonAsync<ProductStockDto>();

        Assert.Equal((17, 12, 5), (oneDelivery.OnHand, oneDelivery.Batches.Single(b => b.BatchNumber == "OLD").OnHand,
            oneDelivery.Batches.Single(b => b.BatchNumber == "NEW").OnHand));
        Assert.Equal(6, neverStocked.OnHand);
        Assert.Equal(6, Assert.Single(neverStocked.Batches).OnHand);
    }

    [Fact]
    public async Task Sales_staff_can_correct_counts_but_viewers_cannot()
    {
        var (_, s) = await ManagerAsync();
        var warehouse = await s.WarehouseAsync();
        var mask = await s.ProductAsync("ALR-HC-MASK", 24m);
        await s.ReceiveAsync(warehouse.Id, mask.Id, 5);
        using var sales = await SignedInAsAsync(SystemRoles.SalesEmployee);
        using var viewer = await SignedInAsAsync(SystemRoles.Viewer);

        var byViewer = await viewer.PostAsync("/api/inventory/counts", new CorrectCountRequest(warehouse.Id, mask.Id, 50, null, null));
        var bySales = await (await sales.PostAsync("/api/inventory/counts", new CorrectCountRequest(warehouse.Id, mask.Id, 4, null, "Miscounted"))).OkJsonAsync<ProductStockDto>();
        var writeOff = await sales.PostAsync("/api/inventory/bundle-withdrawals", new { });

        Assert.Equal(HttpStatusCode.Forbidden, byViewer.StatusCode);
        Assert.Equal(4, bySales.OnHand);
        Assert.Equal(HttpStatusCode.Forbidden, writeOff.StatusCode);
    }

    [Fact]
    public async Task Transfers_move_stock_first_expiry_first_and_record_both_sides()
    {
        var (client, s) = await ManagerAsync();
        var main = await s.WarehouseAsync("MAIN");
        var shop = await s.WarehouseAsync("SHOP");
        var mist = await s.ProductAsync("ALR-HC-MIST-OH", 20m);
        var soon = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(3);
        await s.ReceiveAsync(main.Id, mist.Id, 10, Scenario.InAYear, "LATE");
        await s.ReceiveAsync(main.Id, mist.Id, 4, soon, "EARLY");

        var transfer = await (await client.PostAsync("/api/inventory/transfers",
            new TransferStockRequest(main.Id, shop.Id, "Restock shop", [new TransferLineInput(mist.Id, 6, null)]))).OkJsonAsync<TransferDto>();

        Assert.StartsWith("TR-", transfer.TransferNumber);
        Assert.Equal([("EARLY", 4), ("LATE", 2)], transfer.Lines.Select(l => (l.BatchNumber, l.Quantity)));
        Assert.Equal(8, await s.AvailableAsync(mist.Id, main.Id));
        Assert.Equal(6, await s.AvailableAsync(mist.Id, shop.Id));
        Assert.Equal(14, await s.AvailableAsync(mist.Id));
        var movements = (await client.GetJsonAsync<PagedResult<MovementDto>>($"/api/inventory/movements?referenceId={transfer.Id}")).Items;
        Assert.Equal(4, movements.Count);
        Assert.Equal(0, movements.Sum(m => m.QuantityChange));
    }

    [Fact]
    public async Task Transfer_of_more_than_is_available_fails_and_changes_nothing()
    {
        var (client, s) = await ManagerAsync();
        var main = await s.WarehouseAsync("MAIN");
        var shop = await s.WarehouseAsync("SHOP");
        var mask = await s.ProductAsync("ALR-HC-MASK", 24m);
        await s.ReceiveAsync(main.Id, mask.Id, 3);

        var response = await client.PostAsync("/api/inventory/transfers",
            new TransferStockRequest(main.Id, shop.Id, null, [new TransferLineInput(mask.Id, 5, null)]));

        Assert.Equal("inventory.insufficient_available", await response.ProblemCodeAsync());
        Assert.Equal(3, await s.AvailableAsync(mask.Id, main.Id));
        Assert.Equal(0, await s.AvailableAsync(mask.Id, shop.Id));
    }

    [Fact]
    public async Task Bundle_availability_is_limited_by_its_scarcest_component()
    {
        var (_, s) = await ManagerAsync();
        var warehouse = await s.WarehouseAsync();
        var shampoo = await s.ProductAsync("ALR-SH", 15m);
        var conditioner = await s.ProductAsync("ALR-CO", 15m);
        await s.ReceiveAsync(warehouse.Id, shampoo.Id, 10);
        await s.ReceiveAsync(warehouse.Id, conditioner.Id, 7);
        var duo = await s.BundleAsync("ALR-DUO", 25m, (shampoo.Id, 2), (conditioner.Id, 1));

        Assert.Equal(5, await s.AvailableAsync(duo.Id)); // 10 shampoo / 2 per set
    }

    [Fact]
    public async Task Low_stock_uses_the_threshold_a_manager_sets()
    {
        var (client, s) = await ManagerAsync();
        var warehouse = await s.WarehouseAsync();
        var mask = await s.ProductAsync("ALR-HC-MASK", 24m);
        await s.ReceiveAsync(warehouse.Id, mask.Id, 5);

        Assert.Equal(StockStatus.InStock, (await s.StockAsync(mask.Id)).Status);
        (await client.PutAsync($"/api/inventory/settings/{mask.Id}", new UpdateInventorySettingsRequest(5, true))).EnsureSuccessStatusCode();

        Assert.Equal(StockStatus.Low, (await s.StockAsync(mask.Id)).Status);
        var low = await client.GetJsonAsync<PagedResult<StockRowDto>>("/api/inventory?filter=Low");
        Assert.Equal(mask.Id, Assert.Single(low.Items).ProductId);
    }

    [Fact]
    public async Task Operational_staff_can_receive_stock_but_cannot_adjust_it_or_set_thresholds()
    {
        var (_, s) = await ManagerAsync();
        var warehouse = await s.WarehouseAsync();
        var mask = await s.ProductAsync("ALR-HC-MASK", 24m);
        using var stockKeeper = await SignedInAsAsync(SystemRoles.InventoryManager);
        using var sales = await SignedInAsAsync(SystemRoles.SalesEmployee);

        var threshold = await stockKeeper.PutAsync($"/api/inventory/settings/{mask.Id}", new UpdateInventorySettingsRequest(3, true));
        var receive = await stockKeeper.PostAsync("/api/inventory/receipts", new ReceiveStockRequest(warehouse.Id, null, null, null, null,
            [new ReceiveLineInput(mask.Id, 5, null, null, Scenario.InAYear)]));
        var salesReceive = await sales.PostAsync("/api/inventory/receipts", new ReceiveStockRequest(warehouse.Id, "Supplier", "DIANA-1", null, null,
            [new ReceiveLineInput(mask.Id, 2, null, null, Scenario.InAYear)]));
        var salesAdjust = await sales.PostAsync("/api/inventory/adjustments",
            new AdjustStockRequest(warehouse.Id, mask.Id, 5, AdjustmentReason.CountCorrection, null, null, null, Scenario.InAYear));

        Assert.Equal(HttpStatusCode.Forbidden, threshold.StatusCode);
        Assert.Equal(HttpStatusCode.Created, receive.StatusCode);
        Assert.Equal(HttpStatusCode.Created, salesReceive.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, salesAdjust.StatusCode);
        Assert.Equal(7, (await s.StockAsync(mask.Id)).OnHand);
    }

    [Fact]
    public async Task Ledger_rows_cannot_be_rewritten_by_the_application_role()
    {
        var (_, s) = await ManagerAsync();
        var warehouse = await s.WarehouseAsync();
        var mask = await s.ProductAsync("ALR-HC-MASK", 24m);
        await s.ReceiveAsync(warehouse.Id, mask.Id, 5);

        await using var conn = new Npgsql.NpgsqlConnection(Factory.AppConnectionString);
        await conn.OpenAsync();
        await using var cmd = new Npgsql.NpgsqlCommand("UPDATE inventory.stock_movements SET quantity_change = 500", conn);

        var ex = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => cmd.ExecuteNonQueryAsync());
        Assert.Equal("42501", ex.SqlState); // insufficient_privilege
    }
}
