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

    const string recoverAdminMenusOnlyFlag = "--recover-admin-menus-only";
    var recoveryFlagCount = args.Count(argument =>
        string.Equals(
            argument,
            recoverAdminMenusOnlyFlag,
            StringComparison.Ordinal));

    if (recoveryFlagCount > 1)
    {
        Console.Error.WriteLine(
            "The admin menu recovery flag may be specified only once.");
        Environment.ExitCode = 2;
        return;
    }

    var recoverAdminMenusOnly = recoveryFlagCount == 1;
    var builderArgs = recoverAdminMenusOnly
        ? args.Where(argument =>
            !string.Equals(
                argument,
                recoverAdminMenusOnlyFlag,
                StringComparison.Ordinal))
            .ToArray()
        : args;

    var builder = WebApplication.CreateBuilder(builderArgs);
    builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

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
            .Enrich.FromLogContext()
            .WriteTo.Logger(callback => callback
                .Filter.ByIncludingOnly(Serilog.Filters.Matching.FromSource<GaoApp.Web.Services.Acb.AcbCallbackDiagnostics>())
                .WriteTo.File(new Serilog.Formatting.Json.JsonFormatter(),
                    System.IO.Path.Combine(context.HostingEnvironment.ContentRootPath, "App_Data", "Logs", "acb-callback", "callback-.jsonl"),
                    rollingInterval: RollingInterval.Day, retainedFileCountLimit: 30,
                    fileSizeLimitBytes: 10485760, rollOnFileSizeLimit: true, shared: true));
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
        options.Filters.Add(new Microsoft.AspNetCore.Mvc.AutoValidateAntiforgeryTokenAttribute());
        options.Filters.Add<GaoApp.Web.Services.StoreMonitor.StoreActivityFilter>();
    });

    // =========================================================
    // SignalR
    // =========================================================

    builder.Services.AddSecuredPosRealtime();
    builder.Services.AddScoped<IPosRealtimeNotifier, PosRealtimeNotifier>();
    Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.TryAddSingleton<TimeProvider>(builder.Services, TimeProvider.System);
    builder.Services.AddSingleton<GaoApp.Web.Services.StoreMonitor.StoreActivityRegistry>();
    builder.Services.AddSingleton<GaoApp.Web.Services.StoreMonitor.StoreActivityTicket>();
    builder.Services.AddScoped<GaoApp.Web.Services.StoreMonitor.StoreActivityFilter>();
    builder.Services.AddScoped<GaoApp.Web.Services.Offline.PosOperationFilter>();
    builder.Services.AddScoped<GaoApp.Web.Services.Printing.ReceiptTemplateService>();
    builder.Services.AddScoped<GaoApp.Web.Services.Delivery.DeliveryPosService>();
    builder.Services.AddScoped<GaoApp.Web.Services.CustomerDisplayService>();
    builder.Services.AddSingleton<GaoApp.Web.Services.CustomerDepositDisplayState>();
    builder.Services.AddScoped<GaoApp.Web.Services.Printing.ProductLabelService>();
    builder.Services.AddHttpClient<GaoApp.Web.Services.Acb.AcbProtocol>(client => client.Timeout = TimeSpan.FromSeconds(30))
        .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
    builder.Services.AddScoped<GaoApp.Web.Services.Acb.AcbPaymentService>();
    builder.Services.AddScoped<GaoApp.Web.Services.Acb.AcbCallbackInbox>();
    builder.Services.AddScoped<GaoApp.Web.Services.Acb.AcbReconciliationService>();
    builder.Services.AddSingleton<GaoApp.Web.Services.Acb.AcbCallbackDiagnostics>();
    builder.Services.AddScoped<GaoApp.Web.Services.Acb.AcbSandboxCheck>();
    builder.Services.AddScoped<GaoApp.Web.Services.Acb.IAcbSandboxJournal, GaoApp.Web.Services.Acb.AcbSandboxFileJournal>();
    builder.Services.AddSingleton<GaoApp.Web.Services.Acb.AcbCallbackSignal>();
    builder.Services.AddHostedService<GaoApp.Web.Services.Acb.AcbCallbackWorker>();
    builder.Services.AddScoped<GaoApp.Web.Services.Acb.IAcbOrderLockProvider, GaoApp.Web.Services.Acb.SqlAcbOrderLockProvider>();
    builder.Services.AddScoped<GaoApp.Application.Interfaces.Services.Orders.IOrderFinalizeGuard, GaoApp.Web.Services.Acb.AcbFinalizeGuard>();
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
    builder.Services.AddInfrastructure(builder.Configuration, builder.Environment);
    builder.Services.AddSingleton<GaoApp.Web.Services.Media.MediaCleanupStatus>();
    builder.Services.AddHostedService<GaoApp.Web.Services.Media.MediaCleanupWorker>();
    builder.Services.AddHostedService<GaoApp.Web.Services.Delivery.DeliveryOutboxWorker>();
    builder.Services.AddAcbCallbackRouting(builder.Configuration);

    // =========================================================
    // 5) WEB-SPECIFIC SERVICES
    // =========================================================
    builder.Services.AddScoped<IPOSRuntimeContextAccessor, POSRuntimeContextAccessor>();
    builder.Services.AddScoped<GaoApp.Web.Services.Kiosk.KioskAccess>();
    builder.Services.AddScoped<GaoApp.Web.Services.Kiosk.KioskService>();
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
                 var validator = context.HttpContext.RequestServices
                     .GetRequiredService<GaoApp.Web.Security.SessionPrincipalValidator>();
                 if (!await validator.ValidateAsync(context.Principal, context.HttpContext.RequestAborted))
                 {
                     context.RejectPrincipal();
                     await Microsoft.AspNetCore.Authentication.AuthenticationHttpContextExtensions.SignOutAsync(
                         context.HttpContext, CookieAuthenticationDefaults.AuthenticationScheme);
                 }
             }
         };
     });

    builder.Services.AddScoped<GaoApp.Web.Security.SessionPrincipalValidator>();
    builder.Services.AddScoped<GaoApp.Web.Services.Accounts.EmployeeAccountService>();
    builder.Services.AddScoped<GaoApp.Web.Security.SelfPasswordRateLimitFilter>();
    builder.Services.AddScoped<FluentValidation.IValidator<GaoApp.Web.Areas.Admin.ViewModels.Account.ChangeOwnPasswordVm>,
        GaoApp.Web.Areas.Admin.ViewModels.Account.ChangeOwnPasswordValidator>();
    builder.Services.AddAuthorization();

    GaoApp.Web.Security.LoginRateLimiting.AddLoginRateLimiting(builder.Services, builder.Configuration);
    builder.Services.AddRateLimiter(options => options.AddPolicy("delivery-lookup", context =>
        System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
            context.Request.Host.Value + ":" + context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value,
            _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions {
                PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true })));

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

    if (recoverAdminMenusOnly)
    {
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };
        Console.CancelKeyPress += cancelHandler;

        try
        {
            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var connection = db.Database.GetDbConnection();
            var activeStoreCount = await db.Stores
                .IgnoreQueryFilters()
                .AsNoTracking()
                .CountAsync(
                    store => store.IsActive && !store.IsDeleted,
                    cancellation.Token);

            Console.WriteLine(
                "Environment: {0}",
                app.Environment.EnvironmentName);
            Console.WriteLine("Server: {0}", connection.DataSource);
            Console.WriteLine("Database: {0}", connection.Database);
            Console.WriteLine("Active stores: {0}", activeStoreCount);

            await AdminMenuSeeder.SeedAsync(db, cancellation.Token);

            Console.WriteLine("Admin menu recovery completed successfully.");
            Environment.ExitCode = 0;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Admin menu recovery was canceled.");
            Environment.ExitCode = 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                "Admin menu recovery failed ({0}).",
                ex.GetType().Name);
            Environment.ExitCode = 1;
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }

        return;
    }

    // Normal startup validates configuration only, in every environment.
    // Schema and provisioning are separate explicit Migrator operations.
    await app.ValidateStartupAsync();

    // Local Visual Studio development may point at the developer's current
    // GaoAppDb. Keep schema installation explicit outside Development, but
    // allow the local profile to bring that database up to the compiled
    // model before the first request. This is deliberately disabled for
    // Production/Staging and never seeds or copies business data.
    await app.ApplyDevelopmentSchemaAsync();

    // =========================================================
    // 10) GLOBAL EXCEPTION HANDLING
    // Chỉ dùng 1 lần, đặt rất sớm để bắt lỗi toàn pipeline.
    // =========================================================
    app.UseMiddleware<GaoApp.Web.Security.SecurityResponseHeadersMiddleware>();
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
    app.UseMiddleware<AcbCallbackDiagnosticsMiddleware>();

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

        options.GetLevel = RequestLoggingPolicy.GetLevel;

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
    GaoApp.Web.Security.PublicUploadExtensions.UsePublicUploads(app, app.Environment,
        app.Services.GetRequiredService<GaoApp.Infrastructure.Storage.UploadPathResolver>());

    app.UseStaticFiles();

    // Missing uploads must not enter tenant/terminal resolution and query GaoAppDb.
    app.Use(async (context, next) =>
    {
        if (context.Request.Path.StartsWithSegments("/uploads", StringComparison.OrdinalIgnoreCase))
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
    // Existing policies keep their pre-authentication placement. Delivery lookup needs the
    // validated Store/user principal to isolate quotas, so only that policy runs below auth.
    app.UseWhen(context => context.GetEndpoint()?.Metadata
        .GetMetadata<Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute>()?.PolicyName != "delivery-lookup",
        branch => branch.UseRateLimiter());

    // =========================================================
    // 16) TENANT / TERMINAL RESOLUTION
    // Sau ForwardedHeaders + sau Routing.
    // =========================================================
    // Public probes finish before tenant/terminal/authentication. Otherwise a
    // valid store cookie is rejected on a tenant-free health request.
    app.UseHealthChecks("/health/live", new HealthCheckOptions
    {
        Predicate = check => check.Tags.Contains("live"),
        ResponseWriter = HealthCheckResponseWriter.WriteResponseAsync
    });

    app.UseHealthChecks("/health/ready", new HealthCheckOptions
    {
        Predicate = check => check.Tags.Contains("ready"),
        ResponseWriter = HealthCheckResponseWriter.WriteResponseAsync
    });

    app.UseMiddleware<TenantResolutionMiddleware>();
    app.UseMiddleware<TerminalResolutionMiddleware>();

    // =========================================================
    // 17) AUTH
    // =========================================================
    app.UseAuthentication();
    app.UseAuthorization();
    app.UseWhen(context => context.GetEndpoint()?.Metadata
        .GetMetadata<Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute>()?.PolicyName == "delivery-lookup",
        branch => branch.UseRateLimiter());

    // =========================================================
    // 18) HEALTH CHECKS
    // =========================================================
    app.MapHub<PosHub>("/hubs/pos", options => options.CloseOnAuthenticationExpiration = true);

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
