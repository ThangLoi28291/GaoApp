using System.Globalization;
using GaoApp.Application.Common;
using GaoApp.Domain.Entities;
using GaoApp.Web.Areas.Admin.Controllers;
using GaoApp.Web.Services.Acb;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace GaoApp.Tests.Payments;

public sealed partial class AcbPaymentTests
{
    private static WebApplication SettingsTestHost(Fixture f)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing", ContentRootPath = Path.GetTempPath() });
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton<ITenantContext>(f.Tenant);
        builder.Services.AddControllersWithViews(o => o.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true);
        return builder.Build();
    }

    private static AcbSettingsController SettingsController(Fixture f, IServiceProvider services)
    {
        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Host = new HostString("store1.localhost", 5100);
        context.Request.Scheme = "http";
        return new AcbSettingsController(f.Db, f.Protocol)
        {
            ControllerContext = new ControllerContext { HttpContext = context, RouteData = new Microsoft.AspNetCore.Routing.RouteData(), ActionDescriptor = new Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor() },
            TempData = new TempDataDictionary(context, new SettingsTempDataProvider())
        };
    }

    private sealed class SettingsTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    public async Task Empty_or_invalid_account_form_shows_a_named_Vietnamese_error_and_does_not_save(string account)
    {
        await using var f = await Fixture.Create();
        await using var app = SettingsTestHost(f);
        using var scope = app.Services.CreateScope();
        var controller = SettingsController(f, scope.ServiceProvider);
        var form = new AcbSettingsForm();
        var values = new FormCollection(new Dictionary<string, StringValues>
        {
            [nameof(form.BankAccountId)] = account,
            [nameof(form.ClientId)] = "new-client",
            [nameof(form.ClientSecret)] = "submitted-secret",
            [nameof(form.CallbackApiKey)] = "submitted-callback-key"
        });
        Assert.False(await controller.TryUpdateModelAsync(form, "", new FormValueProvider(BindingSource.Form, values, CultureInfo.InvariantCulture)));
        var result = Assert.IsType<ViewResult>(await controller.Index(form, default));
        var errors = controller.ModelState[nameof(form.BankAccountId)]!.Errors;
        Assert.Single(errors);
        Assert.Contains("Hãy chọn tài khoản nhận tiền ACB", errors[0].ErrorMessage);
        Assert.DoesNotContain("The value", errors[0].ErrorMessage);
        Assert.Equal("new-client", Assert.IsType<AcbSettingsForm>(result.Model).ClientId);
        Assert.Null(form.ClientSecret);
        Assert.Null(form.CallbackApiKey);
        Assert.False(controller.ModelState.ContainsKey(nameof(form.ClientSecret)));
        Assert.False(controller.ModelState.ContainsKey(nameof(form.CallbackApiKey)));
        var saved = (await f.Service.SettingsAsync(default))!;
        Assert.Equal("test-client", saved.ClientId);
        Assert.Equal(1, saved.BankAccountId);
        Assert.Equal(0, f.Bank.TokenCalls);
    }

    [Theory]
    [InlineData("VCB", true)]
    [InlineData("ACB", false)]
    public async Task Unavailable_bank_account_is_excluded_and_rejected_without_changing_settings(string code, bool active)
    {
        await using var f = await Fixture.Create();
        var bank = await f.Db.StoreBankAccounts.SingleAsync();
        bank.BankCode = code; bank.IsActive = active;
        await f.Db.SaveChangesAsync();
        await using var app = SettingsTestHost(f);
        using var scope = app.Services.CreateScope();
        var controller = SettingsController(f, scope.ServiceProvider);
        var form = new AcbSettingsForm { BankAccountId = 1, ClientId = "do-not-save" };
        Assert.IsType<ViewResult>(await controller.Index(form, default));
        Assert.Single(controller.ModelState[nameof(form.BankAccountId)]!.Errors);
        Assert.Empty(Assert.IsType<SelectList>(controller.ViewData["BankAccounts"]));
        Assert.Equal(false, controller.ViewData["HasAcbBankAccounts"]);
        Assert.Equal("test-client", (await f.Service.SettingsAsync(default))!.ClientId);
        Assert.Equal(0, f.Bank.TokenCalls);
    }

    [Fact]
    public async Task Selected_active_ACB_account_saves_and_keeps_existing_secrets_when_left_blank()
    {
        await using var f = await Fixture.Create();
        var bank = await f.Db.StoreBankAccounts.SingleAsync();
        bank.BankCode = "acb";
        await f.Db.SaveChangesAsync();
        var saved = (await f.Service.SettingsAsync(default))!;
        var originalSecret = saved.ClientSecretProtected;
        var originalCallback = saved.CallbackApiKeyProtected;
        await using var app = SettingsTestHost(f);
        using var scope = app.Services.CreateScope();
        var controller = SettingsController(f, scope.ServiceProvider);
        controller.Request.QueryString = new QueryString("?tenant=store1");
        var form = new AcbSettingsForm();
        Assert.True(await controller.TryUpdateModelAsync(form, "", new FormValueProvider(BindingSource.Form,
            new FormCollection(new Dictionary<string, StringValues> { [nameof(form.BankAccountId)] = "1", [nameof(form.ClientId)] = "updated-client" }), CultureInfo.InvariantCulture)));
        var result = Assert.IsType<RedirectToActionResult>(await controller.Index(form, default));
        Assert.Equal("store1", result.RouteValues!["tenant"]);
        Assert.Equal("updated-client", saved.ClientId);
        Assert.Equal(1, saved.BankAccountId);
        Assert.Equal(originalSecret, saved.ClientSecretProtected);
        Assert.Equal(originalCallback, saved.CallbackApiKeyProtected);
        var getController = SettingsController(f, scope.ServiceProvider);
        var get = Assert.IsType<ViewResult>(await getController.Index(default(CancellationToken)));
        Assert.Equal(1, Assert.IsType<AcbSettingsForm>(get.Model).BankAccountId);
        Assert.Single(Assert.IsType<SelectList>(getController.ViewData["BankAccounts"]));
        Assert.Equal(0, f.Bank.TokenCalls);
    }
}
