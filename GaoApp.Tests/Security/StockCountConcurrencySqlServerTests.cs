using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GaoApp.Application.Common.Security;
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

namespace GaoApp.Tests.Security;

// Actual HTTP handlers, SQL blocking chains and inventory posting. Fault injection
// below delegates the first movement to the production service before failing.
[Collection("R1FinalDatabasePreflight")]
public sealed class StockCountConcurrencySqlServerTests(StockCountConcurrencyFixture fixture)
    : IClassFixture<StockCountConcurrencyFixture>
{
    private FullApplicationFixture App => fixture.App;
    private AppDbContext Db() => App.Database.CreateTenantContext(fixture.Store.StoreId);
    private const string Api = "/admin/api/stock-counts";

    [Theory]
    [InlineData("add")]
    [InlineData("update")]
    [InlineData("delete")]
    [InlineData("header")]
    [InlineData("refresh")]
    [InlineData("submit")]
    [InlineData("reject")]
    [InlineData("confirm")]
    public async Task Confirmation_wins_and_all_competing_mutations_recheck_committed_status(string action)
    {
        var count = await CreateAsync();
        await using var blocker = Db();
        await using var transaction = await blocker.Database.BeginTransactionAsync();
        await BlockVariantAsync(blocker, fixture.Store.VariantId);
        var confirming = fixture.Approver.Http.PostAsync($"{Api}/{count.Id}/confirm", null);
        Task<HttpResponseMessage>? competing = null;
        try
        {
            var confirmingSession = await WaitForBlockedAsync(Session(blocker), confirming);
            competing = MutateAsync(action, count);
            await WaitForBlockedAsync(confirmingSession, competing);
        }
        finally { await transaction.RollbackAsync(); }
        using var confirmed = await confirming.WaitAsync(TimeSpan.FromSeconds(25));
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        using var edited = await competing!.WaitAsync(TimeSpan.FromSeconds(25));
        Assert.Equal(HttpStatusCode.BadRequest, edited.StatusCode);
        var error = await edited.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("Chỉ phiếu", error.GetProperty("message").GetString());
        await AssertConfirmedLedgerAsync(count.Id, 1);
        using var replay = await fixture.Approver.Http.PostAsync($"{Api}/{count.Id}/confirm", null);
        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
        await AssertConfirmedLedgerAsync(count.Id, 1);
    }

    [Fact]
    public async Task Original_late_add_now_blocks_submission_and_confirmation_posts_the_fresh_difference()
    {
        var count = await CreateAsync(pending: false, difference: 0);
        await using var editing = Db();
        var gate = new PausingResolver(new InventoryUnitResolver(new StockDocumentRepository(editing)));
        var service = new StockCountService(new StockCountRepository(editing), gate,
            InventoryPosPostingContractTests.CreateRealMovementService(editing), new InventoryMovementFactory());
        var adding = service.AddLineAsync(count.Id, new()
        { ProductVariantId = fixture.Store.VariantId, UnitId = fixture.UnitId, CountedQty = 5 });
        Task<HttpResponseMessage>? submitting = null;
        try
        {
            await gate.Reached.Task.WaitAsync(TimeSpan.FromSeconds(10));
            submitting = MutateAsync("submit", count);
            await WaitForBlockedAsync(Session(editing), submitting);
        }
        finally { gate.Resume.TrySetResult(); }
        Assert.Equal(count.LineId, await adding);
        using var submitted = await submitting!;
        Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);
        await fixture.Approver.JsonAsync(HttpMethod.Post, $"{Api}/{count.Id}/confirm");
        await AssertConfirmedLedgerAsync(count.Id, 1);
        await using var verify = Db();
        Assert.Equal(5m, (await verify.StockCountLines.SingleAsync(x => x.Id == count.LineId)).DifferenceQtyBase);
    }

    [Theory]
    [InlineData("update")]
    [InlineData("delete")]
    [InlineData("header")]
    [InlineData("refresh")]
    [InlineData("reject")]
    public async Task Changes_committed_before_submission_are_preserved_in_confirmation(string action)
    {
        var count = await CreateAsync(pending: false);
        await AddSecondLineAsync(count.Id);
        if (action == "reject") await SubmitAsync(count.Id);
        if (action == "refresh") await ReceiveAsync(fixture.Store.VariantId, 4);
        using var changed = await MutateAsync(action, count);
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        await SubmitAsync(count.Id);
        await fixture.Approver.JsonAsync(HttpMethod.Post, $"{Api}/{count.Id}/confirm");
        await AssertConfirmedLedgerAsync(count.Id, action == "delete" ? 1 : 2);
        await using var verify = Db();
        var doc = await verify.StockCountDocuments.Include(x => x.Lines).SingleAsync(x => x.Id == count.Id);
        if (action == "update") Assert.Equal(count.InitialSystem + 3, doc.Lines.Single(x => x.Id == count.LineId).CountedQty);
        if (action == "header") Assert.Equal("Changed before confirmation", doc.Note);
        if (action == "refresh") Assert.Equal(-2, doc.Lines.Single(x => x.Id == count.LineId).DifferenceQtyBase);
        if (action == "delete") Assert.True(await verify.StockCountLines.IgnoreQueryFilters()
            .Where(x => x.StoreId == fixture.Store.StoreId && x.Id == count.LineId).Select(x => x.IsDeleted).SingleAsync());
    }

    [Fact]
    public async Task Concurrent_scans_merge_all_quantities_and_deleted_line_numbers_are_not_reused()
    {
        var count = await CreateAsync(pending: false);
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => fixture.Editor.Http.PostAsJsonAsync(
            $"{Api}/{count.Id}/lines", new { productVariantId = fixture.SecondVariantId, unitId = fixture.UnitId, countedQty = 1 })));
        foreach (var result in results) { using (result) Assert.Equal(HttpStatusCode.OK, result.StatusCode); }
        int secondId;
        int oldLineNo;
        await using (var verify = Db())
        {
            var second = await verify.StockCountLines.SingleAsync(x => x.StockCountDocumentId == count.Id && x.ProductVariantId == fixture.SecondVariantId);
            Assert.Equal(8m, second.CountedQty);
            secondId = second.Id; oldLineNo = second.LineNo;
        }
        await fixture.Editor.JsonAsync(HttpMethod.Delete, $"{Api}/lines/{secondId}");
        await AddSecondLineAsync(count.Id);
        await using (var verify = Db())
        {
            var second = await verify.StockCountLines.SingleAsync(x => x.StockCountDocumentId == count.Id && x.ProductVariantId == fixture.SecondVariantId);
            Assert.True(second.LineNo > oldLineNo);
        }
        await SubmitAsync(count.Id);
        await fixture.Approver.JsonAsync(HttpMethod.Post, $"{Api}/{count.Id}/confirm");
        await AssertConfirmedLedgerAsync(count.Id, 2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failure_or_cancellation_after_first_real_movement_rolls_back_every_write_and_retry_succeeds(bool cancel)
    {
        var count = await CreateAsync(pending: false);
        await AddSecondLineAsync(count.Id);
        await SubmitAsync(count.Id);
        await using var before = Db();
        var balances = await before.InventoryBalances.AsNoTracking().OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.OnHandQty, x.InventoryValue }).ToArrayAsync();
        var layers = await before.InventoryCostLayers.CountAsync();
        var valuations = await before.InventoryValuationEntries.CountAsync();
        await using (var posting = Db())
        {
            using var cancellation = new CancellationTokenSource();
            var fault = new FailSecondMovement(InventoryPosPostingContractTests.CreateRealMovementService(posting), cancel ? cancellation : null);
            var service = new StockCountService(new StockCountRepository(posting), null!, fault, new InventoryMovementFactory());
            if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ConfirmAsync(count.Id, cancellation.Token));
            else await Assert.ThrowsAsync<InvalidOperationException>(() => service.ConfirmAsync(count.Id));
            Assert.Equal(1, fault.Completed);
            Assert.Null(posting.Database.CurrentTransaction);
        }
        await using (var verify = Db())
        {
            var doc = await verify.StockCountDocuments.Include(x => x.Lines).SingleAsync(x => x.Id == count.Id);
            Assert.Equal(StockCountDocumentStatus.PendingApproval, doc.Status);
            Assert.Null(doc.ConfirmedAtUtc);
            Assert.All(doc.Lines, x => Assert.Equal(0, x.UnitCostSnapshot));
            Assert.Empty(await verify.InventoryTransactions.Where(x => x.ReferenceType == InventoryReferenceType.StockCount && x.ReferenceId == count.Id.ToString()).ToListAsync());
            Assert.Equal(balances, await verify.InventoryBalances.AsNoTracking().OrderBy(x => x.Id)
                .Select(x => new { x.Id, x.OnHandQty, x.InventoryValue }).ToArrayAsync());
            Assert.Equal(layers, await verify.InventoryCostLayers.CountAsync());
            Assert.Equal(valuations, await verify.InventoryValuationEntries.CountAsync());
        }
        await fixture.Approver.JsonAsync(HttpMethod.Post, $"{Api}/{count.Id}/confirm");
        await AssertConfirmedLedgerAsync(count.Id, 2);
    }

    [Theory]
    [InlineData("add")]
    [InlineData("update")]
    [InlineData("delete")]
    [InlineData("submit")]
    [InlineData("refresh")]
    [InlineData("pending-header")]
    [InlineData("pending-line")]
    public async Task Stale_or_unsaved_tracked_graph_is_rejected_without_discarding_pending_changes(string kind)
    {
        var count = await CreateAsync(pending: false);
        await using var stale = Db();
        var old = await stale.StockCountDocuments.Include(x => x.Lines).SingleAsync(x => x.Id == count.Id);
        var version = old.RowVersion.ToArray();
        if (kind == "pending-header") old.Note = "Unsaved header";
        else if (kind == "pending-line") old.Lines.Single().Note = "Unsaved line";
        else
        {
            if (kind == "refresh") await ReceiveAsync(fixture.Store.VariantId, 4);
            using var changed = await MutateAsync(kind, count);
            Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
            await using var fresh = Db();
            var current = await fresh.StockCountDocuments.SingleAsync(x => x.Id == count.Id);
            Assert.False(version.SequenceEqual(current.RowVersion));
        }
        var service = new StockCountService(new StockCountRepository(stale), null!, null!, null!);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => service.RefreshSystemQtyAsync(count.Id));
        Assert.Null(stale.Database.CurrentTransaction);
        if (kind == "pending-header") Assert.Equal("Unsaved header", old.Note);
        if (kind == "pending-line") Assert.Equal("Unsaved line", old.Lines.Single().Note);
        await using var verify = Db();
        Assert.Empty(await verify.InventoryTransactions.Where(x => x.ReferenceType == InventoryReferenceType.StockCount && x.ReferenceId == count.Id.ToString()).ToListAsync());
    }

    [Fact]
    public async Task Reusing_a_fresh_context_reloads_graph_and_advances_version_for_each_child_change()
    {
        var count = await CreateAsync(pending: false);
        await using var db = Db();
        var service = new StockCountService(new StockCountRepository(db),
            new InventoryUnitResolver(new StockDocumentRepository(db)),
            InventoryPosPostingContractTests.CreateRealMovementService(db), new InventoryMovementFactory());
        await service.GetDetailAsync(count.Id);
        var old = (await db.StockCountDocuments.SingleAsync(x => x.Id == count.Id)).RowVersion.ToArray();
        await service.UpdateLineAsync(count.LineId, new() { UnitId = fixture.UnitId, CountedQty = count.InitialSystem - 1 });
        var edited = (await db.StockCountDocuments.SingleAsync(x => x.Id == count.Id)).RowVersion.ToArray();
        Assert.False(old.SequenceEqual(edited));
        await service.SubmitForApprovalAsync(count.Id);
        await service.ConfirmAsync(count.Id);
        Assert.Null(db.Database.CurrentTransaction);
        await AssertConfirmedLedgerAsync(count.Id, 1);
    }

    [Fact]
    public async Task Zero_difference_confirmation_is_final_and_replay_posts_nothing()
    {
        var count = await CreateAsync(difference: 0);
        await fixture.Approver.JsonAsync(HttpMethod.Post, $"{Api}/{count.Id}/confirm");
        using var replay = await fixture.Approver.Http.PostAsync($"{Api}/{count.Id}/confirm", null);
        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
        await AssertConfirmedLedgerAsync(count.Id, 0);
    }

    [Fact]
    public async Task Cancellation_while_waiting_releases_owned_transaction_and_allows_a_new_request()
    {
        var count = await CreateAsync(pending: false);
        await using var blocker = Db();
        await using var transaction = await blocker.Database.BeginTransactionAsync();
        await blocker.Database.ExecuteSqlInterpolatedAsync($"SELECT [Id] FROM [StockCountDocument] WITH (XLOCK,HOLDLOCK,ROWLOCK) WHERE [StoreId]={fixture.Store.StoreId} AND [Id]={count.Id}");
        await using var editing = Db();
        var service = new StockCountService(new StockCountRepository(editing), null!, null!, null!);
        using var cancellation = new CancellationTokenSource();
        var saving = service.UpdateHeaderAsync(HeaderBody(count.Id), cancellation.Token);
        try
        {
            await WaitForBlockedAsync(Session(blocker), saving);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => saving);
            Assert.Null(editing.Database.CurrentTransaction);
        }
        finally { await transaction.RollbackAsync(); }
        await using (var verify = Db())
            Assert.Equal("B5 concurrency test", (await verify.StockCountDocuments.SingleAsync(x => x.Id == count.Id)).Note);
        using var retry = await MutateAsync("header", count);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        await SubmitAsync(count.Id);
        await fixture.Approver.JsonAsync(HttpMethod.Post, $"{Api}/{count.Id}/confirm");
        await AssertConfirmedLedgerAsync(count.Id, 1);
    }

    [Fact]
    public async Task Locking_one_document_allows_another_document_and_rejects_ownership_of_an_outer_transaction()
    {
        var first = await CreateAsync(pending: false);
        var second = await CreateAsync(pending: false);
        await using var db = Db();
        var repo = new StockCountRepository(db);
        await repo.BeginTransactionAsync();
        try
        {
            await repo.LockDocumentForUpdateAsync(first.Id);
            using var changed = await MutateAsync("header", second).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
            await Assert.ThrowsAsync<InvalidOperationException>(() => repo.BeginTransactionAsync());
        }
        finally { await repo.RollbackTransactionAsync(); }
        await using var outer = await db.Database.BeginTransactionAsync();
        var service = new StockCountService(new StockCountRepository(db), null!, null!, null!);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateHeaderAsync(HeaderBody(first.Id)));
        Assert.Same(outer, db.Database.CurrentTransaction);
        await outer.RollbackAsync();
    }

    [Theory]
    [InlineData("foreign-document")]
    [InlineData("foreign-line")]
    [InlineData("no-permission")]
    [InlineData("editor-confirm")]
    [InlineData("approver-edit")]
    [InlineData("csrf")]
    [InlineData("invalid-unit")]
    [InlineData("invalid-update-unit")]
    [InlineData("missing-line")]
    public async Task Denied_requests_preserve_document_and_inventory(string kind)
    {
        var count = await CreateAsync(pending: false);
        var add = new { productVariantId = fixture.SecondVariantId, unitId = fixture.UnitId, countedQty = 1 };
        HttpResponseMessage response;
        if (kind == "foreign-document") response = await fixture.Foreign.Http.PostAsJsonAsync($"{Api}/{count.Id}/lines", add);
        else if (kind == "foreign-line") response = await fixture.Foreign.Http.DeleteAsync($"{Api}/lines/{count.LineId}");
        else if (kind == "no-permission") response = await fixture.Reader.Http.PostAsJsonAsync($"{Api}/{count.Id}/lines", add);
        else if (kind == "editor-confirm") response = await fixture.Editor.Http.PostAsync($"{Api}/{count.Id}/confirm", null);
        else if (kind == "approver-edit") response = await fixture.Approver.Http.PostAsJsonAsync($"{Api}/{count.Id}/lines", add);
        else if (kind == "invalid-unit") response = await fixture.Editor.Http.PostAsJsonAsync($"{Api}/{count.Id}/lines",
            new { productVariantId = fixture.SecondVariantId, unitId = int.MaxValue, countedQty = 1 });
        else if (kind == "invalid-update-unit") response = await fixture.Editor.Http.PutAsJsonAsync($"{Api}/lines/{count.LineId}",
            new { unitId = int.MaxValue, countedQty = 1 });
        else if (kind == "missing-line") response = await fixture.Editor.Http.DeleteAsync($"{Api}/lines/{int.MaxValue}");
        else
        {
            using var noCsrf = new HttpClient(new HttpClientHandler { CookieContainer = fixture.Editor.Cookies }) { BaseAddress = App.Address };
            noCsrf.DefaultRequestHeaders.Host = fixture.Store.Host;
            response = await noCsrf.PostAsJsonAsync($"{Api}/{count.Id}/lines", add);
        }
        using (response)
        {
            Assert.False(response.IsSuccessStatusCode);
            Assert.NotEqual(HttpStatusCode.InternalServerError, response.StatusCode);
            if (kind is "no-permission" or "editor-confirm" or "approver-edit") Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            if (kind is "invalid-unit" or "invalid-update-unit") Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        await using (var verify = Db())
        {
            var doc = await verify.StockCountDocuments.Include(x => x.Lines).SingleAsync(x => x.Id == count.Id);
            Assert.Equal(StockCountDocumentStatus.Draft, doc.Status);
            Assert.Single(doc.Lines);
            Assert.Equal(count.InitialSystem + 2, doc.Lines.Single().CountedQty);
            Assert.Empty(await verify.InventoryTransactions.Where(x => x.ReferenceType == InventoryReferenceType.StockCount && x.ReferenceId == count.Id.ToString()).ToListAsync());
        }
        await SubmitAsync(count.Id);
        await fixture.Approver.JsonAsync(HttpMethod.Post, $"{Api}/{count.Id}/confirm");
        await AssertConfirmedLedgerAsync(count.Id, 1);
    }

    private sealed record Count(int Id, int LineId, decimal InitialSystem);
    private async Task<Count> CreateAsync(bool pending = true, decimal difference = 2)
    {
        var system = await QuantityAsync(fixture.Store.VariantId);
        var created = await fixture.Admin.JsonAsync(HttpMethod.Post, Api, new
        { warehouseId = fixture.Store.WarehouseId, documentDate = DateTime.Today, note = "B5 concurrency test" });
        var id = created.GetProperty("id").GetInt32();
        var line = await fixture.Editor.JsonAsync(HttpMethod.Post, $"{Api}/{id}/lines", new
        { productVariantId = fixture.Store.VariantId, unitId = fixture.UnitId, countedQty = system + difference });
        if (pending) await SubmitAsync(id);
        return new(id, line.GetProperty("id").GetInt32(), system);
    }
    private Task SubmitAsync(int id) => fixture.Editor.JsonAsync(HttpMethod.Post, $"{Api}/{id}/submit-approval");
    private async Task<decimal> QuantityAsync(int variantId)
    {
        await using var db = Db();
        return await db.InventoryBalances.Where(x => x.WarehouseId == fixture.Store.WarehouseId && x.ProductVariantId == variantId)
            .Select(x => x.OnHandQty).SingleAsync();
    }
    private async Task AddSecondLineAsync(int id)
    {
        await fixture.Editor.JsonAsync(HttpMethod.Post, $"{Api}/{id}/lines", new
        { productVariantId = fixture.SecondVariantId, unitId = fixture.UnitId, countedQty = await QuantityAsync(fixture.SecondVariantId) + 3 });
    }
    private async Task ReceiveAsync(int variantId, decimal quantity)
    {
        await using var db = Db();
        await InventoryPosPostingContractTests.CreateRealMovementService(db).CreateAsync(new InventoryMovementFactory()
            .CreatePurchaseReceipt(fixture.Store.WarehouseId, variantId, quantity, 10, Guid.NewGuid().ToString(), 1, "B5 owned stock", 1));
    }
    private UpdateStockCountDocumentHeaderRequest HeaderBody(int id) => new()
    { StockCountDocumentId = id, WarehouseId = fixture.Store.WarehouseId, DocumentDate = DateTime.Today, Note = "Changed before confirmation" };
    private Task<HttpResponseMessage> MutateAsync(string action, Count count) => action switch
    {
        "add" => fixture.Editor.Http.PostAsJsonAsync($"{Api}/{count.Id}/lines",
            new { productVariantId = fixture.Store.VariantId, unitId = fixture.UnitId, countedQty = 1 }),
        "update" => fixture.Editor.Http.PutAsJsonAsync($"{Api}/lines/{count.LineId}", new { unitId = fixture.UnitId, countedQty = count.InitialSystem + 3 }),
        "delete" => fixture.Editor.Http.DeleteAsync($"{Api}/lines/{count.LineId}"),
        "header" => fixture.Editor.Http.PostAsJsonAsync($"{Api}/update-header", HeaderBody(count.Id)),
        "refresh" => fixture.Editor.Http.PostAsync($"{Api}/{count.Id}/refresh-system-qty", null),
        "submit" => fixture.Editor.Http.PostAsync($"{Api}/{count.Id}/submit-approval", null),
        "reject" => fixture.Approver.Http.PostAsync($"{Api}/{count.Id}/reject", null),
        "confirm" => fixture.Approver.Http.PostAsync($"{Api}/{count.Id}/confirm", null),
        _ => throw new ArgumentOutOfRangeException(nameof(action))
    };
    private async Task<int> WaitForBlockedAsync(int blocker, Task operation)
    {
        var clock = Stopwatch.StartNew();
        await using var monitor = Db();
        while (clock.Elapsed < TimeSpan.FromSeconds(12))
        {
            var sessions = await monitor.Database.SqlQuery<int>($"SELECT CAST([session_id] AS int) AS [Value] FROM sys.dm_exec_requests WHERE [blocking_session_id]={blocker} AND [database_id]=DB_ID()").ToListAsync();
            if (sessions.Count > 0) return sessions[0];
            Assert.False(operation.IsCompleted, "Operation completed without waiting for the expected SQL lock.");
            await Task.Delay(50);
        }
        throw new TimeoutException("Expected SQL blocking chain was not observed.");
    }
    private Task BlockVariantAsync(AppDbContext db, int id)
        => db.Database.ExecuteSqlInterpolatedAsync($"SELECT [Id] FROM [ProductVariant] WITH (XLOCK,HOLDLOCK,ROWLOCK) WHERE [StoreId]={fixture.Store.StoreId} AND [Id]={id}");
    private static int Session(AppDbContext db) => ((SqlConnection)db.Database.GetDbConnection()).ServerProcessId;
    private async Task AssertConfirmedLedgerAsync(int id, int expectedMovements)
    {
        await using var db = Db();
        var doc = await db.StockCountDocuments.AsNoTracking().Include(x => x.Lines).SingleAsync(x => x.Id == id);
        Assert.Equal(StockCountDocumentStatus.Confirmed, doc.Status);
        Assert.NotNull(doc.ConfirmedAtUtc);
        var ledger = await db.InventoryTransactions.AsNoTracking()
            .Where(x => x.ReferenceType == InventoryReferenceType.StockCount && x.ReferenceId == id.ToString()).ToListAsync();
        Assert.Equal(expectedMovements, ledger.Count);
        foreach (var line in doc.Lines)
        {
            var movements = ledger.Where(x => x.ReferenceLineId == line.Id).ToArray();
            if (line.DifferenceQtyBase == 0) Assert.Empty(movements);
            else
            {
                var movement = Assert.Single(movements);
                Assert.Equal(line.DifferenceQtyBase, movement.QuantityChange);
                Assert.Equal(doc.WarehouseId, movement.WarehouseId);
                Assert.Equal(line.ProductVariantId, movement.ProductVariantId);
                Assert.Equal(line.DifferenceQtyBase > 0 ? InventoryTransactionType.StockCountGain : InventoryTransactionType.StockCountLoss, movement.TransactionType);
                Assert.Equal(line.DifferenceQtyBase * 10, movement.TotalCost);
            }
            Assert.Equal(line.CountedQtyBase, await QuantityAsync(line.ProductVariantId));
        }
    }
    private sealed class FailSecondMovement(IInventoryMovementService inner, CancellationTokenSource? cancellation) : IInventoryMovementService
    {
        public int Completed { get; private set; }
        public Task PreLockBalancesAsync(IEnumerable<InventoryPostingLockKey> keys, CancellationToken ct = default) => inner.PreLockBalancesAsync(keys, ct);
        public Task<decimal> PeekOutboundUnitCostAsync(int warehouseId, int variantId, decimal quantity, CancellationToken ct = default)
            => inner.PeekOutboundUnitCostAsync(warehouseId, variantId, quantity, ct);
        public async Task<InventoryMovementResultDto> CreateAsync(CreateInventoryMovementRequest request, CancellationToken ct = default)
        {
            if (Completed == 1)
            {
                if (cancellation is not null) { cancellation.Cancel(); throw new OperationCanceledException(ct); }
                throw new InvalidOperationException("B5 injected second movement failure");
            }
            var result = await inner.CreateAsync(request, ct);
            Completed++;
            return result;
        }
    }

    private sealed class PausingResolver(IInventoryUnitResolver inner) : IInventoryUnitResolver
    {
        internal readonly TaskCompletionSource Reached = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Resume = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<(int UnitId, string? UnitName, decimal Factor)> ResolveAsync(int productVariantId, int? unitId, CancellationToken ct = default)
        {
            Reached.TrySetResult();
            await Resume.Task.WaitAsync(ct);
            return await inner.ResolveAsync(productVariantId, unitId, ct);
        }
    }
}

public sealed class StockCountConcurrencyFixture : IAsyncLifetime
{
    internal FullApplicationFixture App { get; private set; } = null!;
    internal FullApplicationFixture.StoreSeed Store => App.Stores[0];
    internal FullApplicationFixture.Client Admin { get; private set; } = null!;
    internal FullApplicationFixture.Client Editor { get; private set; } = null!;
    internal FullApplicationFixture.Client Approver { get; private set; } = null!;
    internal FullApplicationFixture.Client Reader { get; private set; } = null!;
    internal FullApplicationFixture.Client Foreign { get; private set; } = null!;
    internal int UnitId { get; private set; }
    internal int SecondVariantId { get; private set; }
    public async Task InitializeAsync()
    {
        App = await FullApplicationFixture.StartWithAuditPausedAsync();
        try
        {
            Admin = await App.LoginAsync(await App.AddAccountAsync(Store, "*"));
            Editor = await App.LoginAsync(await App.AddAccountAsync(Store, PermissionCodes.Inventory.StockCount.Update));
            Approver = await App.LoginAsync(await App.AddAccountAsync(Store, PermissionCodes.Inventory.StockCount.Approve));
            Reader = await App.LoginAsync(await App.AddAccountAsync(Store, PermissionCodes.Inventory.StockCount.View));
            Foreign = await App.LoginAsync(await App.AddAccountAsync(App.Stores[1], "*"));
            await using var db = App.Database.CreateTenantContext(Store.StoreId);
            var original = await db.ProductVariants.Include(x => x.Product).SingleAsync(x => x.Id == Store.VariantId);
            UnitId = original.Product.BaseUnitId;
            var second = new ProductVariant { StoreId = Store.StoreId, ProductId = original.ProductId,
                Sku = "B5-SECOND", ProductVariantName = "B5 owned second variant", IsActive = true, CostPrice = 10, Price = 20 };
            db.Add(second); await db.SaveChangesAsync(); SecondVariantId = second.Id;
            await InventoryPosPostingContractTests.CreateRealMovementService(db).CreateAsync(new InventoryMovementFactory()
                .CreatePurchaseReceipt(Store.WarehouseId, second.Id, 10000, 10, "B5-OPENING", 1, "B5 owned stock", 1));
        }
        catch { await DisposeAsync(); throw; }
    }
    public async Task DisposeAsync()
    {
        Admin?.Dispose(); Editor?.Dispose(); Approver?.Dispose(); Reader?.Dispose(); Foreign?.Dispose();
        if (App is not null) await App.DisposeAsync();
    }
}
