namespace GaoApp.Web.Configuration;

/// <summary>
/// Hosted service chạy 1 lần khi ứng dụng khởi động.
/// Nếu validation thất bại, app sẽ fail fast.
/// </summary>
public class StartupValidationHostedService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<StartupValidationHostedService> _logger;

    public StartupValidationHostedService(
        IServiceProvider serviceProvider,
        ILogger<StartupValidationHostedService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("StartupValidationHostedService bắt đầu chạy...");

        using var scope = _serviceProvider.CreateScope();

        var validator = scope.ServiceProvider.GetRequiredService<IStartupValidationService>();

        await validator.ValidateAsync(cancellationToken);

        _logger.LogInformation("StartupValidationHostedService chạy xong.");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}