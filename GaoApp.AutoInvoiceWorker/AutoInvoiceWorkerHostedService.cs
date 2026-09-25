using GaoApp.Application.Interfaces.Repositories.Invoices;
using GaoApp.Application.Interfaces.Services.Invoices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

internal sealed class AutoInvoiceWorkerHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AutoInvoiceWorkerHostedService> _logger;

    public AutoInvoiceWorkerHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<AutoInvoiceWorkerHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("GaoApp auto invoice worker started.");
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        try
        {
            do
            {
                await RunCycleAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("GaoApp auto invoice worker stopping.");
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "GaoApp auto invoice worker stopped unexpectedly.");
            throw;
        }
        finally
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                scope.ServiceProvider.GetRequiredService<IAutoInvoiceService>()
                    .MarkWorkerStoppedAsync(CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Không ghi được trạng thái worker đã dừng.");
            }
        }
    }

    private async Task RunCycleAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IAutoInvoiceService>();
        try
        {
            await service.RunOnceAsync(force: false, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // A temporary SQL/network outage must not terminate the Windows
            // Service. The next timer tick retries with a fresh scope; when
            // SQL is available the persisted worker heartbeat resumes.
            _logger.LogError(ex, "Auto invoice worker cycle failed; sẽ thử lại ở chu kỳ kế tiếp.");
        }
    }
}
