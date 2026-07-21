using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;

namespace GaoApp.Web.Configuration;

/// <summary>
/// Extension cấu hình forwarded headers cho GaoApp.
/// </summary>
public static class ForwardedHeadersExtensions
{
    /// <summary>
    /// Đăng ký cấu hình ForwardedHeadersOptions vào DI.
    /// </summary>
    public static IServiceCollection AddGaoAppForwardedHeaders(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders =
                ForwardedHeaders.XForwardedFor |
                ForwardedHeaders.XForwardedProto |
                ForwardedHeaders.XForwardedHost;

            // Xóa mặc định để chủ động chỉ trust proxy/network do mình khai báo
            options.KnownProxies.Clear();
            options.KnownNetworks.Clear();

            var proxyOptions = configuration
                .GetSection(ProxyOptions.SectionName)
                .Get<ProxyOptions>() ?? new ProxyOptions();

            foreach (var proxy in proxyOptions.KnownProxies)
            {
                if (IPAddress.TryParse(proxy, out var ip))
                {
                    options.KnownProxies.Add(ip);
                }
            }

            foreach (var network in proxyOptions.KnownNetworks)
            {
                var parts = network.Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length != 2)
                {
                    continue;
                }

                if (!IPAddress.TryParse(parts[0], out var prefix))
                {
                    continue;
                }

                if (!int.TryParse(parts[1], out var prefixLength))
                {
                    continue;
                }

                options.KnownNetworks.Add(
     new Microsoft.AspNetCore.HttpOverrides.IPNetwork(prefix, prefixLength));
            }

            // Cho phép tối đa 2 hop proxy.
            options.ForwardLimit = 2;
        });

        return services;
    }

    /// <summary>
    /// Bật middleware forwarded headers.
    /// Phải đặt rất sớm trong pipeline, trước tenant middleware.
    /// </summary>
    public static IApplicationBuilder UseGaoAppForwardedHeaders(
        this IApplicationBuilder app,
        IOptions<ProxyOptions> proxyOptions)
    {
        if (!proxyOptions.Value.EnableForwardedHeaders)
        {
            return app;
        }

        app.UseForwardedHeaders();
        return app;
    }
}