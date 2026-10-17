using AlaraERP.Application.Common;
using AlaraERP.Application.Common.Security;
using AlaraERP.Application.Dashboard;
using AlaraERP.Application.Catalog;
using AlaraERP.Application.Sales;
using AlaraERP.Domain.Catalog;
using AlaraERP.Infrastructure.Seeding;
using AlaraERP.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace AlaraERP.IntegrationTests.Business;

public sealed class DashboardAndImportTests(AlaraApiFactory factory) : ApiTestBase(factory)
{
    [Fact]
    public async Task Dashboard_reflects_real_records_and_counts_every_sold_order_as_collected()
    {
        var (client, s) = await ManagerAsync();
        var warehouse = await s.WarehouseAsync();
        var mask = await s.ProductAsync("ALR-HC-MASK", 24m);
        await s.ReceiveAsync(warehouse.Id, mask.Id, 10);
        await s.OrderAsync(warehouse.Id, confirm: true, (mask.Id, 1));
        await s.OrderAsync(warehouse.Id, confirm: true, (mask.Id, 2));
        await s.OrderAsync(warehouse.Id, confirm: false, (mask.Id, 1)); // reserved: not a sale yet
        var cancelled = await s.OrderAsync(warehouse.Id, confirm: false, (mask.Id, 1));
        (await client.PostAsync($"/api/orders/{cancelled.Id}/cancel", new TransitionRequest(null))).EnsureSuccessStatusCode();

        var dashboard = await client.GetJsonAsync<DashboardDto>("/api/dashboard");

        Assert.Equal(1, dashboard.Catalog!.ActiveProducts);
        Assert.Equal((4, 2, 1, 1, 2), (dashboard.Orders!.Total, dashboard.Orders.Sold, dashboard.Orders.Reserved,
            dashboard.Orders.Cancelled, dashboard.Orders.SoldToday));
        Assert.Equal(72.00m, dashboard.Finance!.CollectedRevenue); // both sold orders are paid: 24 + 2×24
        Assert.Equal(24.00m, dashboard.Finance.ReservedValue);
        Assert.Null(dashboard.Finance.BillsThisMonth);             // bills are admin-only
        Assert.Equal((7, 1), (dashboard.Inventory!.UnitsOnHand, dashboard.Inventory.UnitsReserved));
        Assert.Equal(72.00m, dashboard.DailyOrders!.Sum(d => d.Value)); // reserved and cancelled orders are excluded
        Assert.NotEmpty(dashboard.RecentMovements!);
    }

    [Fact]
    public async Task Dashboard_hides_sections_the_employee_may_not_see()
    {
        using var stockKeeper = await SignedInAsAsync(SystemRoles.InventoryManager);
        using var sales = await SignedInAsAsync(SystemRoles.SalesEmployee);

        var forStock = await stockKeeper.GetJsonAsync<DashboardDto>("/api/dashboard");
        var forSales = await sales.GetJsonAsync<DashboardDto>("/api/dashboard");

        Assert.Null(forStock.Finance);
        Assert.Null(forStock.Orders);
        Assert.NotNull(forStock.Inventory);
        Assert.Null(forSales.Finance);
        Assert.NotNull(forSales.RecentOrders);
    }

    [Fact]
    public async Task Real_catalogue_imports_once_and_bundles_add_up()
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../../data/catalog/alaraline-catalog.json"));

        CatalogImporter.Result first, second;
        await using (var scope = Factory.Services.CreateAsyncScope())
            first = await scope.ServiceProvider.GetRequiredService<CatalogImporter>().ImportAsync(path, CancellationToken.None);
        await using (var scope = Factory.Services.CreateAsyncScope())
            second = await scope.ServiceProvider.GetRequiredService<CatalogImporter>().ImportAsync(path, CancellationToken.None);

        Assert.Equal((5, 28, 8, 0, true), (first.CategoriesCreated, first.ProductsCreated, first.BundlesCreated, first.Skipped, first.WarehouseCreated));
        Assert.Equal((0, 0, 0, 36, false), (second.CategoriesCreated, second.ProductsCreated, second.BundlesCreated, second.Skipped, second.WarehouseCreated));

        using var client = await SignedInAsAsync(SystemRoles.Viewer);
        var bundles = await client.GetJsonAsync<PagedResult<ProductListItemDto>>("/api/products?type=Bundle&pageSize=50");
        Assert.Equal(8, bundles.TotalCount);
        foreach (var b in bundles.Items)
        {
            var detail = await client.GetJsonAsync<ProductDetailDto>($"/api/products/{b.Id}");
            Assert.Equal(detail.CompareAtPrice, detail.ComponentsValue); // full price = sum of parts, as on the website
            Assert.True(detail.SellingPrice < detail.CompareAtPrice);
        }
        var fullOh = bundles.Items.Single(b => b.Name == "Full Treatment Set – Oh Honey");
        var components = (await client.GetJsonAsync<ProductDetailDto>($"/api/products/{fullOh.Id}")).BundleItems.Select(i => i.Name).ToHashSet();
        Assert.Contains("Intensive Repair Shampoo – Oh Honey", components);
        Assert.DoesNotContain("Intensive Repair Shampoo – Tropical Vanilla", components);
        Assert.Equal(0, await new Scenario(client, Factory).CountAsync("SELECT count(*) FROM inventory.stock_movements"));
    }
}
