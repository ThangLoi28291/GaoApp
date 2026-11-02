using System.Data.Common;
using System.Diagnostics;
using System.Text.Json;
using GaoApp.Domain.Entities;
using GaoApp.Application.Common.Security;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Repositories.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit.Abstractions;

namespace GaoApp.Tests.Security;

[Collection("SqlServerConcurrency")]
public sealed class InventoryLedgerAllSearchPerformanceTests(ITestOutputHelper output)
{
    [Fact]
    public async Task All_search_reuses_sparse_text_matches_and_preserves_dense_empty_and_filtered_results()
    {
        await using var app = await FullApplicationFixture.StartWithAuditPausedAsync();
        var store = app.Stores[0];
        await InventoryLedgerSearchSqlServerTests.SeedAsync(app, store, 1_500_000);
        await using (var seed = app.Database.CreateTenantContext(store.StoreId))
        {
            // Keep existing sparse terms while adding a realistic broad term that
            // matches BOTH catalog names and most of the historical movement notes.
            (await seed.Products.SingleAsync(x => x.Name == "Sữa tươi Đà Lạt")).Name = "Hàng sữa tươi Đà Lạt";
            await seed.SaveChangesAsync();
        }
        var viewer = await app.AddAccountAsync(store, PermissionCodes.Inventory.Transaction.View);
        var protectedBefore = await ProtectedAsync(app);
        var capture = new SqlCapture();
        await using var db = app.Database.CreateTenantContext(store.StoreId, capture);
        var repo = new InventoryLedgerIndexReadRepository(db);
        var evidence = new List<object>();
        var baseline = Environment.GetEnvironmentVariable("GAO_LEDGER_SEARCH_BASELINE") == "1";
        var folder = Path.Combine(FullApplicationFixture.SourceRoot(), "TestResults", "inventory-ledger-all-search-20261029", baseline ? "baseline" : "candidate");
        Directory.CreateDirectory(folder);
        var firstPageIds = new HashSet<int>();
        var terms = new List<(string Keyword, int Count)> { ("sua tuoi", 46), ("doi soat", 1), ("no-such-unique-ledger-value", 0) };
        if (!baseline) terms.AddRange([("ĐÀ LẠT", 45), ("MILK-LEDGER", 45), ("8938505974194", 45)]);
        foreach (var (keyword, count) in terms)
        {
            capture.Clear();
            var clock = Stopwatch.StartNew();
            var result = await repo.QueryAsync(store.StoreId, new() { Keyword = keyword, SearchScope = "all", PageSize = 20 }, null);
            clock.Stop(); Assert.Equal(count, result.TotalItems);
            if (keyword == "sua tuoi") firstPageIds = result.Items.Select(x => x.TransactionId).ToHashSet();
            output.WriteLine($"All '{keyword}': {clock.ElapsedMilliseconds}ms, {result.TotalItems} rows");
            evidence.Add(new { keyword, ms = clock.ElapsedMilliseconds, result.TotalItems, sql = capture.Commands.ToArray() });
            if (!baseline)
            {
                Assert.Single(capture.Texts, sql => sql.Contains("[InventoryTransactions]") && sql.Contains("REPLACE"));
                Assert.True(clock.ElapsedMilliseconds < 10_000, $"Sparse All '{keyword}' exceeded 10s: {clock.ElapsedMilliseconds}ms");
            }
        }
        // Isolate the physical text scan from EF's aggregate/semi-join/page shape.
        foreach (var keyword in baseline ? new[] { "sua tuoi", "doi soat", "no-such-unique-ledger-value" } : [])
        {
            capture.Clear();
            var clock = Stopwatch.StartNew();
            var ids = await db.InventoryTransactions.IgnoreQueryFilters().AsNoTracking()
                .Where(t => t.StoreId == store.StoreId && !t.IsDeleted
                    && (t.ReferenceId!.Contains(keyword) || (t.Note != null
                        && EF.Functions.Collate(t.Note.Replace("Đ", "D").Replace("đ", "d"), "Latin1_General_100_CI_AI").Contains(keyword))))
                .Select(t => t.Id).Take(4097).ToArrayAsync();
            output.WriteLine($"Narrow text '{keyword}': {clock.ElapsedMilliseconds}ms, {ids.Length} IDs");
            evidence.Add(new { narrowText = keyword, ms = clock.ElapsedMilliseconds, count = ids.Length, sql = capture.Commands.ToArray() });
        }
        if (!baseline)
        {
            var clock = Stopwatch.StartNew();
            var last = await repo.QueryAsync(store.StoreId, new() { Keyword = "sua tuoi", SearchScope = "all", IncludeSummary = false, Page = 3 }, null);
            Assert.Equal(46, last.TotalItems); Assert.Equal(6, last.Items.Count);
            Assert.Empty(firstPageIds.Intersect(last.Items.Select(x => x.TransactionId)));
            evidence.Add(new { test = "sparse-last-page-without-summary", ms = clock.ElapsedMilliseconds, last.TotalItems });
            Assert.True(clock.ElapsedMilliseconds < 10_000);
            clock.Restart();
            var dense = await repo.QueryAsync(store.StoreId, new() { Keyword = "Xuất bán hàng", SearchScope = "all", State = "decrease" }, null);
            Assert.Equal(1_500_000, dense.TotalItems); Assert.Equal(1_500_000, dense.Summary.DecreaseItems);
            Assert.Equal(20, dense.Items.Count);
            evidence.Add(new { test = "dense-no-truncation", ms = clock.ElapsedMilliseconds, dense.TotalItems });
            output.WriteLine($"Dense All first page: {clock.ElapsedMilliseconds}ms");
            Assert.True(clock.ElapsedMilliseconds < 18_000);
            clock.Restart();
            var denseLast = await repo.QueryAsync(store.StoreId, new() { Keyword = "Xuất bán hàng", SearchScope = "all",
                IncludeSummary = false, Page = 75000, SortDirection = "asc" }, null);
            Assert.Equal(1_500_000, denseLast.TotalItems); Assert.Equal(20, denseLast.Items.Count);
            evidence.Add(new { test = "dense-deep-page-no-truncation", ms = clock.ElapsedMilliseconds, denseLast.TotalItems });
            output.WriteLine($"Dense All deep page: {clock.ElapsedMilliseconds}ms");
            await File.WriteAllTextAsync(Path.Combine(folder, "profile.json"), JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
            Assert.True(clock.ElapsedMilliseconds < 18_000);
            var expectedMixed = await db.InventoryTransactions.AsNoTracking().Where(t => t.StoreId == store.StoreId && !t.IsDeleted
                && (EF.Functions.Collate(t.ProductVariant.Product.Name.Replace("Đ", "D").Replace("đ", "d"), "Latin1_General_100_CI_AI").Contains("hang")
                    || t.ReferenceId!.Contains("hang") || (t.Note != null && EF.Functions.Collate(t.Note.Replace("Đ", "D").Replace("đ", "d"), "Latin1_General_100_CI_AI").Contains("hang"))))
                .CountAsync();
            clock.Restart();
            var mixed = await repo.QueryAsync(store.StoreId, new() { Keyword = "hang", SearchScope = "all" }, null);
            Assert.Equal(expectedMixed, mixed.TotalItems); Assert.True(mixed.TotalItems >= 1_500_045);
            evidence.Add(new { test = "dense-mixed-catalog-text-no-truncation", ms = clock.ElapsedMilliseconds, mixed.TotalItems });
            output.WriteLine($"Dense mixed All: {clock.ElapsedMilliseconds}ms");
            Assert.True(clock.ElapsedMilliseconds < 18_000);
            await BrowserAsync(app, viewer, folder);
        }
        await File.WriteAllTextAsync(Path.Combine(folder, "profile.json"), JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
        Assert.Equal(protectedBefore, await ProtectedAsync(app));
        Assert.False(db.Database.HasPendingModelChanges()); Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task All_search_matches_original_predicate_for_Vietnamese_literals_soft_deletes_and_all_sort_orders()
    {
        await using var app = await FullApplicationFixture.StartWithAuditPausedAsync();
        var store = app.Stores[0];
        await InventoryLedgerSearchSqlServerTests.SeedAsync(app, store, 0);
        await using (var seed = app.Database.CreateTenantContext(store.StoreId))
        {
            var legalId = await seed.Warehouses.Where(x => x.Id == store.WarehouseId).Select(x => x.LegalEntityId).SingleAsync();
            var retired = new Warehouse { StoreId = store.StoreId, LegalEntityId = legalId, Code = "SEARCH-RETIRED", Name = "Retired", IsActive = true };
            seed.Add(retired); await seed.SaveChangesAsync();
            for (var i = 0; i < 5002; i++) seed.Add(new InventoryTransaction {
                StoreId = store.StoreId, WarehouseId = i == 5000 ? retired.Id : store.WarehouseId, ProductVariantId = store.VariantId,
                ReferenceType = InventoryReferenceType.Adjustment, ReferenceId = "EDGE-" + i,
                TransactionType = i % 2 == 0 ? InventoryTransactionType.AdjustmentIncrease : InventoryTransactionType.AdjustmentDecrease,
                QuantityChange = i % 2 == 0 ? 1 : -1, BeforeQty = 0, AfterQty = i % 2 == 0 ? 1 : -1,
                OccurredAtUtc = new DateTime(2026, 1, 10, 1, 0, 0).AddSeconds(i % 2), IsDeleted = i == 5001,
                Note = i < 10 ? "Phiếu đặc biệt 100%_ [A] ' & <script>" : "ĐƠN đã đối soát 100%_ [A]"
            });
            await seed.SaveChangesAsync();
            // The audit interceptor intentionally initializes Added entities as active.
            // Soft-delete persisted fixture rows in a subsequent save, like the application.
            (await seed.InventoryTransactions.SingleAsync(x => x.ReferenceId == "EDGE-5001")).IsDeleted = true;
            retired.IsDeleted = true; await seed.SaveChangesAsync();
        }
        var protectedBefore = await ProtectedAsync(app);
        await using var db = app.Database.CreateTenantContext(store.StoreId);
        var repo = new InventoryLedgerIndexReadRepository(db);
        foreach (var term in new[] { "đặc biệt", "DAC BIET", "ĐƠN", "doi soat", "100%_", "[A]", "EDGE-99", "sua tuoi" })
        {
            var normalized = term.Replace('Đ', 'D').Replace('đ', 'd');
            // Original unsplit predicate is the semantic oracle, independent of the new
            // bounded-ID plan. Include inactive catalog and all active same-store parents.
            var oracle = db.InventoryTransactions.AsNoTracking().Where(t => t.StoreId == store.StoreId && !t.IsDeleted
                && t.Warehouse.StoreId == store.StoreId && !t.Warehouse.IsDeleted
                && t.ProductVariant.StoreId == store.StoreId && !t.ProductVariant.IsDeleted
                && t.ProductVariant.Product.StoreId == store.StoreId && !t.ProductVariant.Product.IsDeleted
                && (t.ReferenceId!.Contains(term) || (t.Note != null && EF.Functions.Collate(t.Note.Replace("Đ", "D").Replace("đ", "d"), "Latin1_General_100_CI_AI").Contains(normalized))
                    || EF.Functions.Collate(t.ProductVariant.Product.Name.Replace("Đ", "D").Replace("đ", "d"), "Latin1_General_100_CI_AI").Contains(normalized)
                    || t.ProductVariant.Sku.Contains(term)
                    || t.ProductVariant.UnitConversions.Any(c => c.StoreId == store.StoreId && !c.IsDeleted && c.IsActive
                        && c.Barcodes.Any(b => b.StoreId == store.StoreId && !b.IsDeleted && b.IsActive && b.Barcode.Contains(term)))));
            var expectedCount = await oracle.CountAsync();
            foreach (var sort in new[] { "date", "product", "warehouse", "change", "after" })
            foreach (var descending in new[] { false, true })
            {
                IOrderedQueryable<InventoryTransaction> sorted = sort switch {
                    "product" => descending ? oracle.OrderByDescending(x => x.ProductVariant.Product.Name) : oracle.OrderBy(x => x.ProductVariant.Product.Name),
                    "warehouse" => descending ? oracle.OrderByDescending(x => x.Warehouse.Name) : oracle.OrderBy(x => x.Warehouse.Name),
                    "change" => descending ? oracle.OrderByDescending(x => x.QuantityChange) : oracle.OrderBy(x => x.QuantityChange),
                    "after" => descending ? oracle.OrderByDescending(x => x.AfterQty) : oracle.OrderBy(x => x.AfterQty),
                    _ => descending ? oracle.OrderByDescending(x => x.OccurredAtUtc) : oracle.OrderBy(x => x.OccurredAtUtc)
                };
                sorted = descending ? sorted.ThenByDescending(x => x.OccurredAtUtc).ThenByDescending(x => x.Id)
                    : sorted.ThenBy(x => x.OccurredAtUtc).ThenBy(x => x.Id);
                var expectedIds = await sorted.Take(20).Select(x => x.Id).ToArrayAsync();
                var result = await repo.QueryAsync(store.StoreId, new() { Keyword = term, SearchScope = "all", SortBy = sort,
                    SortDirection = descending ? "desc" : "asc", PageSize = 20 }, null);
                Assert.Equal(expectedCount, result.TotalItems);
                Assert.Equal(expectedIds, result.Items.Select(x => x.TransactionId));
                Assert.Equal(await oracle.CountAsync(x => x.QuantityChange > 0), result.Summary.IncreaseItems);
                Assert.Equal(await oracle.CountAsync(x => x.QuantityChange < 0), result.Summary.DecreaseItems);
                Assert.Equal(await oracle.CountAsync(x => x.AfterQty < 0), result.Summary.NegativeItems);
            }
        }
        foreach (var state in new[] { "increase", "decrease", "negative" })
        {
            var result = await repo.QueryAsync(store.StoreId, new() { Keyword = "EDGE-", SearchScope = "all", State = state,
                WarehouseId = store.WarehouseId, FromDate = new(2026, 1, 10), ToDate = new(2026, 1, 10), PageSize = 20 }, null);
            Assert.Equal(2500, result.TotalItems); Assert.Equal(5000, result.Summary.TotalItems);
        }
        using (var cancellation = new CancellationTokenSource())
        {
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => repo.QueryAsync(store.StoreId, new() { Keyword = "EDGE-", SearchScope = "all" }, null, cancellation.Token));
        }
        Assert.Equal(5000, (await repo.QueryAsync(store.StoreId, new() { Keyword = "EDGE-", SearchScope = "all", PageSize = 20 }, null)).TotalItems);
        Assert.Equal(protectedBefore, await ProtectedAsync(app)); Assert.Empty(db.ChangeTracker.Entries());
    }

    private static async Task<string> ProtectedAsync(FullApplicationFixture app)
    {
        await using var db = app.Database.CreateHostContext();
        async Task<object> Rows<T>() where T : class
        {
            var rows = await db.Set<T>().IgnoreQueryFilters().AsNoTracking().ToListAsync();
            return rows.Select(row => db.Entry(row).Properties.OrderBy(p => p.Metadata.Name)
                .ToDictionary(p => p.Metadata.Name, p => p.CurrentValue)).ToArray();
        }
        return JsonSerializer.Serialize(new {
            stores = await Rows<Store>(), menus = await Rows<AdminMenuItem>(), roles = await Rows<Role>(), grants = await Rows<RolePermission>(),
            stock = await Rows<InventoryBalance>(), rewards = await Rows<RewardSettings>(), vouchers = await Rows<CustomerRewardVoucher>(),
            ledger = await db.InventoryTransactions.IgnoreQueryFilters().AsNoTracking().GroupBy(x => x.StoreId)
                .Select(g => new { g.Key, Count = g.Count(), Change = g.Sum(x => x.QuantityChange), First = g.Min(x => x.Id), Last = g.Max(x => x.Id) }).OrderBy(x => x.Key).ToListAsync()
        });
    }

    private sealed class SqlCapture : DbCommandInterceptor
    {
        internal List<object> Commands { get; } = [];
        internal List<string> Texts { get; } = [];
        internal void Clear() { Commands.Clear(); Texts.Clear(); }
        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            Texts.Add(command.CommandText);
            Commands.Add(new { sql = command.CommandText, ms = eventData.Duration.TotalMilliseconds,
                parameters = command.Parameters.Cast<DbParameter>().Select(p => new { p.ParameterName, value = p.Value?.ToString() }).ToArray() });
            return ValueTask.FromResult(result);
        }
    }

    private static async Task BrowserAsync(FullApplicationFixture app, FullApplicationFixture.Account viewer, string folder)
    {
        var root = FullApplicationFixture.SourceRoot();
        var start = new ProcessStartInfo("node") { WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.Environment["NODE_PATH"] = Path.Combine(root, "Logs", "pos-offline-browser-deps", "node_modules");
        start.ArgumentList.Add(Path.Combine(root, "GaoApp.Tests.Browser", "inventory-ledger-all-search.browser.cjs"));
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { baseUrl = $"http://{viewer.Store.Host}:{app.Address.Port}",
            user = viewer.Name, password = viewer.Password, terminalId = viewer.Store.TerminalId, output = folder }));
        process.StandardInput.Close();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        try { await process.WaitForExitAsync(timeout.Token); } catch { if (!process.HasExited) process.Kill(true); throw; }
        var log = await stdout + "\n" + await stderr;
        await File.WriteAllTextAsync(Path.Combine(folder, "chrome.log"), log); Assert.True(process.ExitCode == 0, log);
    }
}
