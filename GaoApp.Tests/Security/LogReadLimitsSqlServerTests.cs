using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.Audit;
using GaoApp.Application.DTOs.Invoices;
using GaoApp.Application.Services.Invoices;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Repositories.Audit;
using GaoApp.Infrastructure.Repositories.Invoices;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

[CollectionDefinition("LogReadLimits", DisableParallelization = true)]
public sealed class LogReadLimitsCollection : ICollectionFixture<LogReadLimitsFixture> { }

[Collection("LogReadLimits")]
public sealed class LogReadLimitsSqlServerTests(LogReadLimitsFixture f)
{
    private const string AuditUrl = "/admin/api/audit-logs";
    private const string InvoiceUrl = "/Admin/InvoiceIntegrationLog";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Huge_HTTP_page_sizes_return_only_100_own_rows_and_report_the_effective_page_size(bool invoice)
    {
        using var response = await f.Own.Http.GetAsync(ListUrl(invoice) + "?pageSize=2147483647&keyword=b3-owned");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        var ids = Ids(invoice, text);
        Assert.Equal(100, ids.Length);
        Assert.Equal(Expected(invoice).Take(100), ids);
        if (invoice)
            Assert.Contains("Trang 1 / 13", WebUtility.HtmlDecode(text));
        else
        {
            using var json = JsonDocument.Parse(text);
            Assert.Equal(100, json.RootElement.GetProperty("pageSize").GetInt32());
            Assert.Equal(LogReadLimitsFixture.Count, json.RootElement.GetProperty("totalItems").GetInt32());
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task All_1205_rows_remain_reachable_once_with_deterministic_order_when_timestamps_tie(bool invoice)
    {
        var seen = new List<long>();
        for (var page = 1; page <= 13; page++)
        {
            using var response = await f.Own.Http.GetAsync(ListUrl(invoice) + $"?page={page}&pageSize=100&keyword=b3-owned");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            seen.AddRange(Ids(invoice, await response.Content.ReadAsStringAsync()));
        }
        Assert.Equal(Expected(invoice), seen);
        Assert.Equal(LogReadLimitsFixture.Count, seen.Distinct().Count());
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(-1, 20)]
    [InlineData(50, 50)]
    [InlineData(100, 100)]
    [InlineData(int.MaxValue, 100)]
    public async Task Direct_repositories_and_invoice_service_enforce_the_same_page_size_without_MVC(int requested, int expected)
    {
        await using var db = f.App.Database.CreateTenantContext(f.App.Stores[0].StoreId);
        var audit = await new AuditLogRepository(db).SearchAsync(f.App.Stores[0].StoreId,
            new AuditLogQueryDto { Page = 0, PageSize = requested, Keyword = "b3-owned" });
        Assert.Equal((1, expected, LogReadLimitsFixture.Count), (audit.Page, audit.PageSize, audit.TotalItems));
        Assert.Equal(expected, audit.Items.Count);
        var query = new InvoiceIntegrationLogQueryDto { Page = -1, PageSize = requested, Keyword = "b3-owned" };
        var repo = new InvoiceIntegrationLogRepository(db);
        var direct = await repo.QueryAsync(query);
        Assert.Equal(expected, direct.Items.Count);
        Assert.Equal(LogReadLimitsFixture.Count, direct.Total);
        var result = await new InvoiceIntegrationLogService(repo).GetLogsAsync(
            new InvoiceIntegrationLogQueryDto { Page = 0, PageSize = requested, Keyword = "b3-owned" });
        Assert.True(result.IsSuccess);
        Assert.Equal((1, expected), (result.Value.Page, result.Value.PageSize));
        Assert.Equal(expected, result.Value.Items.Count);
        var latest = await repo.GetLatestByInvoiceHeadAsync(f.InvoiceIds[0], requested);
        Assert.Equal(expected, latest.Count);
    }

    public static IEnumerable<object[]> InvalidQueries()
    {
        foreach (var invoice in new[] { false, true })
        {
            foreach (var query in new[] { "page=2147483647&pageSize=100", "page=2147483648", "page=nope", "pageSize=nope",
                "keyword=" + new string('x', 201), "keyword=" + new string('+', 201) + "x" })
                yield return [invoice, query];
            yield return [invoice, invoice ? "FromDate=bad" : "FromUtc=bad"];
            yield return [invoice, invoice ? "FromDate=2026-10-21&ToDate=2026-10-20" : "FromUtc=2026-10-21&ToUtc=2026-10-20"];
        }
        yield return [true, "FromDate=0001-01-01"];
        yield return [true, "ToDate=0001-01-01"];
        yield return [true, "ToDate=9999-12-31"];
        yield return [false, "EntityName=" + new string('x', 151)];
        yield return [false, "EntityId=" + new string('x', 101)];
    }

    [Theory, MemberData(nameof(InvalidQueries))]
    public async Task Invalid_HTTP_filters_and_overflowing_paging_return_400_without_500(bool invoice, string query)
    {
        using var response = await f.Own.Http.GetAsync(ListUrl(invoice) + "?" + query);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(await response.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task Safe_but_out_of_range_pages_are_empty_and_invalid_direct_calls_are_rejected()
    {
        await using var db = f.App.Database.CreateTenantContext(f.App.Stores[0].StoreId);
        var audit = new AuditLogRepository(db);
        var invoice = new InvoiceIntegrationLogRepository(db);
        Assert.Empty((await audit.SearchAsync(f.App.Stores[0].StoreId,
            new AuditLogQueryDto { Page = int.MaxValue, PageSize = 1 })).Items);
        Assert.Empty((await invoice.QueryAsync(new InvoiceIntegrationLogQueryDto { Page = int.MaxValue, PageSize = 1 })).Items);
        await Assert.ThrowsAsync<ValidationAppException>(() => audit.SearchAsync(null,
            new AuditLogQueryDto { Page = int.MaxValue, PageSize = 100 }));
        await Assert.ThrowsAsync<ValidationAppException>(() => invoice.QueryAsync(
            new InvoiceIntegrationLogQueryDto { ToDate = DateTime.MaxValue }));
        await Assert.ThrowsAsync<ValidationAppException>(() => invoice.QueryAsync(
            new InvoiceIntegrationLogQueryDto { Keyword = new('x', 201) }));
    }

    [Fact]
    public async Task Filters_keep_their_meaning_including_Vietnam_midnight_and_soft_deleted_rows()
    {
        await using var db = f.App.Database.CreateTenantContext(f.App.Stores[0].StoreId);
        var audit = await new AuditLogRepository(db).SearchAsync(f.App.Stores[0].StoreId, new AuditLogQueryDto
        {
            Keyword = "  b3-owned  ", EntityName = "B3", EntityId = "B3-entity", ActorUserId = f.Reader.UserId,
            Module = (int)AuditModuleType.Orders, ActionType = (int)AuditActionType.Update,
            FromUtc = LogReadLimitsFixture.Stamp, ToUtc = LogReadLimitsFixture.Stamp, PageSize = 100
        });
        Assert.Equal(LogReadLimitsFixture.Count, audit.TotalItems);
        var invoice = await new InvoiceIntegrationLogRepository(db).QueryAsync(new InvoiceIntegrationLogQueryDto
        {
            Keyword = "b3-midnight", FromDate = new(2026, 10, 20), ToDate = new(2026, 10, 20),
            InvoiceHeadId = f.InvoiceIds[1], ActionType = InvoiceIntegrationActionType.IssueInvoice, IsSuccess = true
        });
        Assert.Equal(2, invoice.Total);
        Assert.Equal(f.MidnightIds.Skip(1).Take(2).OrderDescending(), invoice.Items.Select(x => x.Id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Tenant_filters_cannot_be_overridden_and_foreign_details_remain_hidden(bool invoice)
    {
        using var response = await f.Own.Http.GetAsync(ListUrl(invoice) + $"?StoreId={f.App.Stores[1].StoreId}&pageSize=100");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var ownIds = Ids(invoice, await response.Content.ReadAsStringAsync());
        var foreignIds = invoice ? f.ForeignInvoiceLogs : f.ForeignAuditLogs;
        Assert.DoesNotContain(ownIds, foreignIds.Contains);
        var detail = invoice ? InvoiceUrl + "/Detail/" + foreignIds[0] : AuditUrl + "/" + foreignIds[0];
        using var hidden = await f.Own.Http.GetAsync(detail);
        Assert.Equal(invoice ? HttpStatusCode.Redirect : HttpStatusCode.NotFound, hidden.StatusCode);
        using var other = await f.Foreign.Http.GetAsync(ListUrl(invoice) + "?pageSize=100");
        Assert.Equal(HttpStatusCode.OK, other.StatusCode);
        Assert.Equal(foreignIds.OrderDescending(), Ids(invoice, await other.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task Both_log_views_still_require_their_existing_permissions()
    {
        using var client = await f.App.LoginAsync(await f.App.AddAccountAsync(f.App.Stores[0], PermissionCodes.Catalog.Product.View));
        foreach (var route in Routes())
        {
            using var response = await client.Http.GetAsync(route);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Each_log_read_route_uses_the_shared_authenticated_quota_and_does_not_block_POS(int route)
    {
        try
        {
            await f.App.ConfigureResourceProtectionAsync(new Dictionary<string, string> { ["Limits__HeavyRead__CallerPerWindow"] = "1" });
            using var own = await f.App.LoginAsync(await f.CashierAsync());
            using var other = await f.App.LoginAsync(await f.App.AddAccountAsync(f.App.Stores[0], LogReadLimitsFixture.Permissions));
            using (var first = await own.Http.GetAsync(Routes()[route])) Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            var nextRoute = Routes()[(route + 1) % 4];
            using (var limited = await own.Http.GetAsync(nextRoute + (nextRoute.Contains('?') ? "&" : "?") + "StoreId=999&CallerId=other"))
            {
                Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
                Assert.NotNull(limited.Headers.RetryAfter);
                Assert.Contains("resource_busy", await limited.Content.ReadAsStringAsync());
            }
            using (var allowed = await other.Http.GetAsync(Routes()[route])) Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
            await own.JsonAsync(HttpMethod.Post, "/admin/pos/shift/open", new { openingCash = 0, warehouseId = f.App.Stores[0].WarehouseId });
            using var cart = await own.Http.PostAsJsonAsync("/admin/pos/cart/current/new", new { });
            Assert.Equal(HttpStatusCode.OK, cart.StatusCode);
        }
        finally { await f.ResetLimits(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Four_real_SQL_reads_hold_the_default_concurrency_limit_and_a_burst_does_not_block_POS(bool invoice)
    {
        using var own = await f.App.LoginAsync(await f.CashierAsync());
        using var other = await f.App.LoginAsync(await f.App.AddAccountAsync(f.App.Stores[0], LogReadLimitsFixture.Permissions));
        await own.JsonAsync(HttpMethod.Post, "/admin/pos/shift/open", new { openingCash = 0, warehouseId = f.App.Stores[0].WarehouseId });
        await using var locker = new SqlConnection(f.App.Database.ConnectionString);
        await locker.OpenAsync();
        await using var tx = (SqlTransaction)await locker.BeginTransactionAsync();
        var table = invoice ? "InvoiceIntegrationLogs" : "AuditLogs";
        // A schema lock holds real SELECTs even if an ambient tenant read uses
        // snapshot isolation. The test column is rolled back in the same GUID DB.
        await using (var command = new SqlCommand($"ALTER TABLE dbo.{table} ADD B3TestReadGate int NULL;", locker, tx))
            await command.ExecuteNonQueryAsync();
        var held = Enumerable.Range(0, 4).Select(_ => own.Http.GetAsync(ListUrl(invoice) + "?keyword=b3-owned&pageSize=100")).ToArray();
        var watch = Stopwatch.StartNew();
        try
        {
            await using var observer = new SqlConnection(f.App.Database.ConnectionString);
            await observer.OpenAsync();
            var blocked = 0;
            while (blocked != 4 && watch.Elapsed < TimeSpan.FromSeconds(10))
            {
                // Concurrent compilation can queue readers behind the first
                // blocked reader. Count the whole lock chain, not just its head.
                await using var command = new SqlCommand("""
                    WITH blocked AS (
                        SELECT session_id FROM sys.dm_exec_requests WHERE database_id=DB_ID() AND blocking_session_id=@session
                        UNION ALL
                        SELECT r.session_id FROM sys.dm_exec_requests r JOIN blocked b ON r.blocking_session_id=b.session_id
                        WHERE r.database_id=DB_ID()
                    ) SELECT COUNT(DISTINCT session_id) FROM blocked OPTION (MAXRECURSION 32);
                    """, observer);
                command.Parameters.AddWithValue("@session", locker.ServerProcessId);
                blocked = (int)(await command.ExecuteScalarAsync())!;
                if (blocked != 4) await Task.Delay(50);
            }
            if (blocked != 4)
            {
                await using var trace = new SqlCommand("SELECT session_id,blocking_session_id,wait_type,status,command FROM sys.dm_exec_requests WHERE database_id=DB_ID() FOR JSON PATH", observer);
                var detail = (string?)await trace.ExecuteScalarAsync();
                Assert.Fail($"Expected four queued SQL reads, saw {blocked}. Completed HTTP tasks: {held.Count(x => x.IsCompleted)}. SQL requests: {detail}");
            }
            var burst = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => own.Http.GetAsync(ListUrl(!invoice))));
            foreach (var response in burst)
            {
                using (response) { Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode); Assert.NotNull(response.Headers.RetryAfter); }
            }
            using (var available = await other.Http.GetAsync(ListUrl(!invoice))) Assert.Equal(HttpStatusCode.OK, available.StatusCode);
            using var pos = await own.Http.PostAsJsonAsync("/admin/pos/cart/current/new", new { }).WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(HttpStatusCode.OK, pos.StatusCode);
        }
        finally
        {
            await tx.RollbackAsync();
            var completed = await Task.WhenAll(held).WaitAsync(TimeSpan.FromSeconds(20));
            foreach (var response in completed) { using (response) Assert.Equal(HttpStatusCode.OK, response.StatusCode); }
        }
        using var recovered = await own.Http.GetAsync(ListUrl(invoice));
        Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
        var directory = Path.Combine(FullApplicationFixture.SourceRoot(), ".artifacts/b3-log-read-limits-20261023/load");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, invoice ? "invoice.json" : "audit.json"),
            JsonSerializer.Serialize(new { fixtureRows = LogReadLimitsFixture.Count, heldSqlRequests = 4, burstRejected = 12, posSucceeded = true, readsRecovered = true, elapsedMs = watch.ElapsedMilliseconds }));
    }

    private string[] Routes() => [AuditUrl + "?keyword=b3-owned", AuditUrl + "/" + f.AuditIds[0],
        InvoiceUrl + "/Index?Keyword=b3-owned", InvoiceUrl + "/Detail/" + f.InvoiceLogIds[0]];
    private static string ListUrl(bool invoice) => invoice ? InvoiceUrl + "/Index" : AuditUrl;
    private IEnumerable<long> Expected(bool invoice) => (invoice ? f.InvoiceLogIds : f.AuditIds).OrderDescending();
    private static long[] Ids(bool invoice, string text)
    {
        if (invoice) return Regex.Matches(text, @"<strong>#(\d+)</strong>").Select(x => long.Parse(x.Groups[1].Value)).ToArray();
        using var json = JsonDocument.Parse(text);
        return json.RootElement.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetInt64()).ToArray();
    }
}

public sealed class LogReadLimitsFixture : IAsyncLifetime
{
    internal const int Count = 1205;
    internal static readonly DateTime Stamp = new(2026, 10, 20, 3, 0, 0, DateTimeKind.Utc);
    internal static readonly string[] Permissions = [PermissionCodes.System.AuditLog.View, PermissionCodes.System.Integration.View];
    internal FullApplicationFixture App { get; private set; } = null!;
    internal FullApplicationFixture.Account Reader { get; private set; } = null!;
    internal FullApplicationFixture.Client Own { get; private set; } = null!;
    internal FullApplicationFixture.Client Foreign { get; private set; } = null!;
    internal long[] AuditIds = [], InvoiceLogIds = [], ForeignAuditLogs = [], ForeignInvoiceLogs = [];
    internal int[] InvoiceIds = [];
    internal int[] MidnightIds = [];

    public async Task InitializeAsync()
    {
        App = await FullApplicationFixture.StartWithAuditPausedAsync();
        try
        {
            Reader = await App.AddAccountAsync(App.Stores[0], Permissions);
            Own = await App.LoginAsync(Reader);
            Foreign = await App.LoginAsync(await App.AddAccountAsync(App.Stores[1], Permissions));
            for (var index = 0; index < 2; index++)
            {
                var store = App.Stores[index];
                await using var db = App.Database.CreateTenantContext(store.StoreId);
                var invoice = new InvoiceHead { StoreId = store.StoreId, IsAutoInvoiceGroup = true, InvoiceNumber = "b3-owned" };
                db.Add(invoice); await db.SaveChangesAsync();
                var audits = Enumerable.Range(0, index == 0 ? Count : 37).Select(_ => new AuditLog
                {
                    StoreId = store.StoreId, ActorUserId = Reader.UserId, CreatedAtUtc = Stamp, Module = AuditModuleType.Orders,
                    ActionType = AuditActionType.Update, EntityName = "B3", EntityId = "B3-entity", Summary = "b3-owned"
                }).ToArray();
                var logs = Enumerable.Range(0, audits.Length).Select(_ => new InvoiceIntegrationLog
                {
                    StoreId = store.StoreId, InvoiceHeadId = invoice.Id, StartedAtUtc = Stamp,
                    ActionType = InvoiceIntegrationActionType.IssueInvoice, IsSuccess = true, ErrorMessage = "b3-owned"
                }).ToArray();
                db.AddRange(audits); db.AddRange(logs); await db.SaveChangesAsync();
                if (index == 0)
                {
                    AuditIds = audits.Select(x => x.Id).ToArray(); InvoiceLogIds = logs.Select(x => (long)x.Id).ToArray();
                    var edgeInvoice = new InvoiceHead { StoreId = store.StoreId, IsAutoInvoiceGroup = true, InvoiceNumber = "b3-midnight" };
                    db.Add(edgeInvoice); await db.SaveChangesAsync();
                    InvoiceIds = [invoice.Id, edgeInvoice.Id];
                    var start = new DateTime(2026, 10, 19, 17, 0, 0, DateTimeKind.Utc);
                    var midnight = new[] { start.AddSeconds(-1), start, start.AddDays(1).AddSeconds(-1), start.AddDays(1) }
                        .Select(time => new InvoiceIntegrationLog { StoreId = store.StoreId, InvoiceHeadId = edgeInvoice.Id,
                            StartedAtUtc = time, ActionType = InvoiceIntegrationActionType.IssueInvoice, IsSuccess = true, ErrorMessage = "b3-midnight" }).ToArray();
                    db.AddRange(midnight);
                    var deleted = new InvoiceIntegrationLog { StoreId = store.StoreId, InvoiceHeadId = invoice.Id, StartedAtUtc = Stamp,
                        ActionType = InvoiceIntegrationActionType.IssueInvoice, IsSuccess = true, ErrorMessage = "b3-owned b3-midnight" };
                    db.Add(deleted);
                    await db.SaveChangesAsync(); MidnightIds = midnight.Select(x => x.Id).ToArray();
                    // AppDbContext initializes all new rows as active. Mark the
                    // persisted fixture row deleted after insertion, as in production.
                    await db.InvoiceIntegrationLogs.Where(x => x.Id == deleted.Id)
                        .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.IsDeleted, true));
                }
                else { ForeignAuditLogs = audits.Select(x => x.Id).ToArray(); ForeignInvoiceLogs = logs.Select(x => (long)x.Id).ToArray(); }
            }
        }
        catch { await App.DisposeAsync(); throw; }
    }

    internal Task ResetLimits() => App.ConfigureResourceProtectionAsync(new Dictionary<string, string>
    {
        ["Limits__HeavyRead__CallerPerWindow"] = "120"
    });

    internal async Task<FullApplicationFixture.Account> CashierAsync()
    {
        var store = App.Stores[0];
        await using var db = App.Database.CreateTenantContext(store.StoreId);
        var terminal = new POSTerminal { StoreId = store.StoreId, Code = "B3-" + Guid.NewGuid().ToString("N")[..8], Name = "B3 isolated POS" };
        db.Add(terminal); await db.SaveChangesAsync();
        return await App.AddAccountAsync(store with { TerminalId = terminal.Id }, "*");
    }

    public async Task DisposeAsync() { Own?.Dispose(); Foreign?.Dispose(); if (App is not null) await App.DisposeAsync(); }
}
