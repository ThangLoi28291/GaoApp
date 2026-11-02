using System.Collections.Concurrent;
using System.Data.Common;
using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using GaoApp.Application.Common;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Interfaces.Common;
using GaoApp.Application.Interfaces.Services.Security;
using GaoApp.Application.Services.Security;
using GaoApp.Application.Storefront;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Repositories.Inventory;
using GaoApp.Infrastructure.Repositories.Security;
using GaoApp.Infrastructure.Services.Storefront;
using GaoApp.Infrastructure.Tenant;
using GaoApp.Tests.Inventory;
using GaoApp.Web.Areas.Admin.Controllers;
using GaoApp.Web.Middlewares;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GaoApp.Tests.Storefront;

// Actual production MVC/controller/services/policies/antiforgery and actual feature
// Razor body. The configurable admin layout/menu/bootstrap is outside this harness.
internal sealed class Web07AdminHost(WebApplication app, Web07Case c, string directory,
    ConcurrentQueue<Web07AdminHost.SqlSession> sessions) : IAsyncDisposable
{
    internal sealed record SqlSession(int SessionId, string Operation, DateTime AtUtc);
    internal SqlSession[] OwnedSqlSessions => sessions.ToArray();
    internal string Origin => app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single().TrimEnd('/');
    internal string Directory => directory;
    internal HttpClient Client(bool authenticated = true, int? user = null)
    {
        var client = new HttpClient(new HttpClientHandler { CookieContainer = new(), AllowAutoRedirect = false }) { BaseAddress = new(Origin) };
        if (authenticated) client.DefaultRequestHeaders.Add("X-Web07-User", (user ?? c.Seed.UserId).ToString());
        return client;
    }
    internal static async Task CsrfAsync(HttpClient client, int orderId)
    {
        var html = await client.GetStringAsync("/admin/storefront/refunds/" + orderId);
        var input = Regex.Match(html, "<input[^>]*name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"", RegexOptions.IgnoreCase);
        Assert.True(input.Success); client.DefaultRequestHeaders.Remove("RequestVerificationToken");
        client.DefaultRequestHeaders.Add("RequestVerificationToken", WebUtility.HtmlDecode(input.Groups[1].Value));
    }
    internal static async Task GrantAsync(Web07Case c, IEnumerable<string> codes)
    {
        await using var db = c.Context();
        var role = await db.UserInStores.Where(x => x.UserId == c.Seed.UserId).Select(x => x.RoleId).SingleAsync();
        foreach (var code in codes.Distinct())
        {
            var permission = await db.Permissions.SingleOrDefaultAsync(x => x.Code == code);
            if (permission is null)
            {
                permission = new() { Code = code, Name = "WEB07 isolated " + code, GroupName = "WEB07 fixture permissions" };
                db.Add(permission); await db.SaveChangesAsync();
            }
            if (!await db.RolePermissions.AnyAsync(x => x.RoleId == role && x.PermissionId == permission.Id))
                db.RolePermissions.Add(new() { RoleId = role, PermissionId = permission.Id });
        }
        await db.SaveChangesAsync(); Assert.Empty(await db.AdminMenuItems.ToListAsync());
    }
    internal static async Task RevokeAsync(Web07Case c, string code)
    {
        await using var db = c.Context();
        var role = await db.UserInStores.Where(x => x.UserId == c.Seed.UserId).Select(x => x.RoleId).SingleAsync();
        var permission = await db.Permissions.SingleAsync(x => x.Code == code);
        await db.RolePermissions.Where(x => x.RoleId == role && x.PermissionId == permission.Id).ExecuteDeleteAsync();
    }
    internal static async Task<Web07AdminHost> StartAsync(Web07Case c, IEnumerable<string>? codes = null)
    {
        await GrantAsync(c, codes ?? Web07Support.Permissions);
        var directory = Web07Support.Runtime("WEB07 actual native cancellation/refund admin MVC body/auth/policies/CSRF; no Main/menu");
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Testing", ApplicationName = typeof(StorefrontRefundsController).Assembly.FullName,
            ContentRootPath = Path.Combine(Web03Support.Root(), "GaoApp.Web")
        });
        builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
        var accessor = new HttpContextAccessor(); var sessions = new ConcurrentQueue<SqlSession>();
        builder.Services.AddSingleton<IHttpContextAccessor>(accessor);
        builder.Services.AddSingleton<IDataProtectionProvider>(Web05Support.AdminProtection(Path.Combine(directory, "admin-csrf-keys")));
        builder.Services.AddScoped<AppDbContext>(_ => c.Seed.Database.CreateTenantContext(c.StoreId, new ConnectionProbe(accessor, sessions)));
        builder.Services.AddScoped<ITenantContext>(_ => { var tenant = new TenantContext(); tenant.SetStore(c.StoreId, "web07-" + c.StoreId); return tenant; });
        builder.Services.AddScoped<ICurrentUser, Actor>();
        builder.Services.AddScoped<ICurrentStorePermissionService>(s => new CurrentStorePermissionService(new UserInStoreRepository(s.GetRequiredService<AppDbContext>())));
        builder.Services.AddScoped<IStorefrontCancellationService>(s => new StorefrontCancellationService(s.GetRequiredService<AppDbContext>(),
            s.GetRequiredService<ICurrentUser>(), new InventoryBalanceRepository(s.GetRequiredService<AppDbContext>()), TimeProvider.System,
            s.GetRequiredService<ICurrentStorePermissionService>()));
        builder.Services.AddScoped<IStorefrontRefundService>(s =>
        {
            var db = s.GetRequiredService<AppDbContext>(); var actor = s.GetRequiredService<ICurrentUser>(); var rights = s.GetRequiredService<ICurrentStorePermissionService>();
            return new StorefrontRefundService(db, actor, rights,
                Web05Support.AdminProtocol(c.Banking?.AdminKeys ?? Path.Combine(c.Checkout.Directory, "admin-keys")), TimeProvider.System);
        });
        builder.Services.AddControllersWithViews(options => options.Filters.Add<FeatureBodyOnly>()).AddApplicationPart(typeof(StorefrontRefundsController).Assembly);
        builder.Services.AddAntiforgery(options => options.HeaderName = "RequestVerificationToken");
        builder.Services.AddAuthentication("Web07Fixture").AddScheme<AuthenticationSchemeOptions, Authentication>("Web07Fixture", _ => { });
        builder.Services.AddAuthorization(options =>
        {
            foreach (var code in Web07Support.Permissions.Distinct())
                options.AddPolicy(code, policy => policy.RequireAuthenticatedUser().RequireAssertion(async context =>
                {
                    var http = context.Resource as HttpContext ?? accessor.HttpContext;
                    return http is not null && int.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var user) &&
                        await http.RequestServices.GetRequiredService<ICurrentStorePermissionService>().HasPermissionAsync(c.StoreId, user, code);
                }));
        });
        WebApplication? app = null;
        try
        {
            app = builder.Build(); app.UseMiddleware<GlobalExceptionMiddleware>(); app.UseStaticFiles(); app.UseAuthentication(); app.UseAuthorization(); app.MapControllers();
            await app.StartAsync();
            await File.WriteAllTextAsync(Path.Combine(directory, "owned-host.json"), System.Text.Json.JsonSerializer.Serialize(new
            {
                StartedAtUtc = DateTime.UtcNow, ProcessId = Environment.ProcessId,
                ProcessStartedUtc = System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime(), c.StoreId,
                Purpose = "web07-native-admin-feature-body-no-main", UsesLayout = false,
                GuardedDatabase = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(c.Seed.Database.ConnectionString).InitialCatalog
            }));
            return new(app, c, directory, sessions);
        }
        catch { if (app is not null) { await app.StopAsync(); await app.DisposeAsync(); } throw; }
    }
    public async ValueTask DisposeAsync() { await app.StopAsync(); await app.DisposeAsync(); }
    private sealed class Actor(IHttpContextAccessor accessor) : ICurrentUser
    {
        public int? UserId => int.TryParse(accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
        public int? TerminalId => null;
        public bool IsAuthenticated => accessor.HttpContext?.User.Identity?.IsAuthenticated == true;
        public string? UserName => "WEB07 owned fixture staff";
        public string? TerminalCode => null;
    }
    private sealed class ConnectionProbe(IHttpContextAccessor accessor, ConcurrentQueue<SqlSession> sessions) : DbConnectionInterceptor
    {
        public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData data, CancellationToken ct = default)
        {
            var http = accessor.HttpContext; var path = http?.Request.Path.Value;
            if (connection is Microsoft.Data.SqlClient.SqlConnection && http?.Request.Method == "POST" && path?.StartsWith("/admin/storefront/refunds/", StringComparison.Ordinal) == true)
            {
                await using var command = connection.CreateCommand(); command.CommandText = "SELECT CONVERT(int,@@SPID)";
                command.Transaction = data.Context?.Database.CurrentTransaction?.GetDbTransaction();
                var id = Assert.IsType<int>(await command.ExecuteScalarAsync(ct)); Assert.True(id > 0);
                sessions.Enqueue(new(id, path[(path.LastIndexOf('/') + 1)..], DateTime.UtcNow));
            }
        }
    }
    private sealed class Authentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(!int.TryParse(Request.Headers["X-Web07-User"], out var id)
            ? AuthenticateResult.NoResult() : AuthenticateResult.Success(new(new ClaimsPrincipal(new ClaimsIdentity(
                [new(ClaimTypes.NameIdentifier, id.ToString())], Scheme.Name)), Scheme.Name)));
    }
    private sealed class FeatureBodyOnly : IAsyncResultFilter
    {
        public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
        {
            if (context.Result is ViewResult result)
            {
                var controller = context.ActionDescriptor.RouteValues["controller"]; var action = result.ViewName ?? context.ActionDescriptor.RouteValues["action"];
                var services = context.HttpContext.RequestServices;
                var view = services.GetRequiredService<IRazorViewEngine>().GetView(null, $"/Areas/Admin/Views/{controller}/{action}.cshtml", false); Assert.True(view.Success);
                using var body = new StringWriter();
                var viewContext = new ViewContext(context, view.View, result.ViewData,
                    result.TempData ?? new TempDataDictionary(context.HttpContext, services.GetRequiredService<ITempDataProvider>()), body, new HtmlHelperOptions());
                var page = Assert.IsType<RazorView>(view.View).RazorPage; page.ViewContext = viewContext;
                services.GetRequiredService<IRazorPageActivator>().Activate(page, viewContext); await page.ExecuteAsync();
                using var style = new StringWriter(); viewContext.Writer = style; if (page.SectionWriters.TryGetValue("PageStyles", out var css)) await css();
                using var scripts = new StringWriter(); viewContext.Writer = scripts; if (page.SectionWriters.TryGetValue("Scripts", out var js)) await js();
                context.Result = new ContentResult { ContentType = "text/html; charset=utf-8", Content = "<!doctype html><html lang=\"vi\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"><link rel=\"stylesheet\" href=\"/lib/bootstrap/dist/css/bootstrap.min.css\"><link rel=\"stylesheet\" href=\"/css/site.css\">" + style + "</head><body>" + body + scripts + "</body></html>" };
            }
            await next();
        }
    }
}
