using GaoApp.Web.Areas.Admin.Controllers;
using GaoApp.Web.Services.Acb;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GaoApp.Tests.Payments;

public sealed partial class AcbPaymentTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Store_selector_Razor_renders_saved_id_antiforgery_and_encoded_store_names(bool selected)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing",
            ApplicationName = typeof(AcbCallbackRoutingController).Assembly.FullName, ContentRootPath = Path.GetTempPath() });
        builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddControllersWithViews();
        builder.Services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        await using var app = builder.Build(); app.MapControllers(); await app.StartAsync();
        using var scope = app.Services.CreateScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        context.Request.Scheme = "https"; context.Request.Host = new HostString("admin.gaomart.com.vn");
        context.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(), "View rendering"));
        context.Request.RouteValues = new RouteValueDictionary(new { area = "Admin", controller = "AcbCallbackRouting", action = "Index" });
        var action = new ActionContext(context, new RouteData(context.Request.RouteValues), new ActionDescriptor());
        var view = scope.ServiceProvider.GetRequiredService<IRazorViewEngine>()
            .GetView(null, "/Areas/Admin/Views/AcbCallbackRouting/Index.cshtml", false);
        Assert.True(view.Success);
        var model = new AcbCallbackRoutingPage([new(1, "<script>store-name</script>", "future-domain")],
            [new("www.gaomart.com.vn", selected ? 1 : null, "version-token")], []);
        using var writer = new StringWriter();
        await view.View.RenderAsync(new ViewContext(action, view.View,
            new ViewDataDictionary<AcbCallbackRoutingPage>(new EmptyModelMetadataProvider(), new ModelStateDictionary()) { Model = model },
            new TempDataDictionary(context, new SettingsTempDataProvider()), writer, new HtmlHelperOptions()));
        var html = writer.ToString();
        Assert.Contains("name=\"TargetStoreId\"", html);
        Assert.Contains("value=\"1\"", html);
        Assert.Equal(selected, html.Contains("selected=\"selected\""));
        Assert.Contains("__RequestVerificationToken", html);
        Assert.Contains("version-token", html);
        Assert.Contains("&lt;script&gt;", html); Assert.DoesNotContain("<script>store-name", html);
        Assert.Contains("www.gaomart.com.vn/Admin/api-callback", html);
        Assert.Contains("action=\"/admin/acb/callback-routing\"", html);
    }

    [Fact]
    public async Task Routing_controller_requires_host_admin_and_write_antiforgery()
    {
        await using var f = await Fixture.Create();
        await AddRoutingAdmin(f, hostAdmin: false);
        var controller = new AcbCallbackRoutingController(Router(f))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = RoutingUser() } }
        };
        Assert.IsType<ForbidResult>(await controller.Index(default));
        Assert.IsType<ForbidResult>(await controller.Save(Selection(1), default));
        Assert.NotEmpty(typeof(AcbCallbackRoutingController).GetCustomAttributes(typeof(AuthorizeAttribute), true));
        Assert.NotEmpty(typeof(AcbCallbackRoutingController).GetMethod(nameof(AcbCallbackRoutingController.Save))!
            .GetCustomAttributes(typeof(ValidateAntiForgeryTokenAttribute), true));
    }
}
