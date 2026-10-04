using System.Data.Common;
using System.Diagnostics;
using GaoApp.Application.Common.Security;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Repositories.Orders;
using GaoApp.Tests.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit.Abstractions;

namespace GaoApp.Tests.Security;

[Collection("SqlServerConcurrency")]
public sealed class PosOrderListSqlServerTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Four_hundred_thousand_orders_keep_exact_counts_and_deep_pages_after_count_index_migration()
    {
        // Keep the current runtime schema while exercising this index migration alone.
        // Downgrading the whole database would remove unrelated columns from the current model.
        await using var database = new InventoryPostingLocalDb();
        await database.MigrateAsync();
        var store = await database.SeedInventoryCatalogAsync();
        const int count = 413913;
        var day = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await using var db = database.CreateTenantContext(store.StoreId);
        db.Database.SetCommandTimeout(180);
        var terminal = new POSTerminal { StoreId = store.StoreId, Code = "BENCH", Name = "Benchmark" };
        var shift = new POSShift { StoreId = store.StoreId, Terminal = terminal, WarehouseId = store.WarehouseId, OpenedByUserId = 1 };
        var seed = new Order { StoreId = store.StoreId, POSShift = shift, OrderNumber = "LARGE-0", Status = OrderStatus.Completed,
            PaymentStatus = PaymentStatus.Paid, GrandTotal = 12000, PaidTotal = 12000, CompletedAtUtc = day, Note = new string('x', 450) };
        db.Add(seed); await db.SaveChangesAsync();

        // Copy mapped scalar columns in SQL instead of tracking 400k test entities in memory.
        var columns = db.Model.FindEntityType(typeof(Order))!.GetProperties()
            .Where(p => p.Name != nameof(Order.Id) && !p.IsConcurrencyToken && p.GetComputedColumnSql() == null)
            .Select(p => p.GetColumnName()).ToArray();
        var projections = columns.Select(c => c switch
        {
            "OrderNumber" => "CONCAT(N'LARGE-', n.i)",
            "CompletedAtUtc" => "DATEADD(second, CONVERT(int, n.i), s.CompletedAtUtc)",
            _ => $"s.[{c}]"
        });
        var insertSql = $$"""
            WITH n AS (SELECT TOP ({{count - 1}}) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) i
                       FROM sys.all_objects a CROSS JOIN sys.all_objects b)
            INSERT INTO dbo.Orders ({{string.Join(",", columns.Select(c => $"[{c}]"))}})
            SELECT {{string.Join(",", projections)}} FROM n CROSS JOIN dbo.Orders s WHERE s.Id = {0};
            """;
        await db.Database.ExecuteSqlRawAsync(insertSql, seed.Id);
        db.ChangeTracker.Clear();
        await ApplyCountIndexAsync(db, up: false);
        var repo = new OrderRepository(db);
        var legacy = db.Orders.AsNoTracking().Where(o => o.StoreId == store.StoreId && !o.IsDeleted &&
            (o.Status != OrderStatus.Draft || o.GrandTotal > 0 || o.PaidTotal > 0 || !string.IsNullOrWhiteSpace(o.Note)
             || o.Lines.Any(l => !l.IsDeleted) || o.Payments.Any(p => !p.IsDeleted)));
        var pages = new[] { 1, 2, 1000, (count + 19) / 20 };
        var expected = new Dictionary<int, int[]>();
        var before = new Dictionary<int, double>();
        foreach (var page in pages)
        {
            await legacy.CountAsync();
            var watch = Stopwatch.StartNew();
            Assert.Equal(count, await legacy.CountAsync());
            expected[page] = await legacy.OrderByDescending(o => EF.Property<DateTime>(o, "ListSortAtUtc"))
                .ThenByDescending(o => o.Id).Skip((page - 1) * 20).Take(20).Select(o => o.Id).ToArrayAsync();
            before[page] = watch.Elapsed.TotalMilliseconds;
        }
        await ApplyCountIndexAsync(db, up: true);
        foreach (var page in pages)
        {
            await repo.QueryOrdersAsync(null, null, null, null, page, 20);
            var watch = Stopwatch.StartNew();
            var result = await repo.QueryOrdersAsync(null, null, null, null, page, 20);
            output.WriteLine($"413,913 synthetic orders, page {page}: before count + page IDs {before[page]:F1} ms; after count + page headers + vouchers {watch.Elapsed.TotalMilliseconds:F1} ms.");
            Assert.Equal(count, result.Total);
            Assert.Equal(expected[page], result.Items.Select(x => x.OrderId));
        }
        var filtered = await repo.QueryOrdersAsync(day.AddDays(1), day.AddDays(2), OrderStatus.Completed, null, 1, 20);
        Assert.Equal(86400, filtered.Total);
        Assert.Equal(1, (await repo.QueryOrdersAsync(null, null, null, "LARGE-413912", 1, 20)).Total);
        Assert.Empty(db.ChangeTracker.Entries());
        // The new index must also be removable without losing any orders.
        await ApplyCountIndexAsync(db, up: false);
        Assert.Equal(count, await db.Orders.CountAsync());
    }

    private static async Task ApplyCountIndexAsync(GaoApp.Infrastructure.Data.AppDbContext db, bool up)
    {
        var migration = new GaoApp.Infrastructure.Migrations.OptimizePosOrdersCount
            { ActiveProvider = db.Database.ProviderName! };
        var commands = db.GetService<IMigrationsSqlGenerator>()
            .Generate(up ? migration.UpOperations : migration.DownOperations, db.Model);
        foreach (var command in commands)
            await db.Database.ExecuteSqlRawAsync(command.CommandText);
        var exists = await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM sys.indexes WHERE object_id = OBJECT_ID(N'Orders') AND name = N'IX_Orders_ListCount'").SingleAsync();
        Assert.Equal(up ? 1 : 0, exists);
    }

    [Fact]
    public async Task Large_history_pages_are_bounded_stable_tenant_scoped_and_preserve_related_summaries()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0]; var account = await app.AddAccountAsync(store, "*");
        const int count = 8000;
        int specialId, emptyId, lineDraftId, paidDraftId;
        var day = new DateTime(2026, 9, 20, 0, 0, 0, DateTimeKind.Utc);
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var shift = new POSShift { StoreId = store.StoreId, TerminalId = store.TerminalId, WarehouseId = store.WarehouseId, OpenedByUserId = account.UserId };
            db.Add(shift); await db.SaveChangesAsync();
            var orders = Enumerable.Range(0, count).Select(i => new Order { StoreId = store.StoreId, POSShiftId = shift.Id,
                OrderNumber = $"PERF-{i:D6}", Status = OrderStatus.Completed, PaymentStatus = PaymentStatus.Paid,
                GrandTotal = 12345, PaidTotal = 12345, CompletedAtUtc = day.AddMinutes(i / 2), Note = new string('x', 450) }).ToList();
            var empty = new Order { StoreId = store.StoreId, POSShiftId = shift.Id };
            var lineDraft = new Order { StoreId = store.StoreId, POSShiftId = shift.Id };
            var paidDraft = new Order { StoreId = store.StoreId, POSShiftId = shift.Id };
            db.AddRange(orders); db.AddRange(empty, lineDraft, paidDraft); await db.SaveChangesAsync();
            specialId = orders[^1].Id; emptyId = empty.Id; lineDraftId = lineDraft.Id; paidDraftId = paidDraft.Id;
            var product = await db.ProductVariants.SingleAsync(x => x.Id == store.VariantId);
            db.Add(new OrderLine { StoreId = store.StoreId, OrderId = lineDraft.Id, ProductId = product.ProductId, VariantId = product.Id, Quantity = 1, ItemName = "Draft line" });
            db.Add(new OrderPayment { StoreId = store.StoreId, OrderId = paidDraft.Id, Method = PaymentMethod.BankTransfer, Amount = 10 });
            db.Add(new OrderPayment { StoreId = store.StoreId, OrderId = specialId, Method = PaymentMethod.BankTransfer, Amount = 12345 });
            var customer = new Customer { StoreId = store.StoreId, Name = "Voucher customer", Phone = "0900000000" };
            db.Add(new OrderRewardVoucher { StoreId = store.StoreId, OrderId = specialId, VoucherValue = 3000,
                Voucher = new CustomerRewardVoucher { StoreId = store.StoreId, Customer = customer, VoucherCode = "PERF-GIFT", Value = 3000 } });
            db.Add(new SalesReturn { StoreId = store.StoreId, OrderId = specialId, POSShiftId = shift.Id, Status = SalesReturnStatus.Completed,
                RefundTotal = 1000, ReturnNumber = "PERF-RETURN", Reason = "Test", CreatedByUserId = account.UserId });
            await db.SaveChangesAsync();
            orders[0].IsDeleted = true; await db.SaveChangesAsync();
        }
        var otherStore = app.Stores[1]; var otherAccount = await app.AddAccountAsync(otherStore, "*");
        await using (var db = app.Database.CreateTenantContext(otherStore.StoreId))
        {
            var shift = new POSShift { StoreId = otherStore.StoreId, TerminalId = otherStore.TerminalId, WarehouseId = otherStore.WarehouseId, OpenedByUserId = otherAccount.UserId };
            db.Add(new Order { StoreId = otherStore.StoreId, POSShift = shift, OrderNumber = "FOREIGN-ORDER", Status = OrderStatus.Completed, CompletedAtUtc = day.AddYears(1) });
            await db.SaveChangesAsync();
        }
        var capture = new Capture();
        await using (var db = app.Database.CreateTenantContext(store.StoreId, capture))
        {
            var repo = new OrderRepository(db);
            var legacy = db.Orders.AsNoTracking().Where(o => !o.IsDeleted && (o.Status != OrderStatus.Draft || o.GrandTotal > 0 || o.PaidTotal > 0 ||
                !string.IsNullOrWhiteSpace(o.Note) || o.Lines.Any(l => !l.IsDeleted) || o.Payments.Any(p => !p.IsDeleted)));
            var expected = await legacy.OrderByDescending(o => o.CompletedAtUtc ?? o.CreatedAtUtc).ThenByDescending(o => o.Id).Select(o => o.Id).ToListAsync();
            Assert.Equal(count + 1, expected.Count); Assert.DoesNotContain(emptyId, expected);
            Assert.Contains(lineDraftId, expected); Assert.Contains(paidDraftId, expected);
            foreach (var page in new[] { 1, 2, 200, 400 })
            {
                // Warm both paths once, then measure the same full-history request shape.
                await repo.QueryOrdersAsync(null, null, null, null, page, 20);
                await legacy.OrderByDescending(o => o.CompletedAtUtc ?? o.CreatedAtUtc).ThenByDescending(o => o.Id).Skip((page - 1) * 20).Take(20).ToListAsync();
                var watch = Stopwatch.StartNew();
                await legacy.CountAsync();
                await legacy.OrderByDescending(o => o.CompletedAtUtc ?? o.CreatedAtUtc).ThenByDescending(o => o.Id).Skip((page - 1) * 20).Take(20).ToListAsync();
                var oldMs = watch.Elapsed.TotalMilliseconds;
                capture.Sql.Clear(); watch.Restart();
                var result = await repo.QueryOrdersAsync(null, null, null, null, page, 20);
                output.WriteLine($"8,000 orders, page {page}: old full-entity query {oldMs:F1} ms; indexed headers + vouchers {watch.Elapsed.TotalMilliseconds:F1} ms.");
                Assert.Equal(expected.Skip((page - 1) * 20).Take(20), result.Items.Select(x => x.OrderId));
                Assert.Equal(expected.Count, result.Total); Assert.Equal(5, capture.Sql.Count);
                var sql = Assert.Single(capture.Sql, s => s.Contains("page headers only"));
                Assert.Contains("ListSortAtUtc", sql); Assert.DoesNotContain("InvoiceIssuanceRoute", sql); Assert.DoesNotContain("HoldNote", sql);
                Assert.DoesNotContain("SELECT [o].*", sql); Assert.Empty(db.ChangeTracker.Entries());
            }
            var keyword = await repo.QueryOrdersAsync(null, null, null, "PERF-007999", 1, 20);
            var special = Assert.Single(keyword.Items); Assert.Equal(specialId, special.OrderId);
            Assert.Equal("PERF-GIFT", Assert.Single(special.RewardVouchers).VoucherCode);
            var dated = await repo.QueryOrdersAsync(day, day.AddMinutes(2), OrderStatus.Completed, null, 1, 20);
            Assert.Equal(3, dated.Total);
            var drafts = await repo.QueryOrdersAsync(null, null, OrderStatus.Draft, null, 1, 20);
            Assert.Equal(2, drafts.Total);
            capture.Sql.Clear();
            var beyond = await repo.QueryOrdersAsync(null, null, null, null, int.MaxValue, 200);
            Assert.Empty(beyond.Items); Assert.Equal(expected.Count, beyond.Total); Assert.Single(capture.Sql);
            // Computed sort key follows writes; no application-side backfill is needed.
            await db.Orders.Where(x => x.Id == specialId).ExecuteUpdateAsync(s => s.SetProperty(x => x.CompletedAtUtc, DateTime.UtcNow.AddDays(1)));
            Assert.Equal(specialId, (await repo.QueryOrdersAsync(null, null, null, null, 1, 20)).Items[0].OrderId);
        }
        using var client = await app.LoginAsync(account);
        using var response = await client.Http.GetAsync("/admin/pos/orders?keyword=PERF-007999&pageSize=9999");
        response.EnsureSuccessStatusCode();
        Assert.Matches(@"^orders;dur=\d+\.\d$", Assert.Single(response.Headers.GetValues("Server-Timing")));
        using var document = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var json = document.RootElement;
        Assert.Equal(200, json.GetProperty("pageSize").GetInt32());
        var item = Assert.Single(json.GetProperty("items").EnumerateArray());
        Assert.True(item.GetProperty("hasBankTransfer").GetBoolean());
        Assert.Equal(1000, item.GetProperty("refundedTotal").GetDecimal()); Assert.Equal(1, item.GetProperty("returnCount").GetInt32());
        using var outsider = await app.LoginAsync(otherAccount);
        var foreign = await outsider.JsonAsync(HttpMethod.Get, "/admin/pos/orders?keyword=PERF-"); Assert.Equal(0, foreign.GetProperty("totalItems").GetInt32());
    }

    private sealed class Capture : DbCommandInterceptor
    {
        public List<string> Sql { get; } = [];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        { Sql.Add(command.CommandText); return ValueTask.FromResult(result); }
    }
}
