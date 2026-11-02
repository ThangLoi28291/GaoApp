using System.Net;
using System.Collections.Concurrent;
using System.Data.Common;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using GaoApp.Application.Common;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Interfaces.Common;
using GaoApp.Application.Interfaces.Services.Security;
using GaoApp.Application.Services.Security;
using GaoApp.Application.Storefront;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Repositories.Inventory;
using GaoApp.Infrastructure.Repositories.Security;
using GaoApp.Infrastructure.Services.Orders;
using GaoApp.Infrastructure.Services.Storefront;
using GaoApp.Infrastructure.Tenant;
using GaoApp.Tests.Inventory;
using GaoApp.Web.Areas.Admin.Controllers;
using GaoApp.Web.Common.POS;
using GaoApp.Web.Middlewares;
using GaoApp.Web.Services.Acb;
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

internal sealed class Web06AdminHost(WebApplication app, Web06Case c, int terminal, ConcurrentQueue<Web06AdminHost.SqlSession> sessions) : IAsyncDisposable
{
    internal sealed record SqlSession(int SessionId, string Operation, DateTime AtUtc);
    internal SqlSession[] OwnedSqlSessions => sessions.ToArray();
    internal string Origin => app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single().TrimEnd('/');
    internal int UserId => c.Seed.UserId;
    internal int Terminal => terminal;
    internal HttpClient Client(bool authenticated = true, int? user = null)
    {
        var client = new HttpClient(new HttpClientHandler { CookieContainer = new(), AllowAutoRedirect = false }) { BaseAddress = new(Origin) };
        if (authenticated) client.DefaultRequestHeaders.Add("X-Web06-User", (user ?? UserId).ToString());
        if (terminal > 0) client.DefaultRequestHeaders.Add("X-Web06-Terminal", terminal.ToString()); return client;
    }
    internal static async Task Csrf(HttpClient client, int id)
    {
        var html = await client.GetStringAsync("/admin/storefront/fulfillment/" + id);
        var input = Regex.Match(html, "<input[^>]*name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"", RegexOptions.IgnoreCase);
        Assert.True(input.Success); client.DefaultRequestHeaders.Remove("RequestVerificationToken"); client.DefaultRequestHeaders.Add("RequestVerificationToken", WebUtility.HtmlDecode(input.Groups[1].Value));
    }
    internal static async Task<Web06AdminHost> StartAsync(Web06Case c, int terminal = 0, IEnumerable<string>? codes = null)
    {
        var granted = (codes ?? [.. Web06FinanceSupport.Permissions, StorefrontPermissions.ViewSettings]).Distinct().ToArray();
        await using (var db = c.Context())
        {
            var role = await db.UserInStores.Where(x => x.UserId == c.Seed.UserId).Select(x => x.RoleId).SingleAsync();
            foreach (var code in granted)
            {
                var permission = await db.Permissions.SingleOrDefaultAsync(x => x.Code == code);
                if (permission is null) { permission = new() { Code = code, Name = "WEB06 isolated " + code, GroupName = "WEB06 fixture permissions" }; db.Add(permission); await db.SaveChangesAsync(); }
                if (!await db.RolePermissions.AnyAsync(x => x.RoleId == role && x.PermissionId == permission.Id)) db.RolePermissions.Add(new() { RoleId = role, PermissionId = permission.Id });
            }
            await db.SaveChangesAsync(); Assert.Empty(await db.AdminMenuItems.ToListAsync());
        }
        var directory = Web06Support.Runtime("WEB06 minimal native admin MVC/auth/CSRF; no layout/bootstrap/menu seeder");
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing", ApplicationName = typeof(StorefrontFulfillmentController).Assembly.FullName,
            ContentRootPath = Path.Combine(Web03Support.Root(), "GaoApp.Web") });
        builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
        var httpAccessor = new HttpContextAccessor(); builder.Services.AddSingleton<IHttpContextAccessor>(httpAccessor);
        builder.Services.AddSingleton<IDataProtectionProvider>(Web05Support.AdminProtection(Path.Combine(directory, "admin-csrf-keys")));
        var sessions = new ConcurrentQueue<SqlSession>();
        builder.Services.AddScoped<AppDbContext>(_ => c.Seed.Database.CreateTenantContext(c.StoreId, new ConnectionProbe(httpAccessor, sessions)));
        builder.Services.AddScoped<ITenantContext>(_ => { var tenant = new TenantContext(); tenant.SetStore(c.StoreId, "web06-" + c.StoreId); return tenant; });
        builder.Services.AddScoped<ICurrentUser, Actor>(); builder.Services.AddScoped<IPOSRuntimeContextAccessor, POSRuntimeContextAccessor>(); builder.Services.AddScoped<ICurrentPOSContext, CurrentPOSContext>();
        builder.Services.AddScoped<ICurrentStorePermissionService>(s => new CurrentStorePermissionService(new UserInStoreRepository(s.GetRequiredService<AppDbContext>())));
        builder.Services.AddScoped<IStorefrontFulfillmentService>(s => { var db = s.GetRequiredService<AppDbContext>(); return new StorefrontFulfillmentService(db, s.GetRequiredService<ICurrentUser>(), s.GetRequiredService<ICurrentStorePermissionService>(), new InventoryBalanceRepository(db), InventoryPosPostingContractTests.CreateRealMovementService(db), TimeProvider.System); });
        builder.Services.AddScoped<IStorefrontOrderInboxService>(s => new StorefrontOrderInboxService(s.GetRequiredService<AppDbContext>(), s.GetRequiredService<ICurrentUser>(), s.GetRequiredService<ICurrentStorePermissionService>()));
        builder.Services.AddScoped<IStorefrontCodBankSourceService>(s => new StorefrontCodBankSourceService(s.GetRequiredService<AppDbContext>(), s.GetRequiredService<ICurrentUser>(), s.GetRequiredService<ICurrentStorePermissionService>(), Web05Support.AdminProtocol(Path.Combine(c.Checkout.Directory, "admin-keys")), TimeProvider.System));
        builder.Services.AddScoped<IStorefrontSettlementService>(s =>
        {
            var db = s.GetRequiredService<AppDbContext>(); var actor = s.GetRequiredService<ICurrentUser>(); var rights = s.GetRequiredService<ICurrentStorePermissionService>();
            return new StorefrontSettlementService(db, actor, rights, new InventoryBalanceRepository(db), InventoryPosPostingContractTests.CreateRealMovementService(db),
                new StorefrontSaleBridge(db, actor, s.GetRequiredService<ICurrentPOSContext>(), rights, new OrderNumberGenerator(db, s.GetRequiredService<ITenantContext>()), [new AcbFinalizeGuard(db)], TimeProvider.System),
                new StorefrontInvoiceProcessor(db, Web06FinanceSupport.InvoiceService(c, db), TimeProvider.System), TimeProvider.System);
        });
        builder.Services.AddControllersWithViews(options => options.Filters.Add<NativeBodyOnly>()).AddApplicationPart(typeof(StorefrontFulfillmentController).Assembly);
        builder.Services.AddAntiforgery(options => options.HeaderName = "RequestVerificationToken");
        builder.Services.AddAuthentication("Web06Fixture").AddScheme<AuthenticationSchemeOptions, Authentication>("Web06Fixture", _ => { });
        builder.Services.AddAuthorization(options =>
        {
            foreach (var code in new[] { StorefrontPermissions.ViewOrders, StorefrontPermissions.Prepare, StorefrontPermissions.Dispatch, StorefrontPermissions.RecordDelivery,
                StorefrontPermissions.ReceiveReturns, StorefrontPermissions.Reconcile, StorefrontPermissions.PostSale, StorefrontPermissions.ViewSettings, StorefrontPermissions.ManageSettings })
                options.AddPolicy(code, policy => policy.RequireAuthenticatedUser().RequireAssertion(async context =>
                {
                    var http = context.Resource as HttpContext ?? httpAccessor.HttpContext;
                    return http is not null && int.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var user) &&
                        await http.RequestServices.GetRequiredService<ICurrentStorePermissionService>().HasPermissionAsync(c.StoreId, user, code);
                }));
        });
        var app = builder.Build(); app.UseMiddleware<GlobalExceptionMiddleware>(); app.UseStaticFiles(); app.UseAuthentication(); app.UseAuthorization(); app.MapControllers();
        await app.StartAsync();
        await File.WriteAllTextAsync(Path.Combine(directory, "owned-host.json"), System.Text.Json.JsonSerializer.Serialize(new { StartedAtUtc = DateTime.UtcNow, ProcessId = Environment.ProcessId,
            ProcessStartedUtc = System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime(), StoreId = c.StoreId, Purpose = "native-admin-minimal-mvc-no-main", UsesLayout = false }));
        return new(app, c, terminal, sessions);
    }
    public async ValueTask DisposeAsync() { await app.StopAsync(); await app.DisposeAsync(); }
    private sealed class Actor(IHttpContextAccessor accessor) : ICurrentUser
    {
        public int? UserId => int.TryParse(accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
        public int? TerminalId => int.TryParse(accessor.HttpContext?.User.FindFirstValue("terminal_id"), out var id) && id > 0 ? id : null;
        public bool IsAuthenticated => accessor.HttpContext?.User.Identity?.IsAuthenticated == true; public string? UserName => "WEB06 fixture cashier"; public string? TerminalCode => "WEB06";
    }
    private sealed class ConnectionProbe(IHttpContextAccessor accessor, ConcurrentQueue<SqlSession> sessions) : DbConnectionInterceptor
    {
        public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData data, CancellationToken ct = default)
        {
            var path = accessor.HttpContext?.Request.Path.Value;
            if (connection is Microsoft.Data.SqlClient.SqlConnection && accessor.HttpContext?.Request.Method == "POST" && path?.StartsWith("/admin/storefront/fulfillment/", StringComparison.Ordinal) == true)
            {
                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT CONVERT(int,@@SPID)";
                command.Transaction = data.Context?.Database.CurrentTransaction?.GetDbTransaction();
                var sessionId = Assert.IsType<int>(await command.ExecuteScalarAsync(ct));
                Assert.True(sessionId > 0, "The owned HTTP connection must provide its actual positive SQL session ID.");
                sessions.Enqueue(new(sessionId, path[(path.LastIndexOf('/') + 1)..], DateTime.UtcNow));
            }
        }
    }
    private sealed class Authentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(!int.TryParse(Request.Headers["X-Web06-User"], out var id) ? AuthenticateResult.NoResult() :
            AuthenticateResult.Success(new(new ClaimsPrincipal(new ClaimsIdentity([new(ClaimTypes.NameIdentifier, id.ToString()), new("terminal_id", Request.Headers["X-Web06-Terminal"].ToString())], Scheme.Name)), Scheme.Name)));
    }
    // Execute actual module Razor and its real sections. No admin menu/layout or seeder is instantiated.
    private sealed class NativeBodyOnly : IAsyncResultFilter
    {
        public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
        {
            if (context.Result is ViewResult result)
            {
                var controller = context.ActionDescriptor.RouteValues["controller"]; var action = result.ViewName ?? context.ActionDescriptor.RouteValues["action"];
                var services = context.HttpContext.RequestServices; var view = services.GetRequiredService<IRazorViewEngine>().GetView(null, $"/Areas/Admin/Views/{controller}/{action}.cshtml", false); Assert.True(view.Success);
                using var body = new StringWriter(); var viewContext = new ViewContext(context, view.View, result.ViewData, result.TempData ?? new TempDataDictionary(context.HttpContext, services.GetRequiredService<ITempDataProvider>()), body, new HtmlHelperOptions());
                var page = Assert.IsType<RazorView>(view.View).RazorPage; page.ViewContext = viewContext; services.GetRequiredService<IRazorPageActivator>().Activate(page, viewContext); await page.ExecuteAsync();
                using var style = new StringWriter(); viewContext.Writer = style; if (page.SectionWriters.TryGetValue("PageStyles", out var css)) await css();
                using var scripts = new StringWriter(); viewContext.Writer = scripts; if (page.SectionWriters.TryGetValue("Scripts", out var js)) await js();
                context.Result = new ContentResult { ContentType = "text/html; charset=utf-8", Content = "<!doctype html><html lang=\"vi\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"><link rel=\"stylesheet\" href=\"/lib/bootstrap/dist/css/bootstrap.min.css\"><link rel=\"stylesheet\" href=\"/css/site.css\">" + style + "</head><body>" + body + scripts + "</body></html>" };
            }
            await next();
        }
    }
}
