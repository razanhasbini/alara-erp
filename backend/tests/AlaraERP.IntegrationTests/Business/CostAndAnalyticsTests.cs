using System.Net;
using AlaraERP.Application.Analytics;
using AlaraERP.Application.Catalog;
using AlaraERP.Application.Common;
using AlaraERP.Application.Common.Security;
using AlaraERP.Application.Inventory;
using AlaraERP.Application.Sales;
using AlaraERP.Domain.Inventory;
using AlaraERP.Domain.Sales;
using AlaraERP.IntegrationTests.Infrastructure;

namespace AlaraERP.IntegrationTests.Business;

public sealed class CostAndAnalyticsTests(AlaraApiFactory factory) : ApiTestBase(factory)
{
    private static readonly DateOnly Soon = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(2);

    [Fact]
    public async Task Cost_of_goods_uses_the_actual_batches_sold_and_bills_reduce_net_profit()
    {
        var (client, s) = await AdminAsync();
        var warehouse = await s.WarehouseAsync();
        var mask = await s.ProductAsync("ALR-HC-MASK", 24m);
        await s.ReceiveAsync(warehouse.Id, mask.Id, 2, Soon, "OLD", unitCost: 5m);          // ships first (expires first)
        await s.ReceiveAsync(warehouse.Id, mask.Id, 10, Scenario.InAYear, "NEW", unitCost: 8m);
        await (await s.CreateOrderAsync(warehouse.Id, confirm: true, lines: (mask.Id, 3))).OkJsonAsync<OrderDetailDto>();
        (await client.PostAsync("/api/expenses", new AlaraERP.Application.Finance.ExpenseRequest(
            AlaraERP.Domain.Finance.ExpenseCategory.Electricity, DateOnly.FromDateTime(DateTime.UtcNow), 20m, "Generator", null)))
            .EnsureSuccessStatusCode();

        var summary = await client.GetJsonAsync<AnalyticsSummaryDto>("/api/analytics/summary");

        var t = summary.Totals;
        Assert.True(summary.IncludesCosts);
        Assert.Equal((1, 3), (t.Orders, t.UnitsSold));
        Assert.Equal(72m, t.NetSales);           // 3 × 24
        Assert.Equal(18m, t.CostOfGoods);        // 2 × 5 (OLD) + 1 × 8 (NEW)
        Assert.Equal(54m, t.GrossProfit);
        Assert.Equal(75.0m, t.GrossMarginPercent);
        Assert.Equal(20m, t.Expenses);           // the electricity bill
        Assert.Equal(34m, t.Profit);             // 54 gross − 20 bills
        Assert.Equal(47.2m, t.NetMarginPercent);
        Assert.Equal(72m, t.CashReceived);       // paid when sold, no delivery fee
        Assert.Equal(20m, summary.ExpensesByCategory!.Single(c => c.Category == AlaraERP.Domain.Finance.ExpenseCategory.Electricity).Amount);
        Assert.Equal(0, t.UncostedUnits);
        Assert.Equal(72m, summary.Series.Single(p => p.Orders > 0).NetSales);
    }

    [Fact]
    public async Task Gross_sales_and_discounts_include_both_line_and_order_discounts()
    {
        var (client, s) = await AdminAsync();
        var warehouse = await s.WarehouseAsync();
        var mask = await s.ProductAsync("ALR-HC-MASK", 20m);
        await s.ReceiveAsync(warehouse.Id, mask.Id, 5, unitCost: 4m);
        var order = await (await client.PostAsync("/api/orders", new CreateOrderRequest(
            OrderSource.Instagram, warehouse.Id, PaymentMethod.CashOnDelivery,
            new RecipientInput("Rima Saad", "+961 70 123 456", "Beirut", "Hamra"),
            DiscountTotal: 3m, Notes: null,
            Items: [new OrderLineInput(mask.Id, Quantity: 2, LineDiscount: 5m)], Confirm: true)))
            .OkJsonAsync<OrderDetailDto>();

        var totals = (await client.GetJsonAsync<AnalyticsSummaryDto>("/api/analytics/summary")).Totals;

        Assert.Equal(40m, totals.GrossSales);
        Assert.Equal(8m, totals.Discounts);
        Assert.Equal(32m, totals.NetSales);
    }

    [Fact]
    public async Task Standard_cost_is_used_when_stock_arrives_without_an_invoice_cost()
    {
        var (client, s) = await AdminAsync();
        var warehouse = await s.WarehouseAsync();
        var mist = await s.ProductAsync("ALR-HC-MIST-OH", 20m, standardCost: 6.5m);
        await s.ReceiveAsync(warehouse.Id, mist.Id, 10);
        var stock = await s.StockAsync(mist.Id);

        var order = await s.OrderAsync(warehouse.Id, confirm: true, (mist.Id, 2));
        var summary = await client.GetJsonAsync<AnalyticsSummaryDto>("/api/analytics/summary");

        Assert.Equal(6.5m, stock.Cost!.AverageCost);
        Assert.Equal(65m, stock.Cost.StockValue);
        Assert.Equal(13m, summary.Totals.CostOfGoods);
    }

    [Fact]
    public async Task Restocking_uses_the_saved_cost_and_a_one_off_cost_does_not_change_it()
    {
        var (client, s) = await AdminAsync();
        var warehouse = await s.WarehouseAsync();
        var mask = await s.ProductAsync("ALR-SAVED-COST", 20m, standardCost: 6m);

        var usual = await s.ReceiveAsync(warehouse.Id, mask.Id, 10, batch: "USUAL");
        var oneOff = await s.ReceiveAsync(warehouse.Id, mask.Id, 10, batch: "PRICIER", unitCost: 8m);
        var detail = await client.GetJsonAsync<ProductDetailDto>($"/api/products/{mask.Id}");
        var stock = await s.StockAsync(mask.Id);

        Assert.Equal(6m, usual.Lines.Single().UnitCost);   // recorded on the receipt, not left blank
        Assert.Equal(60m, usual.TotalCost);
        Assert.Equal(8m, oneOff.Lines.Single().UnitCost);
        Assert.Equal(6m, detail.StandardCost);             // the saved cost is untouched
        Assert.Equal(7m, stock.Cost!.AverageCost);         // (10 × $6 + 10 × $8) / 20
    }

    [Fact]
    public async Task Staff_who_cannot_see_costs_can_still_type_one_when_receiving_an_uncosted_product()
    {
        var (admin, s) = await AdminAsync();
        var warehouse = await s.WarehouseAsync();
        var scrub = await s.ProductAsync("ALR-SCRUB", 24m, standardCost: null);
        var mask = await s.ProductAsync("ALR-MASK", 24m, standardCost: 6m);
        using var stockKeeper = await SignedInAsAsync(SystemRoles.InventoryManager);

        var scrubSeen = await stockKeeper.GetJsonAsync<ProductDetailDto>($"/api/products/{scrub.Id}");
        var maskSeen = await stockKeeper.GetJsonAsync<ProductDetailDto>($"/api/products/{mask.Id}");
        var receipt = await (await stockKeeper.PostAsync("/api/inventory/receipts", new ReceiveStockRequest(warehouse.Id, null, null, null, null,
            [new ReceiveLineInput(scrub.Id, 3, 7.25m, null, Scenario.InAYear), new ReceiveLineInput(mask.Id, 2, null, null, Scenario.InAYear)])))
            .OkJsonAsync<ReceiptDto>();
        var asAdmin = await admin.GetJsonAsync<ReceiptDto>($"/api/inventory/receipts/{receipt.Id}");

        Assert.False(scrubSeen.HasStandardCost);
        Assert.True(maskSeen.HasStandardCost);
        Assert.Null(maskSeen.StandardCost);                       // knows a cost is saved, not what it is
        Assert.All(receipt.Lines, l => Assert.Null(l.UnitCost));  // and doesn't see costs on the receipt
        Assert.Equal(7.25m, asAdmin.Lines.Single(l => l.ProductId == scrub.Id).UnitCost);
        Assert.Equal(6m, asAdmin.Lines.Single(l => l.ProductId == mask.Id).UnitCost);
    }

    [Fact]
    public async Task Receipts_saved_without_a_line_cost_show_the_cost_the_stock_is_valued_at()
    {
        var (client, s) = await AdminAsync();
        var warehouse = await s.WarehouseAsync();
        var serum = await s.ProductAsync("ALR-LEGACY", 22m, standardCost: 7.5m);
        var receipt = await s.ReceiveAsync(warehouse.Id, serum.Id, 4, batch: "OLD");
        // A receipt recorded before costs were copied onto the line, for stock whose batch had no cost either.
        await s.ExecuteAsync("UPDATE inventory.stock_receipt_lines SET unit_cost = NULL WHERE receipt_id = @id", ("id", receipt.Id));
        await s.ExecuteAsync("UPDATE inventory.batches SET unit_cost = NULL WHERE id = @id", ("id", receipt.Lines.Single().BatchId));

        var shown = await client.GetJsonAsync<ReceiptDto>($"/api/inventory/receipts/{receipt.Id}");

        var line = Assert.Single(shown.Lines);
        Assert.Equal(7.5m, line.UnitCost);
        Assert.True(line.CostFromSaved);
        Assert.Equal(30m, shown.TotalCost);
    }

    [Fact]
    public async Task Stock_cannot_be_received_without_a_cost_when_the_product_has_none_saved()
    {
        var (client, s) = await AdminAsync();
        var warehouse = await s.WarehouseAsync();
        var product = await s.ProductAsync("ALR-NO-COST", 20m, standardCost: null);

        var refused = await client.PostAsync("/api/inventory/receipts", new ReceiveStockRequest(warehouse.Id, "Supplier", null, null, null,
            [new ReceiveLineInput(product.Id, 5, null, null, Scenario.InAYear)]));
        var receipt = await s.ReceiveAsync(warehouse.Id, product.Id, 5, unitCost: 3m);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, refused.StatusCode);
        Assert.Equal("inventory.cost_required", await refused.ProblemCodeAsync());
        Assert.Equal(15m, receipt.TotalCost);
        Assert.Equal(5, (await s.StockAsync(product.Id)).OnHand);
    }

    [Fact]
    public async Task Current_average_cost_uses_standard_cost_for_only_the_uncosted_batches()
    {
        var (client, s) = await AdminAsync();
        var warehouse = await s.WarehouseAsync();
        var product = await s.ProductAsync("ALR-MIXED", 20m, standardCost: null);
        await s.ReceiveAsync(warehouse.Id, product.Id, 2, batch: "COSTED", unitCost: 5m);
        // Stock received before costs were required has batches with no cost.
        var legacy = await s.ReceiveAsync(warehouse.Id, product.Id, 2, batch: "UNCOSTED", unitCost: 1m);
        await s.ExecuteAsync("UPDATE inventory.batches SET unit_cost = NULL WHERE id = @id", ("id", legacy.Lines.Single().BatchId));

        var incomplete = await s.StockAsync(product.Id);
        Assert.Null(incomplete.Cost!.AverageCost);
        Assert.Null(incomplete.Cost.StockValue);
        Assert.Equal(2, incomplete.Cost.UncostedUnits);

        var detail = await client.GetJsonAsync<ProductDetailDto>($"/api/products/{product.Id}");
        await (await client.PutAsync($"/api/products/{product.Id}", new UpdateProductRequest(
            detail.Name, detail.Slug, detail.CategoryId,
            detail.ShortDescription, detail.Description, detail.HowToUse, detail.Ingredients,
            detail.SizeValue, detail.SizeUnit, detail.WeightGrams, detail.SellingPrice, detail.CompareAtPrice, 7m)))
            .EnsureSuccessWithBodyAsync();

        var stock = await s.StockAsync(product.Id);
        var report = await client.GetJsonAsync<ProductAnalyticsDto>("/api/analytics/products");

        Assert.Equal(6m, stock.Cost!.AverageCost); // (2 × $5 + 2 × $7 standard fallback) / 4
        Assert.Equal(stock.Cost.AverageCost, report.Products.Single(p => p.ProductId == product.Id).AverageCost);
    }

    [Fact]
    public async Task Bundle_revenue_and_cost_are_attributed_to_the_products_inside_it()
    {
        var (client, s) = await AdminAsync();
        var warehouse = await s.WarehouseAsync();
        var shampoo = await s.ProductAsync("ALR-SH", 15m);
        var mask = await s.ProductAsync("ALR-MA", 24m);
        await s.ReceiveAsync(warehouse.Id, shampoo.Id, 10, unitCost: 3m);
        await s.ReceiveAsync(warehouse.Id, mask.Id, 10, unitCost: 5m);
        var duo = await s.BundleAsync("ALR-DUO", 33m, (shampoo.Id, 1), (mask.Id, 1));
        var order = await s.OrderAsync(warehouse.Id, confirm: true, (duo.Id, 2), (shampoo.Id, 1));

        var report = await client.GetJsonAsync<ProductAnalyticsDto>("/api/analytics/products");

        var sh = report.Products.Single(p => p.ProductId == shampoo.Id);
        var ma = report.Products.Single(p => p.ProductId == mask.Id);
        var bundle = report.Bundles.Single(b => b.ProductId == duo.Id);
        Assert.Equal((3, 2), (sh.UnitsSold, sh.UnitsSoldInBundles));
        Assert.Equal(81m, sh.NetSales + ma.NetSales);                       // 2 × 33 + 15: nothing lost in the split
        Assert.Equal(Math.Round(66m * 15 / 39, 2) + 15m, sh.NetSales);       // bundle split by full prices
        Assert.Equal((9m, 10m), (sh.CostOfGoods, ma.CostOfGoods));
        Assert.Equal((2, 66m, 16m, 50m), (bundle.SetsSold, bundle.NetSales, bundle.CostOfGoods, bundle.GrossProfit));
        Assert.Equal((8m, 25m), (bundle.ComponentsCost, bundle.UnitMargin)); // today's cost of one set vs its $33 price
        Assert.Equal((3m, 12m), (sh.AverageCost, sh.UnitMargin));
    }

    [Fact]
    public async Task Confirmed_orders_are_sales_and_returned_orders_are_excluded()
    {
        var (client, s) = await AdminAsync();
        var warehouse = await s.WarehouseAsync();
        var mask = await s.ProductAsync("ALR-HC-MASK", 24m);
        await s.ReceiveAsync(warehouse.Id, mask.Id, 10, unitCost: 5m);
        var returned = await s.OrderAsync(warehouse.Id, confirm: true, (mask.Id, 1));
        (await client.PostAsync($"/api/orders/{returned.Id}/return", new TransitionRequest(null))).EnsureSuccessStatusCode();
        await s.OrderAsync(warehouse.Id, confirm: true, (mask.Id, 2)); // confirmed: cost is estimated from reserved stock

        var summary = await client.GetJsonAsync<AnalyticsSummaryDto>("/api/analytics/summary");

        Assert.Equal((1, 2, 48m, 10m), (summary.Totals.Orders, summary.Totals.UnitsSold, summary.Totals.NetSales, summary.Totals.CostOfGoods));
        var returns = (await client.GetJsonAsync<PagedResult<MovementDto>>($"/api/inventory/movements?referenceId={returned.Id}")).Items;
        Assert.Equal([MovementType.Shipment, MovementType.Return], returns.Select(m => m.Type).Order());
        Assert.All(returns, m => Assert.Equal(5m, m.UnitCost)); // the return comes back at the cost it left with
    }

    [Fact]
    public async Task Reopening_and_reconfirming_an_order_does_not_double_count_cost_or_units()
    {
        var (client, s) = await AdminAsync();
        var warehouse = await s.WarehouseAsync();
        var mask = await s.ProductAsync("ALR-HC-MASK", 24m);
        await s.ReceiveAsync(warehouse.Id, mask.Id, 10, unitCost: 5m);
        var order = await s.OrderAsync(warehouse.Id, confirm: true, (mask.Id, 2));

        (await client.PostAsync($"/api/orders/{order.Id}/reserve", new ReserveOrderRequest(Scenario.Tomorrow, null))).EnsureSuccessStatusCode();
        (await client.PostAsync($"/api/orders/{order.Id}/confirm", new TransitionRequest(null))).EnsureSuccessStatusCode();

        var summary = await client.GetJsonAsync<AnalyticsSummaryDto>("/api/analytics/summary");
        var products = await client.GetJsonAsync<ProductAnalyticsDto>("/api/analytics/products");

        Assert.Equal(8, (await s.StockAsync(mask.Id)).OnHand);            // stock moved out, back, and out again: net 2
        Assert.Equal((1, 2, 10m), (summary.Totals.Orders, summary.Totals.UnitsSold, summary.Totals.CostOfGoods));
        Assert.Equal((2, 10m), (products.Products.Single(p => p.ProductId == mask.Id).UnitsSold,
            products.Products.Single(p => p.ProductId == mask.Id).CostOfGoods));
    }

    [Fact]
    public async Task Grouping_by_week_and_month_and_validating_ranges()
    {
        var (client, _) = await AdminAsync();

        var weeks = await client.GetJsonAsync<AnalyticsSummaryDto>("/api/analytics/summary?from=2026-09-01&to=2026-09-30&groupBy=Week");
        var months = await client.GetJsonAsync<AnalyticsSummaryDto>("/api/analytics/summary?from=2026-01-01&to=2026-12-31&groupBy=Month");
        var backwards = await client.GetAsync("/api/analytics/summary?from=2026-10-10&to=2026-10-01");
        var tooManyDays = await client.GetAsync("/api/analytics/summary?from=2024-01-01&to=2026-01-01&groupBy=Day");
        var outOfRange = await client.GetAsync("/api/analytics/summary?from=9999-12-31&to=9999-12-31");

        Assert.Equal(new DateOnly(2026, 9, 1), weeks.Series[0].PeriodStart);      // clamped to the range
        Assert.Equal(new DateOnly(2026, 9, 6), weeks.Series[0].PeriodEnd);        // Sunday
        Assert.Equal(new DateOnly(2026, 9, 30), weeks.Series[^1].PeriodEnd);
        Assert.Equal(12, months.Series.Count);
        Assert.Equal("analytics.invalid_range", await backwards.ProblemCodeAsync());
        Assert.Equal("analytics.too_many_periods", await tooManyDays.ProblemCodeAsync());
        Assert.Equal("analytics.invalid_range", await outOfRange.ProblemCodeAsync());
    }

    [Fact]
    public async Task Costs_are_hidden_from_employees_without_costs_view()
    {
        var (client, s) = await AdminAsync();
        var warehouse = await s.WarehouseAsync();
        var mask = await s.ProductAsync("ALR-HC-MASK", 24m, standardCost: 5m);
        var receipt = await s.ReceiveAsync(warehouse.Id, mask.Id, 4, unitCost: 5m);
        var unknownCost = await s.ProductAsync("ALR-NO-COST", 10m);
        await s.ReceiveAsync(warehouse.Id, unknownCost.Id, 1);
        var soldWithoutCost = await s.OrderAsync(warehouse.Id, confirm: true, (unknownCost.Id, 1));
        using var viewer = await SignedInAsAsync(SystemRoles.Viewer);           // has reports.view, not costs.view
        using var stockKeeper = await SignedInAsAsync(SystemRoles.InventoryManager);
        using var sales = await SignedInAsAsync(SystemRoles.SalesEmployee);
        using var manager = await SignedInAsAsync(SystemRoles.Manager);         // runs operations, but costs are admin-only

        var summary = await viewer.GetJsonAsync<AnalyticsSummaryDto>("/api/analytics/summary");
        var products = await viewer.GetJsonAsync<ProductAnalyticsDto>("/api/analytics/products");
        var product = await viewer.GetJsonAsync<ProductDetailDto>($"/api/products/{mask.Id}");
        var stock = await stockKeeper.GetJsonAsync<ProductStockDto>($"/api/inventory/products/{mask.Id}");
        var receiptView = await stockKeeper.GetJsonAsync<ReceiptDto>($"/api/inventory/receipts/{receipt.Id}");

        Assert.False(summary.IncludesCosts);
        Assert.Null(summary.Totals.CostOfGoods);
        Assert.Null(summary.Totals.Profit);
        Assert.Equal(0, summary.Totals.UncostedUnits);
        Assert.All(products.Products, p => Assert.Null(p.AverageCost));
        Assert.All(products.Products, p => Assert.Equal(0, p.UncostedUnits));
        Assert.Null(product.StandardCost);
        Assert.Null(stock.Cost);
        Assert.All(stock.Batches, b => Assert.Null(b.UnitCost));
        Assert.All(receiptView.Lines, l => Assert.Null(l.UnitCost));
        Assert.All((await manager.GetJsonAsync<ReceiptDto>($"/api/inventory/receipts/{receipt.Id}")).Lines, l => Assert.Null(l.UnitCost));
        Assert.Null((await manager.GetJsonAsync<ProductStockDto>($"/api/inventory/products/{mask.Id}")).Cost);
        Assert.Equal(HttpStatusCode.Forbidden, (await sales.GetAsync("/api/analytics/summary")).StatusCode);
    }

    [Fact]
    public async Task Taking_a_bundle_out_of_stock_deducts_its_components()
    {
        var (client, s) = await AdminAsync();
        var warehouse = await s.WarehouseAsync();
        var shampoo = await s.ProductAsync("ALR-SH", 15m);
        var mask = await s.ProductAsync("ALR-MA", 24m);
        await s.ReceiveAsync(warehouse.Id, shampoo.Id, 10, unitCost: 3m);
        await s.ReceiveAsync(warehouse.Id, mask.Id, 10, unitCost: 5m);
        var duo = await s.BundleAsync("ALR-DUO", 33m, (shampoo.Id, 2), (mask.Id, 1));
        await s.OrderAsync(warehouse.Id, confirm: true, (mask.Id, 7)); // 7 masks deducted for a customer

        var movements = await (await client.PostAsync("/api/inventory/bundle-withdrawals",
            new WithdrawBundleRequest(warehouse.Id, duo.Id, 3, AdjustmentReason.SampleOrGift, "Influencer PR boxes")))
            .OkJsonAsync<List<MovementDto>>();
        var tooMany = await client.PostAsync("/api/inventory/bundle-withdrawals",
            new WithdrawBundleRequest(warehouse.Id, duo.Id, 1, AdjustmentReason.SampleOrGift, null));

        Assert.Equal(4, (await s.StockAsync(shampoo.Id)).OnHand);           // 10 − 3 sets × 2
        Assert.Equal((0, 0, 0), ((await s.StockAsync(mask.Id)).OnHand, (await s.StockAsync(mask.Id)).Reserved, (await s.StockAsync(mask.Id)).Available));
        Assert.Equal(2, movements.Count);
        Assert.All(movements, m => Assert.Contains("3 × Bundle ALR-DUO", m.Notes));
        Assert.Equal("inventory.insufficient_available", await tooMany.ProblemCodeAsync());
        Assert.Equal(0, await s.AvailableAsync(duo.Id));
    }

    [Fact]
    public async Task Only_managers_can_set_product_costs()
    {
        var (client, s) = await AdminAsync();
        var product = await s.ProductAsync("ALR-X", 20m, standardCost: 4m);
        var detail = await client.GetJsonAsync<ProductDetailDto>($"/api/products/{product.Id}");

        var updated = await (await client.PutAsync($"/api/products/{product.Id}", new UpdateProductRequest(detail.Name, null, null,
            null, null, null, null, null, null, null, 20m, null, 4.75m))).OkJsonAsync<ProductDetailDto>();

        Assert.Equal(4m, detail.StandardCost);
        Assert.Equal(4.75m, updated.StandardCost);
    }
}
