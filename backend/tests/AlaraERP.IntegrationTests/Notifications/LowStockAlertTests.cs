using AlaraERP.Application.Common;
using AlaraERP.Application.Common.Security;
using AlaraERP.Application.Inventory;
using AlaraERP.Application.Notifications;
using AlaraERP.Domain.Notifications;
using AlaraERP.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace AlaraERP.IntegrationTests.Notifications;

public sealed class LowStockAlertTests(AlaraApiFactory factory) : ApiTestBase(factory)
{
    private async Task<int> RunJobAsync()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<LowStockAlertJob>().RunAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Low_stock_raises_one_alert_per_drop_and_restocking_resets_it()
    {
        var (client, s) = await ManagerAsync();
        var warehouse = await s.WarehouseAsync();
        var mask = await s.ProductAsync("ALR-MASK", 24m, name: "Miracle Mask");
        await s.ProductAsync("ALR-NEVER", 15m, name: "Never Stocked"); // no stock ever: no "out of stock" noise
        (await client.PutAsync($"/api/inventory/settings/{mask.Id}", new UpdateInventorySettingsRequest(3, false))).EnsureSuccessStatusCode();
        await s.ReceiveAsync(warehouse.Id, mask.Id, 5);

        Assert.Equal(0, await RunJobAsync());                              // 5 in stock, alert level 3
        await s.OrderAsync(warehouse.Id, confirm: true, (mask.Id, 2));     // 3 left
        Assert.Equal(1, await RunJobAsync());
        Assert.Equal(0, await RunJobAsync());                              // not repeated every minute
        await s.OrderAsync(warehouse.Id, confirm: true, (mask.Id, 3));     // 0 left, still the same drop
        Assert.Equal(0, await RunJobAsync());

        await s.ReceiveAsync(warehouse.Id, mask.Id, 10);                   // restocked above the level
        Assert.Equal(0, await RunJobAsync());
        await s.OrderAsync(warehouse.Id, confirm: true, (mask.Id, 9));     // 1 left: a new drop
        Assert.Equal(1, await RunJobAsync());

        var alerts = await client.GetJsonAsync<PagedResult<NotificationDto>>("/api/notifications");
        Assert.Equal(["Miracle Mask is low on stock: 1 piece left", "Miracle Mask is low on stock: 3 pieces left"],
            alerts.Items.Select(a => a.Title));
        var latest = alerts.Items[0];
        Assert.Equal(NotificationTypes.LowStock, latest.Type);
        Assert.Contains("Alert level: 3", latest.Body);
        Assert.Equal($"/inventory?product={mask.Id}", latest.Link);
        Assert.Equal(WhatsAppStatus.NotConfigured, Assert.Single(latest.WhatsApp).Status); // goes out on WhatsApp once connected
    }

    [Fact]
    public async Task With_no_alert_level_set_it_alerts_when_a_product_runs_out()
    {
        var (client, s) = await ManagerAsync();
        var warehouse = await s.WarehouseAsync();
        var mist = await s.ProductAsync("ALR-MIST", 20m, name: "Hair Mist");
        await s.ReceiveAsync(warehouse.Id, mist.Id, 2);
        await s.OrderAsync(warehouse.Id, confirm: true, (mist.Id, 2));
        using var stockKeeper = await SignedInAsAsync(SystemRoles.InventoryManager);
        using var viewer = await SignedInAsAsync(SystemRoles.Viewer);

        Assert.Equal(1, await RunJobAsync());

        var alert = Assert.Single((await stockKeeper.GetJsonAsync<PagedResult<NotificationDto>>("/api/notifications")).Items);
        Assert.Equal("Hair Mist is out of stock", alert.Title);
        Assert.Equal(1, (await viewer.GetJsonAsync<PagedResult<NotificationDto>>("/api/notifications")).TotalCount); // anyone who sees inventory
    }
}
