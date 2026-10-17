using AlaraERP.Application.Inventory;
using AlaraERP.Application.Notifications;
using AlaraERP.Application.Sales;

namespace AlaraERP.Api;

/// <summary>
/// Periodic work: raise low-stock and "reservation time is up" alerts and send queued WhatsApp messages.
/// Disabled with <c>Jobs:Enabled=false</c> (tests run the jobs directly instead).
/// </summary>
public sealed class BackgroundJobs(IServiceScopeFactory scopes, IConfiguration config, ILogger<BackgroundJobs> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!config.GetValue("Jobs:Enabled", true)) return;
        var interval = TimeSpan.FromSeconds(Math.Max(10, config.GetValue("Jobs:IntervalSeconds", 60)));
        using var timer = new PeriodicTimer(interval);
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var raised = await scope.ServiceProvider.GetRequiredService<LowStockAlertJob>().RunAsync(stoppingToken)
                             + await scope.ServiceProvider.GetRequiredService<ReservationExpiryJob>().RunAsync(stoppingToken);
                var sent = await scope.ServiceProvider.GetRequiredService<WhatsAppDispatcher>().SendPendingAsync(50, stoppingToken);
                if (raised + sent > 0) logger.LogInformation("Background jobs: {Raised} alert(s) raised, {Sent} WhatsApp message(s) processed.", raised, sent);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Background jobs failed; will retry on the next tick.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
