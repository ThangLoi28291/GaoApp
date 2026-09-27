using System.Globalization;
using System.Reflection;
using System.Security.Claims;
using System.Text.RegularExpressions;
using GaoApp.Application.Common.Results;
using GaoApp.Application.DTOs.Invoices;
using GaoApp.Application.Interfaces.Services.Invoices;
using GaoApp.Web.Areas.Admin.Controllers;
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
using Microsoft.Extensions.Primitives;

namespace GaoApp.Tests.Ui;

public sealed class AutoInvoiceSettingsControlsTests
{
    [Theory]
    [InlineData("vi-VN", true)]
    [InlineData("vi-VN", false)]
    [InlineData("en-US", true)]
    [InlineData("en-US", false)]
    public async Task Settings_controls_actual_Razor_preserves_saved_numbers_and_pause_state(string culture, bool enabled)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
        try
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = "Testing", ApplicationName = typeof(AutoInvoiceController).Assembly.FullName,
                ContentRootPath = Path.GetTempPath()
            });
            builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Services.AddControllersWithViews();
            builder.Services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
            builder.Services.AddSingleton<IAuthorizationService>(Proxy<IAuthorizationService>((_, _) =>
                Task.FromResult(AuthorizationResult.Success())));
            await using var app = builder.Build();
            app.MapControllerRoute("areas", "{area:exists}/{controller}/{action}/{id?}");
            await app.StartAsync();
            using var scope = app.Services.CreateScope();
            var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
            http.Request.Scheme = "https"; http.Request.Host = new HostString("admin.example.test");
            http.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(), "Settings rendering"));
            http.Request.RouteValues = new RouteValueDictionary(new { area = "Admin", controller = "AutoInvoice", action = "Index" });
            var action = new ActionContext(http, new RouteData(http.Request.RouteValues), new ActionDescriptor());
            var view = scope.ServiceProvider.GetRequiredService<IRazorViewEngine>()
                .GetView(null, "/Areas/Admin/Views/AutoInvoice/Index.cshtml", false);
            Assert.True(view.Success);
            var model = new AutoInvoiceDashboardDto { Settings = new()
            {
                IsEnabled = enabled, SeparateAmountThreshold = 80_000.25m, GroupTargetAmount = 150_000.50m,
                MinimumAgeMinutes = 3, SendIntervalSeconds = 45, ClosingTimeLocal = new TimeSpan(22, 15, 0),
                TimeZoneId = "SE Asia Standard Time"
            }};
            var data = new ViewDataDictionary<AutoInvoiceDashboardDto>(new EmptyModelMetadataProvider(), new ModelStateDictionary()) { Model = model };
            data["Tab"] = "settings";
            using var writer = new StringWriter();
            await view.View.RenderAsync(new ViewContext(action, view.View, data,
                new TempDataDictionary(http, new MemoryTempData()), writer, new HtmlHelperOptions()));
            var html = writer.ToString();
            Assert.Equal("80000.25", InputValue(html, "SeparateAmountThreshold"));
            Assert.Equal("150000.5", InputValue(html, "GroupTargetAmount"));
            Assert.Equal("3", InputValue(html, "MinimumAgeMinutes"));
            Assert.Equal("45", InputValue(html, "SendIntervalSeconds"));
            Assert.Equal("22:15", InputValue(html, "ClosingTimeLocal"));
            Assert.Contains("/Admin/AutoInvoice/Toggle", html);
            Assert.Contains("/Admin/AutoInvoice/SaveSettings", html);
            Assert.Contains("__RequestVerificationToken", html);
            var decoded = System.Net.WebUtility.HtmlDecode(html);
            Assert.Contains(enabled ? "Tạm ngưng" : "Tiếp tục", decoded);
            var checkbox = Regex.Match(html, "<input[^>]*type=\"checkbox\"[^>]*name=\"IsEnabled\"[^>]*>").Value;
            Assert.NotEmpty(checkbox);
            Assert.Equal(enabled, checkbox.Contains("checked="));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Theory]
    [InlineData("vi-VN", "120000", "250000", 120000, 250000)]
    [InlineData("vi-VN", "120000.25", "250000.50", 120000.25, 250000.50)]
    [InlineData("en-US", "90000.25", "180000.50", 90000.25, 180000.50)]
    public async Task Settings_controls_save_reads_HTML_decimal_format_without_multiplying_amounts(
        string culture, string threshold, string target, decimal expectedThreshold, decimal expectedTarget)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
        try
        {
            UpdateAutoInvoiceSettingsRequest? saved = null;
            var controller = Controller((_, args) =>
            {
                saved = (UpdateAutoInvoiceSettingsRequest)args[0]!;
                return Task.FromResult(Result<AutoInvoiceSettingsDto>.Success(new()));
            }, threshold, target);
            // Simulate a culture-dependent model binder having interpreted the dot as grouping.
            var request = new UpdateAutoInvoiceSettingsRequest { SeparateAmountThreshold = 12000025, GroupTargetAmount = 25000050 };
            await controller.SaveSettings(request, default);
            Assert.NotNull(saved);
            Assert.Equal(expectedThreshold, saved.SeparateAmountThreshold);
            Assert.Equal(expectedTarget, saved.GroupTargetAmount);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Theory]
    [InlineData("", "150000", false)]
    [InlineData("abc", "150000", false)]
    [InlineData("80.000,50", "150000", false)]
    [InlineData("80000", "NaN", false)]
    [InlineData("80000", "150000", true)]
    public async Task Settings_controls_invalid_submission_does_not_write_settings(string threshold, string target, bool invalidDate)
    {
        var controller = Controller((_, _) => throw new InvalidOperationException("Invalid settings must not be saved."), threshold, target);
        if (invalidDate) controller.ModelState.AddModelError("ScopeStartDateLocal", "Invalid date");
        var result = Assert.IsType<RedirectToActionResult>(await controller.SaveSettings(new(), default));
        Assert.Equal("settings", result.RouteValues!["tab"]);
        Assert.NotNull(controller.TempData["Error"]);
    }

    private static string InputValue(string html, string name)
    {
        var input = Regex.Match(html, "<input[^>]*name=\"" + name + "\"[^>]*>").Value;
        Assert.NotEmpty(input);
        return Regex.Match(input, "value=\"([^\"]*)\"").Groups[1].Value;
    }

    private static AutoInvoiceController Controller(Func<MethodInfo, object?[], object?> handler, string threshold, string target)
    {
        var http = new DefaultHttpContext();
        http.Request.ContentType = "application/x-www-form-urlencoded";
        http.Request.Form = new FormCollection(new Dictionary<string, StringValues>
        {
            ["SeparateAmountThreshold"] = threshold, ["GroupTargetAmount"] = target
        });
        return new AutoInvoiceController(Proxy<IAutoInvoiceService>(handler))
        {
            ControllerContext = new ControllerContext { HttpContext = http },
            TempData = new TempDataDictionary(http, new MemoryTempData())
        };
    }

    private static T Proxy<T>(Func<MethodInfo, object?[], object?> handler) where T : class
    {
        var result = DispatchProxy.Create<T, CallProxy>(); ((CallProxy)(object)result).Handler = handler; return result;
    }
    public class CallProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[], object?> Handler = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? args) => Handler(method!, args ?? []);
    }
    private sealed class MemoryTempData : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
}
