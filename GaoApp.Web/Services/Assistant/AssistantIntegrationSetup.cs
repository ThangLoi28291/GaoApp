namespace GaoApp.Web.Services.Assistant;

public static class AssistantIntegrationSetup
{
    public static IServiceCollection AddAssistantIntegration(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AssistantOptions>(configuration.GetSection(AssistantOptions.Section));
        services.AddSingleton<AssistantJwt>();
        services.AddSingleton<AssistantStoreSettings>();
        services.AddScoped<AssistantAccess>();
        services.AddScoped<AssistantCatalogService>();
        services.AddScoped<AssistantAvailabilityService>();
        services.AddSingleton<AssistantSearchKnowledge>();
        services.AddSingleton<AssistantPolicyKnowledge>();
        services.AddScoped<AssistantSourceFilter>();
        GaoApp.Web.Services.CustomerService.CustomerServiceSetup.AddCustomerService(services, configuration);
        return services;
    }
}
