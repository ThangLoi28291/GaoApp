using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.Invoices;
using GaoApp.Application.Services.Invoices;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Repositories.Invoices;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

[CollectionDefinition("InvoiceErrorReadSecurity", DisableParallelization = true)]
public sealed class InvoiceErrorReadSecurityCollection : ICollectionFixture<InvoiceErrorReadSecurityFixture> { }

[Collection("InvoiceErrorReadSecurity")]
public sealed class InvoiceErrorReadSecuritySqlServerTests(InvoiceErrorReadSecurityFixture f)
{
    private const string Secret = InvoiceErrorReadSanitizerTests.Canary;

    [Theory]
    [InlineData("/Admin/Invoice/Index")]
    [InlineData("/Admin/Invoice/Detail/{id}")]
    [InlineData("/Admin/Invoice/InvoiceSummaryPartial/{id}")]
    [InlineData("/Admin/Invoice/ManualLinesPartial/{id}")]
    [InlineData("/Admin/Invoice/ViettelPayload/{id}")]
    [InlineData("/Admin/Invoice/ViettelDashboard")]
    [InlineData("/Admin/InvoiceDashboard/Index")]
    [InlineData("/Admin/AutoInvoice/Index?tab=errors")]
    [InlineData("/Admin/AutoInvoice/Index?tab=history")]
    [InlineData("/Admin/AutoInvoice/Index?tab=today")]
    public async Task Actual_HTTP_views_hide_all_historical_diagnostics(string route)
    {
        using var response = await f.Own.Http.GetAsync(route.Replace("{id}",f.InvoiceId.ToString()));
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.DoesNotContain(Secret,html);
        // Ensure this is the requested view, not a login/error fallback.
        Assert.DoesNotContain("name=\"Password\"",html);
        if(route.Contains("Detail/",StringComparison.Ordinal) || route.Contains("ViettelPayload/",StringComparison.Ordinal))
            Assert.Contains("B7-VISIBLE",html);
        if(route.Contains("ViettelPayload/",StringComparison.Ordinal))
            Assert.Contains("Lần phát hành trước chưa rõ kết quả. Cần bấm Tra cứu UUID trước khi phát hành lại.",html);
    }

    [Fact]
    public async Task Actual_DTOs_hide_errors_but_preserve_UUID_recovery_and_business_totals()
    {
        await using var db=f.App.Database.CreateTenantContext(f.App.Stores[0].StoreId);
        var repo=new InvoiceRepository(db);
        var read=new InvoiceReadService(repo,null!);
        var detail=await read.GetInvoiceDetailAsync(f.InvoiceId);
        Assert.True(detail.IsSuccess);
        Assert.Equal(10,detail.Value.GrandTotal);
        Assert.Equal(InvoiceProviderStatus.IssueFailed,detail.Value.ProviderStatus);
        Assert.DoesNotContain(Secret,JsonSerializer.Serialize(detail.Value));
        var list=await read.GetInvoicesAsync(new InvoiceListQueryDto {PageSize=100});
        Assert.True(list.IsSuccess);Assert.Contains(list.Value.Items,x=>x.Id==f.InvoiceId);
        Assert.DoesNotContain(Secret,JsonSerializer.Serialize(list.Value));
        var dashboard=await new InvoiceViettelDashboardService(repo).GetDashboardAsync(new ViettelInvoiceDashboardQueryDto {PageSize=100});
        Assert.True(dashboard.IsSuccess);
        var item=Assert.Single(dashboard.Value.Items.Items,x=>x.Id==f.InvoiceId);
        Assert.DoesNotContain(Secret,JsonSerializer.Serialize(item));
        var corrections=await new InvoiceCorrectionService(new InvoiceCorrectionRepository(db)).GetHistoryAsync(f.InvoiceId);
        Assert.True(corrections.IsSuccess);Assert.DoesNotContain(Secret,JsonSerializer.Serialize(corrections.Value));
        Assert.NotEmpty(corrections.Value.Items);
    }

    [Fact]
    public async Task Every_read_preserves_original_SQL_bytes_rowversions_and_history()
    {
        await using var db=f.App.Database.CreateTenantContext(f.App.Stores[0].StoreId);
        var before=await Fingerprint(db);
        foreach(var route in new[]{"/Admin/Invoice/Detail/","/Admin/Invoice/ViettelPayload/"})
        {
            using var response=await f.Own.Http.GetAsync(route+f.InvoiceId);
            Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        }
        using(var response=await f.Own.Http.GetAsync("/Admin/AutoInvoice?tab=history"))
            Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        await db.SaveChangesAsync();
        await using var after=f.App.Database.CreateTenantContext(f.App.Stores[0].StoreId);
        Assert.Equal(before,await Fingerprint(after));
        var original=await after.InvoiceHeads.AsNoTracking().SingleAsync(x=>x.Id==f.InvoiceId);
        Assert.Contains(Secret,original.LastErrorMessage!);
        Assert.Equal(Secret,original.LastEmailErrorMessage);
        Assert.Contains(Secret,(await after.Set<AutoInvoiceWorkerState>().AsNoTracking().SingleAsync()).LastResult!);
    }

    [Theory]
    [InlineData("/Admin/Invoice/Detail/{id}")]
    [InlineData("/Admin/Invoice/ViettelPayload/{id}")]
    public async Task Foreign_tenant_cannot_read_another_stores_invoice(string route)
    {
        using var response=await f.Foreign.Http.GetAsync(route.Replace("{id}",f.InvoiceId.ToString()));
        Assert.Equal(HttpStatusCode.Redirect,response.StatusCode);
        Assert.DoesNotContain(Secret,await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Permission_and_CSRF_checks_remain_in_effect()
    {
        using var denied=await f.App.LoginAsync(await f.App.AddAccountAsync(f.App.Stores[0],PermissionCodes.Catalog.Product.View));
        using(var response=await denied.Http.GetAsync("/Admin/Invoice/Detail/"+f.InvoiceId))
            Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);
        var token=f.Own.Http.DefaultRequestHeaders.GetValues("RequestVerificationToken").Single();
        f.Own.Http.DefaultRequestHeaders.Remove("RequestVerificationToken");
        try
        {
            using var response=await f.Own.Http.PostAsync("/Admin/Invoice/SyncViettelByUuid",new FormUrlEncodedContent(new Dictionary<string,string>{{"id",f.InvoiceId.ToString()}}));
            Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);
        }
        finally {f.Own.Http.DefaultRequestHeaders.Add("RequestVerificationToken",token);}
    }

    private static async Task<string> Fingerprint(GaoApp.Infrastructure.Data.AppDbContext db)
    {
        var data=new {
            Invoices=await db.InvoiceHeads.AsNoTracking().OrderBy(x=>x.Id).Select(x=>new{x.Id,x.LastErrorCode,x.LastErrorMessage,x.LastEmailErrorMessage,x.ProviderStatus,x.TransactionUuid,x.RowVersion}).ToListAsync(),
            Cases=await db.Set<InvoiceCorrectionCase>().AsNoTracking().OrderBy(x=>x.Id).Select(x=>new{x.Id,x.LastErrorCode,x.LastErrorMessage,x.RowVersion}).ToListAsync(),
            Workers=await db.Set<AutoInvoiceWorkerState>().AsNoTracking().OrderBy(x=>x.Id).Select(x=>new{x.Id,x.LastErrorCode,x.LastErrorMessage,x.LastResult,x.RowVersion}).ToListAsync(),
            Operations=await db.Set<AutoInvoiceOperation>().AsNoTracking().OrderBy(x=>x.Id).Select(x=>new{x.Id,x.ErrorCode,x.ErrorMessage,x.Status,x.RowVersion}).ToListAsync()
        };
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(data))));
    }
}

public sealed class InvoiceErrorReadSecurityFixture:IAsyncLifetime
{
    internal FullApplicationFixture App {get;private set;}=null!;
    internal FullApplicationFixture.Client Own {get;private set;}=null!;
    internal FullApplicationFixture.Client Foreign {get;private set;}=null!;
    internal int InvoiceId;
    public async Task InitializeAsync()
    {
        App=await FullApplicationFixture.StartWithAuditPausedAsync();
        try
        {
            var account=await App.AddAccountAsync(App.Stores[0],"*");
            var orderId=await InvoiceOrderDraftSqlServerTests.SeedOrder(App,account,"B7-VISIBLE-ORDER",InvoiceIssuanceRoute.Automatic);
            Own=await App.LoginAsync(account);
            Foreign=await App.LoginAsync(await App.AddAccountAsync(App.Stores[1],"*"));
            var storeId=App.Stores[0].StoreId;
            await using var db=App.Database.CreateTenantContext(storeId);
            var setting=await db.InvoiceProviderSettings.SingleAsync();
            var invoice=new InvoiceHead { StoreId=storeId,OrderId=orderId,InvoiceNumber="B7-VISIBLE",InvoiceDate=DateTime.UtcNow,
                InvoiceProviderSettingId=setting.Id,ProviderCode="VIETTEL",ProviderStatus=InvoiceProviderStatus.IssueFailed,
                TransactionUuid=Guid.NewGuid().ToString(),GrandTotal=10,SubTotal=10,
                LastErrorCode="TIMEOUT",LastErrorMessage="timeout; password="+InvoiceErrorReadSanitizerTests.Canary,
                LastEmailErrorMessage=InvoiceErrorReadSanitizerTests.Canary };
            invoice.Details.Add(new InvoiceDetail {StoreId=storeId,ItemName="B7 item",UnitName="Unit",Quantity=1,UnitPrice=10,Amount=10,TotalAmount=10});
            db.Add(invoice);await db.SaveChangesAsync();InvoiceId=invoice.Id;
            db.Add(new InvoiceCorrectionCase {StoreId=storeId,OriginalInvoiceHeadId=invoice.Id,Type=InvoiceCorrectionType.Replacement,
                Reason="B7 visible correction",AgreementDocumentNo="B7-AGREEMENT",AgreementDateUtc=DateTime.UtcNow,
                LastErrorCode=InvoiceErrorReadSanitizerTests.Canary,LastErrorMessage=InvoiceErrorReadSanitizerTests.Canary});
            db.Add(new AutoInvoiceSettings {StoreId=storeId,IsEnabled=false});
            db.Add(new AutoInvoiceWorkerState {StoreId=storeId,LastErrorCode=InvoiceErrorReadSanitizerTests.Canary,
                LastErrorMessage=InvoiceErrorReadSanitizerTests.Canary,LastResult=InvoiceErrorReadSanitizerTests.Canary});
            var operation=new AutoInvoiceOperation {StoreId=storeId,InvoiceHeadId=invoice.Id,Status=AutoInvoiceOperationStatus.Unknown,
                SaleDateLocal=DateTime.Today,TransactionUuid=invoice.TransactionUuid,
                ErrorCode=InvoiceErrorReadSanitizerTests.Canary,ErrorMessage=InvoiceErrorReadSanitizerTests.Canary};
            operation.Sources.Add(new AutoInvoiceOperationSource {StoreId=storeId,InvoiceHeadId=invoice.Id,OrderId=orderId,
                ErrorCode=InvoiceErrorReadSanitizerTests.Canary,ErrorMessage=InvoiceErrorReadSanitizerTests.Canary});
            db.Add(operation);await db.SaveChangesAsync();
        }
        catch{await App.DisposeAsync();throw;}
    }
    public async Task DisposeAsync(){Own?.Dispose();Foreign?.Dispose();if(App is not null)await App.DisposeAsync();}
}
