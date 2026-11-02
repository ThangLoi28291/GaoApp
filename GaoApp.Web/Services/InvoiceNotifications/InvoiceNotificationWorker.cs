using GaoApp.Application.InvoiceNotifications;
using GaoApp.Infrastructure.Tenant;
using GaoApp.Web.Services.CustomerService;

namespace GaoApp.Web.Services.InvoiceNotifications;

/// <summary>Scoped, bounded tenant processing. Disabling new sends still cancels/settles previously accepted jobs.</summary>
public sealed class InvoiceNotificationWorker(IServiceScopeFactory scopes, CustomerChannelRegistry registry,
    TimeProvider clock, ILogger<InvoiceNotificationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5), clock);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    // Include configured disabled connections so already accepted jobs can settle by GET only.
                    foreach (var storeId in registry.All().Where(c => c.Provider == "zalomarketing").Select(c => c.StoreId).Distinct())
                    {
                        for (var count = 0; count < 5 && !stoppingToken.IsCancellationRequested; count++)
                        {
                            using var scope = scopes.CreateScope();
                            scope.ServiceProvider.GetRequiredService<TenantContext>().SetStore(storeId, "invoice-notifications");
                            if (!await scope.ServiceProvider.GetRequiredService<InvoiceNotificationDispatcher>().ProcessNextAsync(stoppingToken)) break;
                        }
                    }
                }
                catch (Exception e) when (e is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
                { logger.LogWarning("Invoice notification worker unavailable: {Type}", e.GetType().Name); }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
