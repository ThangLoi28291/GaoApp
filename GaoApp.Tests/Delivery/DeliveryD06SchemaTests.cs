using System.Data;
using System.Text.Json;
using GaoApp.Domain.Delivery;
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

[Collection("DeliveryD06"), Trait("Category", "DeliveryD06")]
public sealed class DeliveryD06SchemaTests(DeliveryD02Fixture fixture)
{
    private const string Previous = "20261007100000_AddDeliveryInventoryPosting";
    private const string Current = "20261008100000_AddDeliveryTrips";

    [Fact]
    public async Task M01_Current73_real_SQL_matches_the_complete_manifest_and_seven_store_scoped_custody_tables()
    {
        await using var db = fixture.Web.Database.CreateHostContext();
        var actual = await AssertSchema(db, current: true);
        Assert.False(db.Database.HasPendingModelChanges());
        var names = new[] { "deliverytrips", "deliverytriporders", "deliverycustodyevents", "deliverycustodylines",
            "deliverytriprevisions", "deliverytripcommandreceipts", "deliverytripoutboxmessages" };
        foreach (var name in names)
            Assert.Single(actual.Tables, x => x.Identity.Name == name);
        var quantity = actual.Tables.Single(x => x.Identity.Name == "deliverycustodylines").Columns.Single(x => x.Name == "confirmedbasequantity");
        Assert.Equal(18, quantity.Precision); Assert.Equal(4, quantity.Scale);
        Assert.True(actual.Tables.Single(x => x.Identity.Name == "deliveryjournalentries").Columns.Single(x => x.Name == "deliveryorderlineid").IsNullable);
        Assert.True(actual.Tables.Single(x => x.Identity.Name == "deliveryjournalentries").Columns.Single(x => x.Name == "variantid").IsNullable);
    }

    [Fact]
    public async Task M02_Genuine_D05_export_survives_empty_trip_Down_upgrade_without_manufactured_courier_or_custody()
    {
        var isolated = new DeliveryD02Fixture(); await isolated.InitializeAsync();
        try
        {
            using var c = await DeliveryD05Case.CreateAsync(isolated); await c.ReadyAsync(); await c.DispatchAsync();
            var before = await c.SnapshotAsync(); await using var db = isolated.Web.Database.CreateHostContext();
            try
            {
                await db.GetService<IMigrator>().MigrateAsync(Previous); await AssertSchema(db, current: false);
                Assert.Equal(before, await c.SnapshotAsync());
                await db.GetService<IMigrator>().MigrateAsync(Current); await AssertSchema(db, current: true);
                Assert.Equal(before, await c.SnapshotAsync());
                var inventory = await c.GetAsync(); Assert.Null(inventory.Custody); Assert.Null(inventory.InCustodyBaseQuantityText);
                Assert.Empty(await db.DeliveryCustodyEvents.AsNoTracking().ToArrayAsync());
            }
            finally { await db.GetService<IMigrator>().MigrateAsync(Current); await AssertSchema(db, current: true); }
        }
        finally { await isolated.DisposeAsync(); }
    }

    [Theory]
    [InlineData("draft")]
    [InlineData("abandoned")]
    [InlineData("handed")]
    [InlineData("departed")]
    public async Task M03_Any_planned_or_physical_trip_history_refuses_Down_before_DDL_and_preserves_all_facts(string state)
    {
        var isolated = new DeliveryD02Fixture(); await isolated.InitializeAsync();
        try
        {
            using var c = await DeliveryD06Case.CreateAsync(isolated); await c.CreateDraftAsync();
            if (state is "handed" or "departed") await c.HandoverAsync();
            if (state == "departed") await c.DepartAsync();
            if (state == "abandoned")
            {
                await using var abandon = c.Context();
                await c.Service(abandon).AbandonDraftAsync(c.TripId,
                    new(Guid.NewGuid(), (await c.DetailAsync()).Version, "Keep abandoned planning history"));
            }
            var before = await c.SnapshotAsync(); await using var db = isolated.Web.Database.CreateHostContext();
            try
            {
                var error = await Assert.ThrowsAsync<SqlException>(() => db.GetService<IMigrator>().MigrateAsync(Previous));
                Assert.Equal(51039, error.Number); Assert.Equal(before, await c.SnapshotAsync());
                await AssertSchema(db, current: true);
            }
            finally { await db.GetService<IMigrator>().MigrateAsync(Current); await AssertSchema(db, current: true); }
        }
        finally { await isolated.DisposeAsync(); }
    }

    [Theory]
    [InlineData("warehouse")]
    [InlineData("legal-entity")]
    [InlineData("store")]
    [InlineData("courier-store")]
    public async Task M04_SQL_source_and_same_store_courier_foreign_keys_reject_valid_but_unrelated_identifiers(string fault)
    {
        using var c = await DeliveryD06Case.CreateAsync(fixture); await c.CreateDraftAsync();
        var other = await fixture.Web.AddAccountAsync(fixture.Web.Stores[1]);
        await using var db = c.Context(); var binding = await db.DeliveryTripOrders.AsNoTracking().SingleAsync(x => x.TripId == c.TripId && x.DeliveryOrderId == c.Orders[0].Id);
        var origin = c.Orders[1].Picking.Source.Detail; var before = await c.SnapshotAsync();
        var statement = fault switch {
            "warehouse" => "UPDATE DeliveryTripOrders SET SourceWarehouseId=@foreign WHERE Id=@id",
            "legal-entity" => "UPDATE DeliveryTripOrders SET SourceLegalEntityId=@foreign WHERE Id=@id",
            "store" => "UPDATE DeliveryTripOrders SET StoreId=@foreign WHERE Id=@id",
            _ => "UPDATE DeliveryTrips SET CourierUserId=@foreign,Revision=Revision+1 WHERE Id=@id" };
        var value = fault switch { "warehouse" => origin.SourceWarehouseId, "legal-entity" => origin.SourceLegalEntityId,
            "store" => other.Store.StoreId, _ => other.UserId };
        var error = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync(statement,
            new SqlParameter("@foreign", value), new SqlParameter("@id", fault == "courier-store" ? c.TripId : binding.Id)));
        Assert.Equal(547, error.Number); Assert.Equal(before, await c.SnapshotAsync());
    }

    [Theory]
    [InlineData("DeliveryCustodyEvents", "update")]
    [InlineData("DeliveryCustodyEvents", "delete")]
    [InlineData("DeliveryCustodyLines", "update")]
    [InlineData("DeliveryCustodyLines", "delete")]
    [InlineData("DeliveryTripRevisions", "update")]
    [InlineData("DeliveryTripRevisions", "delete")]
    [InlineData("DeliveryTripCommandReceipts", "update")]
    [InlineData("DeliveryTripCommandReceipts", "delete")]
    [InlineData("DeliveryTripOutboxMessages", "update")]
    [InlineData("DeliveryTripOutboxMessages", "delete")]
    public async Task M05_Physical_and_command_evidence_is_append_only_for_both_update_and_delete(string table, string operation)
    {
        using var c = await DeliveryD06Case.CreateAsync(fixture); await c.CreateDraftAsync(); await c.HandoverAsync();
        await using var db = c.Context();
        var id = table switch {
            "DeliveryCustodyEvents" => await db.DeliveryCustodyEvents.Where(x => x.TripId == c.TripId).OrderBy(x => x.Id).Select(x => x.Id).FirstAsync(),
            "DeliveryCustodyLines" => await db.DeliveryCustodyLines.Where(x => db.DeliveryCustodyEvents.Any(e => e.Id == x.CustodyEventId && e.TripId == c.TripId)).OrderBy(x => x.Id).Select(x => x.Id).FirstAsync(),
            "DeliveryTripRevisions" => await db.DeliveryTripRevisions.Where(x => x.TripId == c.TripId).OrderBy(x => x.Id).Select(x => x.Id).FirstAsync(),
            "DeliveryTripCommandReceipts" => await db.DeliveryTripCommandReceipts.Where(x => x.TripId == c.TripId).OrderBy(x => x.Id).Select(x => x.Id).FirstAsync(),
            _ => await db.DeliveryTripOutboxMessages.Where(x => x.TripId == c.TripId).OrderBy(x => x.Id).Select(x => x.Id).FirstAsync() };
        var before = await c.SnapshotAsync();
        // Identifier values come only from the five fixed InlineData table names.
        var statement = operation == "update" ? $"UPDATE [{table}] SET IsDeleted=1 WHERE Id=@id" : $"DELETE [{table}] WHERE Id=@id";
        var error = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync(statement, new SqlParameter("@id", id)));
        Assert.Equal(51035, error.Number); Assert.Equal(before, await c.SnapshotAsync());
    }

    [Theory]
    [InlineData("DROP TRIGGER [TR_DeliveryCustodyLines_Source];")]
    [InlineData("DISABLE TRIGGER [TR_DeliveryTripCommandReceipts_Namespace] ON [DeliveryTripCommandReceipts];")]
    [InlineData("ALTER TRIGGER [TR_DeliveryTripOrders_Source] ON [DeliveryTripOrders] AFTER INSERT, UPDATE, DELETE AS BEGIN RETURN; END;")]
    [InlineData("ALTER TABLE [DeliveryCustodyLines] NOCHECK CONSTRAINT [CK_DeliveryCustodyLines_Quantity]; ALTER TABLE [DeliveryCustodyLines] CHECK CONSTRAINT [CK_DeliveryCustodyLines_Quantity];")]
    [InlineData("quantity-check")]
    [InlineData("trip-state-check")]
    [InlineData("custody-kind-check")]
    public async Task M06_Current_preflight_rejects_missing_disabled_changed_untrusted_and_loosened_new_protections(string statement)
    {
        await using var db = fixture.Web.Database.CreateHostContext(); await using var tx = await db.Database.BeginTransactionAsync();
        try
        {
            if (statement.EndsWith("-check", StringComparison.Ordinal))
            {
                var (table, name, predicate, changed) = statement switch
                {
                    "quantity-check" => ("DeliveryCustodyLines", "CK_DeliveryCustodyLines_Quantity", "[ConfirmedBaseQuantity]>=0", "[ConfirmedBaseQuantity]>=-1"),
                    "custody-kind-check" => ("DeliveryCustodyEvents", "CK_DeliveryCustodyEvents_Kind", "LEN(LTRIM(RTRIM([Reason])))>0", "LEN(LTRIM(RTRIM([Reason])))>=0"),
                    _ => ("DeliveryTrips", "CK_DeliveryTrips_State", "[DepartedAtUtc]>=[HandedOverAtUtc]", "[DepartedAtUtc]>=DATEADD(day,-1,[HandedOverAtUtc])")
                };
                var source = Assert.Single(db.GetService<IDesignTimeModel>().Model.GetEntityTypes().Single(x => x.GetTableName() == table)
                    .GetCheckConstraints(), x => x.Name == name).Sql;
                var first = source.IndexOf(predicate, StringComparison.Ordinal);
                Assert.True(first >= 0); Assert.Equal(first, source.LastIndexOf(predicate, StringComparison.Ordinal));
                var altered = source.Replace(predicate, changed, StringComparison.Ordinal); Assert.NotEqual(source, altered);
                await using var command = db.Database.GetDbConnection().CreateCommand(); command.Transaction = tx.GetDbTransaction();
                command.CommandText = $"ALTER TABLE [{table}] DROP CONSTRAINT [{name}]; ALTER TABLE [{table}] WITH CHECK ADD CONSTRAINT [{name}] CHECK ({altered});";
                await command.ExecuteNonQueryAsync();
            }
            else await db.Database.ExecuteSqlRawAsync(statement);
            var preflight = new SqlServerDatabaseBaselinePreflight(db, new EfCoreDatabaseMigrationCatalog(db),
                new SqlServerDatabaseObjectInventoryReader(db), new EfCoreDatabaseSchemaManifestCatalog(db), new SqlServerSchemaSnapshotReader(db));
            var inspected = await preflight.InspectAsync(); Assert.False(inspected.IsAllowed);
            Assert.Equal("StructuralSchemaMismatch", inspected.SafeReasonCode);
        }
        finally { await tx.RollbackAsync(); }
        await AssertSchema(db, current: true);
    }

    [Theory]
    [InlineData("departure-time")]
    [InlineData("courier")]
    [InlineData("handover-time")]
    public async Task M07_SQL_cannot_rewrite_a_recorded_departure_or_responsible_courier_without_a_new_physical_chain(string fault)
    {
        using var c = await DeliveryD06Case.CreateAsync(fixture); await c.CreateDraftAsync(); await c.HandoverAsync(); await c.DepartAsync();
        await using var db = c.Context(); var before = await c.SnapshotAsync();
        var statement = fault switch {
            "departure-time" => "UPDATE DeliveryTrips SET DepartedAtUtc=DATEADD(second,1,DepartedAtUtc),Revision=Revision+1 WHERE Id=@id",
            "handover-time" => "UPDATE DeliveryTrips SET HandedOverAtUtc=DATEADD(second,-1,HandedOverAtUtc),Revision=Revision+1 WHERE Id=@id",
            _ => "UPDATE DeliveryTrips SET CourierUserId=@courier,Revision=Revision+1 WHERE Id=@id" };
        var error = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync(statement,
            new SqlParameter("@id", c.TripId), new SqlParameter("@courier", c.OtherCourier.UserId)));
        Assert.Equal(51030, error.Number); Assert.Equal(before, await c.SnapshotAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task M08_SQL_command_GUID_namespace_rejects_collision_in_both_receipt_directions(bool tripToOrder)
    {
        using var c = await DeliveryD06Case.CreateAsync(fixture); var create = await c.CreateDraftAsync();
        await using var db = c.Context(); var orderReceipt = await db.DeliveryCommandReceipts.AsNoTracking()
            .Where(x => x.DeliveryOrderId == c.Orders[0].Id).OrderBy(x => x.Id).FirstAsync();
        var before = await c.SnapshotAsync();
        SqlException error;
        if (tripToOrder)
            error = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT DeliveryCommandReceipts(StoreId,DeliveryOrderId,ClientRequestId,ActorUserId,Operation,RequestHash,OutcomeJson,CreatedAtUtc,IsDeleted)
                SELECT StoreId,DeliveryOrderId,{create.ClientRequestId},ActorUserId,Operation,RequestHash,OutcomeJson,SYSUTCDATETIME(),0
                FROM DeliveryCommandReceipts WHERE Id={orderReceipt.Id}
                """));
        else
            error = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT DeliveryTripCommandReceipts(StoreId,TripId,ClientRequestId,ActorUserId,Operation,RequestHash,OutcomeJson,RequiredCapabilities,CreatedAtUtc,IsDeleted)
                SELECT StoreId,TripId,{orderReceipt.ClientRequestId},ActorUserId,Operation,RequestHash,OutcomeJson,RequiredCapabilities,SYSUTCDATETIME(),0
                FROM DeliveryTripCommandReceipts WHERE TripId={c.TripId} AND ClientRequestId={create.ClientRequestId}
                """));
        Assert.Equal(51034, error.Number); Assert.Equal(before, await c.SnapshotAsync());
    }

    private static async Task<DatabaseSchemaManifest> AssertSchema(AppDbContext db, bool current)
    {
        var applied = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
        Assert.Equal(current ? 73 : 72, applied.Length); Assert.Equal(current ? Current : Previous, applied[^1]);
        Assert.Equal(Previous, applied[71]);
        var catalog = new EfCoreDatabaseSchemaManifestCatalog(db);
        Assert.True(catalog.TryGetManifestForAppliedMigrationPrefix(applied, out var expected));
        var wasOpen = db.Database.GetDbConnection().State == ConnectionState.Open;
        if (!wasOpen) await db.Database.OpenConnectionAsync();
        DatabaseSchemaManifest actual;
        try { actual = await new SqlServerSchemaSnapshotReader(db).ReadAsync(applied); }
        finally { if (!wasOpen) await db.Database.CloseConnectionAsync(); }
        var compared = DatabaseSchemaComparer.Compare(expected, actual);
        Assert.True(compared.IsMatch, JsonSerializer.Serialize(compared)); Assert.Equal(0, compared.Mismatches.Total);
        Assert.Equal(current ? 38 : 27, actual.Tables.Sum(x => x.Triggers.Count)); return actual;
    }
}
