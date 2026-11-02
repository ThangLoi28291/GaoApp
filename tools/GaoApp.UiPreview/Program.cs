using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using GaoApp.Application.Common.Security;
using GaoApp.Web.Areas.Admin.Controllers;
using GaoApp.Web.Areas.Admin.ViewModels.Commerce;
using Microsoft.AspNetCore.Authorization;
using System.Reflection;

// Isolated, read-only loopback preview. No application startup, DB, workers or real accounts.
var repository = FindRepository();
if (args.Contains("--check-navigation")) { NavigationContract.Check(); return; }
if (args.Contains("--check-contract"))
{
    var controller = typeof(UiFoundationController);
    if (!controller.GetCustomAttributes<AuthorizeAttribute>(true).Any(a => a.Policy == PermissionCodes.Security.Role.Permissions)
        || controller.GetCustomAttribute<AllowAnonymousAttribute>(true) is not null
        || typeof(BaseAdminController).GetCustomAttribute<AuthorizeAttribute>() is null
        || controller.GetCustomAttribute<RouteAttribute>()?.Template != "admin/ui-foundation"
        || controller.GetMethod("Index")?.GetCustomAttribute<HttpGetAttribute>() is null
        || controller.GetMethod("Compare")?.GetCustomAttribute<HttpGetAttribute>()?.Template != "compare"
        || controller.GetMethod("Mobile")?.GetCustomAttribute<HttpGetAttribute>()?.Template != "mobile")
        throw new InvalidOperationException("Production preview route/authorization contract failed.");
    Console.WriteLine("PASS: production controller compiles; inherited authentication, existing permission policy and GET route verified.");
    return;
}
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    WebRootPath = Path.Combine(repository, "GaoApp.Web", "wwwroot"),
    EnvironmentName = "Production"
});
builder.Configuration["AllowedHosts"] = "127.0.0.1;localhost";
builder.Services.AddControllersWithViews();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<NavigationFixture>();
builder.Services.AddScoped<GaoApp.Web.Services.AdminNavigationProvider>();
builder.Services.AddScoped<GaoApp.Application.Interfaces.Services.AdminMenus.IAdminMenuService>(sp => sp.GetRequiredService<NavigationFixture>());
builder.Services.AddScoped<GaoApp.Application.Interfaces.Services.AdminMenus.IAdminMenuPinService>(sp => sp.GetRequiredService<NavigationFixture>());
builder.Services.AddScoped<GaoApp.Application.Common.ITenantContext>(sp => sp.GetRequiredService<NavigationFixture>());
builder.Services.AddScoped<GaoApp.Application.Interfaces.Services.Security.IStoreAdminAccess>(sp => sp.GetRequiredService<NavigationFixture>());
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(PermissionCodes.Security.Role.Permissions,
        policy => policy.RequireClaim("preview.menu-admin", "yes"));
    foreach (var permission in new[] { PermissionCodes.Pos.Order.View, PermissionCodes.Pos.Shift.View, PermissionCodes.Admin.DashboardView })
        options.AddPolicy(permission, policy => policy.RequireAuthenticatedUser());
});
builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
var app = builder.Build();
app.Use(async (context, next) =>
{
    if (context.Request.Method is not ("GET" or "HEAD"))
    {
        context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
        return;
    }
    context.Response.Headers["Cache-Control"] = "no-store";
    await next(context);
});
app.UseStaticFiles();
app.MapGet("/", () => Results.Redirect("/admin/ui-foundation"));
app.MapGet("/health", () => Results.Text("UI preview only; no application services"));
app.MapGet("/admin/ui-foundation", context => Render(context, "Index"));
app.MapGet("/admin/ui-foundation/compare", context => Render(context, "Compare"));
app.MapGet("/admin/ui-foundation/mobile", context => Render(context, "Mobile"));
app.MapGet("/admin/ui-foundation/navigation", context => Render(context, "Navigation"));
app.MapGet("/isolation", context => Render(context, "Isolation"));
app.Run("http://127.0.0.1:5206");

static async Task Render(HttpContext http, string viewName)
{
    if (viewName == "Navigation")
    {
        var claims = new List<System.Security.Claims.Claim>();
        if (http.Request.Query["case"] != "userless") claims.Add(new("UserId", "99"));
        if (http.Request.Query["admin"] == "1") claims.Add(new("preview.menu-admin", "yes"));
        http.User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(claims, "UI fixture"));
    }
    var context = new ActionContext(http, new RouteData(), new ActionDescriptor());
    context.RouteData.Values["area"] = "Admin";
    context.RouteData.Values["controller"] = "UiFoundation";
    context.RouteData.Values["action"] = viewName;
    var engine = http.RequestServices.GetRequiredService<IRazorViewEngine>();
    var result = engine.GetView(null, $"~/Areas/Admin/Views/UiFoundation/{viewName}.cshtml", true);
    if (!result.Success) throw new InvalidOperationException(string.Join(Environment.NewLine, result.SearchedLocations));
    var metadata = http.RequestServices.GetRequiredService<IModelMetadataProvider>();
    var data = new ViewDataDictionary(metadata, new ModelStateDictionary());
    if (viewName == "Mobile") data.Model = MobilePreview.CreateDemo();
    var temp = new TempDataDictionary(http, http.RequestServices.GetRequiredService<ITempDataProvider>());
    using var writer = new StringWriter();
    await result.View.RenderAsync(new ViewContext(context, result.View, data, temp, writer, new HtmlHelperOptions()));
    http.Response.ContentType = "text/html; charset=utf-8";
    await http.Response.WriteAsync(writer.ToString());
}

static string FindRepository()
{
    // Resolve from the project/executable location, never from a user-supplied request path.
    foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
    {
        for (var folder = new DirectoryInfo(start); folder is not null; folder = folder.Parent)
            if (File.Exists(Path.Combine(folder.FullName, "GaoApp.Web", "GaoApp.Web.csproj"))) return folder.FullName;
    }
    throw new DirectoryNotFoundException("Run this preview from a GaoApp checkout.");
}
