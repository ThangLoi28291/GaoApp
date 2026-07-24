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
using GaoApp.Web.Hubs;
using GaoApp.Web.Middlewares;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Events;
using System.Security.Claims;
using System.Threading.RateLimiting;
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

    // Custom validation environments run from the project output just like
    // Development, so they also need the generated static-web-assets manifest
    // (notably the Razor scoped CSS bundle). Published Production output keeps
    // serving the physical files copied to wwwroot by dotnet publish.
    if (!builder.Environment.IsProduction())
    {
        builder.WebHost.UseStaticWebAssets();
    }

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
    if (builder.Environment.IsDevelopment())
    {
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.ConfigureEndpointDefaults(lo =>
            {
                lo.Protocols = HttpProtocols.Http1;
            });
        });
    }

    // =========================================================
    // 2) MVC
    // Chỉ đăng ký 1 lần.
    // =========================================================
    builder.Services.AddControllersWithViews(options =>
    {
        options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
    });

    // =========================================================
    // SignalR
    // =========================================================

    builder.Services.AddSignalR();
    builder.Services.AddScoped<IPosRealtimeNotifier, PosRealtimeNotifier>();
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
    builder.Services.AddScoped<ICurrentPOSContext, CurrentPOSContext>();

    // =========================================================
    // 6) AUTHENTICATION / AUTHORIZATION
    // =========================================================
    builder.Services
     .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
     .AddCookie(options =>
     {
         options.LoginPath = "/admin/account/login";
         options.AccessDeniedPath = "/admin/account/access-denied";
         options.Cookie.HttpOnly = true;
         options.Cookie.SameSite = SameSiteMode.Lax;
         options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
             ? CookieSecurePolicy.SameAsRequest
             : CookieSecurePolicy.Always;
         options.SlidingExpiration = true;
         options.ExpireTimeSpan = TimeSpan.FromHours(8);

         options.Events = new CookieAuthenticationEvents
         {
             OnRedirectToLogin = context =>
             {
                 var path = context.Request.Path.Value ?? string.Empty;
                 var accept = context.Request.Headers.Accept.ToString();
                 var requestedWith = context.Request.Headers["X-Requested-With"].ToString();

                 var isApiRequest =
    path.StartsWith("/admin/pos/", StringComparison.OrdinalIgnoreCase)||
      path.StartsWith("/admin/api", StringComparison.OrdinalIgnoreCase) ||
      (!string.IsNullOrWhiteSpace(accept) &&
       accept.Contains("application/json", StringComparison.OrdinalIgnoreCase)) ||
      string.Equals(requestedWith, "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);

                 if (isApiRequest)
                 {
                     context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                     return Task.CompletedTask;
                 }

                 context.Response.Redirect(context.RedirectUri);
                 return Task.CompletedTask;
             },

             OnRedirectToAccessDenied = context =>
             {
                 var path = context.Request.Path.Value ?? string.Empty;
                 var accept = context.Request.Headers.Accept.ToString();
                 var requestedWith = context.Request.Headers["X-Requested-With"].ToString();

                 var isApiRequest =
                     path.StartsWith("/admin/pos/shift", StringComparison.OrdinalIgnoreCase) ||
                     path.StartsWith("/admin/api", StringComparison.OrdinalIgnoreCase) ||
                     (!string.IsNullOrWhiteSpace(accept) &&
                      accept.Contains("application/json", StringComparison.OrdinalIgnoreCase)) ||
                     string.Equals(requestedWith, "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);

                 if (isApiRequest)
                 {
                     context.Response.StatusCode = StatusCodes.Status403Forbidden;
                     return Task.CompletedTask;
                 }

                 context.Response.Redirect(context.RedirectUri);
                 return Task.CompletedTask;
             },

             OnValidatePrincipal = async context =>
             {
                 var rawUserId = context.Principal?
                     .FindFirstValue(ClaimTypes.NameIdentifier);

                 if (!int.TryParse(rawUserId, out var userId) || userId <= 0)
                 {
                     context.RejectPrincipal();
                     return;
                 }

                 var services = context.HttpContext.RequestServices;
                 var db = services.GetRequiredService<AppDbContext>();
                 var tenant = services.GetRequiredService<ITenantContext>();

                 bool isValid;

                 if (tenant.IsHostAdmin)
                 {
                     isValid = await db.Users
                         .AsNoTracking()
                         .AnyAsync(x =>
                             x.Id == userId &&
                             x.IsActive &&
                             !x.IsDeleted &&
                             x.IsHostAdmin,
                             context.HttpContext.RequestAborted);
                 }
                 else if (tenant.StoreId.HasValue && tenant.StoreId.Value > 0)
                 {
                     var claimStoreId = context.Principal?
                         .FindFirstValue("store_id");

                     isValid = int.TryParse(claimStoreId, out var cookieStoreId) &&
                         cookieStoreId == tenant.StoreId.Value &&
                         await db.UserInStores
                             .IgnoreQueryFilters()
                             .AsNoTracking()
                             .AnyAsync(x =>
                                 x.StoreId == tenant.StoreId.Value &&
                                 x.UserId == userId &&
                                 x.IsActive &&
                                 !x.IsDeleted &&
                                 x.User.IsActive &&
                                 !x.User.IsDeleted &&
                                 x.Role.StoreId == tenant.StoreId.Value &&
                                 !x.Role.IsDeleted,
                                 context.HttpContext.RequestAborted);
                 }
                 else
                 {
                     isValid = false;
                 }

                 if (!isValid)
                 {
                     context.RejectPrincipal();
                 }
             }
         };
     });

    builder.Services.AddAuthorization();

    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.AddPolicy("login", httpContext =>
        {
            var clientKey = httpContext.Connection.RemoteIpAddress?.ToString()
                ?? "unknown";

            return RateLimitPartition.GetFixedWindowLimiter(
                clientKey,
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 5,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    AutoReplenishment = true
                });
        });
    });

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

    // Startup validation phải hoàn tất trước mọi migration/seed side effect.
    await app.ValidateStartupAsync();

    // 9) DB MIGRATION + SEED
    // Development: web tự migrate + seed để dev nhanh
    // Production/Staging: KHÔNG tự migrate, dùng GaoApp.Migrator riêng
    // =========================================================
    if (app.Environment.IsDevelopment())
    {
        await app.MigrateAndSeedDatabaseAsync();
    }

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
    // File hóa đơn có thể chứa dữ liệu nhạy cảm. Không cho static middleware
    // phục vụ trực tiếp; người dùng phải tải qua controller đã kiểm tra quyền.
    app.Use(async (context, next) =>
    {
        var path = context.Request.Path.Value;
        var isProtectedInvoicePath =
            string.Equals(path, "/uploads/invoices", StringComparison.OrdinalIgnoreCase) ||
            (path?.StartsWith("/uploads/invoices/", StringComparison.OrdinalIgnoreCase) ?? false);

        if (isProtectedInvoicePath)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        await next();
    });

    app.UseStaticFiles();

    // Missing legacy upload paths are a known data-quality condition. Existing
    // files have already been served by StaticFileMiddleware; stop only misses
    // here so they do not enter tenant/terminal resolution and query GaoAppDb.
    app.Use(async (context, next) =>
    {
        var path = context.Request.Path.Value;
        var isMissingDataUpload =
            string.Equals(path, "/uploads/data", StringComparison.OrdinalIgnoreCase) ||
            (path?.StartsWith("/uploads/data/", StringComparison.OrdinalIgnoreCase) ?? false);

        if (isMissingDataUpload)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        await next();
    });

    // =========================================================
    // 15) ROUTING
    // =========================================================
    app.UseRouting();
    app.UseRateLimiter();

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

    app.MapHub<PosHub>("/hubs/pos");

    // =========================================================
    // 19) DEBUG ENDPOINTS
    // =========================================================
    // /ping có thể giữ lại để kiểm tra app còn sống.
    // Endpoint này không trả dữ liệu nhạy cảm.
    app.MapGet("/ping", () => Results.Text("pong"));

    // Các endpoint debug tenant chỉ mở ở Development.
    // Không mở Production/Staging để tránh lộ StoreId/Subdomain/Host.
    if (app.Environment.IsDevelopment())
    {
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
    }

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
    throw;
}
finally
{
    Log.CloseAndFlush();
}

// =========================================================
// LOCAL FUNCTIONS
// =========================================================
