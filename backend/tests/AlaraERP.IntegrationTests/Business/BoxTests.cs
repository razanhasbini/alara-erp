using System.Net;
using AlaraERP.Application.Common;
using AlaraERP.Application.Common.Security;
using AlaraERP.Application.Inventory;
using AlaraERP.Application.Notifications;
using AlaraERP.Domain.Inventory;
using AlaraERP.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace AlaraERP.IntegrationTests.Business;

public sealed class BoxTests(AlaraApiFactory factory) : ApiTestBase(factory)
{
    private async Task<int> RunAlertJobAsync()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<BoxAlertJob>().RunAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Sales_staff_can_add_a_box_with_its_contents_and_keep_its_count()
    {
        var (_, s) = await ManagerAsync();
        var shampoo = await s.ProductAsync("ALR-CURL-SH", 16m, name: "Curl Defining Shampoo");
        var conditioner = await s.ProductAsync("ALR-CURL-CO", 14m, name: "Hydrating Curl Conditioner");
        using var diana = await SignedInAsAsync(SystemRoles.SalesEmployee);

        var box = await (await diana.PostAsync("/api/boxes", new SaveBoxRequest("Curls duo box", null, null, Box.DefaultLowStockThreshold,
            [new BoxContentInput(shampoo.Id, 1), new BoxContentInput(conditioner.Id, 1)], InitialCount: 1200))).OkJsonAsync<BoxDto>();
        var added = await (await diana.PostAsync($"/api/boxes/{box.Id}/count", new ChangeBoxCountRequest(BoxCountAction.Add, 300, "Supplier delivery"))).OkJsonAsync<BoxDto>();
        var used = await (await diana.PostAsync($"/api/boxes/{box.Id}/count", new ChangeBoxCountRequest(BoxCountAction.Remove, 50, "Damaged"))).OkJsonAsync<BoxDto>();
        var tooMany = await diana.PostAsync($"/api/boxes/{box.Id}/count", new ChangeBoxCountRequest(BoxCountAction.Remove, 5000, null));
        var counted = await (await diana.PostAsync($"/api/boxes/{box.Id}/count", new ChangeBoxCountRequest(BoxCountAction.Count, 1440, "Shelf count"))).OkJsonAsync<BoxDto>();
        var unchanged = await diana.PostAsync($"/api/boxes/{box.Id}/count", new ChangeBoxCountRequest(BoxCountAction.Count, 1440, null));

        Assert.Equal((1200, 500, StockStatus.InStock), (box.OnHand, box.LowStockThreshold, box.Status));
        Assert.Equal(["Curl Defining Shampoo", "Hydrating Curl Conditioner"], box.Contents.Select(c => c.ProductName));
        Assert.Equal((1500, 1450, 1440), (added.OnHand, used.OnHand, counted.OnHand));
        Assert.Equal("boxes.insufficient", await tooMany.ProblemCodeAsync());
        Assert.Equal("boxes.count_unchanged", await unchanged.ProblemCodeAsync());
        var history = await diana.GetJsonAsync<List<BoxMovementDto>>($"/api/boxes/{box.Id}/history");
        Assert.Equal([(BoxMovementType.Counted, -10, 1440), (BoxMovementType.Removed, -50, 1450), (BoxMovementType.Added, 300, 1500), (BoxMovementType.Added, 1200, 1200)],
            history.Select(h => (h.Type, h.QuantityChange, h.QuantityAfter)));
        Assert.Equal("Shelf count", history[0].Notes);
    }

    [Fact]
    public async Task Editing_a_box_changes_its_contents_alert_level_and_price_but_not_its_count()
    {
        var (client, s) = await ManagerAsync();
        var mist = await s.ProductAsync("ALR-MIST-TV", 20m, name: "Hair Mist – Tropical Vanilla");
        var other = await s.ProductAsync("ALR-MIST-OH", 20m, name: "Hair Mist – Oh Honey");
        var box = await (await client.PostAsync("/api/boxes", new SaveBoxRequest("Mists box", null, null, 500,
            [new BoxContentInput(mist.Id, 4)], InitialCount: 800))).OkJsonAsync<BoxDto>();
        await client.PostAsync("/api/boxes", new SaveBoxRequest("PR box", "Influencer packages", null, 500, [], InitialCount: 100));

        var edited = await (await client.PutAsync($"/api/boxes/{box.Id}", new SaveBoxRequest("4 mists box", null, 25m, 900,
            [new BoxContentInput(mist.Id, 2), new BoxContentInput(other.Id, 2)]))).OkJsonAsync<BoxDto>();
        var duplicate = await client.PutAsync($"/api/boxes/{box.Id}", new SaveBoxRequest("pr box", null, null, 500, []));

        Assert.Equal(("4 mists box", 25m, 900, 800, StockStatus.Low), (edited.Name, edited.Price, edited.LowStockThreshold, edited.OnHand, edited.Status));
        Assert.Equal([2, 2], edited.Contents.Select(c => c.Quantity));
        Assert.Equal("boxes.duplicate_name", await duplicate.ProblemCodeAsync());
    }

    [Fact]
    public async Task Boxes_for_an_order_include_boxes_made_for_products_inside_its_bundles_and_selling_never_uses_boxes()
    {
        var (client, s) = await ManagerAsync();
        var warehouse = await s.WarehouseAsync();
        var shampoo = await s.ProductAsync("ALR-CURL-SH", 16m, name: "Curl Defining Shampoo");
        var conditioner = await s.ProductAsync("ALR-CURL-CO", 14m, name: "Hydrating Curl Conditioner");
        var mask = await s.ProductAsync("ALR-MASK", 24m, name: "Miracle Mask");
        var duo = await s.BundleAsync("ALR-CURL-DUO", 28m, (shampoo.Id, 1), (conditioner.Id, 1));
        var shampooBox = await (await client.PostAsync("/api/boxes", new SaveBoxRequest("Shampoo box", null, null, 500,
            [new BoxContentInput(shampoo.Id, 1)], InitialCount: 600))).OkJsonAsync<BoxDto>();
        await client.PostAsync("/api/boxes", new SaveBoxRequest("Mask box", null, null, 500, [new BoxContentInput(mask.Id, 1)], InitialCount: 600));
        await s.ReceiveAsync(warehouse.Id, shampoo.Id, 5);
        await s.ReceiveAsync(warehouse.Id, conditioner.Id, 5);

        var forDuo = await client.GetJsonAsync<List<BoxDto>>($"/api/boxes/for-products?productIds={duo.Id}");
        await s.OrderAsync(warehouse.Id, confirm: true, (duo.Id, 2), (shampoo.Id, 1));

        Assert.Equal(["Shampoo box"], forDuo.Select(b => b.Name));
        Assert.Equal(600, (await client.GetJsonAsync<BoxDto>($"/api/boxes/{shampooBox.Id}")).OnHand);
    }

    [Fact]
    public async Task A_box_alerts_once_when_it_falls_to_its_alert_level()
    {
        var (client, _) = await ManagerAsync();
        var box = await (await client.PostAsync("/api/boxes", new SaveBoxRequest("Curls box", null, null, 500, [], InitialCount: 520))).OkJsonAsync<BoxDto>();
        await client.PostAsync("/api/boxes", new SaveBoxRequest("Never counted", null, null, 500, [])); // no count yet: no alert noise
        using var viewer = await SignedInAsAsync(SystemRoles.Viewer);

        Assert.Equal(0, await RunAlertJobAsync());
        await client.PostAsync($"/api/boxes/{box.Id}/count", new ChangeBoxCountRequest(BoxCountAction.Remove, 20, null)); // 500 left
        Assert.Equal(1, await RunAlertJobAsync());
        Assert.Equal(0, await RunAlertJobAsync());
        await client.PostAsync($"/api/boxes/{box.Id}/count", new ChangeBoxCountRequest(BoxCountAction.Add, 1000, null));
        Assert.Equal(0, await RunAlertJobAsync());
        await client.PostAsync($"/api/boxes/{box.Id}/count", new ChangeBoxCountRequest(BoxCountAction.Count, 499, null));
        Assert.Equal(1, await RunAlertJobAsync());

        var alerts = await viewer.GetJsonAsync<PagedResult<NotificationDto>>("/api/notifications");
        Assert.Equal(["Curls box is low: 499 boxes left", "Curls box is low: 500 boxes left"], alerts.Items.Select(a => a.Title));
        Assert.Equal(NotificationTypes.LowBoxes, alerts.Items[0].Type);
        Assert.Equal($"/boxes?box={box.Id}", alerts.Items[0].Link);
    }

    [Fact]
    public async Task Viewers_can_see_boxes_but_not_change_them()
    {
        var (client, _) = await ManagerAsync();
        var box = await (await client.PostAsync("/api/boxes", new SaveBoxRequest("PR box", null, 25m, 500, [], InitialCount: 40))).OkJsonAsync<BoxDto>();
        using var viewer = await SignedInAsAsync(SystemRoles.Viewer);

        var list = await viewer.GetJsonAsync<List<BoxDto>>("/api/boxes");
        var change = await viewer.PostAsync($"/api/boxes/{box.Id}/count", new ChangeBoxCountRequest(BoxCountAction.Add, 10, null));
        var create = await viewer.PostAsync("/api/boxes", new SaveBoxRequest("Other", null, null, 500, []));

        Assert.Equal(["PR box"], list.Select(b => b.Name));
        Assert.Equal(HttpStatusCode.Forbidden, change.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
    }

    [Fact]
    public async Task Box_history_cannot_be_rewritten_by_the_application_role()
    {
        var (client, _) = await ManagerAsync();
        await client.PostAsync("/api/boxes", new SaveBoxRequest("PR box", null, null, 500, [], InitialCount: 40));

        await using var conn = new Npgsql.NpgsqlConnection(Factory.AppConnectionString);
        await conn.OpenAsync();
        await using var cmd = new Npgsql.NpgsqlCommand("UPDATE inventory.box_movements SET quantity_change = 5000", conn);

        var ex = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => cmd.ExecuteNonQueryAsync());
        Assert.Equal("42501", ex.SqlState);
    }
}
