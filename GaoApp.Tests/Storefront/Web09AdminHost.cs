using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Interfaces.Services.Security;
using GaoApp.Application.Services.Security;
using GaoApp.Application.Storefront;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Repositories.Security;
using GaoApp.Infrastructure.Services.Storefront;
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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GaoApp.Tests.Storefront;

// Real controller, compiled view, binder, permission repository and CSRF. The
// test identity header is fixture-owned, not a production login/auth claim.
internal sealed class Web09AdminHost(WebApplication app, string directory) : IAsyncDisposable
{
    internal string BaseUrl => app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
    internal static async Task<Web09AdminHost> StartAsync(Web09Case c, bool activationPolicy = true)
    {
        var directory = Web09Support.Runtime("WEB09 scoped actual settings controller/compiled view, no AdminMain, ephemeral antiforgery only");
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ApplicationName = typeof(StorefrontSettingsController).Assembly.FullName,
            EnvironmentName = "Testing", ContentRootPath = Web09Support.Root() });
        builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["StorefrontRelease:AllowSettingsActivation"] = activationPolicy.ToString(),
            ["StorefrontHost:Host"] = "localhost", ["StorefrontHost:StoreSubdomain"] = await c.Seed.SubdomainAsync() });
        builder.Services.AddControllersWithViews(o => { o.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true; o.Filters.Add<BodyOnly>(); });
        builder.Services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider()); builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<ICurrentUser, HttpActor>(); builder.Services.AddScoped<AppDbContext>(_ => c.Context());
        builder.Services.AddScoped<ICurrentStorePermissionService>(s => new CurrentStorePermissionService(new UserInStoreRepository(s.GetRequiredService<AppDbContext>())));
        builder.Services.AddScoped<IStorefrontSettingsService, StorefrontSettingsService>(); builder.Services.AddScoped<IStorefrontSalesControlService, StorefrontSalesControlService>();
        builder.Services.AddAuthentication("Web09Owned").AddScheme<AuthenticationSchemeOptions, HttpAuthentication>("Web09Owned", _ => { });
        builder.Services.AddAuthorization(options =>
        {
            foreach (var right in new[] { StorefrontPermissions.ViewSettings, StorefrontPermissions.ManageSettings, StorefrontPermissions.ManageShipping })
                options.AddPolicy(right, policy => policy.RequireAuthenticatedUser().RequireAssertion(async context =>
                {
                    var http = (HttpContext)context.Resource!;
                    return int.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var user) &&
                        await http.RequestServices.GetRequiredService<ICurrentStorePermissionService>().HasPermissionAsync(c.StoreId, user, right);
                }));
        });
        builder.Services.AddAntiforgery(o => o.HeaderName = "RequestVerificationToken");
        var app = builder.Build();
        try
        {
            app.UseMiddleware<GlobalExceptionMiddleware>(); app.UseAuthentication(); app.UseAuthorization(); app.MapControllers();
            app.MapGet("/lib/bootstrap/dist/css/bootstrap.min.css", () => Results.File(Path.Combine(Web09Support.Root(), "GaoApp.Web/wwwroot/lib/bootstrap/dist/css/bootstrap.min.css"), "text/css"));
            app.MapGet("/css/site.css", () => Results.File(Path.Combine(Web09Support.Root(), "GaoApp.Web/wwwroot/css/site.css"), "text/css"));
            await app.StartAsync();
            await File.WriteAllTextAsync(Path.Combine(directory, "actual-admin-host.json"), JsonSerializer.Serialize(new { AtUtc = DateTime.UtcNow, Owner = Web09Support.Owner(),
                c.StoreId, c.Seed.UserId, NoAdminMain = true, ActualController = typeof(StorefrontSettingsController).FullName, EphemeralAntiforgery = true,
                BaseUrl = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single(), ProductionAuthenticationClaim = false }));
            return new(app, directory);
        }
        catch { await app.DisposeAsync(); throw; }
    }
    internal HttpClient Client(int? user = null)
    {
        var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, CookieContainer = new CookieContainer() }) { BaseAddress = new Uri(BaseUrl) };
        if (user.HasValue) client.DefaultRequestHeaders.Add("X-Web09-User", user.Value.ToString()); return client;
    }
    public async ValueTask DisposeAsync()
    {
        await app.StopAsync(); await app.DisposeAsync();
        await File.WriteAllTextAsync(Path.Combine(directory, "admin-host-disposed.json"), JsonSerializer.Serialize(new { AtUtc = DateTime.UtcNow, Owner = Web09Support.Owner(), NoPrivateCleanup = true }));
    }
    private sealed class HttpActor(IHttpContextAccessor accessor) : ICurrentUser
    {
        public int? UserId => int.TryParse(accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
        public bool IsAuthenticated => accessor.HttpContext?.User.Identity?.IsAuthenticated == true;
        public string? UserName => "WEB09 synthetic HTTP actor"; public int? TerminalId => null; public string? TerminalCode => null;
    }
    private sealed class HttpAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(!int.TryParse(Request.Headers["X-Web09-User"], out var user)
            ? AuthenticateResult.NoResult() : AuthenticateResult.Success(new(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user.ToString())], Scheme.Name)), Scheme.Name)));
    }
    private sealed class BodyOnly : IAsyncResultFilter
    {
        public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
        {
            if (context.Result is ViewResult result)
            {
                var services = context.HttpContext.RequestServices; var name = result.ViewName ?? context.RouteData.Values["action"]!.ToString();
                var view = services.GetRequiredService<IRazorViewEngine>().GetView(null, "/Areas/Admin/Views/StorefrontSettings/" + name + ".cshtml", false); Assert.True(view.Success);
                using var writer = new StringWriter();
                var vc = new ViewContext(context, view.View, result.ViewData, result.TempData ?? new TempDataDictionary(context.HttpContext, services.GetRequiredService<ITempDataProvider>()), writer, new HtmlHelperOptions());
                var page = Assert.IsType<RazorView>(view.View).RazorPage; page.ViewContext = vc; services.GetRequiredService<IRazorPageActivator>().Activate(page, vc); await page.ExecuteAsync();
                context.Result = new ContentResult { Content = "<!doctype html><html lang=\"vi\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"><link rel=\"stylesheet\" href=\"/lib/bootstrap/dist/css/bootstrap.min.css\"><link rel=\"stylesheet\" href=\"/css/site.css\"></head><body>" + writer + "</body></html>", ContentType = "text/html; charset=utf-8", StatusCode = result.StatusCode };
            }
            await next();
        }
    }
}
