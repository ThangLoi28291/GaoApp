using Microsoft.Extensions.Options;
using GaoApp.Infrastructure.Services.CustomerMarketing;

namespace GaoApp.Web.Services.CustomerService;

public static class CustomerServiceSetup
{
    public static IServiceCollection AddCustomerService(this IServiceCollection services,IConfiguration configuration) {
        services.AddOptions<CustomerServiceOptions>().Bind(configuration.GetSection(CustomerServiceOptions.Section)).PostConfigure(o=>{
            if(string.IsNullOrWhiteSpace(configuration["CustomerService:DatabasePath"]))o.DatabasePath=Path.Combine(configuration["Assistant:StoreSettingsDirectory"]??"App_Data/Assistant","customer-service.db");
            o.StorageId=configuration["Assistant:StorageId"]??"";
        }).Validate(o=>o.Valid(),"Invalid customer service storage configuration.").ValidateOnStart();
        services.AddSingleton<IValidateOptions<CustomerChannelOptions>,CustomerChannelCredentialValidator>();
        services.AddOptions<CustomerChannelOptions>().Bind(configuration.GetSection(CustomerChannelOptions.Section)).Validate(o=>o.Valid(),"Invalid customer channel configuration.").ValidateOnStart();
        services.AddSingleton<CustomerChannelRegistry>();services.AddScoped<CustomerChannelRunner>();
        services.AddScoped<CustomerMarketingConsentWriter>();
        services.AddScoped<CustomerMarketingCampaignPreviewStore>();
        services.AddScoped<CustomerMarketingCampaignDispatchThrottle>();
        services.AddOptions<CustomerMarketingCampaignOptions>().Bind(configuration.GetSection(CustomerMarketingCampaignOptions.Section))
            .Validate(o=>o.Valid(),"Invalid customer marketing campaign configuration.").ValidateOnStart();
        services.AddScoped<CustomerMarketingCampaignDispatcher>();
        services.AddHttpClient<CustomerMarketingCampaignTransport>(c=>{c.Timeout=TimeSpan.FromSeconds(45);c.MaxResponseContentBufferSize=1024*1024;})
            .ConfigurePrimaryHttpMessageHandler(()=>new HttpClientHandler{AllowAutoRedirect=false,UseCookies=false});
        services.AddHostedService<CustomerMarketingCampaignWorker>();
        services.AddHttpClient<CustomerChannelTransport>(c=>{c.Timeout=TimeSpan.FromSeconds(45);c.MaxResponseContentBufferSize=1024*1024;}).ConfigurePrimaryHttpMessageHandler(()=>new HttpClientHandler{AllowAutoRedirect=false,UseCookies=false});
        services.AddHostedService<CustomerChannelWorker>();
        services.AddSingleton<CustomerServiceStore>();services.AddScoped<CustomerBusinessRead>();services.AddScoped<CustomerServiceAccess>();services.AddScoped<CustomerSession>();
        services.AddHttpClient<CustomerAiBridge>(c=>{c.Timeout=TimeSpan.FromSeconds(105);c.MaxResponseContentBufferSize=1024*1024;})
            .ConfigurePrimaryHttpMessageHandler(()=>new HttpClientHandler{AllowAutoRedirect=false,UseCookies=false});
        services.AddHostedService<CustomerServiceCleanup>();return services;
    }
}
public sealed class CustomerServiceCleanup(CustomerServiceStore store,IWebHostEnvironment environment,IOptionsMonitor<CustomerServiceOptions> options,ILogger<CustomerServiceCleanup> logger):BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop) {
        using var timer=new PeriodicTimer(TimeSpan.FromSeconds(options.CurrentValue.CleanupIntervalSeconds));
        while(await timer.WaitForNextTickAsync(stop)) {
            try{if(File.Exists(Path.GetFullPath(options.CurrentValue.DatabasePath,environment.ContentRootPath)))store.RunCleanup(stop);}
            catch(Exception e)when(e is not OperationCanceledException){logger.LogWarning("Customer conversation cleanup unavailable: {Type}",e.GetType().Name);}
        }
    }
}
