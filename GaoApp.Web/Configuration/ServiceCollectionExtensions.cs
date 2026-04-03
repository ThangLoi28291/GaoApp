namespace GaoApp.Web.Configuration;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddStartupValidation(this IServiceCollection services)
    {
        services.AddScoped<IStartupValidationService, StartupValidationService>();
        services.AddHostedService<StartupValidationHostedService>();

        return services;
    }
}