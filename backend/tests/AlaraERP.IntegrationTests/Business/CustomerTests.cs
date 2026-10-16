using AlaraERP.Application.Analytics;
using AlaraERP.Application.Common;
using AlaraERP.Application.Sales;
using AlaraERP.Domain.Sales;
using AlaraERP.IntegrationTests.Infrastructure;

namespace AlaraERP.IntegrationTests.Business;

public sealed class CustomerTests(AlaraApiFactory factory) : ApiTestBase(factory)
{
    [Fact]
    public async Task Orders_create_and_reuse_a_customer_by_normalized_phone()
    {
        var (client, scenario) = await ManagerAsync();
        var warehouse = await scenario.WarehouseAsync();
        var product = await scenario.ProductAsync("ALR-CUSTOMER", 20m);
        await scenario.ReceiveAsync(warehouse.Id, product.Id, 10);

        var first = await scenario.OrderAsync(warehouse.Id, confirm: true, (product.Id, 1));
        var second = await (await client.PostAsync("/api/orders", new CreateOrderRequest(
            OrderSource.Instagram, warehouse.Id, PaymentMethod.CashOnDelivery,
            new RecipientInput("Rima Saad", "70123456", "Beirut", "Another address"),
            0, 0, null, [new OrderLineInput(product.Id, 2)], Confirm: true))).OkJsonAsync<OrderDetailDto>();

        var customers = await client.GetJsonAsync<PagedResult<CustomerDto>>("/api/customers?search=70%20123%20456");
        Assert.NotNull(first.CustomerId);
        Assert.Equal(first.CustomerId, second.CustomerId);
        var customer = Assert.Single(customers.Items);
        Assert.Equal((2, 64m, 64m), (customer.OrderCount, customer.TotalSpent, customer.TotalPaid)); // paid when sold
    }

    [Fact]
    public async Task Loyalty_analytics_exposes_orders_spend_payments_and_reorder_frequency()
    {
        var (client, scenario) = await ManagerAsync();
        var warehouse = await scenario.WarehouseAsync();
        var product = await scenario.ProductAsync("ALR-LOYAL", 25m);
        await scenario.ReceiveAsync(warehouse.Id, product.Id, 10);
        var customer = await (await client.PostAsync("/api/customers", new CustomerRequest(
            "Nour Haddad", "+961 3 111 222", "nour@example.com", "Saida", null, null))).OkJsonAsync<CustomerDto>();

        async Task<OrderDetailDto> Order(int quantity) => await (await client.PostAsync("/api/orders", new CreateOrderRequest(
            OrderSource.WhatsApp, warehouse.Id, PaymentMethod.CashOnDelivery,
            new RecipientInput(customer.Name, customer.Phone, customer.City, null),
            0, 0, null, [new OrderLineInput(product.Id, quantity)], Confirm: true, CustomerId: customer.Id)))
            .OkJsonAsync<OrderDetailDto>();

        var paid = await Order(1);
        (await client.PostAsync($"/api/orders/{paid.Id}/ship", new TransitionRequest(null))).EnsureSuccessStatusCode();
        (await client.PostAsync($"/api/orders/{paid.Id}/deliver", new TransitionRequest(null))).EnsureSuccessStatusCode();
        await Order(2);

        var analytics = await client.GetJsonAsync<CustomerAnalyticsDto>("/api/analytics/customers");
        var row = Assert.Single(analytics.Customers);
        Assert.Equal(customer.Id, row.CustomerId);
        Assert.Equal((2, 1, 75m, 75m, 37.50m), // both orders were paid when sold
            (row.OrderCount, row.RepeatOrders, row.TotalOrdered, row.TotalPaid, row.AverageOrderValue));
        Assert.NotNull(row.AverageDaysBetweenOrders);
    }
}
