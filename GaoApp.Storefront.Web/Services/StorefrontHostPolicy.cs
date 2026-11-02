using System.Net;
using GaoApp.Application.Storefront;

namespace GaoApp.Storefront.Web.Services;

public enum StorefrontRuntimeMode { Closed, Preview, Release }

public sealed record StorefrontHostPolicy(StorefrontRuntimeMode Mode, string? Host, string? Subdomain)
{
    public bool Configured => Host is not null && !string.IsNullOrWhiteSpace(Subdomain);
    public string ModeText => Mode.ToString().ToLowerInvariant();
    public static StorefrontHostPolicy From(IConfiguration configuration, IHostEnvironment environment)
    {
        static bool Flag(IConfiguration source, string key) => bool.TryParse(source[key], out var value) && value;
        var mode = environment.IsDevelopment() && Flag(configuration, "StorefrontHost:PreviewEnabled") ? StorefrontRuntimeMode.Preview :
            environment.IsProduction() && Flag(configuration, "StorefrontHost:ReleaseEnabled") ? StorefrontRuntimeMode.Release : StorefrontRuntimeMode.Closed;
        return new(mode, StorefrontHostIdentity.Normalize(configuration["StorefrontHost:Host"]), configuration["StorefrontHost:StoreSubdomain"]?.Trim().ToLowerInvariant());
    }

    public string? RequestBlocker(HttpContext context)
    {
        if (Host is not null && StorefrontHostIdentity.Normalize(context.Request.Host.Host) != Host) return "HOST_MISMATCH";
        if (Mode == StorefrontRuntimeMode.Closed) return "SERVER_GATE_CLOSED";
        if (!Configured) return "HOST_SCOPE_MISSING";
        if (Mode == StorefrontRuntimeMode.Release) return context.Request.IsHttps ? null : "HTTPS_REQUIRED";
        if (context.Connection.RemoteIpAddress is not { } remote || !IPAddress.IsLoopback(remote) ||
            context.Connection.LocalIpAddress is not { } local || !IPAddress.IsLoopback(local)) return "PREVIEW_LOOPBACK_REQUIRED";
        return null;
    }
}
