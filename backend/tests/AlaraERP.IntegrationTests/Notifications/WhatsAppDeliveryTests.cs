using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AlaraERP.Application.Common;
using AlaraERP.Application.Common.Security;
using AlaraERP.Application.Notifications;
using AlaraERP.Domain.Notifications;
using AlaraERP.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AlaraERP.IntegrationTests.Notifications;

/// <summary>The real Meta sender and webhook, with only Meta's servers replaced.</summary>
public sealed class WhatsAppDeliveryTests(AlaraApiFactory factory) : ApiTestBase(factory)
{
    private const string AppSecret = "test-app-secret";
    private const string VerifyToken = "test-verify-token";
    private const string AccessToken = "test-access-token";

    private WebApplicationFactory<Program> Connected(FakeMeta? meta = null) => Factory.WithWebHostBuilder(b =>
    {
        b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["WhatsApp:Enabled"] = "true", ["WhatsApp:PhoneNumberId"] = "123", ["WhatsApp:AccessToken"] = AccessToken,
            ["WhatsApp:Recipients:0"] = "+961 70 000 001", ["WhatsApp:Recipients:1"] = "03 000 002", // no country code: ignored
            ["WhatsApp:TemplateName"] = "erp_alert", ["WhatsApp:AppSecret"] = AppSecret, ["WhatsApp:WebhookVerifyToken"] = VerifyToken,
            ["App:FrontendUrl"] = "https://erp.test/",
        }));
        if (meta is not null)
            b.ConfigureTestServices(s => s.ConfigureHttpClientDefaults(h => h.ConfigurePrimaryHttpMessageHandler(() => meta)));
    });

    private static HttpClient Anonymous(WebApplicationFactory<Program> app) =>
        app.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });

    private static async Task<ErpClient> AdminAsync(WebApplicationFactory<Program> app, string role = SystemRoles.SuperAdmin)
    {
        var employee = await TestEmployees.CreateAsync(app.Services, [role]);
        var client = new ErpClient(app.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true, AllowAutoRedirect = false }));
        (await client.LoginAsync(employee.Email, employee.Password)).EnsureSuccessStatusCode();
        return client;
    }

    private static async Task<int> DispatchAsync(WebApplicationFactory<Program> app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<WhatsAppDispatcher>().SendPendingAsync(10, CancellationToken.None);
    }

    private static async Task<WhatsAppDeliveryDto> DeliveryAsync(ErpClient admin) =>
        Assert.Single((await admin.GetJsonAsync<PagedResult<NotificationDto>>("/api/notifications")).Items[0].WhatsApp);

    private static Task<HttpResponseMessage> ReportAsync(WebApplicationFactory<Program> app, string messageId, string status, int? errorCode = null, string secret = AppSecret)
    {
        var errors = errorCode is null ? "" : $$""","errors":[{"code":{{errorCode}},"title":"from meta"}]""";
        return PostSignedAsync(app, $$$"""{"entry":[{"changes":[{"value":{"statuses":[{"id":"{{{messageId}}}","status":"{{{status}}}"{{{errors}}}}]}}]}]}""", secret);
    }

    private static Task<HttpResponseMessage> PostSignedAsync(WebApplicationFactory<Program> app, string json, string secret = AppSecret)
    {
        var body = Encoding.UTF8.GetBytes(json);
        var content = new ByteArrayContent(body);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/whatsapp/webhook") { Content = content };
        request.Headers.Add("X-Hub-Signature-256", "sha256=" + Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body)));
        return Anonymous(app).SendAsync(request);
    }

    /// <summary>Sends the test message through the real path, leaving it accepted by Meta as "wamid.1".</summary>
    private async Task<(WebApplicationFactory<Program> App, ErpClient Admin)> SentMessageAsync()
    {
        var app = Connected(new FakeMeta());
        var admin = await AdminAsync(app);
        (await admin.PostAsync("/api/notifications/whatsapp/test")).EnsureSuccessStatusCode();
        await DispatchAsync(app);
        return (app, admin);
    }

    [Fact]
    public async Task A_message_goes_to_meta_as_the_template_with_flat_text_and_its_link()
    {
        var meta = new FakeMeta();
        await using var app = Connected(meta);
        using var admin = await AdminAsync(app);

        (await admin.PostAsync("/api/notifications/whatsapp/test")).EnsureSuccessStatusCode();
        Assert.Equal(1, await DispatchAsync(app));

        var request = Assert.Single(meta.Requests);
        Assert.Equal("https://graph.facebook.com/v26.0/123/messages", request.Url);
        Assert.Equal($"Bearer {AccessToken}", request.Authorization);
        using var body = JsonDocument.Parse(request.Body);
        Assert.Equal("96170000001", body.RootElement.GetProperty("to").GetString()); // the number without a country code is left out
        var template = body.RootElement.GetProperty("template");
        Assert.Equal("erp_alert", template.GetProperty("name").GetString());
        Assert.Equal("WhatsApp test message - If you can read this, ERP alerts are reaching this number. https://erp.test/alerts",
            template.GetProperty("components")[0].GetProperty("parameters")[0].GetProperty("text").GetString());
        Assert.Equal(WhatsAppStatus.Sent, (await DeliveryAsync(admin)).Status);
    }

    [Fact]
    public async Task A_problem_that_retrying_cant_fix_fails_at_once_and_says_what_to_do()
    {
        await using var app = Connected(new FakeMeta { Reply = (HttpStatusCode.BadRequest, """{"error":{"code":132001,"message":"Template name does not exist"}}""") });
        using var admin = await AdminAsync(app);
        (await admin.PostAsync("/api/notifications/whatsapp/test")).EnsureSuccessStatusCode();

        await DispatchAsync(app);

        var delivery = await DeliveryAsync(admin);
        Assert.Equal(WhatsAppStatus.Failed, delivery.Status);
        Assert.Contains("WhatsApp__TemplateName", delivery.LastError);
        Assert.Equal(1, (await admin.GetJsonAsync<NotificationChannelsDto>("/api/notifications/channels")).WhatsAppNotSent);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, """{"error":{"code":130429,"message":"Rate limit hit"}}""")] // Meta sends rate limits as a 400
    [InlineData(HttpStatusCode.ServiceUnavailable, "upstream unavailable")]
    public async Task A_temporary_problem_is_retried_later_not_straight_away(HttpStatusCode status, string reply)
    {
        var meta = new FakeMeta { Reply = (status, reply) };
        await using var app = Connected(meta);
        using var admin = await AdminAsync(app);
        (await admin.PostAsync("/api/notifications/whatsapp/test")).EnsureSuccessStatusCode();

        Assert.Equal(1, await DispatchAsync(app));
        Assert.Equal(0, await DispatchAsync(app)); // waiting
        Assert.Equal(WhatsAppStatus.Pending, (await DeliveryAsync(admin)).Status);

        meta.Reply = (HttpStatusCode.OK, """{"messages":[{"id":"wamid.later"}]}""");
        await new Scenario(NewClient(), Factory).ExecuteAsync("UPDATE notifications.whatsapp_messages SET next_attempt_at = now() - interval '1 second'");
        Assert.Equal(1, await DispatchAsync(app));
        Assert.Equal(WhatsAppStatus.Sent, (await DeliveryAsync(admin)).Status);
    }

    [Fact]
    public async Task An_unreadable_reply_to_a_successful_send_does_not_send_it_again()
    {
        var meta = new FakeMeta { Reply = (HttpStatusCode.OK, "not json") };
        await using var app = Connected(meta);
        using var admin = await AdminAsync(app);
        (await admin.PostAsync("/api/notifications/whatsapp/test")).EnsureSuccessStatusCode();

        await DispatchAsync(app);

        Assert.Equal(0, await DispatchAsync(app));
        Assert.Single(meta.Requests);
    }

    [Fact]
    public async Task The_webhook_handshake_only_answers_the_right_verify_token()
    {
        await using var app = Connected();
        using var meta = Anonymous(app);
        using var unconfigured = Anonymous(Factory);

        var right = await meta.GetAsync($"/api/whatsapp/webhook?hub.mode=subscribe&hub.verify_token={VerifyToken}&hub.challenge=12345");
        Assert.Equal(HttpStatusCode.OK, right.StatusCode);
        Assert.Equal("12345", await right.Content.ReadAsStringAsync());
        Assert.Equal("text/plain", right.Content.Headers.ContentType?.MediaType);

        Assert.Equal(HttpStatusCode.Forbidden, (await meta.GetAsync("/api/whatsapp/webhook?hub.mode=subscribe&hub.verify_token=wrong&hub.challenge=12345")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await unconfigured.GetAsync("/api/whatsapp/webhook?hub.mode=subscribe&hub.verify_token=&hub.challenge=12345")).StatusCode);
    }

    [Fact]
    public async Task A_report_not_signed_by_meta_changes_nothing()
    {
        var (app, admin) = await SentMessageAsync();
        await using (app)
        using (admin)
        {
            var forged = await ReportAsync(app, "wamid.1", "failed", 131026, secret: "guessed-secret");
            using var unsigned = Anonymous(app);
            var noSignature = await unsigned.PostAsync("/api/whatsapp/webhook", new StringContent("""{"entry":[]}"""));

            Assert.Equal(HttpStatusCode.Forbidden, forged.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, noSignature.StatusCode);
            Assert.Equal(WhatsAppStatus.Sent, (await DeliveryAsync(admin)).Status);
        }
    }

    [Fact]
    public async Task Without_the_app_secret_every_report_is_refused()
    {
        var response = await PostSignedAsync(Factory, """{"entry":[]}""", secret: "");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Delivery_reports_show_how_far_a_message_got_and_never_move_it_backwards()
    {
        var (app, admin) = await SentMessageAsync();
        await using (app)
        using (admin)
        {
            (await ReportAsync(app, "wamid.1", "delivered")).EnsureSuccessStatusCode();
            Assert.Equal(WhatsAppStatus.Delivered, (await DeliveryAsync(admin)).Status);

            (await ReportAsync(app, "wamid.1", "read")).EnsureSuccessStatusCode();
            (await ReportAsync(app, "wamid.1", "delivered")).EnsureSuccessStatusCode(); // arrived late
            (await ReportAsync(app, "wamid.1", "failed", 131026)).EnsureSuccessStatusCode();
            Assert.Equal(WhatsAppStatus.Read, (await DeliveryAsync(admin)).Status);

            (await ReportAsync(app, "wamid.unknown", "read")).EnsureSuccessStatusCode(); // not ours: Meta must not keep sending it
            (await PostSignedAsync(app, "not json")).EnsureSuccessStatusCode();
            (await PostSignedAsync(app, """{"entry":[{"changes":[{"value":{"messages":[{"from":"96170000009"}]}}]}]}""")).EnsureSuccessStatusCode();
        }
    }

    [Fact]
    public async Task A_message_meta_accepted_but_could_not_deliver_can_be_sent_again()
    {
        var (app, admin) = await SentMessageAsync();
        await using (app)
        using (admin)
        {
            (await ReportAsync(app, "wamid.1", "failed", 131047)).EnsureSuccessStatusCode();
            var failed = await DeliveryAsync(admin);
            Assert.Equal(WhatsAppStatus.Failed, failed.Status);
            Assert.Contains("24 hours", failed.LastError);

            var alert = (await admin.GetJsonAsync<PagedResult<NotificationDto>>("/api/notifications")).Items[0];
            (await admin.PostAsync($"/api/notifications/{alert.Id}/resend-whatsapp")).EnsureSuccessStatusCode();
            (await ReportAsync(app, "wamid.1", "delivered")).EnsureSuccessStatusCode(); // about the old attempt
            Assert.Equal(WhatsAppStatus.Pending, (await DeliveryAsync(admin)).Status);
        }
    }

    [Fact]
    public async Task Only_the_administrator_sees_the_setup_and_it_never_contains_the_secrets()
    {
        await using var app = Connected();
        using var admin = await AdminAsync(app);
        using var manager = await AdminAsync(app, SystemRoles.Manager);

        var response = await admin.GetAsync("/api/notifications/whatsapp/setup");
        var json = await response.Content.ReadAsStringAsync();
        var setup = await admin.GetJsonAsync<WhatsAppSetupDto>("/api/notifications/whatsapp/setup");

        Assert.True(setup.Connected);
        Assert.Empty(setup.MissingSettings);
        Assert.Equal("••••••••001", Assert.Single(setup.Recipients));
        Assert.Equal(1, setup.IgnoredRecipients);
        Assert.True(setup.DeliveryReportsReady);
        Assert.DoesNotContain(AccessToken, json);
        Assert.DoesNotContain(AppSecret, json);
        Assert.DoesNotContain(VerifyToken, json);
        Assert.DoesNotContain("96170000001", json);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.GetAsync("/api/notifications/whatsapp/setup")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.PostAsync("/api/notifications/whatsapp/test")).StatusCode);
    }

    [Fact]
    public async Task A_test_message_needs_a_connection_and_cant_be_repeated_right_away()
    {
        using var notConnected = await SignedInAsAsync(SystemRoles.SuperAdmin);
        Assert.Equal("whatsapp.not_configured", await (await notConnected.PostAsync("/api/notifications/whatsapp/test")).ProblemCodeAsync());
        var missing = await notConnected.GetJsonAsync<WhatsAppSetupDto>("/api/notifications/whatsapp/setup");
        Assert.Contains("WhatsApp__Enabled", missing.MissingSettings);

        await using var app = Connected(new FakeMeta());
        using var admin = await AdminAsync(app);
        (await admin.PostAsync("/api/notifications/whatsapp/test")).EnsureSuccessStatusCode();
        Assert.Equal("whatsapp.test_too_soon", await (await admin.PostAsync("/api/notifications/whatsapp/test")).ProblemCodeAsync());
    }

    public sealed class FakeMeta : HttpMessageHandler
    {
        public (HttpStatusCode Status, string Body)? Reply { get; set; }
        public List<(string Url, string? Authorization, string Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = await request.Content!.ReadAsStringAsync(ct);
            lock (Requests) Requests.Add((request.RequestUri!.ToString(), request.Headers.Authorization?.ToString(), body));
            var (status, reply) = Reply ?? (HttpStatusCode.OK, $$"""{"messages":[{"id":"wamid.{{Requests.Count}}"}]}""");
            return new HttpResponseMessage(status) { Content = new StringContent(reply) };
        }

        // The app's HTTP client factory disposes handlers it created; this one outlives the app so a test can read it.
        protected override void Dispose(bool disposing) { }
    }
}
