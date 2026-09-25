using Microsoft.AspNetCore.SignalR;

namespace GaoApp.Web.Hubs;

public static class PosRealtimeSecurity
{
    public static void AddSecuredPosRealtime(this IServiceCollection services)
    {
        services.AddSignalR().AddHubOptions<PosHub>(options => options.AddFilter<PosSessionHubFilter>());
        services.AddSingleton<PosRealtimeSessionValidator>();
        services.AddSingleton<RevocablePosHubLifetimeManager>();
        services.AddSingleton<HubLifetimeManager<PosHub>>(sp => sp.GetRequiredService<RevocablePosHubLifetimeManager>());
        services.AddHostedService<PosSessionRevocationWorker>();
    }
}

public sealed class PosSessionHubFilter(RevocablePosHubLifetimeManager lifetime) : IHubFilter
{
    public async ValueTask<object?> InvokeMethodAsync(HubInvocationContext invocationContext,
        Func<HubInvocationContext, ValueTask<object?>> next)
    {
        // Hub invocations have fresh DI scopes without the HTTP tenant middleware's scoped tenant.
        // Check the operation against the freshly validated connection's store and grants instead.
        await lifetime.EnsureCurrentAsync(invocationContext.Context.ConnectionId, invocationContext.Context.ConnectionAborted,
            requirePaymentPermission: invocationContext.HubMethodName == nameof(PosHub.BroadcastTerminalEvent));
        return await next(invocationContext);
    }
}

public sealed class PosSessionRevocationWorker(RevocablePosHubLifetimeManager lifetime,
    ILogger<PosSessionRevocationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try { await lifetime.RevalidateConnectionsAsync(stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception ex)
                { logger.LogWarning("POS connection access check failed; affected connections closed. ExceptionType={ExceptionType}", ex.GetType().Name); }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
    }
}
