using System.Data;
using GaoApp.Application.Services.Inventory;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Data.Migrations;
using GaoApp.Tests.Configuration;
using GaoApp.Tests.Inventory;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;

namespace GaoApp.Tests.Delivery;

[Collection("DeliveryD05"), Trait("Category", "DeliveryD05")]
public sealed class DeliveryD05SchemaTests(DeliveryD02Fixture fixture)
{
    private const string Previous = "20261007090000_AddMixedQuantityPromotions";
    private const string Current = "20261007100000_AddDeliveryInventoryPosting";

    [Fact]
    public async Task M01_Current72_real_SQL_matches_manifest_and_preserves_legacy_quantity_capacity()
    {
        await using var db = fixture.Web.Database.CreateHostContext();
        var manifest = await AssertCurrent(db);
        var reservations = manifest.Tables.Single(x => x.Identity.Name == "inventoryreservations");
        var reserved = reservations.Columns.Single(x => x.Name == "reservedqty");
        Assert.Equal(19, reserved.Precision); Assert.Equal(4, reserved.Scale);
        foreach (var (table, column) in new[] { ("inventorybalances", "onhandqty"), ("inventorybalances", "reservedqty"),
            ("inventorytransactions", "quantitychange"), ("inventorycostlayers", "remainingquantity") })
        {
            var quantity = manifest.Tables.Single(x => x.Identity.Name == table).Columns.Single(x => x.Name == column);
            Assert.Equal(18, quantity.Precision); Assert.Equal(4, quantity.Scale);
        }
        Assert.Equal(6, manifest.Tables.Single(x => x.Identity.Name == "deliveryreturnedcostfragments").Columns.Single(x => x.Name == "unitcost").Scale);
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task M02_Historical71_maximum_legacy_reservation_survives_upgrade_safe_Down_and_reupgrade_exactly()
    {
        await using var sql = new InventoryPostingLocalDb(); await sql.MigrateAsync(Previous);
        var seed = await sql.SeedInventoryCatalogAsync();
        await using var db = sql.CreateTenantContext(seed.StoreId);
        const decimal maximum = 999999999999999.999m;
        var exactMaximum = new SqlParameter("@d05HistoricalMaximum", SqlDbType.Decimal)
            { Precision = 18, Scale = 3, Value = maximum };
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT InventoryReservations(StoreId,WarehouseId,ProductVariantId,ReferenceType,ReferenceId,ReservedQty,Status,ReservedAtUtc,ReleasedAtUtc,CreatedAtUtc,IsDeleted)
            VALUES({seed.StoreId},{seed.WarehouseId},{seed.ProductVariantId},8,N'D05-HISTORICAL-RELEASED-CAPACITY',{exactMaximum},2,SYSUTCDATETIME(),SYSUTCDATETIME(),SYSUTCDATETIME(),0);
            """);
        async Task<decimal> Read()
        {
            return await db.Database.SqlQuery<decimal>($"""
                SELECT ReservedQty AS Value FROM InventoryReservations
                WHERE StoreId={seed.StoreId} AND ReferenceId=N'D05-HISTORICAL-RELEASED-CAPACITY'
                """).SingleAsync();
        }
        var before = await Read(); Assert.Equal(maximum, before);
        await sql.MigrateAsync(); Assert.Equal(before, await Read()); await AssertCurrent(db);
        await sql.MigrateAsync(Previous); Assert.Equal(before, await Read());
        Assert.Equal(71, (await db.Database.GetAppliedMigrationsAsync()).Count());
        await sql.MigrateAsync(); Assert.Equal(before, await Read()); await AssertCurrent(db);
    }

    [Fact]
    public async Task M03_Unsafe_Down_with_promoted_hold_is_rejected_before_DDL_and_current_graph_remains_intact()
    {
        var isolated = new DeliveryD02Fixture(); await isolated.InitializeAsync();
        try
        {
            using var c = await DeliveryD05Case.CreateAsync(isolated); await c.Picking.ClaimAsync(); await c.Picking.ReportAsync("8");
            var before = await c.SnapshotAsync();
            await using var db = isolated.Web.Database.CreateHostContext();
            var error = await Assert.ThrowsAsync<SqlException>(() => db.GetService<IMigrator>().MigrateAsync(Previous));
            Assert.Equal(51029, error.Number);
            Assert.Equal(before, await c.SnapshotAsync()); await AssertCurrent(db);
        }
        finally { await isolated.DisposeAsync(); }
    }

    [Fact]
    public async Task M04_Promoted_balance_marker_cannot_go_back_to_legacy_even_after_every_hold_is_released()
    {
        using var c = await DeliveryD05Case.CreateAsync(fixture); await c.Picking.ClaimAsync(); await c.Picking.ReportAsync("8");
        var detail = await c.Picking.GetAsync();
        await using var db = c.Picking.Source.Context();
        await c.Service(db).CancelBeforeHandoverAsync(c.Id, new(Guid.NewGuid(), detail.Delivery.Version, "Release hold"));
        var variant = await c.VariantAsync(); var before = await c.SnapshotAsync();
        var error = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE InventoryBalances SET QuantityPrecisionVersion=0
            WHERE StoreId={c.Picking.Account.Store.StoreId} AND WarehouseId={c.Picking.Account.Store.WarehouseId} AND ProductVariantId={variant}
            """));
        Assert.Equal(51021, error.Number);
        Assert.Equal(before, await c.SnapshotAsync());
    }

    [Theory]
    [InlineData("amount")]
    [InlineData("source")]
    [InlineData("interval")]
    [InlineData("delete")]
    public async Task M05_Durable_budget_debits_are_append_only_and_cannot_change_value_source_or_interval(string fault)
    {
        using var c = await DeliveryD05Case.CreateAsync(fixture); await c.ReadyAsync(); await c.DispatchAsync();
        await using var db = c.Picking.Source.Context();
        var id = await db.InventoryCostBudgetDebits.OrderBy(x => x.Id).Select(x => x.Id).FirstAsync();
        var before = await c.SnapshotAsync();
        var statement = fault switch {
            "amount" => "UPDATE InventoryCostBudgetDebits SET Amount=Amount+1 WHERE Id=@id",
            "source" => "UPDATE InventoryCostBudgetDebits SET SourceLegalEntityId=0 WHERE Id=@id",
            "interval" => "UPDATE InventoryCostBudgetDebits SET EndQuantity=EndQuantity+0.0001 WHERE Id=@id",
            _ => "DELETE InventoryCostBudgetDebits WHERE Id=@id" };
        var error = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync(statement, new SqlParameter("@id", id)));
        Assert.Equal(51026, error.Number); Assert.Equal(before, await c.SnapshotAsync());
    }

    [Fact]
    public async Task M06_Duplicate_or_overlapping_return_fragments_are_rejected_by_SQL_and_original_receiving_evidence_survives()
    {
        using var c = await DeliveryD05Case.CreateAsync(fixture); await c.ReadyAsync(); await c.DispatchAsync(); await c.ReceiveAsync("2");
        await using var db = c.Picking.Source.Context();
        var id = await db.DeliveryReturnedCostFragments.Where(x => x.DeliveryOrderId == c.Id).Select(x => x.Id).SingleAsync();
        var before = await c.SnapshotAsync();
        var error = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT DeliveryReturnedCostFragments(StoreId,DeliveryOrderId,DeliveryOrderLineId,SourceWarehouseId,SourceLegalEntityId,VariantId,
              DeliveryJournalEntryId,DispatchCostFragmentId,InventoryTransactionId,InventoryValuationEntryId,StartQuantity,EndQuantity,BaseQuantity,UnitCost,CostAmount,IsDeleted)
            SELECT StoreId,DeliveryOrderId,DeliveryOrderLineId,SourceWarehouseId,SourceLegalEntityId,VariantId,
              DeliveryJournalEntryId,DispatchCostFragmentId,InventoryTransactionId,InventoryValuationEntryId,StartQuantity,EndQuantity,BaseQuantity,UnitCost,CostAmount,0
            FROM DeliveryReturnedCostFragments WHERE Id={id}
            """));
        Assert.Contains(error.Number, new[] { 2601, 2627 }); Assert.Equal(before, await c.SnapshotAsync());
    }

    [Fact]
    public async Task M07_SQL_budget_source_foreign_key_rejects_another_store_even_when_all_local_numeric_fields_are_valid()
    {
        using var c = await DeliveryD05Case.CreateAsync(fixture); await c.ReadyAsync(); await c.DispatchAsync();
        await using var db = c.Picking.Source.Context();
        var id = await db.DeliveryDispatchCostFragments.Where(x => x.DeliveryOrderId == c.Id).OrderBy(x => x.Id).Select(x => x.InventoryCostBudgetDebitId).FirstAsync();
        var otherStore = fixture.Web.Stores[1].StoreId; var before = await c.SnapshotAsync();
        var error = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT InventoryCostBudgetDebits(StoreId,WarehouseId,ProductVariantId,SourceLegalEntityId,InventoryCostLayerId,InventoryTransactionId,
              InventoryValuationEntryId,InventoryCostLayerAllocationId,AnchorQuantity,AnchorAmount,StartQuantity,EndQuantity,UnitCost,Amount,IsProvisionalResolution,IsDeleted)
            SELECT {otherStore},WarehouseId,ProductVariantId,SourceLegalEntityId,InventoryCostLayerId,InventoryTransactionId,
              InventoryValuationEntryId,InventoryCostLayerAllocationId,AnchorQuantity,AnchorAmount,StartQuantity,EndQuantity,UnitCost,Amount,0,0
            FROM InventoryCostBudgetDebits WHERE Id={id}
            """));
        Assert.Equal(547, error.Number); Assert.Equal(before, await c.SnapshotAsync());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task M08_Fourth_digit_in_any_historical_reservation_status_refuses_Down_before_any_DDL(int status)
    {
        await using var sql = new InventoryPostingLocalDb(); await sql.MigrateAsync(Previous);
        var seed = await sql.SeedInventoryCatalogAsync(); await using var db = sql.CreateTenantContext(seed.StoreId);
        if (status == 1)
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT InventoryBalances(StoreId,WarehouseId,ProductVariantId,OnHandQty,ReservedQty,InventoryValue,AverageUnitCost,CreatedAtUtc,IsDeleted)
                VALUES({seed.StoreId},{seed.WarehouseId},{seed.ProductVariantId},1,1,10,10,SYSUTCDATETIME(),0);
                """);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT InventoryReservations(StoreId,WarehouseId,ProductVariantId,ReferenceType,ReferenceId,ReservedQty,Status,ReservedAtUtc,CreatedAtUtc,IsDeleted)
            VALUES({seed.StoreId},{seed.WarehouseId},{seed.ProductVariantId},8,N'D05-HISTORICAL-FOURTH-DIGIT',1,{status},SYSUTCDATETIME(),SYSUTCDATETIME(),0);
            """);
        await sql.MigrateAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE InventoryReservations SET ReservedQty=0.0001 WHERE StoreId={seed.StoreId} AND ReferenceId=N'D05-HISTORICAL-FOURTH-DIGIT';
            """);
        if (status == 1)
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE InventoryBalances SET ReservedQty=0.0001 WHERE StoreId={seed.StoreId} AND WarehouseId={seed.WarehouseId} AND ProductVariantId={seed.ProductVariantId};
                """);
        var error = await Assert.ThrowsAsync<SqlException>(() => sql.MigrateAsync(Previous)); Assert.Equal(51029, error.Number);
        var row = await db.InventoryReservations.AsNoTracking().SingleAsync(x => x.ReferenceId == "D05-HISTORICAL-FOURTH-DIGIT");
        Assert.Equal(.0001m, row.ReservedQty); Assert.Equal(status, (int)row.Status);
        Assert.Null(row.DeliveryOrderId); Assert.Null(row.DeliveryOrderLineId); await AssertCurrent(db);
        Assert.All(await db.InventoryBalances.AsNoTracking().ToArrayAsync(), x => Assert.Equal((byte)0, x.QuantityPrecisionVersion));
    }

    [Theory]
    [InlineData("DROP TRIGGER [TR_InventoryCostBudgetDebits_Immutable];")]
    [InlineData("DISABLE TRIGGER [TR_InventoryBalances_DeliveryPrecision] ON [InventoryBalances];")]
    [InlineData("ALTER TRIGGER [TR_DeliveryReturnedCostFragments_StockSource] ON [DeliveryReturnedCostFragments] AFTER INSERT AS BEGIN RETURN; END;")]
    [InlineData("ALTER TABLE [InventoryCostBudgetDebits] NOCHECK CONSTRAINT [CK_InventoryCostBudgetDebits_Interval]; ALTER TABLE [InventoryCostBudgetDebits] CHECK CONSTRAINT [CK_InventoryCostBudgetDebits_Interval];")]
    [InlineData("loosen-journal")]
    [InlineData("loosen-returned-fragment")]
    [InlineData("loosen-debit")]
    [InlineData("loosen-layer")]
    [InlineData("loosen-reservation")]
    [InlineData("loosen-transaction")]
    public async Task M09_Preflight_rejects_missing_disabled_changed_or_untrusted_new_D05_durable_protections(string statement)
    {
        await using var db = fixture.Web.Database.CreateHostContext();
        await using var tx = await db.Database.BeginTransactionAsync();
        try
        {
            if (statement.StartsWith("loosen-", StringComparison.Ordinal))
            {
                var (table, constraint, predicate, loosened) = statement switch {
                    "loosen-journal" => ("DeliveryJournalEntries", "CK_DeliveryJournalEntries_StockSource", "[MoneyAmount]=0", "[MoneyAmount]>=0"),
                    "loosen-returned-fragment" => ("DeliveryReturnedCostFragments", "CK_DeliveryReturnedCostFragments_Interval", "[EndQuantity]>[StartQuantity]", "[EndQuantity]>=[StartQuantity]"),
                    "loosen-debit" => ("InventoryCostBudgetDebits", "CK_InventoryCostBudgetDebits_Interval", "[EndQuantity]<=[AnchorQuantity]", "[EndQuantity]<=[AnchorQuantity]+1"),
                    "loosen-layer" => ("InventoryCostLayers", "CK_InventoryCostLayers_Budget", "[RemainingCostAmount]<=[BudgetAnchorAmount]", "[RemainingCostAmount]<=[BudgetAnchorAmount]+1"),
                    "loosen-reservation" => ("InventoryReservations", "CK_InventoryReservations_DeliverySource", "[DeliveryRevision]>0", "[DeliveryRevision]>=0"),
                    _ => ("InventoryTransactions", "CK_InventoryTransactions_DeliveryPosting", "[QuantityChange]>0", "[QuantityChange]>=0") };
                var source = Assert.Single(db.GetService<IDesignTimeModel>().Model.GetEntityTypes()
                    .Single(x => x.GetTableName() == table).GetCheckConstraints(), x => x.Name == constraint).Sql;
                var first = source.IndexOf(predicate, StringComparison.Ordinal);
                Assert.True(first >= 0); Assert.Equal(first, source.LastIndexOf(predicate, StringComparison.Ordinal));
                var changed = source.Replace(predicate, loosened, StringComparison.Ordinal);
                Assert.NotEqual(source, changed);
                // Table/constraint identifiers are exact fixed fixture constants;
                // change one predicate only, retaining trusted/enabled metadata.
                await using var command = db.Database.GetDbConnection().CreateCommand();
                command.Transaction = tx.GetDbTransaction();
                command.CommandText = $"ALTER TABLE [{table}] DROP CONSTRAINT [{constraint}]; ALTER TABLE [{table}] WITH CHECK ADD CONSTRAINT [{constraint}] CHECK ({changed});";
                await command.ExecuteNonQueryAsync();
            }
            else await db.Database.ExecuteSqlRawAsync(statement);
            var preflight = new SqlServerDatabaseBaselinePreflight(db, new EfCoreDatabaseMigrationCatalog(db),
                new SqlServerDatabaseObjectInventoryReader(db), new EfCoreDatabaseSchemaManifestCatalog(db), new SqlServerSchemaSnapshotReader(db));
            var result = await preflight.InspectAsync();
            Assert.False(result.IsAllowed); Assert.Equal("StructuralSchemaMismatch", result.SafeReasonCode);
        }
        finally { await tx.RollbackAsync(); }
        await AssertCurrent(db);
    }

    [Theory]
    [InlineData("pos", "amount")]
    [InlineData("pos", "final")]
    [InlineData("transfer-out", "amount")]
    [InlineData("transfer-in", "unit")]
    [InlineData("neutral", "amount")]
    [InlineData("historical", "amount")]
    [InlineData("historical", "subkey")]
    [InlineData("historical", "source-type")]
    public async Task M10_Finalized_budget_proof_owner_transactions_cannot_change_cost_or_source_after_positive_construction(string owner, string fault)
    {
        using var c = await DeliveryD05Case.CreateAsync(fixture, ordered: 1, stock: [(4m, 12m)]);
        var variant = await c.VariantAsync(); int transactionId;
        if (owner == "historical")
        {
            await c.ReadyAsync("1"); await c.DispatchAsync();
            await using var read = c.Picking.Source.Context();
            var source = await read.InventoryCostLayers.SingleAsync(x => x.ProductVariantId == variant);
            Assert.Equal((byte)1, source.CostBudgetPolicy);
            transactionId = source.InventoryTransactionId;
        }
        else
        {
            await InventorySourceMirrorSqlServerTests.PromoteAndRelease(c);
            if (owner == "pos")
            {
                var sale = await c.FinalizePosCashSaleAsync(1m);
                await using var read = c.Picking.Source.Context();
                transactionId = await read.InventoryTransactions.Where(x => x.ReferenceType == InventoryReferenceType.Order &&
                    x.ReferenceId == sale.OrderId.ToString() && x.TransactionType == InventoryTransactionType.SaleIssue).Select(x => x.Id).SingleAsync();
            }
            else
            {
                var transfer = await InventorySourceMirrorSqlServerTests.TransferDocument(c, 4m);
                await using (var seed = c.Picking.Source.Context())
                {
                    // This ordinary central-writer provisional fixture is distinct
                    // from the authenticated POS proof above.
                    var negative = new InventoryMovementFactory().CreateSaleFinalize(transfer.WarehouseId, variant,
                        97501, 1, "Owned destination provisional", 2m, 10m);
                    negative.AllowNegativeBalance = true;
                    await InventoryPosPostingContractTests.CreateRealMovementService(seed).CreateAsync(negative);
                }
                await InventorySourceMirrorSqlServerTests.PostTransfer(c, transfer.DocumentId, transfer.LineId);
                await using var read = c.Picking.Source.Context();
                if (owner == "neutral")
                    transactionId = await read.InventoryTransactions.Where(x => x.ProductVariantId == variant &&
                        x.TransactionType == InventoryTransactionType.Revaluation).Select(x => x.Id).SingleAsync();
                else
                {
                    var type = owner == "transfer-out" ? InventoryTransactionType.TransferOut : InventoryTransactionType.TransferIn;
                    transactionId = await read.InventoryTransactions.Where(x => x.ReferenceType == InventoryReferenceType.StockTransfer &&
                        x.ReferenceId == transfer.DocumentId.ToString() && x.TransactionType == type).Select(x => x.Id).SingleAsync();
                }
            }
        }
        await using var db = c.Picking.Source.Context();
        var original = await db.InventoryTransactions.AsNoTracking().SingleAsync(x => x.Id == transactionId);
        Assert.Equal(owner == "historical" ? (byte)0 : (byte)1, original.PostingPrecisionPolicy);
        Assert.NotNull(original.CostFinalizedAtUtc); Assert.False(original.IsProvisionalCost);
        var before = await c.SnapshotAsync(); var originalProof = Json(original);
        var statement = fault switch {
            "amount" => "UPDATE InventoryTransactions SET TotalCost=TotalCost+1 WHERE Id=@id",
            "unit" => "UPDATE InventoryTransactions SET UnitCostSnapshot=UnitCostSnapshot+1 WHERE Id=@id",
            "final" => "UPDATE InventoryTransactions SET CostFinalizedAtUtc=NULL WHERE Id=@id",
            "subkey" => "UPDATE InventoryTransactions SET ReferenceSubKey=N'changed-owned-proof' WHERE Id=@id",
            _ => "UPDATE InventoryTransactions SET CostSourceType=0 WHERE Id=@id" };
        var error = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync(statement, new SqlParameter("@id", transactionId)));
        Assert.Equal(51023, error.Number); Assert.Equal(before, await c.SnapshotAsync());
        Assert.Equal(originalProof, Json(await db.InventoryTransactions.AsNoTracking().SingleAsync(x => x.Id == transactionId)));
    }

    [Theory]
    [InlineData("null-to-empty")]
    [InlineData("different")]
    [InlineData("case")]
    [InlineData("trailing-space")]
    [InlineData("final-null")]
    [InlineData("final-change")]
    public async Task M11_Promoted_source_valuation_preserves_exact_nullable_source_subkey_and_final_timestamp(string fault)
    {
        using var c = await DeliveryD05Case.CreateAsync(fixture, ordered: 1, stock: [(4m, 12m)]);
        var variant = await c.VariantAsync(); int valuationId;
        if (fault is "case" or "trailing-space")
        {
            await InventorySourceMirrorSqlServerTests.PromoteAndRelease(c);
            var transfer = await InventorySourceMirrorSqlServerTests.TransferDocument(c, 4m);
            await InventorySourceMirrorSqlServerTests.PostTransfer(c, transfer.DocumentId, transfer.LineId);
            await using var read = c.Picking.Source.Context();
            valuationId = await read.InventoryCostLayers.Where(x => x.ProductVariantId == variant && x.WarehouseId == transfer.WarehouseId)
                .Select(x => x.InventoryValuationEntryId).SingleAsync();
        }
        else
        {
            await c.ReadyAsync("1"); await c.DispatchAsync();
            await using var read = c.Picking.Source.Context();
            valuationId = await read.InventoryCostLayers.Where(x => x.ProductVariantId == variant).Select(x => x.InventoryValuationEntryId).SingleAsync();
        }
        await using var db = c.Picking.Source.Context();
        var original = await db.InventoryValuationEntries.AsNoTracking().SingleAsync(x => x.Id == valuationId);
        Assert.NotNull(original.CostFinalizedAtUtc);
        string? replacement = fault switch {
            "null-to-empty" => "",
            "case" => original.SourceReferenceSubKey!.ToUpperInvariant(),
            "trailing-space" => original.SourceReferenceSubKey + " ",
            _ => "changed-owned-source-subkey" };
        if (fault is "case" or "trailing-space")
        {
            Assert.False(string.IsNullOrEmpty(original.SourceReferenceSubKey));
            Assert.NotEqual(original.SourceReferenceSubKey, replacement);
        }
        else Assert.Null(original.SourceReferenceSubKey);
        var before = await c.SnapshotAsync(); var originalProof = Json(original);
        var statement = fault switch {
            "final-null" => "UPDATE InventoryValuationEntries SET CostFinalizedAtUtc=NULL WHERE Id=@id",
            "final-change" => "UPDATE InventoryValuationEntries SET CostFinalizedAtUtc=DATEADD(second,1,CostFinalizedAtUtc) WHERE Id=@id",
            _ => "UPDATE InventoryValuationEntries SET SourceReferenceSubKey=@value WHERE Id=@id" };
        var error = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync(
            statement,
            new SqlParameter("@value", replacement), new SqlParameter("@id", valuationId)));
        Assert.Equal(51025, error.Number); Assert.Equal(before, await c.SnapshotAsync());
        Assert.Equal(originalProof, Json(await db.InventoryValuationEntries.AsNoTracking().SingleAsync(x => x.Id == valuationId)));
    }

    private static async Task<DatabaseSchemaManifest> AssertCurrent(AppDbContext db)
    {
        var applied = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
        Assert.Equal(72, applied.Length); Assert.Equal(Current, applied[^1]); Assert.Equal(Previous, applied[^2]);
        var catalog = new EfCoreDatabaseSchemaManifestCatalog(db); Assert.True(catalog.TryGetManifestForAppliedMigrationPrefix(applied, out var expected));
        await db.Database.OpenConnectionAsync();
        DatabaseSchemaManifest actual;
        try { actual = await new SqlServerSchemaSnapshotReader(db).ReadAsync(applied); }
        finally { await db.Database.CloseConnectionAsync(); }
        var comparison = DatabaseSchemaComparer.Compare(expected, actual);
        Assert.True(comparison.IsMatch, Json(comparison)); Assert.Equal(0, comparison.Mismatches.Total);
        Assert.Equal(27, actual.Tables.Sum(x => x.Triggers.Count));
        return actual;
    }
    private static string Json(object value) => System.Text.Json.JsonSerializer.Serialize(value);
}
