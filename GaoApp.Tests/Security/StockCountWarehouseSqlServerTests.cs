using System.Data.Common;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GaoApp.Application.DTOs.Inventory;
using GaoApp.Application.Interfaces.Services.Inventory;
using GaoApp.Application.Services.Inventory;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Repositories.Inventory;
using GaoApp.Tests.Inventory;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GaoApp.Tests.Security;

[Collection("R1FinalDatabasePreflight")]
public sealed class StockCountWarehouseSqlServerTests(StockCountConcurrencyFixture fixture)
    : IClassFixture<StockCountConcurrencyFixture>
{
    private const string Api = "/admin/api/stock-counts";
    private AppDbContext Db(IInterceptor? interceptor = null) => fixture.App.Database.CreateTenantContext(fixture.Store.StoreId, interceptor);
    private sealed record Count(int Id, int Source, int Destination, int? LineId);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Lines_block_a_warehouse_change_but_allow_other_header_edits_and_correct_posting(bool rejected)
    {
        var count = await CreateAsync(withLine: true);
        if (rejected) await RejectAsync(count.Id);
        await using var before = Db();
        var original = await before.StockCountDocuments.AsNoTracking().SingleAsync(x => x.Id == count.Id);
        using var response = await ChangeAsync(count, count.Destination);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("không thể đổi kho", error.GetProperty("message").GetString());
        await using (var verify = Db())
        {
            var doc = await verify.StockCountDocuments.AsNoTracking().Include(x => x.Lines).SingleAsync(x => x.Id == count.Id);
            Assert.Equal(count.Source, doc.WarehouseId);
            Assert.Equal(original.Note, doc.Note);
            Assert.Equal(original.DocumentDate, doc.DocumentDate);
            Assert.True(original.RowVersion.SequenceEqual(doc.RowVersion));
            var line = Assert.Single(doc.Lines);
            Assert.Equal(100m, line.SystemQtyBase); Assert.Equal(110m, line.CountedQtyBase); Assert.Equal(10m, line.DifferenceQtyBase);
        }
        using var edited = await ChangeAsync(count, count.Source);
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        await using (var verify = Db())
        {
            var doc = await verify.StockCountDocuments.SingleAsync(x => x.Id == count.Id);
            Assert.Equal("B6 changed header", doc.Note);
            Assert.Equal(DateTime.Today.AddDays(-1), doc.DocumentDate);
        }
        await ConfirmAsync(count.Id);
        await AssertPostingAsync(count, count.Source, 10, 110, 0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Empty_draft_or_rejected_document_can_move_and_new_lines_snapshot_the_new_warehouse(bool rejected)
    {
        var count = await CreateAsync(withLine: rejected);
        if (rejected)
        {
            await RejectAsync(count.Id);
            await fixture.Editor.JsonAsync(HttpMethod.Delete, $"{Api}/lines/{count.LineId}");
        }
        using var changed = await ChangeAsync(count, count.Destination);
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        await AddAsync(count.Id, 12);
        await ConfirmAsync(count.Id);
        await AssertPostingAsync(count, count.Destination, 12, 100, 12);
    }

    [Theory]
    [InlineData(100)]
    [InlineData(0)]
    public async Task A_line_with_no_difference_or_zero_count_still_prevents_a_warehouse_change(int counted)
    {
        var count = await CreateAsync(withLine: true);
        await fixture.Editor.JsonAsync(HttpMethod.Put, $"{Api}/lines/{count.LineId}", new { unitId = fixture.UnitId, countedQty = counted });
        using var denied = await ChangeAsync(count, count.Destination);
        Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
        await using var verify = Db();
        var doc = await verify.StockCountDocuments.Include(x => x.Lines).SingleAsync(x => x.Id == count.Id);
        Assert.Equal(count.Source, doc.WarehouseId);
        Assert.Equal(counted, Assert.Single(doc.Lines).CountedQty);
        Assert.Empty(await LedgerAsync(verify, count.Id));
    }

    [Fact]
    public async Task Partial_deletion_still_blocks_change_and_removing_all_active_lines_allows_it_without_reusing_line_numbers()
    {
        var count = await CreateAsync(withLine: true);
        var extra = await fixture.Editor.JsonAsync(HttpMethod.Post, $"{Api}/{count.Id}/lines", new
        { productVariantId = fixture.SecondVariantId, unitId = fixture.UnitId, countedQty = 3 });
        await fixture.Editor.JsonAsync(HttpMethod.Delete, $"{Api}/lines/{count.LineId}");
        using var denied = await ChangeAsync(count, count.Destination);
        Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
        await fixture.Editor.JsonAsync(HttpMethod.Delete, $"{Api}/lines/{extra.GetProperty("id").GetInt32()}");
        using var allowed = await ChangeAsync(count, count.Destination);
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        await AddAsync(count.Id, 12);
        await using (var verify = Db())
        {
            var deleted = await verify.StockCountLines.IgnoreQueryFilters().Where(x => x.StoreId == fixture.Store.StoreId && x.StockCountDocumentId == count.Id && x.IsDeleted).ToListAsync();
            Assert.Equal(2, deleted.Count);
            var active = await verify.StockCountLines.SingleAsync(x => x.StockCountDocumentId == count.Id);
            Assert.True(active.LineNo > deleted.Max(x => x.LineNo));
            Assert.Equal(0, active.SystemQtyBase); Assert.Equal(12, active.DifferenceQtyBase);
        }
        await ConfirmAsync(count.Id);
        await AssertPostingAsync(count, count.Destination, 12, 100, 12);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    public async Task Non_editable_states_remain_locked(int status)
    {
        var count = await CreateAsync(withLine: true);
        await fixture.Editor.JsonAsync(HttpMethod.Post, $"{Api}/{count.Id}/submit-approval");
        if (status == 3) await fixture.Approver.JsonAsync(HttpMethod.Post, $"{Api}/{count.Id}/confirm");
        if (status == 5)
        {
            await using var db = Db();
            (await db.StockCountDocuments.SingleAsync(x => x.Id == count.Id)).Status = StockCountDocumentStatus.Cancelled;
            await db.SaveChangesAsync();
        }
        using var denied = await ChangeAsync(count, count.Destination);
        Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
        await using var verify = Db();
        var doc = await verify.StockCountDocuments.SingleAsync(x => x.Id == count.Id);
        Assert.Equal(count.Source, doc.WarehouseId); Assert.Equal((StockCountDocumentStatus)status, doc.Status);
    }

    [Theory]
    [InlineData("missing-warehouse")]
    [InlineData("foreign-warehouse")]
    [InlineData("foreign-document")]
    [InlineData("missing-document")]
    [InlineData("no-permission")]
    [InlineData("csrf")]
    public async Task Invalid_or_denied_header_requests_preserve_saved_state(string kind)
    {
        var count = await CreateAsync(withLine: false);
        var body = Header(count.Id, kind == "missing-warehouse" ? int.MaxValue : kind == "foreign-warehouse" ? fixture.App.Stores[1].WarehouseId : count.Destination);
        if (kind == "missing-document") body.StockCountDocumentId = int.MaxValue;
        var client = kind == "foreign-document" ? fixture.Foreign : kind == "no-permission" ? fixture.Reader : fixture.Editor;
        HttpResponseMessage response;
        if (kind == "csrf")
        {
            using var noCsrf = new HttpClient(new HttpClientHandler { CookieContainer = client.Cookies }) { BaseAddress = fixture.App.Address };
            noCsrf.DefaultRequestHeaders.Host = fixture.Store.Host;
            response = await noCsrf.PostAsJsonAsync(Api + "/update-header", body);
        }
        else response = await client.Http.PostAsJsonAsync(Api + "/update-header", body);
        using (response)
        {
            Assert.Equal(kind == "no-permission" ? HttpStatusCode.Forbidden : HttpStatusCode.BadRequest, response.StatusCode);
        }
        await using var verify = Db();
        var doc = await verify.StockCountDocuments.Include(x => x.Lines).SingleAsync(x => x.Id == count.Id);
        Assert.Equal(count.Source, doc.WarehouseId); Assert.Equal("B6 original", doc.Note); Assert.Empty(doc.Lines);
        Assert.Empty(await LedgerAsync(verify, count.Id));
    }

    [Fact]
    public async Task Stale_tracked_empty_document_cannot_move_after_another_request_adds_a_line()
    {
        var count = await CreateAsync(withLine: false);
        await using var stale = Db();
        await stale.StockCountDocuments.Include(x => x.Lines).SingleAsync(x => x.Id == count.Id);
        await AddAsync(count.Id, 110);
        var service = Service(stale);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => service.UpdateHeaderAsync(Header(count.Id, count.Destination)));
        Assert.Null(stale.Database.CurrentTransaction);
        await ConfirmAsync(count.Id);
        await AssertPostingAsync(count, count.Source, 10, 110, 0);
    }

    [Fact]
    public async Task Add_holds_the_document_lock_and_a_waiting_warehouse_change_is_rejected_after_commit()
    {
        var count = await CreateAsync(withLine: false);
        await using var editing = Db();
        var gate = new PausingResolver(new InventoryUnitResolver(new StockDocumentRepository(editing)));
        var adding = Service(editing, gate).AddLineAsync(count.Id, AddBody(110));
        Task<HttpResponseMessage>? changing = null;
        try
        {
            await gate.Reached.Task.WaitAsync(TimeSpan.FromSeconds(10));
            changing = ChangeAsync(count, count.Destination);
            await WaitForBlockedAsync(editing, changing);
        }
        finally { gate.Resume.TrySetResult(); }
        await adding;
        using var changed = await changing!;
        Assert.Equal(HttpStatusCode.BadRequest, changed.StatusCode);
        await ConfirmAsync(count.Id);
        await AssertPostingAsync(count, count.Source, 10, 110, 0);
    }

    [Fact]
    public async Task Warehouse_change_committed_first_makes_waiting_add_use_the_new_warehouse_snapshot()
    {
        var count = await CreateAsync(withLine: false);
        var gate = new PausingWrite("UPDATE [StockCountDocument]");
        await using var editing = Db(gate);
        var changing = Service(editing).UpdateHeaderAsync(Header(count.Id, count.Destination));
        Task<HttpResponseMessage>? adding = null;
        try
        {
            await gate.Reached.Task.WaitAsync(TimeSpan.FromSeconds(10));
            adding = fixture.Editor.Http.PostAsJsonAsync($"{Api}/{count.Id}/lines", AddBody(12));
            await WaitForBlockedAsync(editing, adding);
        }
        finally { gate.Resume.TrySetResult(); }
        await changing;
        using var added = await adding!;
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);
        await ConfirmAsync(count.Id);
        await AssertPostingAsync(count, count.Destination, 12, 100, 12);
    }

    [Fact]
    public async Task Deleting_the_last_line_committed_first_allows_the_waiting_warehouse_change()
    {
        var count = await CreateAsync(withLine: true);
        var gate = new PausingWrite("UPDATE [StockCountLine]");
        await using var deleting = Db(gate);
        var removal = Service(deleting).DeleteLineAsync(count.LineId!.Value);
        Task<HttpResponseMessage>? changing = null;
        try
        {
            await gate.Reached.Task.WaitAsync(TimeSpan.FromSeconds(10));
            changing = ChangeAsync(count, count.Destination);
            await WaitForBlockedAsync(deleting, changing);
        }
        finally { gate.Resume.TrySetResult(); }
        await removal;
        using var changed = await changing!;
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        await AddAsync(count.Id, 12);
        await ConfirmAsync(count.Id);
        await AssertPostingAsync(count, count.Destination, 12, 100, 12);
    }

    [Fact]
    public async Task Chrome_locks_warehouse_reloads_stale_state_and_reenables_it_after_all_lines_are_deleted()
    {
        var locked = await CreateAsync(withLine: true);
        var empty = await CreateAsync(withLine: false);
        var stale = await CreateAsync(withLine: false);
        var moved = await CreateAsync(withLine: false);
        // Exercise a persisted warehouse outside the select2 endpoint's first 20
        // options, with change targets still available in the real response.
        await using (var db = Db())
        {
            foreach (var count in new[] { locked, empty, stale, moved })
            {
                (await db.Warehouses.SingleAsync(x => x.Id == count.Source)).Name = "Z B6 browser source " + count.Id;
                (await db.Warehouses.SingleAsync(x => x.Id == count.Destination)).Name = "A B6 browser target " + count.Id;
            }
            var legalEntity = (await db.Warehouses.SingleAsync(x => x.Id == locked.Source)).LegalEntityId;
            for (var i = 0; i < 25; i++) db.Add(new Warehouse
            { StoreId = fixture.Store.StoreId, LegalEntityId = legalEntity, Code = "B6-PAD-" + Guid.NewGuid().ToString("N")[..8], Name = "M B6 browser option " + i, IsActive = true });
            await db.SaveChangesAsync();
        }
        var account = await fixture.App.AddAccountAsync(fixture.Store, "*");
        var root = FullApplicationFixture.SourceRoot();
        var output = Path.Combine(root, ".artifacts/b6-stock-count-warehouse-20261024/browser");
        Directory.CreateDirectory(output);
        var start = new ProcessStartInfo("node") { WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.Environment["NODE_PATH"] = Path.Combine(root, "Logs/pos-offline-browser-deps/node_modules");
        start.ArgumentList.Add(Path.Combine(root, "GaoApp.Tests.Browser/stock-count-warehouse.browser.cjs"));
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new
        {
            baseUrl = $"http://{fixture.Store.Host}:{fixture.App.Address.Port}", terminalId = fixture.Store.TerminalId,
            user = account.Name, password = account.Password, output,
            locked = new { id = locked.Id, source = locked.Source, destination = locked.Destination },
            empty = new { id = empty.Id, source = empty.Source, destination = empty.Destination },
            stale = new { id = stale.Id, source = stale.Source, destination = stale.Destination },
            moved = new { id = moved.Id, source = moved.Source, destination = moved.Destination },
            variantId = fixture.Store.VariantId, unitId = fixture.UnitId
        }));
        process.StandardInput.Close();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { if (!process.HasExited) process.Kill(true); throw; }
        var log = await stdout + "\n" + await stderr;
        await File.WriteAllTextAsync(Path.Combine(output, "chrome.log"), log);
        Assert.True(process.ExitCode == 0, log);
        await using var verify = Db();
        var persisted = await verify.StockCountDocuments.Include(x => x.Lines).SingleAsync(x => x.Id == stale.Id);
        Assert.Equal(stale.Destination, persisted.WarehouseId);
        Assert.Equal(12, Assert.Single(persisted.Lines).DifferenceQtyBase);
    }

    private async Task<Count> CreateAsync(bool withLine)
    {
        await using var db = Db();
        var original = await db.Warehouses.SingleAsync(x => x.Id == fixture.Store.WarehouseId);
        Warehouse Warehouse(string suffix) => new() { StoreId = fixture.Store.StoreId, LegalEntityId = original.LegalEntityId,
            Code = "B6-" + suffix + "-" + Guid.NewGuid().ToString("N")[..8],
            Name = "B6 owned " + suffix + " " + Guid.NewGuid().ToString("N")[..8], IsActive = true };
        var source = Warehouse("source"); var destination = Warehouse("target");
        db.AddRange(source, destination); await db.SaveChangesAsync();
        await InventoryPosPostingContractTests.CreateRealMovementService(db).CreateAsync(new InventoryMovementFactory()
            .CreatePurchaseReceipt(source.Id, fixture.Store.VariantId, 100, 10, Guid.NewGuid().ToString(), 1, "B6 owned opening", 1));
        var created = await fixture.Admin.JsonAsync(HttpMethod.Post, Api, new
        { warehouseId = source.Id, documentDate = DateTime.Today, note = "B6 original" });
        var id = created.GetProperty("id").GetInt32();
        int? lineId = withLine ? (await fixture.Editor.JsonAsync(HttpMethod.Post, $"{Api}/{id}/lines", AddBody(110))).GetProperty("id").GetInt32() : null;
        return new(id, source.Id, destination.Id, lineId);
    }
    private AddStockCountLineRequest AddBody(decimal count) => new() { ProductVariantId = fixture.Store.VariantId, UnitId = fixture.UnitId, CountedQty = count };
    private Task AddAsync(int id, decimal count) => fixture.Editor.JsonAsync(HttpMethod.Post, $"{Api}/{id}/lines", AddBody(count));
    private static UpdateStockCountDocumentHeaderRequest Header(int id, int warehouse) => new()
    { StockCountDocumentId = id, WarehouseId = warehouse, DocumentDate = DateTime.Today.AddDays(-1), Note = "B6 changed header" };
    private Task<HttpResponseMessage> ChangeAsync(Count count, int warehouse) => fixture.Editor.Http.PostAsJsonAsync(Api + "/update-header", Header(count.Id, warehouse));
    private StockCountService Service(AppDbContext db, IInventoryUnitResolver? resolver = null) => new(
        new StockCountRepository(db), resolver ?? new InventoryUnitResolver(new StockDocumentRepository(db)),
        InventoryPosPostingContractTests.CreateRealMovementService(db), new InventoryMovementFactory());
    private async Task RejectAsync(int id)
    {
        await fixture.Editor.JsonAsync(HttpMethod.Post, $"{Api}/{id}/submit-approval");
        await fixture.Approver.JsonAsync(HttpMethod.Post, $"{Api}/{id}/reject");
    }
    private async Task ConfirmAsync(int id)
    {
        await fixture.Editor.JsonAsync(HttpMethod.Post, $"{Api}/{id}/submit-approval");
        await fixture.Approver.JsonAsync(HttpMethod.Post, $"{Api}/{id}/confirm");
    }
    private static Task<List<InventoryTransaction>> LedgerAsync(AppDbContext db, int id) => db.InventoryTransactions.AsNoTracking()
        .Where(x => x.ReferenceType == InventoryReferenceType.StockCount && x.ReferenceId == id.ToString()).ToListAsync();
    private async Task AssertPostingAsync(Count count, int warehouseId, decimal difference, decimal sourceQty, decimal destinationQty)
    {
        await using var db = Db();
        var doc = await db.StockCountDocuments.AsNoTracking().Include(x => x.Lines).SingleAsync(x => x.Id == count.Id);
        Assert.Equal(StockCountDocumentStatus.Confirmed, doc.Status); Assert.Equal(warehouseId, doc.WarehouseId);
        var line = Assert.Single(doc.Lines);
        Assert.Equal(warehouseId == count.Source ? 100m : 0m, line.SystemQtyBase);
        Assert.Equal(difference, line.DifferenceQtyBase);
        var entry = Assert.Single(await LedgerAsync(db, count.Id));
        Assert.Equal(warehouseId, entry.WarehouseId); Assert.Equal(line.Id, entry.ReferenceLineId);
        Assert.Equal(difference, entry.QuantityChange); Assert.Equal(difference * 10, entry.TotalCost);
        async Task<decimal> Quantity(int warehouse) => await db.InventoryBalances.AsNoTracking()
            .Where(x => x.WarehouseId == warehouse && x.ProductVariantId == fixture.Store.VariantId).Select(x => (decimal?)x.OnHandQty).SingleOrDefaultAsync() ?? 0;
        Assert.Equal(sourceQty, await Quantity(count.Source)); Assert.Equal(destinationQty, await Quantity(count.Destination));
    }
    private async Task WaitForBlockedAsync(AppDbContext blocker, Task request)
    {
        var session = ((SqlConnection)blocker.Database.GetDbConnection()).ServerProcessId;
        var clock = Stopwatch.StartNew();
        await using var monitor = Db();
        while (clock.Elapsed < TimeSpan.FromSeconds(10))
        {
            if (await monitor.Database.SqlQuery<int>($"SELECT CAST([session_id] AS int) AS [Value] FROM sys.dm_exec_requests WHERE [blocking_session_id]={session} AND [database_id]=DB_ID()").AnyAsync()) return;
            Assert.False(request.IsCompleted, "Competing operation did not wait for the stock-count document lock.");
            await Task.Delay(50);
        }
        throw new TimeoutException("Expected SQL lock wait was not observed.");
    }
    private sealed class PausingResolver(IInventoryUnitResolver inner) : IInventoryUnitResolver
    {
        internal readonly TaskCompletionSource Reached = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Resume = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<(int UnitId, string? UnitName, decimal Factor)> ResolveAsync(int variant, int? unit, CancellationToken ct = default)
        { Reached.TrySetResult(); await Resume.Task.WaitAsync(ct); return await inner.ResolveAsync(variant, unit, ct); }
    }
    private sealed class PausingWrite(string sql) : DbCommandInterceptor
    {
        private int paused;
        internal readonly TaskCompletionSource Reached = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Resume = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains(sql, StringComparison.Ordinal) && Interlocked.Exchange(ref paused, 1) == 0)
            { Reached.TrySetResult(); await Resume.Task.WaitAsync(cancellationToken); }
            return result;
        }
    }
}
