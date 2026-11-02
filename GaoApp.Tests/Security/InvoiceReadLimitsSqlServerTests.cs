using System.Data.Common;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GaoApp.Application.Common;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.Invoices;
using GaoApp.Application.Services.Invoices;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Repositories.Invoices;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GaoApp.Tests.Security;

[CollectionDefinition("InvoiceReadLimits",DisableParallelization=true)]
public sealed class InvoiceReadLimitsCollection:ICollectionFixture<InvoiceReadLimitsFixture> { }

[Collection("InvoiceReadLimits")]
public sealed class InvoiceReadLimitsSqlServerTests(InvoiceReadLimitsFixture f)
{
    private static string[] Roots=>["/Admin/Invoice/Index","/Admin/Invoice/ViettelDashboard","/Admin/InvoiceDashboard/Index"];
    public static IEnumerable<object[]> InvalidQueries()=>
        from root in Roots
        from query in new[]{"ToDate=9999-12-31","FromDate=0001-01-01","ToDate=0001-01-01",
            "FromDate=9999-12-31","FromDate=2026-10-25&ToDate=2026-10-24",
            "FromDate=bad-date","ToDate=bad-date"}
        select new object[]{root,query};

    [Theory,MemberData(nameof(InvalidQueries))]
    public async Task Date_extremes_and_malformed_dates_return_400_on_actual_Web(string route,string query)
    {
        using var response=await f.Own.Http.GetAsync(route+"?"+query);
        Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);
    }
    public static IEnumerable<object[]> InvalidLists()=>
        from root in Roots.Take(2)
        from query in new[]{"Page=2147483647&PageSize=100","Page=2147483648","Page=B8_UNTRUSTED_INPUT",
            "PageSize=2147483648","Keyword="+new string('x',201),"Keyword="+new string(' ',201),"FromDate=2026-01-15&ToDate=2026-01-14"}
        select new object[]{root,query};
    [Theory,MemberData(nameof(InvalidLists))]
    public async Task Invalid_paging_and_long_filters_are_rejected_without_echoing_input(string route,string query)
    {
        using var response=await f.Own.Http.GetAsync(route+"?"+query.Replace(" ","%20"));
        Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);
        Assert.DoesNotContain("B8_UNTRUSTED_INPUT",await response.Content.ReadAsStringAsync());
    }
    [Theory]
    [InlineData(20)]
    [InlineData(100)]
    [InlineData(int.MaxValue)]
    public async Task All_pages_remain_accessible_with_exact_counts_ordering_and_B7_protection(int requestedSize)
    {
        var seen=new List<int>();var size=Math.Min(requestedSize,100);
        await using var db=f.App.Database.CreateTenantContext(f.Store.StoreId);
        var service=new InvoiceReadService(new InvoiceRepository(db),null!);
        for(var page=1;page<=(int)Math.Ceiling(InvoiceReadLimitsFixture.Count/(double)size);page++)
        {
            var result=await service.GetInvoicesAsync(new(){Page=page,PageSize=requestedSize,Keyword="B8-OWN"});
            Assert.True(result.IsSuccess);Assert.Equal(size,result.Value.PageSize);
            Assert.Equal(InvoiceReadLimitsFixture.Count,result.Value.TotalItems);
            Assert.InRange(result.Value.Items.Count,1,size);
            Assert.DoesNotContain(InvoiceErrorReadSanitizerTests.Canary,JsonSerializer.Serialize(result.Value));
            Assert.All(result.Value.Items,x=>{Assert.Equal(3,x.DetailCount);Assert.Equal(1,x.ManualLineCount);Assert.Equal(2,x.AutoLineCount);});
            Assert.All(result.Value.Items,x=>Assert.Equal(Array.IndexOf(f.InvoiceIds,x.Id)%2==0?"B8 buyer":"B8 company",x.BuyerName));
            seen.AddRange(result.Value.Items.Select(x=>x.Id));
        }
        Assert.Equal(f.InvoiceIds.OrderDescending(),seen);Assert.Empty(db.ChangeTracker.Entries());
    }
    [Fact]
    public async Task Valid_offset_past_last_page_does_not_execute_an_expensive_SKIP_query()
    {
        var trace=new ReadTrace();await using var db=f.App.Database.CreateTenantContext(f.Store.StoreId,trace);
        var repo=new InvoiceRepository(db);
        var result=await repo.QueryInvoiceHeadsAsync(null,null,null,"B8-OWN",InvoiceListDisplayMode.All,InvoiceListSortMode.OrderIdDesc,int.MaxValue,1);
        Assert.Empty(result.Items);Assert.Equal(InvoiceReadLimitsFixture.Count,result.Total);
        Assert.Single(trace.Reads);Assert.DoesNotContain("OFFSET",trace.Reads[0].Sql,StringComparison.OrdinalIgnoreCase);
    }
    [Theory]
    [InlineData("/Admin/Invoice/Index?SortMode=999")]
    [InlineData("/Admin/Invoice/Index?DisplayMode=999")]
    [InlineData("/Admin/Invoice/ViettelDashboard?Filter=999")]
    public async Task Unknown_enum_filters_return_400_in_actual_MVC(string route)
    {
        using var response=await f.Own.Http.GetAsync(route);Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);
    }
    [Fact]
    public async Task Repository_boundaries_reject_invalid_input_before_any_SQL_and_allow_full_history()
    {
        var trace=new ReadTrace();await using var db=f.App.Database.CreateTenantContext(f.Store.StoreId,trace);
        var repo=new InvoiceRepository(db);
        await Assert.ThrowsAsync<ValidationAppException>(()=>repo.QueryInvoiceHeadsAsync(null,null,null,null,InvoiceListDisplayMode.All,InvoiceListSortMode.OrderIdDesc,int.MaxValue,100));
        await Assert.ThrowsAsync<ValidationAppException>(()=>repo.QueryViettelDashboardAsync(new(){ToDate=DateTime.MaxValue}));
        await Assert.ThrowsAsync<ValidationAppException>(()=>repo.GetInvoiceDashboardAggregatesAsync(DateTime.MinValue,DateTime.MaxValue));
        Assert.Empty(trace.Reads);
        var result=await new InvoiceDashboardService(repo,new InvoiceIntegrationLogRepository(db)).GetDashboardAsync(new(){FromDate=new(1,1,2),ToDate=new(9999,12,30)});
        Assert.True(result.IsSuccess);Assert.True(result.Value.TotalInvoices>=InvoiceReadLimitsFixture.Count);
    }
    [Fact]
    public async Task SQL_list_projects_counts_without_loading_details_notes_snapshots_or_credentials()
    {
        var trace=new ReadTrace();await using var db=f.App.Database.CreateTenantContext(f.Store.StoreId,trace);
        var result=await new InvoiceReadService(new InvoiceRepository(db),null!).GetInvoicesAsync(new(){Keyword="B8-OWN",PageSize=int.MaxValue});
        Assert.Equal(100,result.Value.Items.Count);
        var columns=trace.Reads.SelectMany(x=>x.Columns).ToArray();
        foreach(var excluded in new[]{"LegacySnapshotJson","Note","Password","SourceSnapshotJson","ItemName"})
            Assert.DoesNotContain(columns,x=>x.Equals(excluded,StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(trace.Reads,x=>x.Sql.Contains("JOIN [InvoiceDetail",StringComparison.OrdinalIgnoreCase));
        Assert.Empty(db.ChangeTracker.Entries());
    }
    [Fact]
    public async Task SQL_dashboard_matches_original_business_counts_including_unicode_whitespace_and_full_period()
    {
        await using var original=f.App.Database.CreateTenantContext(f.Store.StoreId);
        var rows=await original.InvoiceHeads.AsNoTracking().Where(x=>x.InvoiceDate>=InvoiceReadLimitsFixture.Day&&x.InvoiceDate<InvoiceReadLimitsFixture.Day.AddDays(1)).ToListAsync();
        Assert.Equal(InvoiceReadLimitsFixture.Count,rows.Count);
        var trace=new ReadTrace();await using var db=f.App.Database.CreateTenantContext(f.Store.StoreId,trace);
        var result=await new InvoiceDashboardService(new InvoiceRepository(db),new InvoiceIntegrationLogRepository(db)).GetDashboardAsync(new(){FromDate=InvoiceReadLimitsFixture.Day,ToDate=InvoiceReadLimitsFixture.Day});
        Assert.True(result.IsSuccess);var x=result.Value;var issued=rows.Where(Issued).ToList();
        Assert.Equal(rows.Count,x.TotalInvoices);Assert.Equal(issued.Count,x.IssuedCount);Assert.Equal(issued.Sum(i=>i.GrandTotal),x.IssuedAmount);
        Assert.Equal(rows.Count(i=>i.ProviderStatus==InvoiceProviderStatus.LocalDraft),x.LocalDraftCount);
        Assert.Equal(rows.Count(i=>i.ProviderStatus==InvoiceProviderStatus.ReadyToIssue),x.ReadyToIssueCount);
        Assert.Equal(rows.Count(i=>i.ProviderStatus==InvoiceProviderStatus.IssueFailed),x.IssueFailedCount);
        Assert.Equal(rows.Count(i=>i.ProviderStatus==InvoiceProviderStatus.EmailSent),x.EmailSentCount);
        Assert.Equal(rows.Count(Uuid),x.NeedUuidSyncCount);
        Assert.Equal(rows.Count(i=>!string.IsNullOrWhiteSpace(i.PdfFilePath)),x.PdfDownloadedCount);
        Assert.Equal(rows.Count(i=>!string.IsNullOrWhiteSpace(i.ZipFilePath)),x.ZipDownloadedCount);
        Assert.Equal(issued.Count(i=>string.IsNullOrWhiteSpace(i.PdfFilePath)),x.MissingPdfCount);
        Assert.Equal(issued.Count(i=>string.IsNullOrWhiteSpace(i.ZipFilePath)),x.MissingZipCount);
        Assert.Equal(rows.Sum(i=>i.GrandTotal),x.StatusRows.Sum(i=>i.Amount));Assert.Equal(rows.Count,x.StatusRows.Sum(i=>i.Count));
        var aggregate=Assert.Single(trace.Reads,r=>r.Sql.Contains("GROUP BY",StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(aggregate.Columns,c=>c is "LastErrorMessage" or "LegacySnapshotJson" or "BuyerName" or "PdfFilePath");
        Assert.DoesNotContain(InvoiceErrorReadSanitizerTests.Canary,JsonSerializer.Serialize(x));Assert.Empty(db.ChangeTracker.Entries());
    }
    [Fact]
    public async Task Actual_HTML_page_sizes_are_bounded_and_foreign_store_and_permission_boundaries_remain()
    {
        using(var response=await f.Own.Http.GetAsync(Roots[0]+"?Keyword=B8-OWN&PageSize=2147483647"))
        {
            Assert.Equal(HttpStatusCode.OK,response.StatusCode);var html=WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
            Assert.Equal(100,Regex.Matches(html,"aria-label=\"Xem chi tiết hóa đơn [0-9]+\"").Count);
            Assert.DoesNotContain(InvoiceErrorReadSanitizerTests.Canary,html);Assert.DoesNotContain("B8-FOREIGN",html);
        }
        using(var response=await f.Own.Http.GetAsync(Roots[1]+"?Keyword=B8-OWN&PageSize=2147483647"))
        {
            Assert.Equal(HttpStatusCode.OK,response.StatusCode);var html=WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
            Assert.Equal(100,Regex.Matches(html,"href=\"[^\"]*/Invoice/ViettelPayload/[0-9]+\"").Count);
        }
        using var denied=await f.App.LoginAsync(await f.App.AddAccountAsync(f.Store,PermissionCodes.Catalog.Product.View));
        foreach(var route in Roots)
        {
            using(var response=await denied.Http.GetAsync(route))Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);
            using(var response=await f.Foreign.Http.GetAsync(route+"?StoreId="+f.Store.StoreId))
            {Assert.Equal(HttpStatusCode.OK,response.StatusCode);Assert.DoesNotContain("B8-OWN",WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));}
        }
    }
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Every_read_route_obeys_shared_quota_but_a_cash_POS_sale_still_completes(int route)
    {
        try
        {
            await f.App.ConfigureResourceProtectionAsync(new Dictionary<string,string>{{"Limits__HeavyRead__CallerPerWindow","1"}});
            using var own=await f.App.LoginAsync(await f.CashierAsync());
            using var other=await f.App.LoginAsync(await f.App.AddAccountAsync(f.Store,"*"));
            using(var first=await own.Http.GetAsync(Roots[route]))Assert.Equal(HttpStatusCode.OK,first.StatusCode);
            using(var limited=await own.Http.GetAsync(Roots[(route+1)%3]+"?StoreId=999&CallerId=other"))
            {Assert.Equal(HttpStatusCode.TooManyRequests,limited.StatusCode);Assert.NotNull(limited.Headers.RetryAfter);}
            using(var head=await own.Http.SendAsync(new HttpRequestMessage(HttpMethod.Head,Roots[route])))
                Assert.True(head.StatusCode is HttpStatusCode.MethodNotAllowed or HttpStatusCode.TooManyRequests);
            using(var allowed=await other.Http.GetAsync(Roots[route]))Assert.Equal(HttpStatusCode.OK,allowed.StatusCode);
            await own.JsonAsync(HttpMethod.Post,"/admin/pos/shift/open",new{openingCash=0,warehouseId=f.Store.WarehouseId});
            var draft=await own.JsonAsync(HttpMethod.Post,"/admin/pos/draft");var id=draft.GetProperty("orderId").GetInt32();
            var cart=await own.JsonAsync(HttpMethod.Post,$"/admin/pos/{id}/items?variantId={f.Store.VariantId}&qty=1");
            await using(var db=f.App.Database.CreateTenantContext(f.Store.StoreId))
            {
                var total=await db.Orders.Where(o=>o.Id==id).Select(o=>o.GrandTotal).SingleAsync();
                Assert.True(total>0);
                await own.JsonAsync(HttpMethod.Post,$"/admin/pos/{id}/payments",new{clientRequestId=Guid.NewGuid(),amount=total,method=0});
            }
            await own.JsonAsync(HttpMethod.Post,$"/admin/pos/{id}/finalize");
            await using(var db=f.App.Database.CreateTenantContext(f.Store.StoreId))
                Assert.Equal(OrderStatus.Completed,(await db.Orders.SingleAsync(o=>o.Id==id)).Status);
        }
        finally{await f.ResetLimits();}
    }
    [Fact]
    public async Task Four_SQL_reads_hold_default_concurrency_and_burst_does_not_block_POS_or_leak_leases()
    {
        using var own=await f.App.LoginAsync(await f.CashierAsync());
        await own.JsonAsync(HttpMethod.Post,"/admin/pos/shift/open",new{openingCash=0,warehouseId=f.Store.WarehouseId});
        await using var locker=new SqlConnection(f.App.Database.ConnectionString);await locker.OpenAsync();
        await using var tx=(SqlTransaction)await locker.BeginTransactionAsync();
        await using(var command=new SqlCommand("ALTER TABLE dbo.InvoiceHeads ADD B8TestReadGate int NULL;",locker,tx))await command.ExecuteNonQueryAsync();
        var held=Enumerable.Range(0,4).Select(i=>own.Http.GetAsync(Roots[i%3])).ToArray();var timer=Stopwatch.StartNew();
        try
        {
            await using var observer=new SqlConnection(f.App.Database.ConnectionString);await observer.OpenAsync();var blocked=0;
            while(blocked!=4&&timer.Elapsed<TimeSpan.FromSeconds(10))
            {
                await using var command=new SqlCommand("WITH blocked AS (SELECT session_id FROM sys.dm_exec_requests WHERE database_id=DB_ID() AND blocking_session_id=@session UNION ALL SELECT r.session_id FROM sys.dm_exec_requests r JOIN blocked b ON r.blocking_session_id=b.session_id WHERE r.database_id=DB_ID()) SELECT COUNT(DISTINCT session_id) FROM blocked OPTION(MAXRECURSION 32);",observer);
                command.Parameters.AddWithValue("@session",locker.ServerProcessId);blocked=(int)(await command.ExecuteScalarAsync())!;
                if(blocked!=4)await Task.Delay(50);
            }
            Assert.Equal(4,blocked);
            foreach(var response in await Task.WhenAll(Enumerable.Range(0,12).Select(i=>own.Http.GetAsync(Roots[i%3]))))
                using(response){Assert.Equal(HttpStatusCode.TooManyRequests,response.StatusCode);Assert.NotNull(response.Headers.RetryAfter);}
            using var pos=await own.Http.PostAsJsonAsync("/admin/pos/cart/current/new",new{}).WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(HttpStatusCode.OK,pos.StatusCode);
        }
        finally
        {
            await tx.RollbackAsync();foreach(var response in await Task.WhenAll(held).WaitAsync(TimeSpan.FromSeconds(20)))
                using(response)Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        }
        using var recovered=await own.Http.GetAsync(Roots[0]);Assert.Equal(HttpStatusCode.OK,recovered.StatusCode);
    }
    [Fact]
    public async Task Reading_does_not_rewrite_source_errors_snapshots_or_rowversions()
    {
        async Task<string> Hash()
        {
            await using var db=f.App.Database.CreateTenantContext(f.Store.StoreId);
            var rows=await db.InvoiceHeads.AsNoTracking().Where(x=>x.InvoiceNumber!.StartsWith("B8-OWN")).OrderBy(x=>x.Id)
                .Select(x=>new{x.Id,x.LastErrorCode,x.LastErrorMessage,x.LegacySnapshotJson,x.RowVersion}).ToListAsync();
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(rows))));
        }
        var before=await Hash();foreach(var route in Roots){using var response=await f.Own.Http.GetAsync(route);Assert.Equal(HttpStatusCode.OK,response.StatusCode);}
        Assert.Equal(before,await Hash());
    }
    private static bool Issued(InvoiceHead x)=>!string.IsNullOrWhiteSpace(x.ProviderInvoiceNo)||x.ProviderStatus is InvoiceProviderStatus.Issued or InvoiceProviderStatus.PdfDownloaded or InvoiceProviderStatus.ZipDownloaded or InvoiceProviderStatus.EmailSent;
    private static bool Uuid(InvoiceHead x)=>x.ProviderStatus is InvoiceProviderStatus.Issuing or InvoiceProviderStatus.IssuedWaitingNumber||x.ProviderStatus==InvoiceProviderStatus.IssueFailed&&(
        string.Equals(x.LastErrorCode,"TIMEOUT",StringComparison.OrdinalIgnoreCase)||(x.LastErrorCode??"").StartsWith("HTTP_5",StringComparison.OrdinalIgnoreCase)||
        (x.LastErrorMessage??"").Contains("timeout",StringComparison.OrdinalIgnoreCase)||(x.LastErrorMessage??"").Contains("HTTP 500",StringComparison.OrdinalIgnoreCase));
    private sealed class ReadTrace:DbCommandInterceptor
    {
        internal List<(string Sql,string[] Columns)> Reads=[];
        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command,CommandExecutedEventData data,DbDataReader result,CancellationToken ct=default)
        {Reads.Add((command.CommandText,Enumerable.Range(0,result.FieldCount).Select(result.GetName).ToArray()));return ValueTask.FromResult(result);}
    }
}

public sealed class InvoiceReadLimitsFixture:IAsyncLifetime
{
    internal const int Count=251;
    internal static readonly DateTime Day=new(2026,1,15);
    internal FullApplicationFixture App=null!;
    internal FullApplicationFixture.Client Own=null!,Foreign=null!;
    internal FullApplicationFixture.StoreSeed Store=>App.Stores[0];
    internal int[] InvoiceIds=[];
    public async Task InitializeAsync()
    {
        App=await FullApplicationFixture.StartWithAuditPausedAsync();
        try
        {
            for(var storeIndex=0;storeIndex<2;storeIndex++)
            {
                var store=App.Stores[storeIndex];await using var db=App.Database.CreateTenantContext(store.StoreId);
                var rows=Enumerable.Range(0,storeIndex==0?Count:7).Select(i=>
                {
                    var whitespace=new[]{" ","\t\n\r", "\u0085\u00a0\u1680", "\u2000\u2001\u2002\u2003\u2004\u2005\u2006\u2007\u2008\u2009\u200a", "\u2028\u2029\u202f\u205f\u3000"}[i%5];
                    var row=new InvoiceHead {StoreId=store.StoreId,IsAutoInvoiceGroup=true,InvoiceDate=Day.AddHours(i%24),
                        InvoiceNumber=$"B8-{(storeIndex==0?"OWN":"FOREIGN")}-{i:D4}",GrandTotal=i+1,SubTotal=i+1,
                        ProviderCode="VIETTEL",ProviderStatus=(InvoiceProviderStatus)(i%12),BuyerName="B8 buyer",BuyerLegalName=i%2==0?whitespace:"B8 company",
                        PdfFilePath=i%3==0?"b8.pdf":i%3==1?null:whitespace,ZipFilePath=i%3==0?"b8.zip":whitespace,
                        ProviderInvoiceNo=i%5==0?"B8 provider number":whitespace,
                        LastErrorCode=i%2==0?"TIMEOUT":"http_503",LastErrorMessage="timeout password="+InvoiceErrorReadSanitizerTests.Canary,
                        LegacySnapshotJson="{\"synthetic\":\""+new string('x',8192)+"\"}"};
                    if(row.ProviderStatus==InvoiceProviderStatus.IssueFailed)
                    {
                        var cases=new (string Code,string Message)[]{("TIMEOUT",""),("timeout ",""),("TıMEOUT",""),("ŦIMEOUT",""),("tímeout",""),
                            ("http_503",""),("HTTP_5unknown",""),("HTTPA503",""),("OTHER","timeout"),("OTHER","HTTP 500"),("OTHER","http 500"),("OTHER","HŦTP 500"),("OTHER","tímeout")};
                        var sample=cases[(i/12)%cases.Length];row.LastErrorCode=sample.Code;
                        row.LastErrorMessage=sample.Message+" password="+InvoiceErrorReadSanitizerTests.Canary;
                    }
                    for(var line=0;line<3;line++)row.Details.Add(new InvoiceDetail {StoreId=store.StoreId,ItemName="B8 item",UnitName="Unit",Quantity=1,UnitPrice=1,Amount=1,TotalAmount=1,Note="B8 note",SourceType=line==0?InvoiceDetailSourceType.Manual:InvoiceDetailSourceType.FromOrderLine});
                    return row;
                }).ToArray();db.AddRange(rows);await db.SaveChangesAsync();
                if(storeIndex==0)
                {
                    InvoiceIds=rows.Select(x=>x.Id).ToArray();
                    var deleted=new InvoiceHead {StoreId=store.StoreId,IsAutoInvoiceGroup=true,InvoiceNumber="B8-OWN-DELETED",InvoiceDate=Day,GrandTotal=999999};
                    var line=new InvoiceDetail {StoreId=store.StoreId,InvoiceHeadId=rows[0].Id,ItemName="B8 deleted line",UnitName="Unit",Quantity=999};
                    db.Add(deleted);db.Add(line);await db.SaveChangesAsync();db.Remove(deleted);db.Remove(line);await db.SaveChangesAsync();
                }
            }
            Own=await App.LoginAsync(await App.AddAccountAsync(Store,"*"));
            Foreign=await App.LoginAsync(await App.AddAccountAsync(App.Stores[1],"*"));
        }
        catch{await App.DisposeAsync();throw;}
    }
    internal Task ResetLimits()=>App.ConfigureResourceProtectionAsync(new Dictionary<string,string>{{"Limits__HeavyRead__CallerPerWindow","120"}});
    internal async Task<FullApplicationFixture.Account> CashierAsync()
    {
        await using var db=App.Database.CreateTenantContext(Store.StoreId);
        var terminal=new POSTerminal {StoreId=Store.StoreId,Code="B8-"+Guid.NewGuid().ToString("N")[..8],Name="B8 isolated POS"};
        db.Add(terminal);await db.SaveChangesAsync();
        return await App.AddAccountAsync(Store with {TerminalId=terminal.Id},"*");
    }
    public async Task DisposeAsync(){Own?.Dispose();Foreign?.Dispose();if(App is not null)await App.DisposeAsync();}
}
