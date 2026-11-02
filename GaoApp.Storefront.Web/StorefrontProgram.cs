using GaoApp.Application.Common;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Tenant;
using Microsoft.EntityFrameworkCore;
using GaoApp.Application.Storefront;
using GaoApp.Infrastructure.Services.Storefront;
using GaoApp.Storefront.Web.Services;
using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;

namespace GaoApp.Storefront.Web;

// Independent customer-facing process. No reference to GaoApp.Web, admin
// controllers, POS session/cookies, migration runner, seeder or background worker.
public partial class StorefrontProgram
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        if (builder.Configuration["StorefrontAddressCatalog:Path"] is null)
            builder.Configuration["StorefrontAddressCatalog:Path"] = Path.Combine(AppContext.BaseDirectory, "Data", "vn-address-catalog-observed-20261031.json");
        var checkoutReadiness = StorefrontCheckoutReadiness.From(builder.Configuration, builder.Environment);
        builder.Services.AddSingleton(checkoutReadiness);
        var protectedKeys = builder.Configuration["StorefrontHost:KeyProtection"] == "WindowsDpapiCurrentUser";
        var strictKeys = protectedKeys || StorefrontHostPolicy.From(builder.Configuration, builder.Environment).Mode == StorefrontRuntimeMode.Release;
        if (checkoutReadiness.Ready)
        {
            var dataProtection = builder.Services.AddDataProtection().SetApplicationName("GaoApp.Storefront");
            if (strictKeys)
            {
                // Framework logs can include key XML in Debug. No private key
                // material or path is emitted by this host's readiness pipeline.
                builder.Logging.AddFilter("Microsoft.AspNetCore.DataProtection", LogLevel.None);
                dataProtection.DisableAutomaticKeyGeneration();
                if (OperatingSystem.IsWindows() && protectedKeys) dataProtection.ProtectKeysWithDpapi(protectToLocalMachine: false);
                builder.Services.AddSingleton(sp => new StorefrontProtectedKeyRepository(new FileSystemXmlRepository(new DirectoryInfo(checkoutReadiness.KeyDirectory!), sp.GetRequiredService<ILoggerFactory>())));
                builder.Services.AddOptions<KeyManagementOptions>().Configure<StorefrontProtectedKeyRepository>((options, repository) => options.XmlRepository = repository);
            }
            else dataProtection.PersistKeysToFileSystem(new DirectoryInfo(checkoutReadiness.KeyDirectory!));
        }
        else builder.Services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        builder.Services.AddSingleton<StorefrontProtectedKeys>();
        builder.Services.AddScoped<StorefrontRuntimeReadiness>();
        // Preview is opt-in and may never listen on a LAN/public interface, including explicit Kestrel endpoints.
        if (builder.Environment.IsDevelopment() && builder.Configuration.GetValue<bool>("StorefrontHost:PreviewEnabled"))
        {
            var endpoints = builder.Configuration.GetSection("Kestrel:Endpoints").GetChildren().Select(x => x["Url"]).ToList();
            var urls = (builder.Configuration["urls"] ?? "http://127.0.0.1:5083").Split(';', StringSplitOptions.RemoveEmptyEntries).Concat(endpoints);
            foreach (var url in urls)
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") ||
                    !(uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || IPAddress.TryParse(uri.Host.Trim('[', ']'), out var address) && IPAddress.IsLoopback(address)))
                    throw new InvalidOperationException("Storefront preview must bind only to loopback URLs.");
            if (builder.Configuration["urls"] is null) builder.WebHost.UseUrls("http://127.0.0.1:5083");
        }
        builder.Services.AddScoped<TenantContext>();
        builder.Services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
        builder.Services.AddScoped<ITenantContextWriter>(sp => sp.GetRequiredService<TenantContext>());
        builder.Services.AddScoped<ICurrentUser, StorefrontGuestUser>();
        builder.Services.AddDbContext<AppDbContext>((sp, options) => options.UseGaoAppSqlServer(
            sp.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Storefront database configuration is missing.")));
        builder.Services.AddScoped<StorefrontHostGate>();
        builder.Services.AddScoped<IStorefrontCatalogService, StorefrontCatalogService>();
        builder.Services.AddScoped<StorefrontCartReader>();
        builder.Services.AddScoped<StorefrontMediaReader>();
        builder.Services.AddSingleton<StorefrontImageService>();
        builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
        builder.Services.AddScoped<IStorefrontAddressCatalog, StorefrontAddressCatalog>();
        builder.Services.AddScoped<IStorefrontCheckoutService, StorefrontCheckoutService>();
        builder.Services.AddScoped<IStorefrontBankRegistration,GaoApp.Infrastructure.Services.Storefront.StorefrontBankRegistration>();
        builder.Services.AddScoped<IStorefrontQrService,StorefrontQrService>();
        builder.Services.AddScoped<IStorefrontCancellationService, StorefrontCancellationService>();
        builder.Services.AddScoped<GaoApp.Application.Interfaces.Repositories.Promotions.IPromotionRepository, GaoApp.Infrastructure.Repositories.Promotions.PromotionRepository>();
        builder.Services.AddScoped<GaoApp.Application.Interfaces.Services.Promotions.IPromotionEngine, GaoApp.Application.Services.Promotions.PromotionEngine>();
        builder.Services.AddScoped<GaoApp.Application.Interfaces.Repositories.Inventory.IInventoryBalanceRepository, GaoApp.Infrastructure.Repositories.Inventory.InventoryBalanceRepository>();
        builder.Services.AddScoped<GaoApp.Application.Interfaces.Services.Orders.IOrderLegalEntityAllocationService, GaoApp.Application.Services.Orders.OrderLegalEntityAllocationService>();
        builder.Services.AddControllersWithViews(options => options.Filters.Add<StorefrontTimingFilter>());
        builder.Services.AddAntiforgery(options => { options.Cookie.Name = "Gao.Storefront.Csrf"; options.Cookie.SameSite = SameSiteMode.Strict; options.HeaderName = "RequestVerificationToken"; });
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy("cart-read", context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
                { PermitLimit = 120, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            foreach (var policy in new[] { (Name: "checkout-read", Limit: 120), (Name: "checkout-quote", Limit: 30), (Name: "checkout-place", Limit: 20), (Name: "guest-order", Limit: 60) })
                options.AddPolicy(policy.Name, context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions { PermitLimit = policy.Limit, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        });
        var app = builder.Build();
        StorefrontMetadataReadiness.Initialize(app.Services, app.Configuration, app.Environment, app.Logger);
        app.UseExceptionHandler(handler => handler.Run(async context =>
        {
            if (context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error is BadHttpRequestException { StatusCode: StatusCodes.Status413PayloadTooLarge })
            {
                context.Response.Headers.CacheControl = "no-store";
                await Results.Problem(statusCode: StatusCodes.Status413PayloadTooLarge, title: "Yêu cầu vượt giới hạn cho phép.").ExecuteAsync(context);
                return;
            }
            context.Response.StatusCode = 503;
            context.Response.Headers.CacheControl = "no-store";
            if (context.Request.Path.StartsWithSegments("/shop/cart/resolve") || context.Request.Path.StartsWithSegments("/shop/checkout") || context.Request.Path == "/shop/orders/access" ||
                context.Request.Path == "/shop/orders/cancellation" || context.Request.Path == "/shop/orders/cancellation/access")
                await Results.Problem(statusCode: 503, title: "Chưa kiểm tra được giỏ hàng. Vui lòng thử lại.").ExecuteAsync(context);
            else await context.Response.WriteAsync("Website hiện chưa sẵn sàng. Vui lòng thử lại sau.");
        }));
        app.Use(async (context, next) =>
        {
            StorefrontRequestTiming.Begin(context, app.Configuration, app.Logger);
            try { await next(context); }
            finally { StorefrontRequestTiming.Complete(context); }
        });
        app.Use(async (context, next) =>
        {
            if (context.Request.Path == "/health" || context.Request.Path == "/health/ready") { await next(context); return; }
            IResult? blocked;
            using (StorefrontRequestTiming.Measure(context, "host_gate"))
                blocked = await context.RequestServices.GetRequiredService<StorefrontHostGate>().CheckAsync(context, context.RequestAborted);
            if (blocked is not null) { await blocked.ExecuteAsync(context); return; }
            await next(context);
        });
        app.UseStaticFiles(); // Only this host's own CSS/JS/fonts; the configured uploads root is never a static provider.
        app.UseRouting();
        app.UseRateLimiter();
        app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "GaoApp.Storefront.Web" }));
        app.MapGet("/health/ready", async (HttpContext context, StorefrontRuntimeReadiness readiness, CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var result = await readiness.AssessAsync(context, ct);
            return Results.Json(result, statusCode: result.RuntimeReady ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);
        });
        app.MapControllers();
        app.Run();
    }
}

internal sealed class StorefrontGuestUser : ICurrentUser
{
    public int? UserId => null;
    public string? UserName => null;
    public int? TerminalId => null;
    public string? TerminalCode => null;
    public bool IsAuthenticated => false;
}
