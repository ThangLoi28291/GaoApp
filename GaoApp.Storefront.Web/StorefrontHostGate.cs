using GaoApp.Application.Common;
using GaoApp.Infrastructure.Data;
using GaoApp.Storefront.Web.Models;
using GaoApp.Storefront.Web.Services;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Storefront.Web;

public sealed class StorefrontHostGate(IConfiguration configuration, ITenantContextWriter tenant, IServiceProvider services, IHostEnvironment environment)
{
    public async Task<IResult?> CheckAsync(HttpContext context, CancellationToken ct)
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
        context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' blob:; font-src 'self'; connect-src 'self' blob:; object-src 'none'; base-uri 'self'; frame-ancestors 'none'; form-action 'self'";
        var policy = StorefrontHostPolicy.From(configuration, environment);
        if (policy.RequestBlocker(context) is { } blocker)
            return blocker is "HOST_MISMATCH" or "PREVIEW_LOOPBACK_REQUIRED" ? Results.NotFound() : IsHome(context) ? Closed() : Results.NotFound();
        var host = policy.Host;
        var subdomain = policy.Subdomain;
        if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(subdomain) || string.IsNullOrWhiteSpace(configuration.GetConnectionString("DefaultConnection")))
            return IsHome(context) ? Closed() : Results.NotFound();
        var db = ResolveContext(context);
        var store = await StorefrontRequestTiming.TimeAsync(context, "store_read", () => db.Stores.AsNoTracking().SingleOrDefaultAsync(x => x.IsActive && x.SubDomainNormalized == subdomain, ct));
        if (store is null) return Results.NotFound();
        tenant.SetStore(store.Id, store.SubDomainNormalized);
        if (policy.Mode == StorefrontRuntimeMode.Release || configuration["StorefrontHost:KeyProtection"] == "WindowsDpapiCurrentUser")
        {
            var keys = services.GetService<StorefrontProtectedKeys>();
            if (keys is null || await keys.EnsureAsync(db, ct) is not null)
                return IsHome(context) ? Closed() : Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
        var settings = await StorefrontRequestTiming.TimeAsync(context, "settings_read", () => db.StorefrontSettings.AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == store.Id, ct));
        var maintenance = context.Request.Path == "/shop/orders" || context.Request.Path == "/shop/orders/access" || context.Request.Path == "/shop/checkout" ||
            context.Request.Path == "/shop/checkout/place" || context.Request.Path == "/shop/orders/qr" || context.Request.Path == "/shop/orders/qr/image" ||
            context.Request.Path == "/shop/orders/cancellation" || context.Request.Path == "/shop/orders/cancellation/access" || context.Request.Path.StartsWithSegments("/assets");
        if ((settings is null || !settings.IsEnabled) && !maintenance) return IsHome(context) ? Closed() : Results.NotFound();
        var scope = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{host.ToLowerInvariant()}|{store.SubDomainNormalized}|{store.Id}")));
        context.Items[typeof(StorefrontPageContext)] = new StorefrontPageContext(settings?.DisplayName ?? store.Name, settings?.Description, settings?.ContactPhone ?? "", scope) { RuntimeMode = policy.ModeText };
        return null;
    }

    private static bool IsHome(HttpContext context) => context.Request.Path == "/" || context.Request.Path == "/shop" || context.Request.Path == "/shop/";

    public async Task<IResult> ClosedAsync(HttpContext context, CancellationToken ct)
    {
        context.Response.Headers.CacheControl = "no-store";
        var host = configuration["StorefrontHost:Host"]?.Trim().TrimEnd('.');
        var subdomain = configuration["StorefrontHost:StoreSubdomain"]?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(subdomain) ||
            string.IsNullOrWhiteSpace(configuration.GetConnectionString("DefaultConnection"))) return Closed();
        if (!string.Equals(context.Request.Host.Host.TrimEnd('.'), host, StringComparison.OrdinalIgnoreCase)) return Results.NotFound();
        var db = ResolveContext(context);
        // The mapping comes from this process's deployment configuration. Query
        // parameters, cookies and posted StoreId cannot switch the store.
        var store = await StorefrontRequestTiming.TimeAsync(context, "store_read", () => db.Stores.AsNoTracking().SingleOrDefaultAsync(x => x.IsActive && x.SubDomainNormalized == subdomain, ct));
        if (store is null) return Results.NotFound();
        tenant.SetStore(store.Id, store.SubDomainNormalized);
        // Closed display never grants catalog/intake access. The separate server
        // admission policy is checked in CheckAsync, before tenant data access.
        return Closed();
    }

    private static IResult Closed() => Results.Text("Cửa hàng đang chuẩn bị website. Vui lòng quay lại sau.",
        contentType: "text/plain; charset=utf-8", statusCode: StatusCodes.Status503ServiceUnavailable);

    private AppDbContext ResolveContext(HttpContext context)
    {
        AppDbContext db;
        using (StorefrontRequestTiming.Measure(context, "db_context")) db = services.GetRequiredService<AppDbContext>();
        // Diagnostic-only separation at the first real gate query, which already
        // requires this model. No startup/health/catalog warmup or database call.
        if (StorefrontRequestTiming.IsEnabled(context))
            using (StorefrontRequestTiming.Measure(context, "model")) _ = db.Model;
        return db;
    }
}
