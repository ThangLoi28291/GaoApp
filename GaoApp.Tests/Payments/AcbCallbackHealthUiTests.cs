using System.Net;
using GaoApp.Web.Areas.Admin.Controllers;
using GaoApp.Web.Services.Acb;
using Microsoft.AspNetCore.Builder;
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
    [Fact]
    public async Task Callback_monitor_Razor_encodes_diagnostics_and_distinguishes_transport_from_bank_evidence()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing",
            ApplicationName = typeof(AcbReconciliationController).Assembly.FullName, ContentRootPath = Path.GetTempPath() });
        builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddControllersWithViews();
        await using var app = builder.Build();
        using var scope = app.Services.CreateScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        var action = new ActionContext(context, new RouteData(), new ActionDescriptor());
        var view = scope.ServiceProvider.GetRequiredService<IRazorViewEngine>()
            .GetView(null, "/Areas/Admin/Views/Shared/_AcbCallbackHealth.cshtml", false);
        Assert.True(view.Success);
        var at = new DateTime(2026, 10, 12, 5, 15, 0, DateTimeKind.Utc);
        var model = new AcbCallbackHealth("https://chonthanh.gaomart.com.vn:8443/Admin/api-callback", true, "Ready", true,
            [new("https://www.gaomart.com.vn/Admin/api-callback", "CurrentStore")], null, 0, 0, [],
            [new(at, "<script>diagnostic</script>", 1, 1, 403, "AUTH_REJECTED", null, "<script>host</script>")], at);
        using var writer = new StringWriter();
        await view.View.RenderAsync(new ViewContext(action, view.View,
            new ViewDataDictionary<AcbCallbackHealth>(new EmptyModelMetadataProvider(), new ModelStateDictionary()) { Model = model },
            new TempDataDictionary(context, new SettingsTempDataProvider()), writer, new HtmlHelperOptions()));
        var html = writer.ToString();
        var decoded = WebUtility.HtmlDecode(html);
        Assert.Contains("Chưa nhận callback hợp lệ", decoded);
        Assert.Contains("AUTH_REJECTED", html);
        Assert.Contains("12/10/2026 12:15:00", html);
        Assert.Contains("Giải mã được", decoded);
        Assert.Contains("www.gaomart.com.vn/Admin/api-callback", html);
        Assert.Contains("callback-routing", html);
        Assert.Contains("&lt;script&gt;diagnostic", html);
        Assert.DoesNotContain("<script>diagnostic", html);
        Assert.DoesNotContain("<script>host", html);
        Assert.Contains("x-api-key", html);
        Assert.DoesNotContain("test-key", html);
    }
}
