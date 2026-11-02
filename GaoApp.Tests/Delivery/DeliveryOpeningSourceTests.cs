using System.Data;
using System.Text.Json;
using GaoApp.Application.DTOs.Delivery;
using GaoApp.Application.Services.Inventory;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Data.Migrations;
using GaoApp.Tests.Inventory;
using GaoApp.Tests.Configuration;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GaoApp.Tests.Delivery;

[Collection("DeliveryD05"), Trait("Category", "DeliveryOpeningSource")]
public sealed class DeliveryOpeningSourceTests(DeliveryD02Fixture fixture)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Opening_FIFO_dispatch_uses_only_the_proven_remaining_budget_and_replays_once(bool consumedPreviously)
    {
        using var c = await DeliveryD05Case.CreateAsync(fixture, ordered: 2, stock: []);
        var ids = await SeedOpeningAsync(c);
        var variant = await c.VariantAsync();
        if (consumedPreviously)
        {
            await using var db = c.Picking.Source.Context();
            var request = new InventoryMovementFactory().CreateAdjustmentDecrease(
                c.Picking.Account.Store.WarehouseId, variant, 1, null,
                "Opening fixture prior FIFO consumption");
            var prior = await InventoryPosPostingContractTests.CreateRealMovementService(db).CreateAsync(request);
            Assert.True(prior.IsCreated);
            var allocation = await db.InventoryCostLayerAllocations.AsNoTracking()
                .SingleAsync(x => x.InventoryCostLayerId == ids.Layer);
            Assert.False(allocation.IsProvisional); Assert.Equal(1m, allocation.Quantity); Assert.Equal(8761m, allocation.Amount);
        }
        await c.ReadyAsync("2");
        var openingBefore = await SourceSnapshotAsync(c, ids);
        var financeBefore = await c.FinanceAsync();
        var ready = await c.Picking.GetAsync();
        var requestKey = new DeliveryInventoryDispatchRequest(
            Guid.NewGuid(), ready.Delivery.Version, ready.ApprovedRevision!.Value);
        var ack = await c.DispatchAsync(requestKey);
        Assert.False(ack.Replayed);
        var posted = await c.SnapshotAsync();
        var replay = await c.DispatchAsync(requestKey);
        Assert.True(replay.Replayed); Assert.Equal(ack.JournalEntryIds, replay.JournalEntryIds);
        Assert.Equal(posted, await c.SnapshotAsync());
        Assert.Equal(openingBefore, await SourceSnapshotAsync(c, ids));
        Assert.Equal(financeBefore, await c.FinanceAsync());
        await using var verify = c.Picking.Source.Context();
        var priorQuantity = consumedPreviously ? 1m : 0m;
        var balance = await verify.InventoryBalances.SingleAsync(x => x.ProductVariantId == variant);
        Assert.Equal(2m - priorQuantity, balance.OnHandQty);
        Assert.Equal((2m - priorQuantity) * 8761m, balance.InventoryValue); Assert.Equal(0m, balance.ReservedQty);
        var layer = await verify.InventoryCostLayers.SingleAsync(x => x.Id == ids.Layer);
        Assert.Equal((byte)1, layer.CostBudgetPolicy);
        Assert.Equal(4m - priorQuantity, layer.BudgetAnchorQuantity);
        Assert.Equal((4m - priorQuantity) * 8761m, layer.BudgetAnchorAmount);
        Assert.Equal(2m - priorQuantity, layer.RemainingQuantity);
        Assert.Equal((2m - priorQuantity) * 8761m, layer.RemainingCostAmount);
        var fragment = await verify.DeliveryDispatchCostFragments.SingleAsync(x => x.DeliveryOrderId == c.Id);
        Assert.Equal(2m, fragment.BaseQuantity); Assert.Equal(17522m, fragment.CostAmount);
        Assert.Equal(ids.Layer, fragment.InventoryCostLayerId); Assert.True(fragment.InventoryCostBudgetDebitId > 0);
        var finalValuation = await verify.InventoryValuationEntries.SingleAsync(x => x.Id == fragment.InventoryValuationEntryId);
        Assert.False(finalValuation.IsProvisional); Assert.NotNull(finalValuation.CostFinalizedAtUtc);
        var debit = await verify.InventoryCostBudgetDebits.SingleAsync(x => x.Id == fragment.InventoryCostBudgetDebitId);
        Assert.Equal(0m, debit.StartQuantity); Assert.Equal(2m, debit.EndQuantity); Assert.Equal(17522m, debit.Amount);
        var journal = await verify.DeliveryJournalEntries.SingleAsync(x => x.DeliveryOrderId == c.Id);
        Assert.Equal(2m, journal.BaseQuantity); Assert.Equal(17522m, journal.CostAmount);
        Assert.Equal(0m, journal.MoneyAmount); Assert.Null(journal.SaleOrderId);
    }

    [Theory]
    [InlineData("wrong-transaction")]
    [InlineData("wrong-entry-type")]
    [InlineData("unknown-cost")]
    [InlineData("missing-final")]
    [InlineData("snapshot-mismatch")]
    public async Task Invalid_opening_source_is_rejected_without_changing_holds_stock_or_history(string fault)
    {
        using var c = await DeliveryD05Case.CreateAsync(fixture, ordered: 2, stock: []);
        var ids = await SeedOpeningAsync(c); await c.ReadyAsync("2");
        await using (var db = c.Picking.Source.Context())
        {
            var transaction = await db.InventoryTransactions.SingleAsync(x => x.Id == ids.Transaction);
            var valuation = await db.InventoryValuationEntries.SingleAsync(x => x.Id == ids.Valuation);
            switch (fault)
            {
                case "wrong-transaction": transaction.TransactionType = InventoryTransactionType.PurchaseReceipt; break;
                case "wrong-entry-type": valuation.EntryType = InventoryValuationEntryType.Revaluation; break;
                case "unknown-cost": valuation.CostSourceType = InventoryCostSourceType.Unknown; break;
                case "missing-final": valuation.CostFinalizedAtUtc = null; break;
                case "snapshot-mismatch": transaction.UnitCostSnapshot = 8762m; break;
                default: throw new ArgumentOutOfRangeException(nameof(fault));
            }
            await db.SaveChangesAsync();
        }
        var before = await c.SnapshotAsync(); var sourceBefore = await SourceSnapshotAsync(c, ids);
        var error = await Assert.ThrowsAsync<DeliveryFoundationException>(() => c.DispatchAsync());
        Assert.Equal("COST_NOT_READY", error.Code);
        Assert.Equal(before, await c.SnapshotAsync()); Assert.Equal(sourceBefore, await SourceSnapshotAsync(c, ids));
        await using var verify = c.Picking.Source.Context();
        Assert.False(await verify.DeliveryDispatchCostFragments.AnyAsync(x => x.DeliveryOrderId == c.Id));
        Assert.False(await verify.DeliveryJournalEntries.AnyAsync(x => x.DeliveryOrderId == c.Id));
    }

    [Fact]
    public async Task Opening_forward_guard_matches_the_catalog_rejects_wrong_pairs_and_preserves_promoted_history_on_Down()
    {
        const string openingMigration = "20261008180000_AllowDeliveryOpeningCostSources";
        var isolated = new DeliveryD02Fixture(); await isolated.InitializeAsync();
        try
        {
            using var c = await DeliveryD05Case.CreateAsync(isolated, ordered: 2, stock: []);
            var ids = await SeedOpeningAsync(c); await c.ReadyAsync("2");
            await using var db = c.Picking.Source.Context();
            var migrations = db.Database.GetMigrations().ToArray();
            Assert.Equal(openingMigration, migrations[74]);
            var previous = migrations[73]; var before = await c.SnapshotAsync();
            await MigrationTestSupport.MigrateHistoricalPrefixAsync(isolated.Web.Database, previous);
            await AssertOpeningLayerManifestAsync(db, previous); Assert.Equal(before, await c.SnapshotAsync());
            await db.GetService<IMigrator>().MigrateAsync(openingMigration);
            await AssertOpeningLayerManifestAsync(db, openingMigration); Assert.Equal(before, await c.SnapshotAsync());
            await using (var transaction = await db.Database.BeginTransactionAsync())
            {
                try
                {
                    await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE InventoryTransactions SET TransactionType={(int)InventoryTransactionType.PurchaseReceipt} WHERE Id={ids.Transaction}");
                    var error = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE InventoryCostLayers SET CostBudgetPolicy=1,BudgetAnchorQuantity=RemainingQuantity,BudgetAnchorAmount={35044m},RemainingCostAmount={35044m} WHERE Id={ids.Layer}"));
                    Assert.Equal(51024, error.Number);
                }
                finally { await transaction.RollbackAsync(); }
            }
            Assert.Equal(before, await c.SnapshotAsync()); await AssertOpeningLayerManifestAsync(db, openingMigration);
            await db.Database.MigrateAsync();
            await c.DispatchAsync(); var posted = await c.SnapshotAsync();
            await MigrationTestSupport.MigrateHistoricalPrefixAsync(isolated.Web.Database, openingMigration);
            var refused = await Assert.ThrowsAsync<SqlException>(() => db.GetService<IMigrator>().MigrateAsync(previous));
            Assert.Equal(51042, refused.Number);
            Assert.Equal(posted, await c.SnapshotAsync()); await AssertOpeningLayerManifestAsync(db, openingMigration);
        }
        finally { await isolated.DisposeAsync(); }
    }

    private static async Task AssertOpeningLayerManifestAsync(AppDbContext db, string latest)
    {
        var applied = (await db.Database.GetAppliedMigrationsAsync()).ToArray(); Assert.Equal(latest, applied[^1]);
        Assert.Equal(db.Database.GetMigrations().Take(applied.Length), applied);
        var catalog = new EfCoreDatabaseSchemaManifestCatalog(db);
        Assert.True(catalog.TryGetManifestForAppliedMigrationPrefix(applied, out var expected));
        var wasOpen = db.Database.GetDbConnection().State == ConnectionState.Open;
        if (!wasOpen) await db.Database.OpenConnectionAsync();
        DatabaseSchemaManifest actual;
        try { actual = await new SqlServerSchemaSnapshotReader(db).ReadAsync(applied); }
        finally { if (!wasOpen) await db.Database.CloseConnectionAsync(); }
        // The Opening migration replaces only this protection. Compare every
        // column, key, index, constraint and trigger of its complete table.
        var expectedLayer = Assert.Single(expected.Tables, x => x.Identity.Name == "inventorycostlayers");
        var actualLayer = Assert.Single(actual.Tables, x => x.Identity.Name == "inventorycostlayers");
        var compared = DatabaseSchemaComparer.Compare(
            DatabaseSchemaCanonicalizer.WithFingerprint(applied, [expectedLayer], []),
            DatabaseSchemaCanonicalizer.WithFingerprint(applied, [actualLayer], []));
        Assert.True(compared.IsMatch, JsonSerializer.Serialize(compared)); Assert.Equal(0, compared.Mismatches.Total);
    }

    private static async Task<OpeningIds> SeedOpeningAsync(DeliveryD05Case c)
    {
        // Controlled historical OpeningBalance/Opening graph, not a relabel of shop
        // data or a claim that the ordinary receipt writer creates opening sources.
        await c.SeedHistoricalFinalSourceAsync(4, 8761, 35044);
        var variant = await c.VariantAsync(); await using var db = c.Picking.Source.Context();
        var layer = await db.InventoryCostLayers.SingleAsync(x => x.ProductVariantId == variant);
        Assert.Equal((byte)0, layer.CostBudgetPolicy);
        var transaction = await db.InventoryTransactions.SingleAsync(x => x.Id == layer.InventoryTransactionId);
        var valuation = await db.InventoryValuationEntries.SingleAsync(x => x.Id == layer.InventoryValuationEntryId);
        transaction.TransactionType = InventoryTransactionType.OpeningBalance;
        valuation.EntryType = InventoryValuationEntryType.Opening;
        transaction.ReferenceType = valuation.ReferenceType = layer.ReferenceType = InventoryReferenceType.None;
        transaction.ReferenceId = valuation.ReferenceId = layer.ReferenceId = "OPENING-FIXTURE-" + c.Id;
        await db.SaveChangesAsync();
        return new(transaction.Id, valuation.Id, layer.Id);
    }

    private static async Task<string> SourceSnapshotAsync(DeliveryD05Case c, OpeningIds ids)
    {
        await using var db = c.Picking.Source.Context();
        return JsonSerializer.Serialize(new
        {
            Transaction = await db.InventoryTransactions.AsNoTracking().Where(x => x.Id == ids.Transaction)
                .Select(x => new { x.Id, x.TransactionType, x.ReferenceType, x.ReferenceId, x.ReferenceLineId,
                    x.QuantityChange, x.UnitCostSnapshot, x.TotalCost, x.BeforeQty, x.AfterQty,
                    x.BeforeInventoryValue, x.AfterInventoryValue, x.IsProvisionalCost, x.CostFinalizedAtUtc }).SingleAsync(),
            Valuation = await db.InventoryValuationEntries.AsNoTracking().Where(x => x.Id == ids.Valuation)
                .Select(x => new { x.Id, x.EntryType, x.ReferenceType, x.ReferenceId, x.ReferenceLineId,
                    x.Quantity, x.UnitCost, x.Amount, x.CostSourceType, x.IsProvisional, x.CostFinalizedAtUtc }).SingleAsync()
        });
    }

    private sealed record OpeningIds(int Transaction, int Valuation, int Layer);
}
