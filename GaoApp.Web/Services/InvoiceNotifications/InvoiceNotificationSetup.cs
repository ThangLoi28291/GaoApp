using GaoApp.Application.InvoiceNotifications;
using GaoApp.Infrastructure.Services.InvoiceNotifications;
using GaoApp.Application.Interfaces.Services.Invoices;
using GaoApp.Web.Services.CustomerService;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GaoApp.Web.Services.InvoiceNotifications;

public static class InvoiceNotificationSetup
{
    public static IServiceCollection AddInvoiceNotifications(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<InvoiceNotificationOptions>().Bind(configuration.GetSection(InvoiceNotificationOptions.SectionName))
            .Validate(o => o.PilotPhone == "0398946174" && o.LeaseSeconds is >= 30 and <= 300, "Invoice notification pilot configuration is invalid.")
            .ValidateOnStart();
        services.AddOptions<InvoiceNotificationArtifactOptions>().Bind(configuration.GetSection(InvoiceNotificationArtifactOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.RootDirectory) && o.MaximumStoredBytes >= 50L * 1024 * 1024,
                "Invoice notification private artifact configuration is invalid.").ValidateOnStart();
        services.AddOptions<InvoiceOfficialDocumentLinkOptions>().Bind(configuration.GetSection(InvoiceOfficialDocumentLinkOptions.SectionName))
            .Validate(o => InvoiceOfficialDocumentLinks.ValidConfiguration(o.PublicBaseUrl), "Official invoice link public URL is invalid.").ValidateOnStart();
        services.AddDataProtection();
        services.AddSingleton<InvoiceOfficialDocumentLinks>();
        services.AddSingleton<IInvoiceOfficialDocumentLinkFactory>(p => p.GetRequiredService<InvoiceOfficialDocumentLinks>());
        services.AddScoped<IInvoiceOfficialDocumentLinkAuthorizer>(p => p.GetRequiredService<EfInvoiceNotificationStore>());
        services.AddScoped<IBoundedInvoiceFileStorage>(p => p.GetRequiredService<IInvoiceFileStorage>() is IBoundedInvoiceFileStorage bounded
            ? bounded : throw new InvalidOperationException("Bounded invoice source storage is required."));
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<EfInvoiceNotificationStore>();
        services.AddScoped<IInvoiceNotificationStore>(p => p.GetRequiredService<EfInvoiceNotificationStore>());
        services.AddScoped<IInvoiceNotificationOutbox>(p => p.GetRequiredService<EfInvoiceNotificationStore>());
        services.AddScoped<IInvoiceNotificationCapture, EfInvoiceNotificationCapture>();
        services.AddSingleton<PrivateInvoicePreparedAssetStore>();
        services.AddSingleton<IInvoicePreparedAssetReader>(p => p.GetRequiredService<PrivateInvoicePreparedAssetStore>());
        services.AddScoped<IInvoiceNotificationBundleBuilder, InvoiceNotificationBundleBuilder>();
        services.AddHttpClient<InvoiceNotificationTransport>(c => { c.Timeout = TimeSpan.FromSeconds(45); c.MaxResponseContentBufferSize = 1024 * 1024; })
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }).RemoveAllLoggers();
        services.AddScoped<InvoiceNotificationDispatcher>();
        services.AddScoped<ICustomerInvoiceRecipientSource, CustomerInvoiceRecipientSource>();
        services.AddScoped<ICustomerInvoiceNotificationAdmin, CustomerInvoiceNotificationAdminService>();
        services.AddHostedService<InvoiceNotificationWorker>();
        return services;
    }
}
