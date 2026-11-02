using GaoApp.Application.Common;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.DTOs.Invoices;
using GaoApp.Application.Services.Invoices;
using GaoApp.Domain.Enums;
using GaoApp.Web.Areas.Admin.Controllers;
using GaoApp.Web.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;

namespace GaoApp.Tests.Security;

public sealed class InvoiceReadLimitsTests
{
    [Theory]
    [InlineData(0,0,1,20,0)]
    [InlineData(-1,-4,1,20,0)]
    [InlineData(2,50,2,50,50)]
    [InlineData(3,int.MaxValue,3,100,200)]
    [InlineData(int.MaxValue,1,int.MaxValue,1,2147483646)]
    [InlineData(21474837,100,21474837,100,2147483600)]
    public void Both_lists_bound_paging_and_repeated_normalization_is_stable(int page,int size,int p,int s,int offset)
    {
        var list=new InvoiceListQueryDto {Page=page,PageSize=size,Keyword="  B8  "};
        var dashboard=new ViettelInvoiceDashboardQueryDto {Page=page,PageSize=size,Keyword="  B8  "};
        for(var repeat=0;repeat<3;repeat++)
        {
            Assert.Equal(offset,list.NormalizeAndValidate());Assert.Equal(offset,dashboard.NormalizeAndValidate());
            Assert.Equal((p,s,"B8"),(list.Page,list.PageSize,list.Keyword));
            Assert.Equal((p,s,"B8"),(dashboard.Page,dashboard.PageSize,dashboard.Keyword));
        }
    }
    [Theory]
    [InlineData(int.MaxValue,100)]
    [InlineData(int.MaxValue,int.MaxValue)]
    [InlineData(21474838,100)]
    public void Oversized_offset_is_rejected_before_SQL(int page,int size)
    {
        Assert.Throws<ValidationAppException>(()=>new InvoiceListQueryDto {Page=page,PageSize=size}.NormalizeAndValidate());
        Assert.Throws<ValidationAppException>(()=>new ViettelInvoiceDashboardQueryDto {Page=page,PageSize=size}.NormalizeAndValidate());
    }
    [Theory]
    [InlineData("0001-01-01","2026-10-24")]
    [InlineData("2026-10-24","9999-12-31")]
    [InlineData("9999-12-31","9999-12-31")]
    [InlineData("2026-10-25","2026-10-24")]
    public void Invalid_date_ranges_are_rejected_by_all_three_query_types(string from,string to)
    {
        var a=DateTime.Parse(from);var b=DateTime.Parse(to);
        Assert.Throws<ValidationAppException>(()=>new InvoiceListQueryDto {FromDate=a,ToDate=b}.NormalizeAndValidate());
        Assert.Throws<ValidationAppException>(()=>new ViettelInvoiceDashboardQueryDto {FromDate=a,ToDate=b}.NormalizeAndValidate());
        Assert.Throws<ValidationAppException>(()=>new InvoiceDashboardQueryDto {FromDate=a,ToDate=b}.NormalizeAndValidate());
    }
    [Fact]
    public void Exact_text_and_date_boundaries_remain_available_without_cutting_the_history_range()
    {
        var list=new InvoiceListQueryDto {FromDate=new(1,1,2),ToDate=new(9999,12,30),Keyword=new('x',200)};
        list.NormalizeAndValidate();Assert.Equal(200,list.Keyword!.Length);
        new InvoiceDashboardQueryDto {FromDate=new(2020,1,1,23,0,0),ToDate=new(2026,10,24)}.NormalizeAndValidate();
        foreach(var text in new[]{new string('x',201),new string(' ',201)})
        {
            Assert.Throws<ValidationAppException>(()=>new InvoiceListQueryDto {Keyword=text}.NormalizeAndValidate());
            Assert.Throws<ValidationAppException>(()=>new ViettelInvoiceDashboardQueryDto {Keyword=text}.NormalizeAndValidate());
        }
        Assert.Throws<ValidationAppException>(()=>new InvoiceListQueryDto {SortMode=(InvoiceListSortMode)999}.NormalizeAndValidate());
        Assert.Throws<ValidationAppException>(()=>new InvoiceListQueryDto {DisplayMode=(InvoiceListDisplayMode)999}.NormalizeAndValidate());
        Assert.Throws<ValidationAppException>(()=>new ViettelInvoiceDashboardQueryDto {Filter=(ViettelInvoiceDashboardFilter)999}.NormalizeAndValidate());
    }
    [Fact]
    public async Task Services_validate_before_repository_access_even_outside_MVC()
    {
        await Assert.ThrowsAsync<ValidationAppException>(()=>new InvoiceReadService(null!,null!).GetInvoicesAsync(new(){Page=int.MaxValue,PageSize=100}));
        await Assert.ThrowsAsync<ValidationAppException>(()=>new InvoiceViettelDashboardService(null!).GetDashboardAsync(new(){ToDate=DateTime.MaxValue}));
        await Assert.ThrowsAsync<ValidationAppException>(()=>new InvoiceDashboardService(null!,null!).GetDashboardAsync(new(){ToDate=DateTime.MaxValue}));
    }
    [Theory]
    [InlineData("Invoice","Index")]
    [InlineData("Invoice","ViettelDashboard")]
    [InlineData("InvoiceDashboard","Index")]
    public void Only_the_intended_GET_actions_use_the_existing_heavy_budget(string controller,string action)
    {
        var context=new DefaultHttpContext();context.Request.Method="GET";
        context.SetEndpoint(new Endpoint(_=>Task.CompletedTask,new EndpointMetadataCollection(new ControllerActionDescriptor {ControllerName=controller,ActionName=action}),"B8"));
        var policy=ResourceEndpointPolicy.For(context);
        Assert.Equal(ResourceGroup.HeavyIngress,policy!.Ingress);Assert.Equal(ResourceGroup.HeavyRead,policy.Actor);
        context.Request.Method="HEAD";Assert.Equal(ResourceGroup.HeavyRead,ResourceEndpointPolicy.For(context)!.Actor);
        context.Request.Method="POST";Assert.Null(ResourceEndpointPolicy.For(context));
    }
    [Theory]
    [InlineData("Invoice","ViettelIssue")]
    [InlineData("Invoice","SyncViettelByUuid")]
    [InlineData("AutoInvoice","RunOnce")]
    [InlineData("AcbWebhook","Callback")]
    [InlineData("BankNotificationDevice","Events")]
    public void Provider_bank_and_worker_actions_do_not_consume_the_invoice_read_budget(string controller,string action)
    {
        var context=new DefaultHttpContext();context.Request.Method="POST";
        context.SetEndpoint(new Endpoint(_=>Task.CompletedTask,new EndpointMetadataCollection(new ControllerActionDescriptor {ControllerName=controller,ActionName=action}),"B8"));
        Assert.NotEqual(ResourceGroup.HeavyRead,ResourceEndpointPolicy.For(context)?.Actor);
    }
    [Fact]
    public async Task Malformed_model_binding_returns_safe_400_instead_of_querying_default_values()
    {
        var invoice=new InvoiceController(null!,null!,null!,null!,null!,null!,null!,null!,null!,null!,null!);
        invoice.ModelState.AddModelError("Page","synthetic_untrusted_input");
        Assert.Equal(InvoiceReadLimits.InvalidQueryMessage,Assert.IsType<BadRequestObjectResult>(await invoice.Index(new(),default)).Value);
        Assert.IsType<BadRequestObjectResult>(await invoice.ViettelDashboard(new(),default));
        var dashboard=new InvoiceDashboardController(null!);dashboard.ModelState.AddModelError("FromDate","synthetic_untrusted_input");
        Assert.IsType<BadRequestObjectResult>(await dashboard.Index(new(),default));
    }
}
