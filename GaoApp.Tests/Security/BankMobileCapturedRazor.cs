using GaoApp.Web.Areas.Admin.Controllers;
using GaoApp.Web.Services.Acb;
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

namespace GaoApp.Tests.Security;

/// <summary>
/// Executes compiled native Razor bodies with owned synthetic read models for states normally reached
/// by bank-command POSTs. It does not invoke application startup, a bank client, a controller command,
/// an application database or a layout. Browser acceptance mounts these bodies in an existing native frame.
/// </summary>
internal static class BankMobileCapturedRazor
{
    internal static async Task<IReadOnlyDictionary<string, string>> CaptureAsync(int orderId, int storeId, int otherStoreId)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Testing",
            ApplicationName = typeof(AcbSettingsController).Assembly.FullName,
            ContentRootPath = Path.GetTempPath()
        });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddControllersWithViews();
        builder.Services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        await using var app = builder.Build();
        app.MapControllers();
        await app.StartAsync();
        using var scope = app.Services.CreateScope();
        var at = new DateTime(2026, 10, 27, 4, 15, 0, DateTimeKind.Utc);
        var sandbox = new AcbSandboxCheckResult
        {
            RunId = new Guid("17000000-0000-0000-0000-000000000001"), StoreId = storeId,
            Environment = "Sandbox", ProviderOrderId = "TSUI17CAPTURED", TraceNumber = "UI17-SYNTHETIC-TRACE",
            StartedAtUtc = at, CancellationConfirmed = true
        };
        sandbox.Steps.AddRange([
            new("Lấy token", true, "Mô hình kiểm thử UI-17: không gọi ngân hàng."),
            new("Tạo QR thử", true, "Kết quả tổng hợp để kiểm tra Razor; không tạo QR thật."),
            new("Tra cứu QR thử", false, "Phản hồi chậm: giữ tham chiếu để đối chiếu lại."),
            new("Hủy QR thử", true, "Trạng thái tổng hợp: đã xác nhận hủy QR thử.")
        ]);
        var routing = new AcbCallbackRoutingPage(
            [new(storeId, "Cửa hàng kiểm thử UI-17", "ui17-current"), new(otherStoreId, "Cửa hàng còn lại UI-17", "ui17-other")],
            [new("legacy-bank-ui17.invalid", storeId, "UI17-CAPTURED-ROWVERSION"), new("paused-bank-ui17.invalid", null, "")],
            [new("legacy-bank-ui17.invalid", null, storeId, 17001, at), new("paused-bank-ui17.invalid", otherStoreId, null, 17001, at.AddMinutes(-1))]);
        var rows = new List<AcbBankLookupOrder>
        {
            new("UI17-QR-TWO-TRANSACTIONS", "UI17-QR-TRACE-1", 170000m, "COMPLETED", at,
                [new("UI17-BANK-TRANSACTION-1", 120000m, "SUCCESS", "Đợt chuyển thứ nhất cho đơn UI-17", "2026-10-27"),
                 new("UI17-BANK-TRANSACTION-2", 50000m, "SUCCESS", "Đợt chuyển thứ hai cho cùng QR UI-17", "2026-10-27")],
                orderId, "UI17-SYNTHETIC-POS", 2, at.AddMinutes(1)),
            new("UI17-QR-NO-LOCAL-ORDER", "UI17-QR-TRACE-2", 80000m, "PENDING", at.AddMinutes(-2), [], null, null, 0, null)
        };
        var bank = new AcbBankLookupPage(at.Date, at.Date, 1, 50, 3, 102, at, "Sandbox", "UI17-SYNTHETIC-MERCHANT", rows);
        var emptyBank = new AcbBankLookupPage(at.Date, at.Date, 1, 50, 0, 0, at, "Sandbox", "UI17-SYNTHETIC-MERCHANT", []);
        return new Dictionary<string, string>
        {
            ["sandboxResult"] = await BodyAsync(scope.ServiceProvider, "AcbSettings", "SandboxResult", sandbox),
            ["routingPopulated"] = await BodyAsync(scope.ServiceProvider, "AcbCallbackRouting", "Index", routing),
            ["bankLookupPopulated"] = await BodyAsync(scope.ServiceProvider, "AcbReconciliation", "_AcbBankLookup", bank),
            ["bankLookupEmpty"] = await BodyAsync(scope.ServiceProvider, "AcbReconciliation", "_AcbBankLookup", emptyBank)
        };
    }

    private static async Task<string> BodyAsync<T>(IServiceProvider services, string controller, string viewName, T model)
    {
        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Scheme = "http";
        context.Request.Host = new HostString("ui17-captured.invalid");
        context.Request.Path = controller == "AcbCallbackRouting" ? "/admin/acb/callback-routing" : "/admin/acb/settings";
        context.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(), "UI-17 captured Razor only"));
        context.Request.RouteValues = new RouteValueDictionary(new { area = "Admin", controller, action = viewName });
        var action = new ActionContext(context, new RouteData(context.Request.RouteValues), new ActionDescriptor());
        var engine = services.GetRequiredService<IRazorViewEngine>();
        var found = engine.GetView(null, $"/Areas/Admin/Views/{controller}/{viewName}.cshtml", false);
        if (!found.Success || found.View is not RazorView view)
            throw new InvalidOperationException("UI-17 native compiled Razor body was not found: " + controller + "/" + viewName);
        using var writer = new StringWriter();
        var viewContext = new ViewContext(action, view,
            new ViewDataDictionary<T>(new EmptyModelMetadataProvider(), new ModelStateDictionary()) { Model = model },
            new TempDataDictionary(context, new CapturedTempDataProvider()), writer, new HtmlHelperOptions());
        // Execute the actual generated page and TagHelpers, while deliberately skipping RazorView's
        // layout pipeline. Thus SandboxResult's explicit Layout cannot resolve app services or menus.
        var page = view.RazorPage;
        page.ViewContext = viewContext;
        services.GetRequiredService<IRazorPageActivator>().Activate(page, viewContext);
        await page.ExecuteAsync();
        return writer.ToString();
    }

    private sealed class CapturedTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
}
