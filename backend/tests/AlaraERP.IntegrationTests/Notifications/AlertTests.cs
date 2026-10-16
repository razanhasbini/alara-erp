using System.Net;
using AlaraERP.Application.Common;
using AlaraERP.Application.Common.Security;
using AlaraERP.Application.Notifications;
using AlaraERP.Application.Sales;
using AlaraERP.Domain.Notifications;
using AlaraERP.Domain.Sales;
using AlaraERP.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AlaraERP.IntegrationTests.Notifications;

public sealed class AlertTests(AlaraApiFactory factory) : ApiTestBase(factory)
{
    private async Task<(ErpClient Client, Scenario S, OrderDetailDto Order)> ExpiredReservationAsync()
    {
        var (client, s) = await ManagerAsync();
        var warehouse = await s.WarehouseAsync();
        var mask = await s.ProductAsync("ALR-MASK", 24m, name: "Miracle Mask");
        await s.ReceiveAsync(warehouse.Id, mask.Id, 5);
        var order = await s.OrderAsync(warehouse.Id, confirm: false, (mask.Id, 2));
        await s.ExecuteAsync("UPDATE sales.orders SET reserved_until = now() - interval '1 minute' WHERE id = @id", ("id", order.Id));
        return (client, s, order);
    }

    private static async Task<int> RunExpiryJobAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ReservationExpiryJob>().RunAsync(CancellationToken.None);
    }

    [Fact]
    public async Task An_expired_reservation_raises_one_alert_kept_in_the_app_even_without_whatsapp()
    {
        var (client, _, order) = await ExpiredReservationAsync();

        Assert.Equal(1, await RunExpiryJobAsync(Factory.Services));
        Assert.Equal(0, await RunExpiryJobAsync(Factory.Services)); // only once per deadline

        var alerts = await client.GetJsonAsync<PagedResult<NotificationDto>>("/api/notifications");
        var alert = Assert.Single(alerts.Items);
        Assert.Equal($"Reservation time is up: {order.OrderNumber}", alert.Title);
        Assert.Contains("2 × Miracle Mask", alert.Body);
        Assert.Equal($"/orders/{order.Id}", alert.Link);
        Assert.Equal(WhatsAppStatus.NotConfigured, Assert.Single(alert.WhatsApp).Status); // visible as "not sent"
        Assert.Equal(OrderStatus.Reserved, (await client.GetJsonAsync<OrderDetailDto>($"/api/orders/{order.Id}")).Status); // nothing auto-released
    }

    [Fact]
    public async Task Alerts_follow_permissions_and_each_employee_has_their_own_read_state()
    {
        var (manager, _, _) = await ExpiredReservationAsync();
        await RunExpiryJobAsync(Factory.Services);
        using var sales = await SignedInAsAsync(SystemRoles.SalesEmployee);
        using var stockKeeper = await SignedInAsAsync(SystemRoles.InventoryManager); // cannot see orders

        Assert.Equal(1, (await sales.GetJsonAsync<UnreadCount>("/api/notifications/unread-count")).Count);
        Assert.Equal(0, (await stockKeeper.GetJsonAsync<PagedResult<NotificationDto>>("/api/notifications")).TotalCount);

        var id = (await sales.GetJsonAsync<PagedResult<NotificationDto>>("/api/notifications")).Items.Single().Id;
        (await sales.PostAsync($"/api/notifications/{id}/read")).EnsureSuccessStatusCode();
        Assert.Equal(0, (await sales.GetJsonAsync<UnreadCount>("/api/notifications/unread-count")).Count);
        Assert.Equal(1, (await manager.GetJsonAsync<UnreadCount>("/api/notifications/unread-count")).Count); // still unread for the manager
        Assert.Empty((await sales.GetJsonAsync<PagedResult<NotificationDto>>("/api/notifications?unreadOnly=true")).Items);

        Assert.Equal(1, (await manager.PostAsync("/api/notifications/read-all").ContinueWith(t => t.Result.ReadJsonAsync<UnreadCount>()).Unwrap()).Count);
        Assert.Equal(HttpStatusCode.NotFound, (await stockKeeper.PostAsync($"/api/notifications/{id}/read")).StatusCode);
    }

    [Fact]
    public async Task Giving_more_time_rearms_the_alert_for_the_new_deadline()
    {
        var (client, s, order) = await ExpiredReservationAsync();
        await RunExpiryJobAsync(Factory.Services);

        (await client.PutAsync($"/api/orders/{order.Id}/reservation", new ReserveOrderRequest(Scenario.Tomorrow, null))).EnsureSuccessStatusCode();
        Assert.Equal(0, await RunExpiryJobAsync(Factory.Services));             // new deadline not reached
        await s.ExecuteAsync("UPDATE sales.orders SET reserved_until = now() - interval '1 minute' WHERE id = @id", ("id", order.Id));
        Assert.Equal(1, await RunExpiryJobAsync(Factory.Services));             // a second alert for the second deadline
    }

    [Fact]
    public async Task With_whatsapp_connected_messages_are_sent_to_every_recipient_and_failures_are_retried()
    {
        var sender = new FakeWhatsAppSender { FailFirstAttemptFor = "96170000002" };
        await using var app = Factory.WithWebHostBuilder(b =>
        {
            b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["WhatsApp:Enabled"] = "true", ["WhatsApp:PhoneNumberId"] = "123", ["WhatsApp:AccessToken"] = "test-token",
                ["WhatsApp:Recipients:0"] = "+961 70 000 001", ["WhatsApp:Recipients:1"] = "96170000002",
            }));
            b.ConfigureTestServices(services => services.AddSingleton<IWhatsAppSender>(sender));
        });
        await ExpiredReservationAsync();
        await RunExpiryJobAsync(app.Services);

        await using (var scope = app.Services.CreateAsyncScope())
        {
            var dispatcher = scope.ServiceProvider.GetRequiredService<WhatsAppDispatcher>();
            Assert.Equal(2, await dispatcher.SendPendingAsync(10, CancellationToken.None)); // one sent, one failed (will retry)
            Assert.Equal(1, await dispatcher.SendPendingAsync(10, CancellationToken.None)); // retry succeeds
            Assert.Equal(0, await dispatcher.SendPendingAsync(10, CancellationToken.None));
        }

        Assert.Equal(["96170000001", "96170000002", "96170000002"], sender.Sent.Select(m => m.To).Order());
        Assert.All(sender.Sent, m => Assert.StartsWith("Reservation time is up", m.Body));
        Assert.Equal(2, await new Scenario(NewClient(), Factory).CountAsync("SELECT count(*) FROM notifications.whatsapp_messages WHERE status = 'Sent'"));
    }

    private sealed record UnreadCount(int Count);

    private sealed class FakeWhatsAppSender : IWhatsAppSender
    {
        public string? FailFirstAttemptFor { get; init; }
        public List<(string To, string Body)> Sent { get; } = [];
        private bool _failed;

        public Task<WhatsAppSendResult> SendAsync(string toPhone, string body, CancellationToken ct)
        {
            lock (Sent)
            {
                Sent.Add((toPhone, body));
                if (toPhone == FailFirstAttemptFor && !_failed)
                {
                    _failed = true;
                    return Task.FromResult(new WhatsAppSendResult(false, null, "503: try later", Permanent: false));
                }
                return Task.FromResult(new WhatsAppSendResult(true, $"wamid.{Sent.Count}", null, false));
            }
        }
    }
}
