using System.Net;
using AlaraERP.Application.Activity;
using AlaraERP.Application.Common;
using AlaraERP.Application.Common.Security;
using AlaraERP.Application.Sales;
using AlaraERP.Domain.Activity;
using AlaraERP.IntegrationTests.Infrastructure;

namespace AlaraERP.IntegrationTests.Business;

public sealed class ActivityLogTests(AlaraApiFactory factory) : ApiTestBase(factory)
{
    private Task<PagedResult<ActivityEntryDto>> LogAsync(ErpClient admin, string query = "") =>
        admin.GetJsonAsync<PagedResult<ActivityEntryDto>>($"/api/activity{query}");

    [Fact]
    public async Task Everyday_work_reads_as_plain_sentences_with_who_did_it()
    {
        var (admin, _) = await AdminAsync();
        var (manager, s) = await ManagerAsync();
        var warehouse = await s.WarehouseAsync();
        var mask = await s.ProductAsync("ALR-MASK", 24m, name: "Miracle Mask");
        var receipt = await s.ReceiveAsync(warehouse.Id, mask.Id, 60);
        var order = await s.OrderAsync(warehouse.Id, confirm: true, (mask.Id, 2));
        (await manager.PostAsync("/api/customers", new CustomerRequest("Nour Haddad", "+961 71 222 333", null, "Tripoli", null, null)))
            .EnsureSuccessStatusCode();

        var entries = (await LogAsync(admin)).Items;
        var mine = entries.Where(e => e.Who == "Test Manager").ToList();
        Assert.Contains(mine, e => e.Area == ActivityArea.Products && e.Summary.StartsWith("added product Miracle Mask at $24.00"));
        var restock = Assert.Single(mine, e => e.Summary.StartsWith("restocked"));
        Assert.Matches(@"^restocked 60 × Miracle Mask from Supplier, received on \d\d \w{3} \d{4} \(receipt ", restock.Summary);
        Assert.Contains($"(receipt {receipt.ReceiptNumber})", restock.Summary);
        Assert.Equal($"/inventory/receipts/{receipt.Id}", restock.Link);
        var entered = Assert.Single(mine, e => e.Summary.StartsWith("entered order"));
        Assert.Equal($"entered order {order.OrderNumber} for Rima Saad (a new customer): 2 × Miracle Mask — total $48.00", entered.Summary);
        Assert.Equal($"/orders/{order.Id}", entered.Link);
        Assert.Contains(mine, e => e.Summary == "added customer Nour Haddad (+96171222333)");
        Assert.Contains(entries, e => e.Area == ActivityArea.SignIn && e.Summary == "signed in" && e.Who == "Test Manager");

        // Newest first, and the filters narrow it down.
        Assert.True(entries.Zip(entries.Skip(1)).All(p => p.First.At >= p.Second.At));
        var orders = (await LogAsync(admin, "?area=Orders")).Items;
        Assert.All(orders, e => Assert.Equal(ActivityArea.Orders, e.Area));
        Assert.Single((await LogAsync(admin, "?search=nour")).Items);
    }

    [Fact]
    public async Task Opening_a_page_is_noted_once_per_half_hour()
    {
        var (admin, _) = await AdminAsync();
        using var viewer = await SignedInAsAsync(SystemRoles.Viewer);

        for (var i = 0; i < 3; i++)
            Assert.Equal(HttpStatusCode.NoContent, (await viewer.PostAsync("/api/activity/viewed", new ViewedRequest(ViewedPage.Inventory))).StatusCode);
        await viewer.PostAsync("/api/activity/viewed", new ViewedRequest(ViewedPage.Orders));

        var viewed = (await LogAsync(admin, "?area=Viewed")).Items;
        Assert.Equal(["looked at the orders", "checked the inventory"], viewed.Select(e => e.Summary));
        Assert.All(viewed, e => Assert.Equal("Test Viewer", e.Who));
    }

    [Fact]
    public async Task Only_the_administrator_can_read_the_log()
    {
        var (manager, _) = await ManagerAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.GetAsync("/api/activity")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await NewClient().GetAsync("/api/activity")).StatusCode);
    }
}
