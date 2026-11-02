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
using GaoApp.Infrastructure.Services.Invoices;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

[CollectionDefinition("InvoiceLogReadSecurity", DisableParallelization = true)]
public sealed class InvoiceLogReadSecurityCollection : ICollectionFixture<InvoiceLogReadSecurityFixture> { }

[Collection("InvoiceLogReadSecurity")]
public sealed class InvoiceLogReadSecuritySqlServerTests(InvoiceLogReadSecurityFixture f)
{
    private const string Url = "/Admin/InvoiceIntegrationLog";
    private const string Secret = InvoiceLogReadSanitizerTests.Canary;

    public static IEnumerable<object[]> Rows() => Enumerable.Range(0,18).Select(i => new object[] { i });

    [Theory, MemberData(nameof(Rows))]
    public async Task Read_only_integration_account_receives_safe_HTML_for_each_legacy_payload_format(int index)
    {
        using var response = await f.Own.Http.GetAsync(Url + "/Detail/" + f.Ids[index] + "?raw=true&includeSecrets=true");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.DoesNotContain(Secret, html);
        Assert.Contains("REDACTED", html);
        Assert.Contains("HTTP 500", html);
    }

    [Fact]
    public async Task Lists_dashboard_and_service_DTOs_do_not_expose_historical_credentials()
    {
        foreach (var route in new[] { Url+"/Index?PageSize=100", "/Admin/InvoiceDashboard/Index?FromDate=2026-10-23&ToDate=2026-10-23" })
        {
            using var response = await f.Own.Http.GetAsync(route);
            Assert.Equal(HttpStatusCode.OK,response.StatusCode);
            Assert.DoesNotContain(Secret, WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
        }
        await using var db = f.App.Database.CreateTenantContext(f.App.Stores[0].StoreId);
        var repo = new InvoiceIntegrationLogRepository(db);
        var service = new InvoiceIntegrationLogService(repo);
        var detail = await service.GetDetailAsync(f.Ids[0]);
        Assert.True(detail.IsSuccess);
        Assert.DoesNotContain(Secret, JsonSerializer.Serialize(detail.Value));
        using var body = JsonDocument.Parse(detail.Value.ResponseBody!);
        Assert.Equal(InvoiceLogReadSanitizerTests.Trace, body.RootElement.GetProperty("requestId").GetString());
        Assert.Equal(500, body.RootElement.GetProperty("statusCode").GetInt32());
        var list = await service.GetLogsAsync(new InvoiceIntegrationLogQueryDto { PageSize = int.MaxValue });
        Assert.Equal(100, list.Value.PageSize);
        Assert.Equal(f.Ids.Length, list.Value.TotalItems);
        Assert.DoesNotContain(Secret, JsonSerializer.Serialize(list.Value));
        var dashboard = await new InvoiceDashboardService(new InvoiceRepository(db),repo).GetDashboardAsync(
            new InvoiceDashboardQueryDto { FromDate = new(2026,10,23), ToDate = new(2026,10,23) });
        Assert.True(dashboard.IsSuccess);
        Assert.Equal(f.Ids.Length, dashboard.Value.ErrorLogCount);
        Assert.Equal(f.Ids.Length, dashboard.Value.TimeoutLogCount);
        Assert.Equal(f.Ids.Length, dashboard.Value.Http500LogCount);
        Assert.DoesNotContain(Secret, JsonSerializer.Serialize(dashboard.Value.RecentErrors));
    }

    [Fact]
    public async Task Every_repository_read_is_sanitized_untracked_and_does_not_rewrite_original_bytes_or_rowversions()
    {
        await using var db = f.App.Database.CreateTenantContext(f.App.Stores[0].StoreId);
        var before = await Fingerprint(db);
        var repo = new InvoiceIntegrationLogRepository(db);
        var all = new List<InvoiceIntegrationLog>();
        all.Add((await repo.GetByIdAsync(f.Ids[0]))!);
        all.AddRange((await repo.QueryAsync(new InvoiceIntegrationLogQueryDto { PageSize = 100 })).Items);
        all.AddRange(await repo.GetLatestByInvoiceHeadAsync(f.InvoiceId,100));
        all.AddRange(await repo.GetLogsForDashboardAsync(new(2026,10,23),new(2026,10,23)));
        Assert.Equal(1 + 3*f.Ids.Length, all.Count);
        Assert.All(all, log => Assert.DoesNotContain(Secret, JsonSerializer.Serialize(new { log.RequestBody,log.ResponseBody,log.RequestUrl,log.ErrorMessage,log.ErrorCode })));
        Assert.Empty(db.ChangeTracker.Entries<InvoiceIntegrationLog>());
        await db.SaveChangesAsync();
        await using var after = f.App.Database.CreateTenantContext(f.App.Stores[0].StoreId);
        Assert.Equal(before,await Fingerprint(after));
        Assert.Contains(Secret,(await after.InvoiceIntegrationLogs.AsNoTracking().SingleAsync(x=>x.Id==f.Ids[0])).RequestBody!);
    }

    [Theory]
    [InlineData(Secret)]
    [InlineData("B4_SECRET")]
    [InlineData("password")]
    [InlineData("Bearer")]
    [InlineData("provider.invalid")]
    public async Task Search_cannot_probe_hidden_raw_URL_or_error_text_through_result_counts(string guess)
    {
        using var response = await f.Own.Http.GetAsync(Url+"/Index?Keyword="+Uri.EscapeDataString(guess));
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.DoesNotContain("<strong>#",html);
        await using var db = f.App.Database.CreateTenantContext(f.App.Stores[0].StoreId);
        Assert.Equal(0,(await new InvoiceIntegrationLogRepository(db).QueryAsync(new InvoiceIntegrationLogQueryDto { Keyword = guess })).Total);
    }

    [Fact]
    public async Task Visible_invoice_identifiers_and_standard_error_codes_remain_searchable()
    {
        await using var db = f.App.Database.CreateTenantContext(f.App.Stores[0].StoreId);
        var repo = new InvoiceIntegrationLogRepository(db);
        Assert.Equal(f.Ids.Length,(await repo.QueryAsync(new InvoiceIntegrationLogQueryDto { Keyword="B4-visible" })).Total);
        Assert.Equal(f.Ids.Length-2,(await repo.QueryAsync(new InvoiceIntegrationLogQueryDto { Keyword="HTTP_500" })).Total);
        Assert.Equal(1,(await repo.QueryAsync(new InvoiceIntegrationLogQueryDto { Keyword="Viettel.IssueBusinessFailed" })).Total);
        Assert.Equal(f.Ids.Length,(await repo.QueryAsync(new InvoiceIntegrationLogQueryDto { ActionType=InvoiceIntegrationActionType.IssueInvoice,IsSuccess=false,InvoiceHeadId=f.InvoiceId })).Total);
    }

    [Fact]
    public async Task Permission_and_tenant_boundaries_still_block_unprivileged_and_foreign_reads()
    {
        using var denied = await f.App.LoginAsync(await f.App.AddAccountAsync(f.App.Stores[0], PermissionCodes.Catalog.Product.View));
        using(var response = await denied.Http.GetAsync(Url+"/Detail/"+f.Ids[0])) Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);
        using(var response = await f.Foreign.Http.GetAsync(Url+"/Detail/"+f.Ids[0])) Assert.Equal(HttpStatusCode.Redirect,response.StatusCode);
        using(var response = await f.Own.Http.GetAsync(Url+"/Detail/"+f.ForeignId+"?StoreId="+f.App.Stores[1].StoreId)) Assert.Equal(HttpStatusCode.Redirect,response.StatusCode);
        await using var other = f.App.Database.CreateTenantContext(f.App.Stores[1].StoreId);
        Assert.Null(await new InvoiceIntegrationLogRepository(other).GetByIdAsync(f.Ids[0]));
    }

    [Fact]
    public async Task New_provider_logs_continue_to_minimize_secrets_before_persistence()
    {
        var seed = await f.App.Database.SeedInventoryCatalogAsync();
        await using var db = f.App.Database.CreateTenantContext(seed.StoreId);
        var invoice = new InvoiceHead { StoreId=seed.StoreId, IsAutoInvoiceGroup=true };
        db.Add(invoice); await db.SaveChangesAsync();
        using var http = new HttpClient(new ProviderStub());
        var result = await new ViettelInvoiceLookupClient(http,new InvoiceIntegrationLogRepository(db))
            .SearchByTransactionUuidAsync(invoice.Id,"https://provider.invalid",Secret,Secret,InvoiceProviderAuthMode.BasicAuth,"0000000000","synthetic-b4-uuid");
        Assert.False(result.IsSuccess);
        var persisted = await db.InvoiceIntegrationLogs.AsNoTracking().SingleAsync();
        Assert.DoesNotContain(Secret,JsonSerializer.Serialize(persisted));
        Assert.Contains("redacted",persisted.RequestBody!);
        Assert.Contains("redacted",persisted.ResponseBody!);
        Assert.Equal("HTTP_503",persisted.ErrorCode);
    }

    private static async Task<string> Fingerprint(GaoApp.Infrastructure.Data.AppDbContext db)
    {
        var rows = await db.InvoiceIntegrationLogs.AsNoTracking().OrderBy(x=>x.Id)
            .Select(x=>new {x.Id,x.RequestBody,x.ResponseBody,x.RequestUrl,x.ErrorCode,x.ErrorMessage,x.RowVersion,x.CreatedAtUtc,x.UpdatedAtUtc}).ToListAsync();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(rows))));
    }
    private sealed class ProviderStub : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content=new StringContent("{\"access_token\":\""+Secret+"\"}"),RequestMessage=request });
    }
}

public sealed class InvoiceLogReadSecurityFixture : IAsyncLifetime
{
    internal FullApplicationFixture App {get;private set;} = null!;
    internal FullApplicationFixture.Client Own {get;private set;} = null!;
    internal FullApplicationFixture.Client Foreign {get;private set;} = null!;
    internal int[] Ids=[];
    internal int InvoiceId,ForeignId;
    public async Task InitializeAsync()
    {
        App=await FullApplicationFixture.StartWithAuditPausedAsync();
        try
        {
            Own=await App.LoginAsync(await App.AddAccountAsync(App.Stores[0],PermissionCodes.System.Integration.View));
            Foreign=await App.LoginAsync(await App.AddAccountAsync(App.Stores[1],PermissionCodes.System.Integration.View));
            for(var index=0;index<2;index++)
            {
                var store=App.Stores[index];
                await using var db=App.Database.CreateTenantContext(store.StoreId);
                var invoice=new InvoiceHead {StoreId=store.StoreId,IsAutoInvoiceGroup=true,InvoiceNumber="IV-B4-visible",InvoiceDate=new(2026,10,23)};
                db.Add(invoice);await db.SaveChangesAsync();
                var structured="{\"statusCode\":500,\"requestId\":\""+InvoiceLogReadSanitizerTests.Trace+"\",\"password\":\""+InvoiceLogReadSanitizerTests.Canary+"\"}";
                var bodies=new[]{structured}.Concat(InvoiceLogReadSanitizerTests.Bodies().Select(x=>(string)x[0])).ToArray();
                var logs=(index==0?bodies:bodies.Take(1)).Select((raw,i)=>new InvoiceIntegrationLog
                {
                    StoreId=store.StoreId,InvoiceHeadId=invoice.Id,ActionType=InvoiceIntegrationActionType.IssueInvoice,
                    IsSuccess=false,StartedAtUtc=new(2026,10,23,3,0,0,DateTimeKind.Utc),RequestBody=raw,ResponseBody=raw,
                    RequestUrl="https://user:"+InvoiceLogReadSanitizerTests.Canary+"@provider.invalid/files/"+InvoiceLogReadSanitizerTests.Canary+"?api_key="+InvoiceLogReadSanitizerTests.Canary,
                    ErrorCode=i==bodies.Length-1?"password="+InvoiceLogReadSanitizerTests.Canary:i==0?"Viettel.IssueBusinessFailed":"HTTP_500",
                    ErrorMessage="HTTP 500 timeout password="+InvoiceLogReadSanitizerTests.Canary
                }).ToArray();
                db.AddRange(logs);await db.SaveChangesAsync();
                if(index==0){Ids=logs.Select(x=>x.Id).ToArray();InvoiceId=invoice.Id;}else ForeignId=logs[0].Id;
            }
        }
        catch{await App.DisposeAsync();throw;}
    }
    public async Task DisposeAsync(){Own?.Dispose();Foreign?.Dispose();if(App is not null)await App.DisposeAsync();}
}
