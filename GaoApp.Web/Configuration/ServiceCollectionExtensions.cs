namespace GaoApp.Web.Configuration;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddStartupValidation(this IServiceCollection services)
    {
        services.AddScoped<IStartupValidationService, StartupValidationService>();
        return services;
    }

    /// <summary>
    /// Chạy đúng IStartupValidationService đã đăng ký trước mọi migration/seed.
    /// Không dùng hosted service vì hosted service chỉ chạy khi app.Run bắt đầu.
    /// </summary>
    public static async Task ValidateStartupAsync(
        this WebApplication app,
        CancellationToken cancellationToken = default)
    {
        using var scope = app.Services.CreateScope();
        var validator = scope.ServiceProvider
            .GetRequiredService<IStartupValidationService>();

        await validator.ValidateAsync(cancellationToken);
    }
}
