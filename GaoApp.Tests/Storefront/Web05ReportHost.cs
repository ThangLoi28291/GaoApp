using System.Net;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Common.Security;
using GaoApp.Application.Interfaces.Services.Reports;
using GaoApp.Application.Interfaces.Services.Security;
using GaoApp.Application.Services.Reports;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Repositories.Security;
using GaoApp.Infrastructure.Services.Reports;
using GaoApp.Application.Services.Security;
using GaoApp.Web.Areas.Admin.Controllers;
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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GaoApp.Tests.Storefront;

internal sealed class Web05ReportHost(WebApplication app, int userId) : IAsyncDisposable
{
    internal string BaseUrl => app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single().TrimEnd('/');
    internal int UserId => userId;
    internal HttpClient Client(bool authenticate = true)
    {
        var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, CookieContainer = new CookieContainer() }) { BaseAddress = new Uri(BaseUrl) };
        if (authenticate) client.DefaultRequestHeaders.Add("X-Web05-User", userId.ToString()); return client;
    }
    internal static async Task<Web05ReportHost> StartAsync(Web01Case seed)
    {
        await using (var db = seed.Context())
        {
            var role = await db.UserInStores.Where(x => x.UserId == seed.UserId).Select(x => x.RoleId).SingleAsync();
            foreach (var code in new[] { PermissionCodes.Report.Profit.View, PermissionCodes.Report.Profit.Export })
            {
                var permission = await db.Permissions.SingleOrDefaultAsync(x => x.Code == code);
                if (permission is null)
                {
                    permission = new Permission { Code = code, Name = "Synthetic WEB05 report " + code, GroupName = "Synthetic WEB05 report" };
                    db.Permissions.Add(permission); await db.SaveChangesAsync();
                }
                if (!await db.RolePermissions.AnyAsync(x => x.RoleId == role && x.PermissionId == permission.Id)) db.RolePermissions.Add(new() { RoleId = role, PermissionId = permission.Id });
            }
            await db.SaveChangesAsync(); Assert.Empty(await db.AdminMenuItems.ToListAsync());
        }
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ApplicationName = typeof(OperationsReportController).Assembly.FullName,
            EnvironmentName = "Testing", ContentRootPath = Path.Combine(Web03Support.Root(), "GaoApp.Web") });
        builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddControllersWithViews(options => options.Filters.Add<ReportBodyOnly>());
        var accessor = new HttpContextAccessor(); builder.Services.AddSingleton<IHttpContextAccessor>(accessor);
        var policyChecks = new ConcurrentQueue<object>(); var trace = Guid.NewGuid().ToString("N");
        builder.Services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider()); builder.Services.AddScoped<AppDbContext>(_ => seed.Context());
        builder.Services.AddScoped<ICurrentUser, Actor>(); builder.Services.AddScoped<ICurrentStorePermissionService>(s => new CurrentStorePermissionService(new UserInStoreRepository(s.GetRequiredService<AppDbContext>())));
        builder.Services.AddSingleton(new SalesReportingPeriodPolicy()); builder.Services.AddScoped<IOperationsReportService, OperationsReportService>();
        builder.Services.AddScoped<ITreasuryService>(_ => System.Reflection.DispatchProxy.Create<ITreasuryService, Web05Proxy>());
        builder.Services.AddAuthentication("Web05Report").AddScheme<AuthenticationSchemeOptions, Authentication>("Web05Report", _ => { });
        builder.Services.AddAuthorization(options =>
        {
            foreach (var code in new[] { PermissionCodes.Report.Profit.View, PermissionCodes.Report.Profit.Export, PermissionCodes.Report.Treasury.Manage, PermissionCodes.Report.Sales.View })
                options.AddPolicy(code, policy => policy.RequireAuthenticatedUser().RequireAssertion(async context =>
                {
                    var http = context.Resource as HttpContext ?? accessor.HttpContext;
                    var allowed = http is not null && int.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) && await http.RequestServices.GetRequiredService<ICurrentStorePermissionService>().HasPermissionAsync(seed.StoreId, id, code);
                    policyChecks.Enqueue(new { Policy = code, ResourceIsHttpContext = context.Resource is HttpContext, RequestContextAvailable = http is not null, Allowed = allowed });
                    return allowed;
                }));
        });
        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            Exception? failure = null;
            try { await next(); }
            catch (Exception error) { failure = error; throw; }
            finally
            {
                await File.WriteAllTextAsync(Web05Support.Artifact("report-host/" + trace + "/" + Guid.NewGuid().ToString("N") + ".json"),
                    JsonSerializer.Serialize(new { Path = context.Request.Path.Value, ResponseStatus = context.Response.StatusCode, ExceptionType = failure?.GetType().FullName, PolicyChecks = policyChecks.ToArray() }));
            }
        });
        app.UseStaticFiles(); app.UseAuthentication(); app.UseAuthorization(); app.MapControllers(); await app.StartAsync(); return new(app, seed.UserId);
    }
    public async ValueTask DisposeAsync() { await app.StopAsync(); await app.DisposeAsync(); }
    private sealed class Authentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(!int.TryParse(Request.Headers["X-Web05-User"], out var id) ? AuthenticateResult.NoResult() : AuthenticateResult.Success(new(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id.ToString())], Scheme.Name)), Scheme.Name)));
    }
    private sealed class Actor(IHttpContextAccessor accessor) : ICurrentUser
    {
        public int? UserId => int.TryParse(accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
        public bool IsAuthenticated => accessor.HttpContext?.User.Identity?.IsAuthenticated == true; public string? UserName => "WEB05 synthetic report user"; public int? TerminalId => null; public string? TerminalCode => null;
    }
    // Executes the production workspace and its real CSS/JS sections. The menu
    // layout is not executed and no admin bootstrap/seeder is registered.
    private sealed class ReportBodyOnly : IAsyncResultFilter
    {
        public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
        {
            if (context.Result is ViewResult result)
            {
                var services = context.HttpContext.RequestServices; var view = services.GetRequiredService<IRazorViewEngine>().GetView(null, "/Areas/Admin/Views/OperationsReport/Workspace.cshtml", false); Assert.True(view.Success);
                using var body = new StringWriter(); var viewContext = new ViewContext(context, view.View, result.ViewData, result.TempData ?? new TempDataDictionary(context.HttpContext, services.GetRequiredService<ITempDataProvider>()), body, new HtmlHelperOptions());
                var page = Assert.IsType<RazorView>(view.View).RazorPage; page.ViewContext = viewContext; services.GetRequiredService<IRazorPageActivator>().Activate(page, viewContext); await page.ExecuteAsync();
                using var styles = new StringWriter(); viewContext.Writer = styles; if (page.SectionWriters.TryGetValue("VendorStyles", out var style)) await style();
                using var scripts = new StringWriter(); viewContext.Writer = scripts; foreach (var name in new[] { "VendorScripts", "PageScripts" }) if (page.SectionWriters.TryGetValue(name, out var script)) await script();
                context.Result = new ContentResult { Content = "<!doctype html><html lang=\"vi\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">" + styles + "</head><body>" + body + scripts + "</body></html>", ContentType = "text/html; charset=utf-8" };
            }
            await next();
        }
    }
}
