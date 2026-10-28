using System.Net;
using AlaraERP.Application.Common.Security;
using AlaraERP.Application.Sales;
using AlaraERP.IntegrationTests.Infrastructure;

namespace AlaraERP.IntegrationTests.Business;

/// <summary>
/// Every GET the SPA uses must answer 200 with data present. Catches query-translation errors
/// (EF can't translate some projections) that only appear once rows exist.
/// </summary>
public sealed class ReadEndpointSmokeTests(AlaraApiFactory factory) : ApiTestBase(factory)
{
    [Fact]
    public async Task All_list_and_detail_endpoints_respond_with_data_present()
    {
        using var admin = await SignedInAsAsync(SystemRoles.SuperAdmin);
        var s = new Scenario(admin, Factory);
        var warehouse = await s.WarehouseAsync();
        await s.WarehouseAsync("SHOP");
        var mask = await s.ProductAsync("ALR-HC-MASK", 24m);
        var receipt = await s.ReceiveAsync(warehouse.Id, mask.Id, 10);
        var order = await s.OrderAsync(warehouse.Id, confirm: true, (mask.Id, 1));

        string[] urls =
        [
            "/api/settings", "/api/dashboard", "/api/auth/me",
            "/api/categories?includeInactive=true", "/api/products", $"/api/products/{mask.Id}", "/api/products?type=Bundle",
            "/api/warehouses", "/api/warehouses?includeInactive=true", $"/api/warehouses/{warehouse.Id}",
            "/api/inventory", "/api/inventory?filter=Low", "/api/inventory?filter=Expiring&sort=expiry", $"/api/inventory/products/{mask.Id}",
            $"/api/inventory/availability?productIds={mask.Id}", $"/api/inventory/settings/{mask.Id}",
            "/api/inventory/receipts", $"/api/inventory/receipts/{receipt.Id}", "/api/inventory/transfers", "/api/inventory/movements",
            "/api/orders", "/api/orders?statuses=Reserved&statuses=Confirmed", $"/api/orders/reserved-stock?productIds={mask.Id}",
            "/api/notifications", "/api/notifications/unread-count", "/api/notifications/channels", $"/api/orders/{order.Id}",
            "/api/expenses", "/api/expenses?from=2026-01-01&to=2026-12-31&category=Wages",
            "/api/users", "/api/roles", "/api/roles/permissions",
            "/api/analytics/summary", "/api/analytics/summary?groupBy=Week", "/api/analytics/summary?from=2026-01-01&to=2026-12-31&groupBy=Month",
            "/api/analytics/products",
        ];
        foreach (var url in urls)
        {
            var response = await admin.GetAsync(url);
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"GET {url} → {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        }
    }

    [Fact]
    public async Task Health_check_reaches_the_database_and_responses_carry_security_headers()
    {
        using var anonymous = NewClient();
        var response = await anonymous.Http.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("frame-ancestors 'none'", response.Headers.GetValues("Content-Security-Policy").Single());
    }
}
