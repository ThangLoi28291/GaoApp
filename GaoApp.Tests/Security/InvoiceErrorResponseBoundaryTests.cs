using System.Reflection;
using GaoApp.Application.Common.Results;
using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.Invoices;
using GaoApp.Application.Interfaces.Services.Invoices;
using GaoApp.Web.Areas.Admin.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace GaoApp.Tests.Security;

public sealed class InvoiceErrorResponseBoundaryTests
{
    [Theory]
    [InlineData("ViettelIssue")]
    [InlineData("SyncViettelByUuid")]
    [InlineData("SendViettelEmail")]
    [InlineData("DownloadOfficialPdf")]
    [InlineData("DownloadOfficialZipXml")]
    [InlineData("ViettelDraftPreview")]
    [InlineData("InvoiceSummaryPartial")]
    [InlineData("ManualLinesPartial")]
    public async Task Raw_service_failure_never_reaches_TempData_or_AJAX(string action)
    {
        var controller=Create();
        var method=typeof(InvoiceController).GetMethod(action)!;
        var args=method.GetParameters().Select(p=>p.ParameterType==typeof(int)?(object)1:
            p.ParameterType==typeof(CancellationToken)?CancellationToken.None:
            Activator.CreateInstance(p.ParameterType)!).ToArray();
        var result=await (Task<IActionResult>)method.Invoke(controller,args)!;
        if(result is BadRequestObjectResult bad)
            Assert.Equal(InvoiceErrorReadSanitizer.GenericMessage,bad.Value);
        else
        {
            Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal(InvoiceErrorReadSanitizer.GenericMessage,controller.TempData["Error"]);
        }
    }

    [Fact]
    public async Task Lookup_success_with_not_found_raw_error_is_sanitized_too()
    {
        var controller=Create();
        var auto=DispatchProxy.Create<IAutoInvoiceService,FailureProxy>();
        ((FailureProxy)(object)auto).LookupNotFound=true;
        typeof(InvoiceController).GetField("_autoInvoiceService",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(controller,auto);
        await controller.SyncViettelByUuid(1,CancellationToken.None);
        Assert.Equal(InvoiceErrorReadSanitizer.GenericMessage,controller.TempData["Error"]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Connection_result_and_warning_cannot_echo_provider_text(bool success)
    {
        var proxy=Proxy<IInvoiceProviderSettingService>();
        ((FailureProxy)(object)proxy).ConnectionSuccess=success;
        var controller=Prepare(new InvoiceProviderSettingController(proxy));
        await controller.TestLogin(7,CancellationToken.None);
        foreach(var item in controller.TempData)
            Assert.DoesNotContain(InvoiceErrorReadSanitizerTests.Canary,item.Value?.ToString()??"");
        if(success)
        {
            Assert.Contains("Kết nối hóa đơn thành công",controller.TempData["Success"]?.ToString());
            Assert.Equal(InvoiceErrorReadSanitizer.GenericWarning,controller.TempData["InvoiceConnectionWarning"]);
        }
        else Assert.Contains(InvoiceErrorReadSanitizer.GenericMessage,controller.TempData["Error"]?.ToString());
    }

    [Fact]
    public async Task Worker_correction_and_list_sync_failures_are_safe_at_controller_boundary()
    {
        var worker=Prepare(new AutoInvoiceController(Proxy<IAutoInvoiceService>()));
        await worker.RunOnce(CancellationToken.None);
        Assert.Equal(InvoiceErrorReadSanitizer.GenericMessage,worker.TempData["Error"]);
        var correction=Prepare(new InvoiceCorrectionController(Proxy<IInvoiceCorrectionService>()));
        await correction.Create(new CreateInvoiceCorrectionRequestDto {OriginalInvoiceHeadId=1},CancellationToken.None);
        Assert.Equal(InvoiceErrorReadSanitizer.GenericMessage,correction.TempData["Error"]);
        var sync=Prepare(new ViettelInvoiceSyncController(Proxy<IViettelInvoiceListSyncService>()));
        await sync.Index(new ViettelInvoiceListSyncRequestDto(),CancellationToken.None);
        Assert.Equal(InvoiceErrorReadSanitizer.GenericMessage,sync.TempData["Error"]);
    }

    private static InvoiceController Create()
    {
        var controller=new InvoiceController(Proxy<IInvoiceService>(),Proxy<IViettelInvoicePayloadBuilder>(),
            Proxy<IViettelInvoicePreviewService>(),Proxy<IViettelInvoiceIssueService>(),Proxy<IViettelOfficialFileService>(),
            Proxy<IViettelInvoiceSyncService>(),Proxy<IViettelInvoiceEmailService>(),Proxy<IInvoiceCorrectionService>(),
            Proxy<IInvoiceViettelDashboardService>(),Proxy<IAutoInvoiceService>(),null!);
        return Prepare(controller);
    }
    private static T Prepare<T>(T controller) where T:Controller
    {
        var context=new DefaultHttpContext();
        controller.ControllerContext=new ControllerContext {HttpContext=context};
        controller.TempData=new TempDataDictionary(context,new EmptyTempData());
        return controller;
    }
    private static T Proxy<T>() where T:class=>DispatchProxy.Create<T,FailureProxy>();
    public class FailureProxy:DispatchProxy
    {
        public bool LookupNotFound;
        public bool? ConnectionSuccess;
        protected override object? Invoke(MethodInfo? method,object?[]? args)
        {
            var resultType=method!.ReturnType.GetGenericArguments().Single();
            object result;
            if(LookupNotFound && resultType==typeof(Result<ViettelInvoiceLookupResultDto>))
                result=Result<ViettelInvoiceLookupResultDto>.Success(new ViettelInvoiceLookupResultDto {IsFound=false,ErrorMessage=InvoiceErrorReadSanitizerTests.Canary});
            else if(ConnectionSuccess.HasValue && resultType==typeof(Result<TestInvoiceProviderLoginResultDto>))
                result=Result<TestInvoiceProviderLoginResultDto>.Success(new TestInvoiceProviderLoginResultDto {
                    IsSuccess=ConnectionSuccess.Value,Message=InvoiceErrorReadSanitizerTests.Canary,
                    WarningMessage=InvoiceErrorReadSanitizerTests.Canary,DurationMs=12});
            else
                result=resultType.GetMethods(BindingFlags.Public|BindingFlags.Static)
                    .Single(x=>x.Name=="Failure" && !x.IsGenericMethod &&
                        x.GetParameters() is [{ParameterType:var parameter}] && parameter==typeof(Error))
                    .Invoke(null,[new Error(InvoiceErrorReadSanitizerTests.Canary,"password="+InvoiceErrorReadSanitizerTests.Canary)])!;
            return typeof(Task).GetMethod("FromResult")!.MakeGenericMethod(resultType).Invoke(null,[result]);
        }
    }
    private sealed class EmptyTempData:ITempDataProvider
    {
        public IDictionary<string,object> LoadTempData(HttpContext context)=>new Dictionary<string,object>();
        public void SaveTempData(HttpContext context,IDictionary<string,object> values) { }
    }
}
