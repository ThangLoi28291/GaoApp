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
                x => IsAbsoluteHttpUrl(x.BaseUrl),
                "AppUrl:BaseUrl phải là absolute URL http/https hợp lệ.")
            .Validate(
                x => IsAbsoluteHttpUrl(x.AdminUrl),
                "AppUrl:AdminUrl phải là absolute URL http/https hợp lệ.")
            .ValidateOnStart();

        // =========================================================
        // Tenant
        // =========================================================
        services.AddOptions<TenantOptions>()
            .Bind(configuration.GetSection(TenantOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(
                x => !string.IsNullOrWhiteSpace(x.RootDomain),
                "Tenant:RootDomain không được để trống.")
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

        services.AddOptions<InputInvoiceLibraryOptions>()
            .Bind(configuration.GetSection(InputInvoiceLibraryOptions.SectionName))
            .Validate(
                x => !x.Enabled || !string.IsNullOrWhiteSpace(x.RootPath),
                "InputInvoiceLibrary:RootPath là bắt buộc khi thư viện hóa đơn được bật.")
            .Validate(
                x => x.MaxMonthPartitions is >= 1 and <= 12,
                "InputInvoiceLibrary:MaxMonthPartitions phải từ 1 đến 12.")
            .Validate(
                x => x.MaxCandidates is >= 1 and <= 200,
                "InputInvoiceLibrary:MaxCandidates phải từ 1 đến 200.")
            .ValidateOnStart();

        // =========================================================
        // SeedData
        // =========================================================
        services.AddOptions<SeedDataOptions>()
            .Bind(configuration.GetSection(SeedDataOptions.SectionName))
            .Validate(
                x => !x.EnableDemoSeed ||
                     !string.IsNullOrWhiteSpace(x.DemoUserPassword),
                "SeedData:DemoUserPassword bắt buộc khi SeedData:EnableDemoSeed = true.")
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

    private static bool IsAbsoluteHttpUrl(string? value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
           !string.IsNullOrWhiteSpace(uri.Host) &&
           (uri.Scheme == Uri.UriSchemeHttp ||
            uri.Scheme == Uri.UriSchemeHttps);
}
