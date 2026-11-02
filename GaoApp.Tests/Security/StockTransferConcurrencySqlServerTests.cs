using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.Inventory;
using GaoApp.Application.Services.Inventory;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Repositories.Inventory;
using GaoApp.Tests.Inventory;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

// Real HTTP, real SQL row locks and the production posting service. No mocked
// business handler or delay injected into production code.
[Collection("R1FinalDatabasePreflight")]
public sealed class StockTransferConcurrencySqlServerTests(StockTransferConcurrencyFixture fixture)
    : IClassFixture<StockTransferConcurrencyFixture>
{
    private FullApplicationFixture App => fixture.App;
    private AppDbContext Db() => App.Database.CreateTenantContext(fixture.Store.StoreId);
    private const string Api = "/admin/api/stock-transfers";

    [Theory]
    [InlineData("add")]
    [InlineData("update")]
    [InlineData("delete")]
    [InlineData("header")]
    [InlineData("submit")]
    [InlineData("reject")]
    [InlineData("confirm")]
    public async Task Confirm_wins_and_every_competing_mutation_rechecks_committed_status(string action)
    {
        var transfer = await CreateAsync();
        await using var blocker = Db();
        await using var transaction = await blocker.Database.BeginTransactionAsync();
        await BlockVariantAsync(blocker, fixture.Store.VariantId);
        var confirming = fixture.Confirmer.Http.PostAsync($"{Api}/{transfer.Id}/confirm", null);
        Task<HttpResponseMessage>? editing = null;
        try
        {
            var confirmingSession = await WaitForBlockedAsync(Session(blocker), confirming);
            editing = MutateAsync(action, transfer);
            await WaitForBlockedAsync(confirmingSession, editing);
        }
        finally { await transaction.RollbackAsync(); }

        using var confirmed = await confirming.WaitAsync(TimeSpan.FromSeconds(25));
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        using var edited = await editing!.WaitAsync(TimeSpan.FromSeconds(25));
        Assert.Equal(HttpStatusCode.BadRequest, edited.StatusCode);
        Assert.Contains(action is "confirm" or "reject" or "submit" ? "Chỉ phiếu" : "đã xác nhận",
            await edited.Content.ReadAsStringAsync());
        await AssertConfirmedLedgerAsync(transfer.Id, expectedLines: 1);

        // Simulate a lost response followed by a replay on a new HTTP request.
        using var replay = await fixture.Confirmer.Http.PostAsync($"{Api}/{transfer.Id}/confirm", null);
        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
        await AssertConfirmedLedgerAsync(transfer.Id, expectedLines: 1);
    }

    [Fact]
    public async Task Original_late_add_reproduction_now_waits_and_posts_both_lines()
    {
        var transfer = await CreateAsync();
        await using var blocker = Db();
        await using var transaction = await blocker.Database.BeginTransactionAsync();
        await BlockVariantAsync(blocker, fixture.SecondVariantId);
        var adding = MutateAsync("add", transfer);
        Task<HttpResponseMessage>? confirming = null;
        try
        {
            var editingSession = await WaitForBlockedAsync(Session(blocker), adding);
            confirming = fixture.Confirmer.Http.PostAsync($"{Api}/{transfer.Id}/confirm", null);
            await WaitForBlockedAsync(editingSession, confirming);
        }
        finally { await transaction.RollbackAsync(); }
        using var added = await adding;
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);
        using var confirmed = await confirming!;
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        await AssertConfirmedLedgerAsync(transfer.Id, expectedLines: 2);
    }

    [Theory]
    [InlineData("update")]
    [InlineData("delete")]
    [InlineData("header")]
    [InlineData("reject")]
    public async Task Edit_committed_first_is_used_by_confirmation(string action)
    {
        var transfer = await CreateAsync();
        // Keep another line so deletion still leaves a valid document to post.
        await fixture.Admin.JsonAsync(HttpMethod.Post, $"{Api}/{transfer.Id}/lines", AddBody());
        using var edited = await MutateAsync(action, transfer);
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        if (action == "reject")
        {
            using var denied = await fixture.Confirmer.Http.PostAsync($"{Api}/{transfer.Id}/confirm", null);
            Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
            await fixture.Editor.JsonAsync(HttpMethod.Post, $"{Api}/{transfer.Id}/submit");
        }
        await fixture.Confirmer.JsonAsync(HttpMethod.Post, $"{Api}/{transfer.Id}/confirm");
        await AssertConfirmedLedgerAsync(transfer.Id, action == "delete" ? 1 : 2);
        await using var db = Db();
        var doc = await db.StockTransferDocuments.AsNoTracking().Include(x => x.Lines).SingleAsync(x => x.Id == transfer.Id);
        if (action == "update") Assert.Equal(3m, doc.Lines.Single(x => x.Id == transfer.LineId).Quantity);
        if (action == "header") Assert.Equal("Changed before confirmation", doc.Note);
        if (action == "delete") Assert.True(await db.StockTransferLines.IgnoreQueryFilters()
            .Where(x => x.StoreId == fixture.Store.StoreId && x.Id == transfer.LineId).Select(x => x.IsDeleted).SingleAsync());
    }

    [Fact]
    public async Task Concurrent_adds_merge_without_losing_quantity_or_reusing_line_numbers()
    {
        var transfer = await CreateAsync();
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => MutateAsync("add", transfer)));
        foreach (var result in results) { using (result) Assert.Equal(HttpStatusCode.OK, result.StatusCode); }
        await using (var db = Db())
        {
            var line = await db.StockTransferLines.SingleAsync(x => x.StockTransferDocumentId == transfer.Id && x.ProductVariantId == fixture.SecondVariantId);
            Assert.Equal(8m, line.Quantity);
        }
        await fixture.Confirmer.JsonAsync(HttpMethod.Post, $"{Api}/{transfer.Id}/confirm");
        await AssertConfirmedLedgerAsync(transfer.Id, expectedLines: 2);
    }

    [Fact]
    public async Task Failure_after_first_line_posting_rolls_back_whole_document_and_allows_new_request()
    {
        var transfer = await CreateAsync();
        var added = await fixture.Admin.JsonAsync(HttpMethod.Post, $"{Api}/{transfer.Id}/lines",
            new { productVariantId = fixture.SecondVariantId, unitId = fixture.UnitId, quantity = 999999m });
        await using var before = Db();
        var quantities = await before.InventoryBalances.AsNoTracking().OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.OnHandQty, x.InventoryValue }).ToArrayAsync();
        using var failed = await fixture.Confirmer.Http.PostAsync($"{Api}/{transfer.Id}/confirm", null);
        Assert.Equal(HttpStatusCode.BadRequest, failed.StatusCode);
        await using (var verify = Db())
        {
            Assert.Equal(StockTransferDocumentStatus.PendingApproval,
                (await verify.StockTransferDocuments.SingleAsync(x => x.Id == transfer.Id)).Status);
            Assert.Empty(await verify.InventoryTransactions.Where(x => x.ReferenceType == InventoryReferenceType.StockTransfer && x.ReferenceId == transfer.Id.ToString()).ToListAsync());
            Assert.Equal(quantities, await verify.InventoryBalances.AsNoTracking().OrderBy(x => x.Id)
                .Select(x => new { x.Id, x.OnHandQty, x.InventoryValue }).ToArrayAsync());
        }
        using var removed = await fixture.Editor.Http.DeleteAsync($"{Api}/lines/{added.GetProperty("id").GetInt32()}");
        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        await fixture.Confirmer.JsonAsync(HttpMethod.Post, $"{Api}/{transfer.Id}/confirm");
        await AssertConfirmedLedgerAsync(transfer.Id, expectedLines: 1);
    }

    [Fact]
    public async Task Child_edit_advances_header_version_and_stale_tracked_graph_is_rejected()
    {
        var transfer = await CreateAsync();
        await using var stale = Db();
        var old = await stale.StockTransferDocuments.Include(x => x.Lines).SingleAsync(x => x.Id == transfer.Id);
        var oldVersion = old.RowVersion.ToArray();
        using var added = await MutateAsync("add", transfer);
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);
        await using (var fresh = Db())
        {
            var current = await fresh.StockTransferDocuments.SingleAsync(x => x.Id == transfer.Id);
            Assert.False(oldVersion.SequenceEqual(current.RowVersion));
        }
        var service = new StockTransferService(new StockTransferRepository(stale), null!, null!, null!);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => service.DeleteLineAsync(transfer.LineId));
        Assert.Null(stale.Database.CurrentTransaction);
        await fixture.Confirmer.JsonAsync(HttpMethod.Post, $"{Api}/{transfer.Id}/confirm");
        await AssertConfirmedLedgerAsync(transfer.Id, expectedLines: 2);
    }

    [Fact]
    public async Task Cancellation_while_waiting_releases_transaction_and_does_not_modify_document()
    {
        var transfer = await CreateAsync();
        await using var blocker = Db();
        await using var transaction = await blocker.Database.BeginTransactionAsync();
        await blocker.Database.ExecuteSqlInterpolatedAsync($"SELECT [Id] FROM [StockTransferDocument] WITH (XLOCK,HOLDLOCK,ROWLOCK) WHERE [StoreId]={fixture.Store.StoreId} AND [Id]={transfer.Id}");
        await using var editing = Db();
        var service = new StockTransferService(new StockTransferRepository(editing), null!, null!, null!);
        using var cancellation = new CancellationTokenSource();
        var saving = service.UpdateHeaderAsync(transfer.Id, HeaderBody(), cancellation.Token);
        try
        {
            await WaitForBlockedAsync(Session(blocker), saving);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => saving);
            Assert.Null(editing.Database.CurrentTransaction);
        }
        finally { await transaction.RollbackAsync(); }
        await using (var verify = Db())
            Assert.Equal("Concurrency test", (await verify.StockTransferDocuments.SingleAsync(x => x.Id == transfer.Id)).Note);
        await fixture.Confirmer.JsonAsync(HttpMethod.Post, $"{Api}/{transfer.Id}/confirm");
        await AssertConfirmedLedgerAsync(transfer.Id, expectedLines: 1);
    }

    [Fact]
    public async Task Lock_on_one_document_does_not_serialize_another_document()
    {
        var first = await CreateAsync();
        var second = await CreateAsync();
        await using var blockingDb = Db();
        var repo = new StockTransferRepository(blockingDb);
        await repo.BeginTransactionAsync();
        try
        {
            await repo.LockDocumentForUpdateAsync(first.Id);
            using var changed = await MutateAsync("header", second).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        }
        finally { await repo.RollbackTransactionAsync(); }
    }

    [Theory]
    [InlineData("foreign-document")]
    [InlineData("foreign-line")]
    [InlineData("no-permission")]
    [InlineData("csrf")]
    [InlineData("invalid-unit")]
    public async Task Denied_requests_preserve_document_and_inventory(string kind)
    {
        var transfer = await CreateAsync();
        HttpResponseMessage response;
        if (kind == "foreign-document") response = await fixture.Foreign.Http.PostAsJsonAsync($"{Api}/{transfer.Id}/lines", AddBody());
        else if (kind == "foreign-line") response = await fixture.Foreign.Http.DeleteAsync($"{Api}/lines/{transfer.LineId}");
        else if (kind == "no-permission") response = await fixture.Reader.Http.PostAsJsonAsync($"{Api}/{transfer.Id}/lines", AddBody());
        else if (kind == "invalid-unit") response = await fixture.Editor.Http.PostAsJsonAsync($"{Api}/{transfer.Id}/lines",
            new { productVariantId = fixture.SecondVariantId, unitId = int.MaxValue, quantity = 1 });
        else
        {
            using var noCsrf = new HttpClient(new HttpClientHandler { CookieContainer = fixture.Editor.Cookies }) { BaseAddress = App.Address };
            noCsrf.DefaultRequestHeaders.Host = fixture.Store.Host;
            response = await noCsrf.PostAsJsonAsync($"{Api}/{transfer.Id}/lines", AddBody());
        }
        using (response)
        {
            Assert.False(response.IsSuccessStatusCode);
            Assert.NotEqual(HttpStatusCode.InternalServerError, response.StatusCode);
            if (kind == "no-permission") Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        await using (var verify = Db())
        {
            Assert.Single(await verify.StockTransferLines.Where(x => x.StockTransferDocumentId == transfer.Id).ToListAsync());
            Assert.Empty(await verify.InventoryTransactions.Where(x => x.ReferenceType == InventoryReferenceType.StockTransfer && x.ReferenceId == transfer.Id.ToString()).ToListAsync());
        }
        await fixture.Confirmer.JsonAsync(HttpMethod.Post, $"{Api}/{transfer.Id}/confirm");
        await AssertConfirmedLedgerAsync(transfer.Id, expectedLines: 1);
    }

    private sealed record Transfer(int Id, int LineId);
    private async Task<Transfer> CreateAsync()
    {
        var doc = await fixture.Admin.JsonAsync(HttpMethod.Post, Api, new
        {
            documentDate = DateTime.Today, fromWarehouseId = fixture.Store.WarehouseId,
            toWarehouseId = fixture.DestinationId, note = "Concurrency test"
        });
        var id = doc.GetProperty("id").GetInt32();
        var line = await fixture.Admin.JsonAsync(HttpMethod.Post, $"{Api}/{id}/lines",
            new { productVariantId = fixture.Store.VariantId, unitId = fixture.UnitId, quantity = 1 });
        await fixture.Admin.JsonAsync(HttpMethod.Post, $"{Api}/{id}/submit");
        return new(id, line.GetProperty("id").GetInt32());
    }
    private object AddBody() => new { productVariantId = fixture.SecondVariantId, unitId = fixture.UnitId, quantity = 1 };
    private UpdateStockTransferHeaderRequest HeaderBody() => new()
    {
        DocumentDate = DateTime.Today, FromWarehouseId = fixture.Store.WarehouseId,
        ToWarehouseId = fixture.DestinationId, Note = "Changed before confirmation"
    };
    private Task<HttpResponseMessage> MutateAsync(string action, Transfer transfer) => action switch
    {
        "add" => fixture.Editor.Http.PostAsJsonAsync($"{Api}/{transfer.Id}/lines", AddBody()),
        "update" => fixture.Editor.Http.PutAsJsonAsync($"{Api}/lines/{transfer.LineId}", new { unitId = fixture.UnitId, quantity = 3 }),
        "delete" => fixture.Editor.Http.DeleteAsync($"{Api}/lines/{transfer.LineId}"),
        "header" => fixture.Editor.Http.PutAsJsonAsync($"{Api}/{transfer.Id}", HeaderBody()),
        "reject" => fixture.Editor.Http.PostAsJsonAsync($"{Api}/{transfer.Id}/reject", new { reason = "Race test" }),
        "submit" => fixture.Editor.Http.PostAsync($"{Api}/{transfer.Id}/submit", null),
        "confirm" => fixture.Admin.Http.PostAsync($"{Api}/{transfer.Id}/confirm", null),
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

    private async Task AssertConfirmedLedgerAsync(int id, int expectedLines)
    {
        await using var db = Db();
        var doc = await db.StockTransferDocuments.AsNoTracking().Include(x => x.Lines).SingleAsync(x => x.Id == id);
        Assert.Equal(StockTransferDocumentStatus.Confirmed, doc.Status);
        Assert.Equal(expectedLines, doc.Lines.Count);
        var ledger = await db.InventoryTransactions.AsNoTracking()
            .Where(x => x.ReferenceType == InventoryReferenceType.StockTransfer && x.ReferenceId == id.ToString()).ToListAsync();
        Assert.Equal(expectedLines * 2, ledger.Count);
        foreach (var line in doc.Lines)
        {
            var pair = ledger.Where(x => x.ReferenceLineId == line.Id).ToArray();
            var outbound = Assert.Single(pair, x => x.TransactionType == InventoryTransactionType.TransferOut);
            var inbound = Assert.Single(pair, x => x.TransactionType == InventoryTransactionType.TransferIn);
            Assert.Equal(doc.FromWarehouseId, outbound.WarehouseId);
            Assert.Equal(doc.ToWarehouseId, inbound.WarehouseId);
            Assert.Equal(line.ProductVariantId, outbound.ProductVariantId);
            Assert.Equal(line.ProductVariantId, inbound.ProductVariantId);
            Assert.Equal(-line.BaseQuantity, outbound.QuantityChange);
            Assert.Equal(line.BaseQuantity, inbound.QuantityChange);
            Assert.Equal(-outbound.TotalCost, inbound.TotalCost);
        }
    }
}

public sealed class StockTransferConcurrencyFixture : IAsyncLifetime
{
    internal FullApplicationFixture App { get; private set; } = null!;
    internal FullApplicationFixture.StoreSeed Store => App.Stores[0];
    internal FullApplicationFixture.Client Admin { get; private set; } = null!;
    internal FullApplicationFixture.Client Editor { get; private set; } = null!;
    internal FullApplicationFixture.Client Confirmer { get; private set; } = null!;
    internal FullApplicationFixture.Client Reader { get; private set; } = null!;
    internal FullApplicationFixture.Client Foreign { get; private set; } = null!;
    internal int DestinationId { get; private set; }
    internal int SecondVariantId { get; private set; }
    internal int UnitId { get; private set; }
    public async Task InitializeAsync()
    {
        App = await FullApplicationFixture.StartWithAuditPausedAsync();
        try
        {
            Admin = await App.LoginAsync(await App.AddAccountAsync(Store, "*"));
            Editor = await App.LoginAsync(await App.AddAccountAsync(Store, PermissionCodes.Inventory.StockTransfer.Update,
                PermissionCodes.Inventory.StockTransfer.Delete, PermissionCodes.Inventory.StockTransfer.Approve));
            Confirmer = await App.LoginAsync(await App.AddAccountAsync(Store, PermissionCodes.Inventory.StockTransfer.Confirm));
            Reader = await App.LoginAsync(await App.AddAccountAsync(Store, PermissionCodes.Inventory.StockTransfer.View));
            Foreign = await App.LoginAsync(await App.AddAccountAsync(App.Stores[1], "*"));
            await using var db = App.Database.CreateTenantContext(Store.StoreId);
            var source = await db.Warehouses.SingleAsync(x => x.Id == Store.WarehouseId);
            var destination = new Warehouse { StoreId = Store.StoreId, LegalEntityId = source.LegalEntityId,
                Code = "B1-DEST", Name = "B1 owned test destination", IsActive = true };
            var original = await db.ProductVariants.Include(x => x.Product).SingleAsync(x => x.Id == Store.VariantId);
            UnitId = original.Product.BaseUnitId;
            var second = new ProductVariant { StoreId = Store.StoreId, ProductId = original.ProductId,
                Sku = "B1-SECOND", ProductVariantName = "B1 owned second variant", IsActive = true, CostPrice = 10, Price = 20 };
            db.AddRange(destination, second);
            await db.SaveChangesAsync();
            DestinationId = destination.Id; SecondVariantId = second.Id;
            await InventoryPosPostingContractTests.CreateRealMovementService(db).CreateAsync(new InventoryMovementFactory()
                .CreatePurchaseReceipt(Store.WarehouseId, second.Id, 10000, 10, "B1-OPENING", 1, "B1 owned stock", 1));
        }
        catch { await DisposeAsync(); throw; }
    }
    public async Task DisposeAsync()
    {
        Admin?.Dispose(); Editor?.Dispose(); Confirmer?.Dispose(); Reader?.Dispose(); Foreign?.Dispose();
        if (App is not null) await App.DisposeAsync();
    }
}
