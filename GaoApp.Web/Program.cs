using FluentValidation.AspNetCore;
using GaoApp.Application;
using GaoApp.Application.Common;
using GaoApp.Application.Interfaces.Common;
using GaoApp.Infrastructure;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Data.Seed;
using GaoApp.Infrastructure.Security;
using GaoApp.Infrastructure.Tenant;
using GaoApp.Web.Common.POS;
using GaoApp.Web.Configuration;
using GaoApp.Web.HealthChecks;
using GaoApp.Web.Middlewares;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Events;

// =========================================================
// Bootstrap logger:
// Dùng để bắt log rất sớm khi app mới khởi động,
// kể cả lúc builder/build bị lỗi.
// =========================================================
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting GaoApp.Web");

    var builder = WebApplication.CreateBuilder(args);

    // =========================================================
    // 0) HOST LOGGING
    // =========================================================
    builder.Host.UseSerilog((context, services, configuration) =>
    {
        configuration
            .ReadFrom.Configuration(context.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext();
    });

    // =========================================================
    // 1) KESTREL
    // Force HTTP/1.1 trong dev/local để tránh lỗi HTTP/2 / h2c.
    // =========================================================
    builder.WebHost.ConfigureKestrel(options =>
    {
        options.ConfigureEndpointDefaults(lo =>
        {
            lo.Protocols = HttpProtocols.Http1;
        });
    });

    // =========================================================
    // 2) MVC
    // Chỉ đăng ký 1 lần.
    // =========================================================
    builder.Services.AddControllersWithViews(options =>
    {
        options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
    });

    // =========================================================
    // 3) FLUENTVALIDATION
    // =========================================================
    builder.Services.AddFluentValidationAutoValidation(options =>
    {
        options.DisableDataAnnotationsValidation = true;
    });

    builder.Services.AddFluentValidationClientsideAdapters();

    // =========================================================
    // 4) APPLICATION + INFRASTRUCTURE
    // =========================================================
    builder.Services.AddApplication();
    builder.Services.AddInfrastructure(builder.Configuration);

    // =========================================================
    // 5) WEB-SPECIFIC SERVICES
    // =========================================================
    builder.Services.AddScoped<IPOSRuntimeContextAccessor, POSRuntimeContextAccessor>();

    // =========================================================
    // 6) AUTHENTICATION / AUTHORIZATION
    // =========================================================
    builder.Services
        .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
        .AddCookie(options =>
        {
            options.LoginPath = "/admin/account/login";
            options.AccessDeniedPath = "/admin/account/access-denied";

            // Có thể bật thêm nếu muốn hardening hơn:
            // options.SlidingExpiration = true;
            // options.ExpireTimeSpan = TimeSpan.FromDays(7);
        });

    builder.Services.AddAuthorization();

    builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
    builder.Services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();

    // =========================================================
    // 7) OPTIONS / STARTUP VALIDATION / HEALTH / PROXY
    // =========================================================
    builder.Services.AddWebAndAppOptions(builder.Configuration);
    builder.Services.AddStartupValidation();
    builder.Services.AddGaoAppHealthChecks();
    builder.Services.AddGaoAppForwardedHeaders(builder.Configuration);

    // =========================================================
    // 8) MIDDLEWARE AS SERVICES
    // Chỉ giữ middleware nào thật sự cần DI theo kiểu IMiddleware/scoped.
    // TenantResolutionMiddleware KHÔNG đăng ký scoped nếu đang là middleware chuẩn.
    // =========================================================
    builder.Services.AddScoped<TerminalResolutionMiddleware>();

    var app = builder.Build();

    // =========================================================
    // 9) DB MIGRATION + SEED
    // Chạy trước khi nhận request.
    // =========================================================
    await MigrateAndSeedAsync(app);

    // =========================================================
    // 10) GLOBAL EXCEPTION HANDLING
    // Chỉ dùng 1 lần, đặt rất sớm để bắt lỗi toàn pipeline.
    // =========================================================
    app.UseMiddleware<GlobalExceptionMiddleware>();

    // =========================================================
    // 11) FORWARDED HEADERS
    // Phải chạy sớm để normalize Scheme / Host / RemoteIp
    // trước khi resolve tenant.
    // =========================================================
    app.UseGaoAppForwardedHeaders(
        app.Services.GetRequiredService<IOptions<ProxyOptions>>());

    // =========================================================
    // 12) HSTS / HTTPS
    // Chỉ bật ngoài môi trường development.
    // =========================================================
    if (!app.Environment.IsDevelopment())
    {
        app.UseHsts();
        app.UseHttpsRedirection();
    }

    // =========================================================
    // 13) REQUEST LOGGING
    // =========================================================
    app.UseSerilogRequestLogging(options =>
    {
        options.MessageTemplate =
            "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";

        options.GetLevel = (httpContext, elapsed, ex) =>
        {
            if (ex != null || httpContext.Response.StatusCode >= 500)
                return LogEventLevel.Error;

            if (httpContext.Response.StatusCode >= 400)
                return LogEventLevel.Warning;

            return LogEventLevel.Information;
        };

        options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
        {
            diagnosticContext.Set("RequestHost", httpContext.Request.Host.ToString());
            diagnosticContext.Set("RequestScheme", httpContext.Request.Scheme);
            diagnosticContext.Set("TraceIdentifier", httpContext.TraceIdentifier);
            diagnosticContext.Set("RemoteIpAddress", httpContext.Connection.RemoteIpAddress?.ToString());
            diagnosticContext.Set("UserAgent", httpContext.Request.Headers.UserAgent.ToString());

            if (httpContext.User?.Identity?.IsAuthenticated == true)
            {
                diagnosticContext.Set("UserName", httpContext.User.Identity?.Name ?? "unknown");
            }
        };
    });

    // =========================================================
    // 14) STATIC FILES
    // =========================================================
    app.UseStaticFiles();

    // =========================================================
    // 15) ROUTING
    // =========================================================
    app.UseRouting();

    // =========================================================
    // 16) TENANT / TERMINAL RESOLUTION
    // Sau ForwardedHeaders + sau Routing.
    // =========================================================
    app.UseMiddleware<TenantResolutionMiddleware>();
    app.UseMiddleware<TerminalResolutionMiddleware>();

    // =========================================================
    // 17) AUTH
    // =========================================================
    app.UseAuthentication();
    app.UseAuthorization();

    // =========================================================
    // 18) HEALTH CHECKS
    // =========================================================
    app.MapHealthChecks("/health/live", new HealthCheckOptions
    {
        Predicate = check => check.Tags.Contains("live"),
        ResponseWriter = HealthCheckResponseWriter.WriteResponseAsync
    });

    app.MapHealthChecks("/health/ready", new HealthCheckOptions
    {
        Predicate = check => check.Tags.Contains("ready"),
        ResponseWriter = HealthCheckResponseWriter.WriteResponseAsync
    });

    // =========================================================
    // 19) DEBUG ENDPOINTS
    // =========================================================
    app.MapGet("/ping", () => Results.Text("pong"));

    app.MapGet("/__tenant-hash", (HttpContext ctx, ITenantContext t) =>
        Results.Json(new
        {
            Host = ctx.Request.Host.ToString(),
            Scheme = ctx.Request.Scheme,
            t.StoreId,
            t.Subdomain,
            t.IsHostAdmin
        }));

    app.MapGet("/__tenant", (TenantContext t) =>
        Results.Json(new
        {
            t.IsHostAdmin,
            t.Subdomain,
            t.StoreId
        }));

    // =========================================================
    // 20) ROUTES
    // app.MapControllers() là không bắt buộc nếu bạn chỉ dùng MVC route thường.
    // Nhưng giữ lại cũng không sao nếu sau này có attribute routing/API.
    // =========================================================
    app.MapControllers();

    app.MapControllerRoute(
        name: "areas",
        pattern: "{area:exists}/{controller=Home}/{action=Index}/{id?}");

    app.MapControllerRoute(
        name: "default",
        pattern: "{controller=Home}/{action=Index}/{id?}");

    Log.Information("GaoApp.Web configured successfully");

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "GaoApp.Web terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

// =========================================================
// LOCAL FUNCTIONS
// =========================================================
static async Task MigrateAndSeedAsync(WebApplication app)
{
    using var scope = app.Services.CreateScope();
    var services = scope.ServiceProvider;

    try
    {
        var db = services.GetRequiredService<AppDbContext>();

        await db.Database.MigrateAsync();

        // =====================================================
        // LUÔN CHẠY
        // production-safe seed
        // =====================================================
        await SecuritySeedData.SeedPermissionsAsync(db);

        // =====================================================
        // CHỈ CHẠY DEV
        // tránh seed user demo ở production
        // =====================================================
        if (app.Environment.IsDevelopment())
        {
            await SecuritySeedData.SeedUsersAsync(db);
            await SecuritySeedData.SeedDefaultRolesForAllStoresAsync(db);

            var storeIds = await db.Stores
                .AsNoTracking()
                .Select(x => x.Id)
                .ToListAsync();

            foreach (var storeId in storeIds)
            {
                await SecuritySeedData.SeedUserInStoresAsync(db, storeId);
            }
        }
    }
    catch (Exception ex)
    {
        Log.Fatal(ex, "Database migration/seed failed");
        throw;
    }
}