using System.Security.Claims;
using GaoApp.Application.Common;
using GaoApp.Application.Common.Security;
using GaoApp.Web.Areas.Admin.Controllers;
using GaoApp.Web.Areas.Admin.ViewModels.Suppliers;
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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GaoApp.Tests.Suppliers;

public sealed class SupplierBankFieldsUiTests
{
    [Fact]
    public async Task Controller_create_edit_clear_status_delete_and_ajax_use_same_store_and_bank_fields()
    {
        await using var f = new SupplierBankFixture();
        using var provider = new ServiceCollection().AddSingleton<ITenantContext>(f.Tenant).BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = provider };
        var controller = new SupplierController(f.Service)
        {
            ControllerContext = new ControllerContext { HttpContext = http },
            TempData = new TempDataDictionary(http, new MemoryTempData())
        };
        controller.Url = new Microsoft.AspNetCore.Mvc.Routing.UrlHelper(new ActionContext(http, new RouteData(), new ActionDescriptor()));
        Assert.Equal("Edit", Assert.IsType<ViewResult>(controller.Create()).ViewName);
        var vm = new SupplierEditViewModel { Name = "Controller synthetic", BankName = "Test bank", BankAccountName = "Test owner", BankAccountNumber = "000123456789" };
        Assert.IsType<RedirectToActionResult>(await controller.Create(vm));
        var row = await f.Db.Suppliers.SingleAsync();
        Assert.Equal(vm.BankName, row.BankName); Assert.Equal(vm.BankAccountName, row.BankAccountName); Assert.Equal(vm.BankAccountNumber, row.BankAccountNumber);
        var edit = Assert.IsType<SupplierEditViewModel>(Assert.IsType<ViewResult>(await controller.Edit(row.Id)).Model);
        Assert.Equal(vm.BankName, edit.BankName); Assert.Equal(vm.BankAccountName, edit.BankAccountName); Assert.Equal(vm.BankAccountNumber, edit.BankAccountNumber);
        edit.BankName = "Updated bank"; edit.BankAccountName = "Updated owner"; edit.BankAccountNumber = "000098765432";
        Assert.IsType<RedirectToActionResult>(await controller.Edit(edit));
        Assert.Equal(edit.BankName, row.BankName); Assert.Equal(edit.BankAccountName, row.BankAccountName); Assert.Equal(edit.BankAccountNumber, row.BankAccountNumber);
        var index = Assert.IsType<SupplierIndexVM>(Assert.IsType<ViewResult>(await controller.Index()).Model);
        var partial = Assert.IsType<PartialViewResult>(await controller.Search("Controller"));
        Assert.Equal("_SupplierTable", partial.ViewName);
        var ajax = Assert.IsType<SupplierIndexVM>(partial.Model);
        Assert.Equal("**** 5432", Assert.Single(index.Paged.Items).MaskedBankAccountNumber);
        Assert.Equal("Updated bank", Assert.Single(ajax.Paged.Items).BankName);
        edit.BankName = " "; edit.BankAccountName = ""; edit.BankAccountNumber = null;
        Assert.IsType<RedirectToActionResult>(await controller.Edit(edit));
        Assert.Null(row.BankName); Assert.Null(row.BankAccountName); Assert.Null(row.BankAccountNumber);
        Assert.IsType<JsonResult>(await controller.ToggleStatus(row.Id));
        Assert.False(row.IsActive);
        Assert.IsType<JsonResult>(await controller.DeleteAjax(row.Id));
        Assert.True(row.IsDeleted);
        Assert.Empty(Assert.IsType<SupplierIndexVM>(Assert.IsType<PartialViewResult>(await controller.Search()).Model).Paged.Items);
    }

    [Fact]
    public async Task Invalid_model_returns_form_with_bank_values_without_persisting()
    {
        await using var f = new SupplierBankFixture();
        var controller = new SupplierController(f.Service);
        var vm = new SupplierEditViewModel { Name = "Synthetic", BankAccountNumber = new string('x', 51), BankName = "Test" };
        controller.ModelState.AddModelError("BankAccountNumber", "Số tài khoản tối đa 50 ký tự");
        Assert.Same(vm, Assert.IsType<ViewResult>(await controller.Create(vm)).Model);
        Assert.Same(vm, Assert.IsType<ViewResult>(await controller.Edit(vm)).Model);
        Assert.Empty(await f.Db.Suppliers.ToListAsync());
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Actual_Razor_partial_masks_desktop_mobile_and_encodes_bank_name(bool withBank, bool allowWrite)
    {
        await using var f = new SupplierBankFixture();
        var request = SupplierBankFieldsTests.Request();
        request.BankAccountNumber = withBank ? "000123456789" : null;
        request.BankName = withBank ? "<script>synthetic-bank</script>" : null;
        Assert.True((await f.Service.CreateAsync(17, request, 9)).IsSuccess);
        var model = new SupplierIndexVM { Paged = await f.Service.GetPagedAsync(17, null, 1, 20), Page = 1, PageSize = 20 };
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing", ApplicationName = typeof(SupplierController).Assembly.FullName, ContentRootPath = Path.GetTempPath() });
        builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddControllersWithViews();
        builder.Services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        builder.Services.AddSingleton<IAuthorizationService>(new FixedAuthorization(allowWrite));
        await using var app = builder.Build(); app.MapControllers(); await app.StartAsync();
        using var scope = app.Services.CreateScope();
        var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        http.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(), "Supplier view rendering"));
        http.Request.RouteValues = new RouteValueDictionary(new { area = "Admin", controller = "Supplier", action = "Search" });
        var action = new ActionContext(http, new RouteData(http.Request.RouteValues), new ActionDescriptor());
        var view = scope.ServiceProvider.GetRequiredService<IRazorViewEngine>().GetView(null, "/Areas/Admin/Views/Supplier/_SupplierTable.cshtml", false);
        Assert.True(view.Success);
        using var writer = new StringWriter();
        await view.View.RenderAsync(new ViewContext(action, view.View,
            new ViewDataDictionary<SupplierIndexVM>(new EmptyModelMetadataProvider(), new ModelStateDictionary()) { Model = model },
            new TempDataDictionary(http, new MemoryTempData()), writer, new HtmlHelperOptions()));
        var html = writer.ToString();
        Assert.DoesNotContain("000123456789", html);
        Assert.DoesNotContain("<script>synthetic-bank", html);
        Assert.Contains("data-supplier-result", html);
        Assert.Contains("gds-desktop-list", html); Assert.Contains("gds-mobile-list", html);
        Assert.Equal(allowWrite, html.Contains("js-delete"));
        if (withBank)
        {
            Assert.Equal(2, html.Split("**** 6789").Length - 1);
            Assert.Equal(2, html.Split("&lt;script&gt;synthetic-bank&lt;/script&gt;").Length - 1);
        }
        else Assert.DoesNotContain("****", html);
    }

    [Theory]
    [InlineData(nameof(SupplierController.ToggleStatus), PermissionCodes.Catalog.Supplier.Update)]
    [InlineData(nameof(SupplierController.DeleteAjax), PermissionCodes.Catalog.Supplier.Delete)]
    public void Status_delete_keep_permission_and_antiforgery_contract(string method, string permission)
    {
        var action = typeof(SupplierController).GetMethod(method)!;
        Assert.Contains(action.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>(), a => a.Policy == permission);
        Assert.NotEmpty(action.GetCustomAttributes(typeof(ValidateAntiForgeryTokenAttribute), true));
        Assert.NotEmpty(action.GetCustomAttributes(typeof(HttpPostAttribute), true));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(42)]
    public async Task Shared_create_edit_Razor_emits_optional_text_inputs_and_validation(int id)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing", ApplicationName = typeof(SupplierController).Assembly.FullName, ContentRootPath = Path.GetTempPath() });
        builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddControllersWithViews();
        builder.Services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        await using var app = builder.Build(); app.MapControllers(); await app.StartAsync();
        using var scope = app.Services.CreateScope();
        var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        http.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(), "Supplier form rendering"));
        http.Request.RouteValues = new RouteValueDictionary(new { area = "Admin", controller = "Supplier", action = id == 0 ? "Create" : "Edit" });
        var action = new ActionContext(http, new RouteData(http.Request.RouteValues), new ActionDescriptor());
        var view = scope.ServiceProvider.GetRequiredService<IRazorViewEngine>().GetView(null, "/Areas/Admin/Views/Supplier/Edit.cshtml", false);
        Assert.True(view.Success);
        var model = new SupplierEditViewModel { Id = id, Code = "SYN", Name = "Synthetic", BankAccountNumber = "000123456789", BankAccountName = "Synthetic owner", BankName = "Test bank", RowVersion = [1, 2] };
        using var writer = new StringWriter();
        var context = new ViewContext(action, view.View,
            new ViewDataDictionary<SupplierEditViewModel>(scope.ServiceProvider.GetRequiredService<IModelMetadataProvider>(), new ModelStateDictionary()) { Model = model },
            new TempDataDictionary(http, new MemoryTempData()), writer, new HtmlHelperOptions());
        // Execute the real form body, excluding the unrelated shared layout/services.
        var page = Assert.IsType<RazorView>(view.View).RazorPage;
        page.ViewContext = context;
        scope.ServiceProvider.GetRequiredService<IRazorPageActivator>().Activate(page, context);
        await page.ExecuteAsync();
        var html = writer.ToString();
        foreach (var (field, max) in new[] { ("BankName", 250), ("BankAccountName", 250), ("BankAccountNumber", 50) })
        {
            var input = System.Text.RegularExpressions.Regex.Match(html, $"<input[^>]*name=\"{field}\"[^>]*>").Value;
            Assert.NotEmpty(input);
            Assert.Contains("type=\"text\"", input);
            Assert.Contains("autocomplete=\"off\"", input);
            Assert.Contains($"maxlength=\"{max}\"", input);
            Assert.Contains($"data-val-length-max=\"{max}\"", input);
            Assert.DoesNotContain("data-val-required", input);
            Assert.Contains($"data-valmsg-for=\"{field}\"", html);
        }
        Assert.Contains("000123456789", html);
        Assert.Contains("__RequestVerificationToken", html);
    }

    private sealed class MemoryTempData : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
    private sealed class FixedAuthorization(bool allowed) : IAuthorizationService
    {
        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, IEnumerable<IAuthorizationRequirement> requirements)
            => Task.FromResult(allowed ? AuthorizationResult.Success() : AuthorizationResult.Failed());
        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, string policyName)
            => Task.FromResult(allowed ? AuthorizationResult.Success() : AuthorizationResult.Failed());
    }
}
