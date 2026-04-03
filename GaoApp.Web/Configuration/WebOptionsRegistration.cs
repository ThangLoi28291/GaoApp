using GaoApp.Application.Common;
using GaoApp.Application.Common.Options;
using Microsoft.Extensions.Options;

namespace GaoApp.Web.Configuration;

/// <summary>
/// Đăng ký bind + validate các options của Web/App.
/// </summary>
public static class WebOptionsRegistration
{
    public static IServiceCollection AddWebAndAppOptions(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // =========================================================
        // ConnectionStrings
        // =========================================================
        services.AddOptions<ConnectionStringOptions>()
            .Bind(configuration.GetSection(ConnectionStringOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(
                x => !string.IsNullOrWhiteSpace(x.DefaultConnection),
                "ConnectionStrings:DefaultConnection không được để trống.")
            .ValidateOnStart();

        // =========================================================
        // AppUrl
        // =========================================================
        services.AddOptions<AppUrlOptions>()
            .Bind(configuration.GetSection(AppUrlOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(
                x => Uri.TryCreate(x.BaseUrl, UriKind.Absolute, out _),
                "AppUrl:BaseUrl phải là absolute URL hợp lệ.")
            .ValidateOnStart();

        // =========================================================
        // Tenant
        // =========================================================
        services.AddOptions<TenantOptions>()
            .Bind(configuration.GetSection(TenantOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(
                x => !x.RootDomain.StartsWith("http://") && !x.RootDomain.StartsWith("https://"),
                "Tenant:RootDomain chỉ chứa domain, không kèm http/https.")
            .Validate(
                x => !string.IsNullOrWhiteSpace(x.AdminSubdomain),
                "Tenant:AdminSubdomain không được để trống.")
            .ValidateOnStart();

        // =========================================================
        // Storage
        // =========================================================
        services.AddOptions<StorageOptions>()
            .Bind(configuration.GetSection(StorageOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(
                x => !string.IsNullOrWhiteSpace(x.UploadRoot),
                "Storage:UploadRoot không được để trống.")
            .ValidateOnStart();

        // =========================================================
        // SeedData
        // =========================================================
        services.AddOptions<SeedDataOptions>()
            .Bind(configuration.GetSection(SeedDataOptions.SectionName))
            .ValidateOnStart();

        // =========================================================
        // Proxy
        // Step 7: bind cấu hình reverse proxy / forwarded headers
        // =========================================================
        services.AddOptions<ProxyOptions>()
            .Bind(configuration.GetSection(ProxyOptions.SectionName))
            .ValidateOnStart();

        return services;
    }
}