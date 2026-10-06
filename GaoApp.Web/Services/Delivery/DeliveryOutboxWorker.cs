using GaoApp.Infrastructure.Services.Delivery;

namespace GaoApp.Web.Services.Delivery;

public sealed class DeliveryOutboxWorker(IServiceScopeFactory scopes, IConfiguration configuration,
    ILogger<DeliveryOutboxWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("DeliveryOutbox:Enabled", true)) return;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    using var scope = scopes.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<DeliveryOutboxProcessor>().ProcessPendingAsync(ct: stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception ex) { logger.LogError(ex, "Delivery outbox checkpoint failed; unacknowledged events remain pending."); }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
