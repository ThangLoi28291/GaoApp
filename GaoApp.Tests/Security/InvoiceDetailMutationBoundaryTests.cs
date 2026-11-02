using System.Reflection;
using GaoApp.Application.Common.Results;
using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.Invoices;
using GaoApp.Application.Interfaces.Services.Invoices;
using GaoApp.Web.Areas.Admin.Controllers;
using GaoApp.Web.Areas.Admin.ViewModels.Invoices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Tests.Security;

public sealed class InvoiceDetailMutationBoundaryTests
{
    [Theory]
    [InlineData(nameof(InvoiceController.UpdateDetail))]
    [InlineData(nameof(InvoiceController.DeleteDetail))]
    public void Detail_mutations_require_existing_management_permission_and_anti_forgery(string action)
    {
        var type = typeof(InvoiceController);
        Assert.Contains(type.GetCustomAttributes<AuthorizeAttribute>(true),
            a => a.Policy == PermissionCodes.System.Integration.Manage);
        var method = type.GetMethod(action)!;
        Assert.NotNull(method.GetCustomAttribute<HttpPostAttribute>());
        Assert.NotNull(method.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
        Assert.Null(method.GetCustomAttribute<AllowAnonymousAttribute>());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Malformed_amount_or_id_never_reaches_mutation_service(bool update)
    {
        var (controller, proxy) = Create();
        controller.ModelState.AddModelError(update ? "UnitPrice" : "invoiceDetailId", "Malformed value");
        var result = update
            ? await controller.UpdateDetail(new() { InvoiceDetailId = 12, Quantity = 1 }, default)
            : await controller.DeleteDetail(12, default);
        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Empty(proxy.Calls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Provider_failure_is_sanitized_before_returning_to_ajax(bool update)
    {
        var (controller, proxy) = Create();
        proxy.Result = Result<InvoiceHeadDto>.Failure(Error.Failure("password=private-provider-secret"));
        var result = update
            ? await controller.UpdateDetail(new() { InvoiceDetailId = 12, Quantity = 1 }, default)
            : await controller.DeleteDetail(12, default);
        var bad = Assert.IsType<BadRequestObjectResult>(result);
        var json = System.Text.Json.JsonSerializer.Serialize(bad.Value);
        Assert.DoesNotContain("private-provider-secret", json);
        Assert.Contains("false", json);
        Assert.Single(proxy.Calls);
    }

    [Fact]
    public async Task Auto_partial_uses_authorized_read_model_and_can_edit_flag()
    {
        var (controller, proxy) = Create();
        var invoice = new InvoiceHeadDto { Id = 41, CanEditDetails = true };
        proxy.Result = Result<InvoiceHeadDto>.Success(invoice);
        var result = Assert.IsType<PartialViewResult>(await controller.AutoLinesPartial(41, default));
        Assert.Equal("_InvoiceAutoLinesTable", result.ViewName);
        Assert.Same(invoice, Assert.IsType<InvoiceDetailViewModel>(result.Model).Invoice);
        Assert.Equal(nameof(IInvoiceService.GetInvoiceDetailAsync), Assert.Single(proxy.Calls));
    }

    private static (InvoiceController, InvoiceProxy) Create()
    {
        var service = DispatchProxy.Create<IInvoiceService, InvoiceProxy>();
        return (new InvoiceController(service, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!),
            (InvoiceProxy)(object)service);
    }

    public class InvoiceProxy : DispatchProxy
    {
        public List<string> Calls { get; } = [];
        public Result<InvoiceHeadDto> Result { get; set; } = Result<InvoiceHeadDto>.Success(new() { Id = 41 });
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            Calls.Add(targetMethod!.Name);
            return Task.FromResult(Result);
        }
    }
}
