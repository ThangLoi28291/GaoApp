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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GaoApp.Tests.Storefront;

[Collection("StorefrontWeb04"), Trait("Category", "StorefrontWeb04")]
public sealed class Web04AdminHttpTests(Web01Fixture fixture)
{
    [Fact]
    public async Task Actual_admin_inbox_detail_notifications_require_live_tenant_permission_encode_customer_snapshot_and_create_no_menu()
    {
        var c = await fixture.Web04Async(); var other = await fixture.Web04Async(); var request = c.Request(); request.RecipientName = "<script>WEB04-INBOX-XSS</script>";
        var placed = await c.PlaceAsync(Web04Case.PlaceRequest(await c.QuoteAsync(request))); await other.PlaceAsync(Web04Case.PlaceRequest(await other.QuoteAsync()));
        int id, foreignId; await using (var db = c.Context()) id = (await db.StorefrontOrders.SingleAsync()).Id; await using (var db = other.Context()) foreignId = (await db.StorefrontOrders.SingleAsync()).Id;
        var before = await c.CountsAsync(); await using var host = await AdminFixture.StartAsync(c); using var client = host.Client();
        foreach (var path in new[] { "/admin/storefront/orders", "/admin/storefront/orders/" + id, "/admin/storefront/orders/notifications?afterId=0" })
        { using var denied = await client.GetAsync(path); Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode); }
        client.DefaultRequestHeaders.Add("X-Web04-User", c.Seed.UserId.ToString()); using (var denied = await client.GetAsync("/admin/storefront/orders")) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        await Grant(c, true);
        using (var index = await client.GetAsync("/admin/storefront/orders?StoreId=" + other.StoreId))
        {
            Assert.Equal(HttpStatusCode.OK, index.StatusCode); var html = await index.Content.ReadAsStringAsync(); Assert.Contains(placed.Order!.Code, html);
            Assert.Contains("&lt;script&gt;WEB04-INBOX-XSS&lt;/script&gt;", html); Assert.DoesNotContain("<script>WEB04-INBOX-XSS", html);
        }
        using (var detail = await client.GetAsync("/admin/storefront/orders/" + id)) { Assert.Equal(HttpStatusCode.OK, detail.StatusCode); Assert.Contains(placed.Order!.Code, await detail.Content.ReadAsStringAsync()); }
        using (var foreign = await client.GetAsync("/admin/storefront/orders/" + foreignId)) Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        using (var notifications = await client.GetAsync("/admin/storefront/orders/notifications?afterId=0"))
        {
            Assert.Equal(HttpStatusCode.OK, notifications.StatusCode); using var json = JsonDocument.Parse(await notifications.Content.ReadAsStringAsync()); Assert.Equal(1, json.RootElement.GetProperty("newOrders").GetInt32()); Assert.Equal(id, json.RootElement.GetProperty("newestId").GetInt32());
            Assert.DoesNotContain(Web04Support.GuestToken(placed.Order!), json.RootElement.ToString());
        }
        using (var none = await client.GetAsync("/admin/storefront/orders/notifications?afterId=" + id))
        { using var json = JsonDocument.Parse(await none.Content.ReadAsStringAsync()); Assert.Equal(0, json.RootElement.GetProperty("newOrders").GetInt32()); }
        await Grant(c, false); foreach (var path in new[] { "/admin/storefront/orders", "/admin/storefront/orders/" + id, "/admin/storefront/orders/notifications?afterId=0" })
        { using var revoked = await client.GetAsync(path); Assert.Equal(HttpStatusCode.Forbidden, revoked.StatusCode); }
        Assert.Equal(before, await c.CountsAsync());
    }
    private static async Task Grant(Web04Case c, bool enabled)
    {
        await using var db = c.Context(); var role = await db.UserInStores.Where(x => x.UserId == c.Seed.UserId).Select(x => x.RoleId).SingleAsync();
        var permission = await db.Permissions.Where(x => x.Code == StorefrontPermissions.ViewOrders).Select(x => x.Id).SingleAsync();
        if (enabled) { db.RolePermissions.Add(new() { RoleId = role, PermissionId = permission }); await db.SaveChangesAsync(); }
        else await db.RolePermissions.Where(x => x.RoleId == role && x.PermissionId == permission).ExecuteDeleteAsync();
    }
    private sealed class AdminFixture(WebApplication app) : IAsyncDisposable
    {
        internal static async Task<AdminFixture> StartAsync(Web04Case c)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ApplicationName = typeof(StorefrontProductsController).Assembly.FullName, EnvironmentName = "Testing", ContentRootPath = Path.GetTempPath() });
            builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Services.AddControllersWithViews(options => { options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true; options.Filters.Add<BodyOnly>(); });
            builder.Services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider()); builder.Services.AddHttpContextAccessor(); builder.Services.AddScoped<ICurrentUser, HttpActor>();
            builder.Services.AddScoped<AppDbContext>(_ => c.Context()); builder.Services.AddScoped<ICurrentStorePermissionService>(s => new CurrentStorePermissionService(new UserInStoreRepository(s.GetRequiredService<AppDbContext>())));
            builder.Services.AddScoped<IStorefrontOrderInboxService, StorefrontOrderInboxService>(); builder.Services.AddAuthentication("Web04Test").AddScheme<AuthenticationSchemeOptions, HttpAuthentication>("Web04Test", _ => { });
            builder.Services.AddAuthorization(options => options.AddPolicy(StorefrontPermissions.ViewOrders, policy => policy.RequireAuthenticatedUser().RequireAssertion(async context => {
                var http = (HttpContext)context.Resource!; return int.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) && await http.RequestServices.GetRequiredService<ICurrentStorePermissionService>().HasPermissionAsync(c.StoreId, id, StorefrontPermissions.ViewOrders); })));
            var app = builder.Build(); app.UseMiddleware<GlobalExceptionMiddleware>(); app.UseAuthentication(); app.UseAuthorization(); app.MapControllers(); await app.StartAsync(); return new(app);
        }
        internal HttpClient Client() => new(new HttpClientHandler { AllowAutoRedirect = false, CookieContainer = new CookieContainer() }) { BaseAddress = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
        public async ValueTask DisposeAsync() { await app.StopAsync(); await app.DisposeAsync(); }
    }
    private sealed class HttpActor(IHttpContextAccessor accessor) : ICurrentUser
    {
        public int? UserId => int.TryParse(accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
        public bool IsAuthenticated => accessor.HttpContext?.User.Identity?.IsAuthenticated == true;
        public string? UserName => "WEB04 isolated HTTP actor"; public int? TerminalId => null; public string? TerminalCode => null;
    }
    private sealed class HttpAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(!int.TryParse(Request.Headers["X-Web04-User"], out var id) ? AuthenticateResult.NoResult() :
            AuthenticateResult.Success(new(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id.ToString())], Scheme.Name)), Scheme.Name)));
    }
    private sealed class BodyOnly : IAsyncResultFilter
    {
        public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
        {
            if (context.Result is ViewResult result)
            {
                var services = context.HttpContext.RequestServices; var name = result.ViewName ?? context.RouteData.Values["action"]!.ToString();
                var view = services.GetRequiredService<IRazorViewEngine>().GetView(null, "/Areas/Admin/Views/StorefrontOrders/" + name + ".cshtml", false); Assert.True(view.Success);
                using var writer = new StringWriter(); var viewContext = new ViewContext(context, view.View, result.ViewData, result.TempData ?? new TempDataDictionary(context.HttpContext, services.GetRequiredService<ITempDataProvider>()), writer, new HtmlHelperOptions());
                var page = Assert.IsType<RazorView>(view.View).RazorPage; page.ViewContext = viewContext; services.GetRequiredService<IRazorPageActivator>().Activate(page, viewContext); await page.ExecuteAsync();
                context.Result = new ContentResult { Content = writer.ToString(), ContentType = "text/html; charset=utf-8", StatusCode = result.StatusCode };
            }
            await next();
        }
    }
}
