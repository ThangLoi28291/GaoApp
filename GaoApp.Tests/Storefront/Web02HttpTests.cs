using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Interfaces.Services.Security;
using GaoApp.Application.Services.Security;
using GaoApp.Application.Storefront;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Repositories.Security;
using GaoApp.Infrastructure.Services.Storefront;
using GaoApp.Web.Areas.Admin.Controllers;
using Microsoft.AspNetCore.Antiforgery;
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

[Collection("StorefrontWeb02"), Trait("Category", "StorefrontWeb02")]
public sealed class Web02HttpTests(Web01Fixture fixture)
{
    [Fact]
    public async Task Real_controller_HTTP_requires_live_store_permissions_and_CSRF_then_binds_saves_and_encodes_actual_Razor_body()
    {
        var c = await fixture.CaseAsync(); var other = await fixture.CaseAsync(); var before = await c.SideEffectsAsync();
        await using var host = await HttpFixture.StartAsync(c); using var client = host.Client();
        var endpoint = "/admin/storefront/products/" + c.ProductId;
        using (var denied = await client.GetAsync(endpoint)) Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        client.DefaultRequestHeaders.Add("X-Web02-User", c.UserId.ToString());
        using (var denied = await client.GetAsync(endpoint)) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        await Grant(c, StorefrontPermissions.ViewProducts, StorefrontPermissions.ManageProducts);
        using (var noCsrf = await client.PostAsync(endpoint, Fields(c, other.StoreId))) Assert.Equal(HttpStatusCode.BadRequest, noCsrf.StatusCode);
        await using (var check = c.Context()) Assert.Empty(await check.StorefrontProducts.ToListAsync());
        var token = await client.GetStringAsync("/web02-test-token"); client.DefaultRequestHeaders.Add("RequestVerificationToken", token);
        using (var saved = await client.PostAsync(endpoint + "?StoreId=" + other.StoreId, Fields(c, other.StoreId)))
        {
            if (saved.StatusCode != HttpStatusCode.Redirect)
                await File.WriteAllTextAsync(ArtifactPath("http-save-failure.html"), await saved.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        }
        using (var page = await client.GetAsync(endpoint))
        {
            Assert.Equal(HttpStatusCode.OK, page.StatusCode); var html = await page.Content.ReadAsStringAsync();
            Assert.Contains("&lt;script&gt;web02-content&lt;/script&gt;", html); Assert.DoesNotContain("<script>web02-content</script>", html);
            Assert.Contains("Product.Variants[0].BaseUnitIsPublished", html); Assert.Contains("__RequestVerificationToken", html);
        }
        using (var stale = await client.PostAsync(endpoint, Fields(c, other.StoreId))) Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        await using (var db = c.Context())
        {
            var role = await db.UserInStores.Where(x => x.UserId == c.UserId).Select(x => x.RoleId).SingleAsync();
            var manage = await db.Permissions.Where(x => x.Code == StorefrontPermissions.ManageProducts).Select(x => x.Id).SingleAsync();
            await db.RolePermissions.Where(x => x.RoleId == role && x.PermissionId == manage).ExecuteDeleteAsync();
        }
        using (var revoked = await client.PostAsync(endpoint, Fields(c, other.StoreId))) Assert.Equal(HttpStatusCode.Forbidden, revoked.StatusCode);
        using (var readOnly = await client.GetAsync(endpoint))
        {
            Assert.Equal(HttpStatusCode.OK, readOnly.StatusCode); var html = await readOnly.Content.ReadAsStringAsync();
            Assert.Contains("disabled=\"disabled\"", html);
        }
        await using var foreign = other.Context(); Assert.Empty(await foreign.StorefrontProducts.ToListAsync()); Assert.Equal(before, await c.SideEffectsAsync());
    }

    [Theory]
    [InlineData("publication")][InlineData("publication/bulk")]
    public async Task Every_publication_HTTP_command_rejects_missing_CSRF_without_writes(string path)
    {
        var c = await fixture.CaseAsync(); await Grant(c, StorefrontPermissions.ViewProducts, StorefrontPermissions.ManageProducts);
        await using var host = await HttpFixture.StartAsync(c); using var client = host.Client(); client.DefaultRequestHeaders.Add("X-Web02-User", c.UserId.ToString());
        using var response = await client.PostAsync("/admin/storefront/products/" + path, new FormUrlEncodedContent(new Dictionary<string, string> { ["ProductId"] = c.ProductId.ToString(), ["IsPublished"] = "true" }));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); await using var db = c.Context(); Assert.Empty(await db.StorefrontProducts.ToListAsync());
    }

    private static FormUrlEncodedContent Fields(Web01Case c, int forgedStore) => new(new Dictionary<string, string>
    {
        ["StoreId"] = forgedStore.ToString(), ["Product.StoreId"] = forgedStore.ToString(), ["Product.Version"] = "", ["Product.IsPublished"] = "true",
        ["Product.Description"] = "<script>web02-content</script>", ["Product.Variants[0].VariantId"] = c.VariantId.ToString(),
        ["Product.Variants[0].IsPublished"] = "true", ["Product.Variants[0].BaseUnitIsPublished"] = "true", ["Product.Variants[0].BaseUnitWeightKg"] = "1"
    });

    private static async Task Grant(Web01Case c, params string[] codes)
    {
        await using var db = c.Context(); var role = await db.UserInStores.Where(x => x.UserId == c.UserId).Select(x => x.RoleId).SingleAsync();
        foreach (var code in codes) db.RolePermissions.Add(new() { RoleId = role, PermissionId = await db.Permissions.Where(x => x.Code == code).Select(x => x.Id).SingleAsync() });
        await db.SaveChangesAsync();
    }

    private sealed class HttpFixture(WebApplication app) : IAsyncDisposable
    {
        internal static async Task<HttpFixture> StartAsync(Web01Case c)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ApplicationName = typeof(StorefrontProductsController).Assembly.FullName,
                EnvironmentName = "Testing", ContentRootPath = Path.GetTempPath() });
            builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Services.AddControllersWithViews(options =>
            {
                // Match GaoApp.Web/Program.cs:135 for intentionally blank create Version.
                options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
                options.Filters.Add<BodyOnly>();
            });
            builder.Services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
            builder.Services.AddHttpContextAccessor(); builder.Services.AddScoped<ICurrentUser, HttpActor>();
            builder.Services.AddScoped<AppDbContext>(_ => c.Context());
            builder.Services.AddScoped<ICurrentStorePermissionService>(s => new CurrentStorePermissionService(new UserInStoreRepository(s.GetRequiredService<AppDbContext>())));
            builder.Services.AddScoped<IStorefrontProductService, StorefrontProductService>();
            builder.Services.AddAuthentication("Web02Test").AddScheme<AuthenticationSchemeOptions, HttpAuthentication>("Web02Test", _ => { });
            builder.Services.AddAuthorization(options =>
            {
                foreach (var right in new[] { StorefrontPermissions.ViewProducts, StorefrontPermissions.ManageProducts })
                    options.AddPolicy(right, policy => policy.RequireAuthenticatedUser().RequireAssertion(async context =>
                    {
                        var http = (HttpContext)context.Resource!;
                        return int.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) &&
                            await http.RequestServices.GetRequiredService<ICurrentStorePermissionService>().HasPermissionAsync(c.StoreId, id, right);
                    }));
            });
            builder.Services.AddAntiforgery(options => options.HeaderName = "RequestVerificationToken");
            var app = builder.Build(); app.UseAuthentication(); app.UseAuthorization(); app.MapControllers();
            app.MapGet("/web02-test-token", (HttpContext http, IAntiforgery csrf) => csrf.GetAndStoreTokens(http).RequestToken).RequireAuthorization();
            await app.StartAsync(); return new(app);
        }
        internal HttpClient Client() => new(new HttpClientHandler { AllowAutoRedirect = false, CookieContainer = new CookieContainer() })
        { BaseAddress = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
        public async ValueTask DisposeAsync() { await app.StopAsync(); await app.DisposeAsync(); }
    }

    private sealed class HttpActor(IHttpContextAccessor accessor) : ICurrentUser
    {
        public int? UserId => int.TryParse(accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
        public bool IsAuthenticated => accessor.HttpContext?.User.Identity?.IsAuthenticated == true;
        public string? UserName => "WEB02 synthetic HTTP actor";
        public int? TerminalId => null;
        public string? TerminalCode => null;
    }
    private sealed class HttpAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!int.TryParse(Request.Headers["X-Web02-User"], out var id)) return Task.FromResult(AuthenticateResult.NoResult());
            return Task.FromResult(AuthenticateResult.Success(new(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id.ToString())], Scheme.Name)), Scheme.Name)));
        }
    }
    // Execute the real compiled view body while excluding unrelated admin layout
    // services and startup. MVC routing, binding, authorization and CSRF stay real.
    private sealed class BodyOnly : IAsyncResultFilter
    {
        public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
        {
            if (context.Result is ViewResult result)
            {
                var services = context.HttpContext.RequestServices; var name = result.ViewName ?? context.RouteData.Values["action"]!.ToString();
                var view = services.GetRequiredService<IRazorViewEngine>().GetView(null, "/Areas/Admin/Views/StorefrontProducts/" + name + ".cshtml", false);
                Assert.True(view.Success); using var writer = new StringWriter();
                var viewContext = new ViewContext(context, view.View, result.ViewData, result.TempData ?? new TempDataDictionary(context.HttpContext, services.GetRequiredService<ITempDataProvider>()), writer, new HtmlHelperOptions());
                var page = Assert.IsType<RazorView>(view.View).RazorPage; page.ViewContext = viewContext;
                services.GetRequiredService<IRazorPageActivator>().Activate(page, viewContext); await page.ExecuteAsync();
                context.Result = new ContentResult { Content = writer.ToString(), ContentType = "text/html; charset=utf-8", StatusCode = result.StatusCode };
            }
            await next();
        }
    }

    private static string ArtifactPath(string file, [System.Runtime.CompilerServices.CallerFilePath] string source = "")
    {
        var directory = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "../../.artifacts/storefront-rollout/WEB02/tests"));
        Directory.CreateDirectory(directory); return Path.Combine(directory, file);
    }
}
