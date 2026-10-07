using System.Collections;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using GaoApp.Application.DTOs.Delivery;
using GaoApp.Domain.Common;
using GaoApp.Domain.Delivery;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Data.Migrations;
using GaoApp.Tests.Configuration;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GaoApp.Tests.Delivery;

[Collection("DeliveryD04"), Trait("Category", "DeliveryD04")]
public sealed class DeliveryD04SchemaTests(DeliveryD02Fixture fixture)
{
    private const string Previous = "20261006170000_PreserveHeldOrderShiftChanges";
    private const string Current = "20261006173000_AddDeliveryPicking";
    private const string Latest = "20261007090000_AddMixedQuantityPromotions";

    [Fact]
    public async Task M01_Current70_SQL_manifest_matches_without_creating_picking_work_for_Created_orders()
    {
        using var c = await DeliveryD04Case.CreateAsync(fixture);
        await using var db = c.Source.Context();
        await AssertSchema(db, 71, 15);
        await AssertPickingCheckNormalizationRejectsAlteredSemantics(db);
        Assert.False(db.Database.HasPendingModelChanges());
        Assert.False(await db.DeliveryPickingWorks.AnyAsync(x => x.DeliveryOrderId == c.Id));
        Assert.False(await db.DeliveryPickingLines.AnyAsync(x => x.DeliveryOrderId == c.Id));
        var created = await c.GetAsync();
        Assert.Equal("Created", created.Delivery.State);
        Assert.All(created.Lines, x => Assert.Null(x.ReportedQuantityText));
    }

    [Fact]
    public async Task M02_M06_Genuine69_data_upgrade_safe_Down_and_reupgrade_preserve_D03_provenance_and_quotes()
    {
        await using var sql = new InventoryPostingLocalDb();
        await sql.MigrateAsync(Previous);
        try
        {
            var id = await SeedHistoricalD03(sql);
            await using var db = sql.CreateHostContext();
            await AssertSchema(db, 69, 12);
            var before = await HistoricalSnapshot(db);
            await sql.MigrateAsync();
            Assert.Equal(before, await HistoricalSnapshot(db));
            await AssertSchema(db, 71, 15);
            Assert.False(await db.DeliveryPickingWorks.AnyAsync());
            Assert.False(await db.DeliveryPickingLines.AnyAsync());
            var original = await db.DeliveryOrderLines.AsNoTracking().SingleAsync(x => x.DeliveryOrderId == id);
            Assert.NotNull(original.SourceOrderLineId); Assert.Null(original.OriginalRootLineId);
            await sql.MigrateAsync(Previous);
            Assert.Equal(before, await HistoricalSnapshot(db));
            await AssertSchema(db, 69, 12);
            await sql.MigrateAsync();
            Assert.Equal(before, await HistoricalSnapshot(db));
            await AssertSchema(db, 71, 15);
        }
        finally { await sql.MigrateAsync(); }
    }

    [Theory]
    [InlineData("actor")]
    [InlineData("line-order")]
    [InlineData("line-store")]
    [InlineData("duplicate-line")]
    public async Task M03_Current_work_actor_line_parent_and_unique_identity_are_durable_SQL_constraints(string fault)
    {
        using var c = await DeliveryD04Case.CreateAsync(fixture);
        using var other = await DeliveryD04Case.CreateAsync(fixture);
        await c.ClaimAsync();
        var foreign = await fixture.Web.AddAccountAsync(fixture.Web.Stores[1], "*");
        await using var db = c.Source.Context();
        var before = await PickingSnapshot(db, c.Id);
        var sql = fault switch
        {
            "actor" => $"UPDATE DeliveryPickingWorks SET PickerUserId={foreign.UserId} WHERE DeliveryOrderId={c.Id}",
            "line-order" => $"UPDATE DeliveryPickingLines SET DeliveryOrderLineId={other.Source.Detail.Lines[0].Id} WHERE DeliveryOrderId={c.Id}",
            "line-store" => $"UPDATE DeliveryPickingLines SET StoreId={foreign.Store.StoreId} WHERE DeliveryOrderId={c.Id}",
            _ => $"INSERT DeliveryPickingLines (StoreId,DeliveryOrderId,DeliveryOrderLineId,IsActive,PlannedQuantity,PlannedOriginalCoverage,ReportFactKind,IsDeleted) SELECT StoreId,DeliveryOrderId,DeliveryOrderLineId,1,PlannedQuantity,0,'unreported',0 FROM DeliveryPickingLines WHERE DeliveryOrderId={c.Id}"
        };
        var ex = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync(sql));
        Assert.Contains(ex.Number, fault == "duplicate-line" ? new[] { 2601, 2627 } : new[] { 547, 51008 });
        Assert.Equal(before, await PickingSnapshot(db, c.Id));
    }

    [Theory]
    [InlineData("root-order")]
    [InlineData("conversion-variant")]
    [InlineData("unit-store")]
    [InlineData("missing-source-root")]
    [InlineData("duplicate-source")]
    public async Task M03_Original_source_and_replacement_root_conversion_unit_constraints_reject_forged_SQL(string fault)
    {
        using var c = await DeliveryD04Case.CreateAsync(fixture);
        using var other = await DeliveryD04Case.CreateAsync(fixture);
        var replacement = await c.ReplacementAsync();
        await using var db = c.Source.Context();
        var root = c.Source.Detail.Lines[0].Id;
        var baseUnit = await db.ProductVariants.Where(x => x.Id == replacement.VariantId).Select(x => x.Product.BaseUnitId).SingleAsync();
        await using var foreignDb = fixture.Web.Database.CreateTenantContext(fixture.Web.Stores[1].StoreId);
        var foreignUnit = await foreignDb.Units.Select(x => x.Id).FirstAsync();
        var rootSql = fault == "root-order" ? other.Source.Detail.Lines[0].Id.ToString() : fault == "missing-source-root" ? "NULL" : root.ToString();
        var variant = fault == "conversion-variant" ? c.Account.Store.VariantId : replacement.VariantId;
        var selling = fault == "unit-store" ? foreignUnit : baseUnit;
        var sql = fault == "duplicate-source"
            ? $"INSERT DeliveryOrderLines (StoreId,DeliveryOrderId,SourceCartId,SourceOrderLineId,VariantId,ItemName,UnitName,BaseUnitName,OrderedQuantity,BaseMultiplier,UnitPrice,Gross,LineDiscount,AllocatedOrderDiscount,Net,IsDeleted) SELECT StoreId,DeliveryOrderId,SourceCartId,SourceOrderLineId,VariantId,ItemName,UnitName,BaseUnitName,OrderedQuantity,BaseMultiplier,UnitPrice,Gross,LineDiscount,AllocatedOrderDiscount,Net,0 FROM DeliveryOrderLines WHERE Id={root}"
            : $"INSERT DeliveryOrderLines (StoreId,DeliveryOrderId,SourceCartId,SourceOrderLineId,OriginalRootLineId,VariantId,ProductUnitConversionId,SellingUnitId,BaseUnitId,ItemName,UnitName,BaseUnitName,OrderedQuantity,BaseMultiplier,UnitPrice,Gross,LineDiscount,AllocatedOrderDiscount,Net,IsDeleted) SELECT StoreId,DeliveryOrderId,SourceCartId,NULL,{rootSql},{variant},{replacement.BottleId},{selling},{baseUnit},N'Forged',N'Unit',N'Unit',1,1,20,20,0,0,20,0 FROM DeliveryOrderLines WHERE Id={root}";
        var count = await db.DeliveryOrderLines.CountAsync(x => x.DeliveryOrderId == c.Id);
        var ex = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync(sql));
        Assert.Contains(ex.Number, fault == "duplicate-source" ? new[] { 2601, 2627 } : new[] { 547, 51007 });
        Assert.Equal(count, await db.DeliveryOrderLines.CountAsync(x => x.DeliveryOrderId == c.Id));
    }

    [Fact]
    public async Task M03_Durable_root_trigger_rejects_replacement_chains_even_with_valid_same_order_FKs()
    {
        using var c = await DeliveryD04Case.CreateAsync(fixture);
        var replacement = await c.ReplacementAsync();
        var detail = await c.ClaimAsync(); var root = detail.Lines[0].LineId;
        await c.CommandAsync("plan", new DeliveryPickingPlanRequest(Guid.NewGuid(), detail.Delivery.Version,
            [new(root, "1", null, null)], [new(root, replacement.VariantId, replacement.BottleId, "1", "1")], "Thay hàng", "Khách đồng ý"));
        var quote = Assert.Single((await c.GetAsync()).Lines, x => x.IsReplacement);
        await using var db = c.Source.Context();
        var ex = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT DeliveryOrderLines (StoreId,DeliveryOrderId,SourceCartId,SourceOrderLineId,OriginalRootLineId,VariantId,ProductUnitConversionId,SellingUnitId,BaseUnitId,ItemName,UnitName,BaseUnitName,OrderedQuantity,BaseMultiplier,UnitPrice,Gross,LineDiscount,AllocatedOrderDiscount,Net,IsDeleted)
            SELECT StoreId,DeliveryOrderId,SourceCartId,NULL,Id,VariantId,ProductUnitConversionId,SellingUnitId,BaseUnitId,ItemName,UnitName,BaseUnitName,OrderedQuantity,BaseMultiplier,UnitPrice,Gross,0,0,Net,0 FROM DeliveryOrderLines WHERE Id={quote.LineId}
            """));
        Assert.Equal(51007, ex.Number);
        Assert.Equal(2, await db.DeliveryOrderLines.CountAsync(x => x.DeliveryOrderId == c.Id));
    }

    [Theory]
    [InlineData("DeliveryPickingWorks", "ApprovedRevision=NULL", 547)]
    [InlineData("DeliveryPickingWorks", "ApprovedTotal=NULL", 547)]
    [InlineData("DeliveryPickingWorks", "ApprovedByUserId=NULL", 547)]
    [InlineData("DeliveryPickingWorks", "ApprovedTotal=40.50", 547)]
    [InlineData("DeliveryPickingLines", "ApprovedNet=NULL", 547)]
    [InlineData("DeliveryPickingLines", "ReportedQuantity=NULL", 547)]
    [InlineData("DeliveryPickingLines", "ReportFactKind='plan-removal',ShortageReason=N'Bỏ hàng'", 547)]
    [InlineData("DeliveryPickingLines", "ReportedQuantity=1,ApprovedQuantity=1,ApprovedNet=20,ShortageReason=NULL", 547)]
    [InlineData("DeliveryPickingLines", "ReportedQuantity=3,ApprovedQuantity=3,ShortageReason=N'Vượt lượng'", 547)]
    [InlineData("DeliveryPickingLines", "ApprovedQuantity=3", 547)]
    [InlineData("DeliveryPickingLines", "PlannedQuantity=3,ShortageReason=N'Vượt quote'", 51008)]
    [InlineData("DeliveryPickingLines", "PlannedOriginalCoverage=1", 51008)]
    [InlineData("DeliveryPickingLines", "ApprovedNet=41", 51008)]
    [InlineData("DeliveryPickingLines", "IsActive=0", 547)]
    public async Task M04_SQL_rejects_partial_null_approval_fake_positive_removal_and_unbounded_progress(string table, string assignment, int number)
    {
        using var c = await DeliveryD04Case.CreateAsync(fixture);
        await c.ClaimAsync(); await c.ReportAsync("2"); await c.SubmitAsync();
        await using var db = c.Source.Context();
        var before = await PickingSnapshot(db, c.Id);
        // Select complete literal statements; the fixture ID is a SQL parameter.
        var statement = (table, assignment) switch
        {
            ("DeliveryPickingWorks", "ApprovedRevision=NULL") => "UPDATE DeliveryPickingWorks SET ApprovedRevision=NULL WHERE DeliveryOrderId={0}",
            ("DeliveryPickingWorks", "ApprovedTotal=NULL") => "UPDATE DeliveryPickingWorks SET ApprovedTotal=NULL WHERE DeliveryOrderId={0}",
            ("DeliveryPickingWorks", "ApprovedByUserId=NULL") => "UPDATE DeliveryPickingWorks SET ApprovedByUserId=NULL WHERE DeliveryOrderId={0}",
            ("DeliveryPickingWorks", "ApprovedTotal=40.50") => "UPDATE DeliveryPickingWorks SET ApprovedTotal=40.50 WHERE DeliveryOrderId={0}",
            ("DeliveryPickingLines", "ApprovedNet=NULL") => "UPDATE DeliveryPickingLines SET ApprovedNet=NULL WHERE DeliveryOrderId={0}",
            ("DeliveryPickingLines", "ReportedQuantity=NULL") => "UPDATE DeliveryPickingLines SET ReportedQuantity=NULL WHERE DeliveryOrderId={0}",
            ("DeliveryPickingLines", "ReportFactKind='plan-removal',ShortageReason=N'Bỏ hàng'") => "UPDATE DeliveryPickingLines SET ReportFactKind='plan-removal',ShortageReason=N'Bỏ hàng' WHERE DeliveryOrderId={0}",
            ("DeliveryPickingLines", "ReportedQuantity=1,ApprovedQuantity=1,ApprovedNet=20,ShortageReason=NULL") => "UPDATE DeliveryPickingLines SET ReportedQuantity=1,ApprovedQuantity=1,ApprovedNet=20,ShortageReason=NULL WHERE DeliveryOrderId={0}",
            ("DeliveryPickingLines", "ReportedQuantity=3,ApprovedQuantity=3,ShortageReason=N'Vượt lượng'") => "UPDATE DeliveryPickingLines SET ReportedQuantity=3,ApprovedQuantity=3,ShortageReason=N'Vượt lượng' WHERE DeliveryOrderId={0}",
            ("DeliveryPickingLines", "ApprovedQuantity=3") => "UPDATE DeliveryPickingLines SET ApprovedQuantity=3 WHERE DeliveryOrderId={0}",
            ("DeliveryPickingLines", "PlannedQuantity=3,ShortageReason=N'Vượt quote'") => "UPDATE DeliveryPickingLines SET PlannedQuantity=3,ShortageReason=N'Vượt quote' WHERE DeliveryOrderId={0}",
            ("DeliveryPickingLines", "PlannedOriginalCoverage=1") => "UPDATE DeliveryPickingLines SET PlannedOriginalCoverage=1 WHERE DeliveryOrderId={0}",
            ("DeliveryPickingLines", "ApprovedNet=41") => "UPDATE DeliveryPickingLines SET ApprovedNet=41 WHERE DeliveryOrderId={0}",
            ("DeliveryPickingLines", "IsActive=0") => "UPDATE DeliveryPickingLines SET IsActive=0 WHERE DeliveryOrderId={0}",
            _ => throw new ArgumentException("Unknown corruption fixture statement.")
        };
        var ex = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync(statement, c.Id));
        Assert.Equal(number, ex.Number);
        Assert.Equal(before, await PickingSnapshot(db, c.Id));
    }

    [Fact]
    public async Task M04_SQL_root_budget_and_picking_delete_guard_preserve_current_rows()
    {
        using var c = await DeliveryD04Case.CreateAsync(fixture);
        var replacement = await c.ReplacementAsync();
        var detail = await c.ClaimAsync();
        await c.CommandAsync("plan", new DeliveryPickingPlanRequest(Guid.NewGuid(), detail.Delivery.Version,
            [new(detail.Lines[0].LineId, "1", null, null)], [new(detail.Lines[0].LineId, replacement.VariantId, replacement.BottleId, "2", "1")], "Bù hàng", "Khách đồng ý"));
        var line = (await c.GetAsync()).Lines.Single(x => x.IsReplacement);
        await using var db = c.Source.Context();
        var before = await PickingSnapshot(db, c.Id);
        foreach (var sql in new[] { $"UPDATE DeliveryPickingLines SET PlannedOriginalCoverage=2 WHERE DeliveryOrderLineId={line.LineId}",
            $"DELETE DeliveryPickingLines WHERE DeliveryOrderId={c.Id}" })
        {
            var ex = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync(sql));
            Assert.Equal(51008, ex.Number);
            Assert.Equal(before, await PickingSnapshot(db, c.Id));
        }
    }

    [Theory]
    [InlineData("original")]
    [InlineData("replacement")]
    [InlineData("revision")]
    [InlineData("receipt")]
    [InlineData("outbox")]
    public async Task M05_EF_and_SQL_block_updates_soft_deletes_and_deletes_of_original_replacement_and_audit_facts(string kind)
    {
        using var c = await DeliveryD04Case.CreateAsync(fixture);
        var replacement = await c.ReplacementAsync();
        var detail = await c.ClaimAsync();
        await c.CommandAsync("plan", new DeliveryPickingPlanRequest(Guid.NewGuid(), detail.Delivery.Version,
            [new(detail.Lines[0].LineId, "1", null, null)], [new(detail.Lines[0].LineId, replacement.VariantId, replacement.BottleId, "1", "1")], "Bù hàng", "Khách đồng ý"));
        await using var db = c.Source.Context();
        BaseStoreEntity entity = kind switch
        {
            "original" => await db.DeliveryOrderLines.SingleAsync(x => x.DeliveryOrderId == c.Id && x.SourceOrderLineId != null),
            "replacement" => await db.DeliveryOrderLines.SingleAsync(x => x.DeliveryOrderId == c.Id && x.OriginalRootLineId != null),
            "revision" => await db.DeliveryRevisions.FirstAsync(x => x.DeliveryOrderId == c.Id),
            "receipt" => await db.DeliveryCommandReceipts.FirstAsync(x => x.DeliveryOrderId == c.Id),
            _ => await db.DeliveryOutboxMessages.FirstAsync(x => x.DeliveryOrderId == c.Id)
        };
        var table = db.Entry(entity).Metadata.GetTableName()!;
        entity.IsDeleted = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        var before = await PickingSnapshot(db, c.Id);
        foreach (var sql in new[] { $"UPDATE [{table}] SET IsDeleted=1 WHERE Id={entity.Id}",
            $"UPDATE [{table}] SET CreatedAtUtc=DATEADD(second,1,CreatedAtUtc) WHERE Id={entity.Id}", $"DELETE [{table}] WHERE Id={entity.Id}" })
        {
            var ex = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync(sql));
            Assert.Equal(51002, ex.Number);
        }
        Assert.Equal(before, await PickingSnapshot(db, c.Id));
    }

    [Fact]
    public async Task M06_Occupied_Down_fails_before_DDL_and_preserves_committed_work_history_and_schema70()
    {
        var isolated = new DeliveryD02Fixture();
        await isolated.InitializeAsync();
        try
        {
            using var c = await DeliveryD04Case.CreateAsync(isolated);
            await c.ClaimAsync();
            await using var db = c.Source.Context();
            await AssertSchema(db, 71, 15);
            var before = await PickingSnapshot(db, c.Id);
            try
            {
                await isolated.Web.Database.MigrateAsync(Current);
                await AssertSchema(db, 70, 15);
                Assert.Equal(before, await PickingSnapshot(db, c.Id));
                var ex = await Assert.ThrowsAsync<SqlException>(() => db.GetService<IMigrator>().MigrateAsync(Previous));
                Assert.Equal(51009, ex.Number);
                Assert.Equal(before, await PickingSnapshot(db, c.Id));
                await AssertSchema(db, 70, 15);
            }
            finally
            {
                await isolated.Web.Database.MigrateAsync(Latest);
                await AssertSchema(db, 71, 15);
            }
        }
        finally
        {
            await isolated.DisposeAsync();
        }
    }

    [Theory]
    [InlineData(43, 0)]
    [InlineData(64, 0)]
    [InlineData(67, 7)]
    [InlineData(68, 10)]
    [InlineData(69, 12)]
    [InlineData(70, 15)]
    [InlineData(71, 15)]
    public void M07_Historical_prefix_manifest_keeps_its_exact_trigger_and_schema_inventory(int count, int triggers)
    {
        using var db = fixture.Web.Database.CreateHostContext();
        var ids = db.Database.GetMigrations().ToArray();
        Assert.Equal(71, ids.Length); Assert.Equal(Latest, ids[^1]); Assert.Equal(Current, ids[69]); Assert.Equal(Previous, ids[68]);
        var catalog = new EfCoreDatabaseSchemaManifestCatalog(db);
        Assert.True(catalog.TryGetManifestForAppliedMigrationPrefix(ids[..count], out var manifest));
        Assert.Equal(count, manifest.AppliedMigrationIds.Count);
        Assert.Equal(triggers, manifest.Tables.Sum(x => x.Triggers.Count));
        Assert.Equal(count >= 70, manifest.Tables.Any(x => x.Identity.Name == "deliverypickingworks"));
        if (count >= 67)
            Assert.Equal(count >= 70, manifest.Tables.Single(x => x.Identity.Name == "deliveryorderlines")
                .Columns.Single(x => x.Name == "sourceorderlineid").IsNullable);
    }

    [Theory]
    [InlineData("DROP TRIGGER [TR_DeliveryOrderLines_Immutable];")]
    [InlineData("DISABLE TRIGGER [TR_DeliveryOrderLines_Root] ON [DeliveryOrderLines];")]
    [InlineData("ALTER TRIGGER [TR_DeliveryPickingLines_Bounds] ON [DeliveryPickingLines] AFTER INSERT,UPDATE,DELETE AS BEGIN RETURN; END;")]
    [InlineData("ALTER TABLE [DeliveryPickingWorks] NOCHECK CONSTRAINT [CK_DeliveryPickingWorks_Approval];")]
    [InlineData("ALTER TABLE [DeliveryPickingLines] DROP CONSTRAINT [CK_DeliveryPickingLines_Approval]; ALTER TABLE [DeliveryPickingLines] ADD CONSTRAINT [CK_DeliveryPickingLines_Approval] CHECK ([ApprovedNet] IS NULL OR [ApprovedNet]>=0);")]
    public async Task M07_Preflight_rejects_missing_disabled_or_changed_D04_durable_protections(string sql)
    {
        await using var db = fixture.Web.Database.CreateHostContext();
        await using var tx = await db.Database.BeginTransactionAsync();
        try
        {
            await db.Database.ExecuteSqlRawAsync(sql);
            var result = await Preflight(db).InspectAsync();
            Assert.False(result.IsAllowed); Assert.Equal("StructuralSchemaMismatch", result.SafeReasonCode);
        }
        finally { await tx.RollbackAsync(); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void M07_Migration_catalog_rejects_modified_or_nontransactional_picking_trigger_SQL(int index)
    {
        using var db = fixture.Web.Database.CreateHostContext();
        var assembly = db.GetService<IMigrationsAssembly>();
        var migration = assembly.CreateMigration(assembly.Migrations[Current], db.Database.ProviderName!);
        var sql = migration.UpOperations.OfType<SqlOperation>().ElementAt(index);
        var catalog = typeof(EfCoreDatabaseSchemaManifestCatalog);
        var table = catalog.GetNestedType("MutableTable", BindingFlags.NonPublic)!;
        var tables = (IDictionary)Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(typeof(DatabaseObjectIdentity), table))!;
        void Apply(SqlOperation operation)
        {
            try { catalog.GetMethod("ApplyOperation", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null,
                new object[] { Current, operation, tables, new Dictionary<DatabaseObjectIdentity, DatabaseSequenceSchema>(), "dbo" }); }
            catch (TargetInvocationException ex) when (ex.InnerException is not null) { ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); }
        }
        Assert.Throws<InvalidOperationException>(() => Apply(new SqlOperation { Sql = sql.Sql.Replace("THROW", "PRINT", StringComparison.Ordinal) }));
        Assert.Throws<InvalidOperationException>(() => Apply(new SqlOperation { Sql = sql.Sql, SuppressTransaction = true }));
    }

    private static SqlServerDatabaseBaselinePreflight Preflight(AppDbContext db) => new(db,
        new EfCoreDatabaseMigrationCatalog(db), new SqlServerDatabaseObjectInventoryReader(db),
        new EfCoreDatabaseSchemaManifestCatalog(db), new SqlServerSchemaSnapshotReader(db));

    private static async Task AssertSchema(AppDbContext db, int count, int triggers)
    {
        var ids = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
        Assert.Equal(count, ids.Length); Assert.Equal(count >= 71 ? Latest : count == 70 ? Current : Previous, ids[^1]);
        var catalog = new EfCoreDatabaseSchemaManifestCatalog(db);
        Assert.True(catalog.TryGetManifestForAppliedMigrationPrefix(ids, out var expected));
        await db.Database.OpenConnectionAsync();
        var actual = await new SqlServerSchemaSnapshotReader(db).ReadAsync(ids);
        var comparison = DatabaseSchemaComparer.Compare(expected, actual);
        Assert.True(comparison.IsMatch, JsonSerializer.Serialize(comparison.Mismatches));
        Assert.Equal(triggers, actual.Tables.Sum(x => x.Triggers.Count));
        Assert.True((await Preflight(db).InspectAsync()).IsAllowed);
    }

    private static async Task AssertPickingCheckNormalizationRejectsAlteredSemantics(AppDbContext db)
    {
        var ids = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
        var expected = new EfCoreDatabaseSchemaManifestCatalog(db).GetCurrentManifest();
        var actual = await new SqlServerSchemaSnapshotReader(db).ReadAsync(ids);
        var designModel = db.GetService<IDesignTimeModel>().Model;
        string[] names = ["CK_DeliveryOrderLines_Identity", "CK_DeliveryPickingLines_Approval",
            "CK_DeliveryPickingLines_Report", "CK_DeliveryPickingWorks_Approval"];
        foreach (var name in names)
        {
            var table = Assert.Single(actual.Tables, t => t.CheckConstraints.Any(c =>
                c.Name.Equals(name, StringComparison.OrdinalIgnoreCase)));
            var check = Assert.Single(table.CheckConstraints, c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            var source = Assert.Single(designModel.GetEntityTypes().SelectMany(e => e.GetCheckConstraints()),
                c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)).Sql;
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = "SELECT definition FROM sys.check_constraints WHERE name=@name;";
            command.Parameters.Add(new SqlParameter("@name", name));
            var persisted = Assert.IsType<string>(await command.ExecuteScalarAsync());
            Assert.Equal(DatabaseSchemaNormalization.NormalizeCheckConstraintExpression(source),
                DatabaseSchemaNormalization.NormalizeCheckConstraintExpression(persisted));
            Assert.False(check.IsDisabled); Assert.False(check.IsNotTrusted);

            var altered = new List<string>
            {
                ReplaceFirst(source, " IS NOT NULL", " IS NULL"),
                ReplaceFirst(source, " AND ", " OR "),
                ReplaceFirst(source, " OR ", " AND ")
            };
            if (source.Contains(">0", StringComparison.Ordinal))
            {
                altered.Add(ReplaceFirst(source, ">0", ">=0"));
                altered.Add(ReplaceFirst(source, ">0", ">1"));
            }
            else
            {
                altered.Add(ReplaceFirst(source, "=0", ">=0"));
                altered.Add(ReplaceFirst(source, "=0", "=1"));
            }
            if (source.Contains("ROUND(", StringComparison.Ordinal))
                altered.Add(ReplaceFirst(source, ",0)", ",1)"));
            if (source.Contains("'unreported'", StringComparison.Ordinal))
                altered.Add(ReplaceFirst(source, "'unreported'", "'picker-report'"));
            foreach (var sql in altered)
                AssertRejected(check with { Expression = DatabaseSchemaNormalization.NormalizeCheckConstraintExpression(sql)! });
            AssertRejected(check with { IsDisabled = true });
            AssertRejected(check with { IsNotTrusted = true });

            void AssertRejected(DatabaseCheckConstraintSchema replacement)
            {
                var changed = DatabaseSchemaCanonicalizer.WithFingerprint(ids,
                    actual.Tables.Select(t => t.Identity == table.Identity
                        ? t with { CheckConstraints = t.CheckConstraints.Select(c => c.Name == check.Name ? replacement : c).ToArray() }
                        : t).ToArray(), actual.Sequences);
                var result = DatabaseSchemaComparer.Compare(expected, changed);
                Assert.False(result.IsMatch);
                Assert.Equal(2, result.Mismatches.CheckConstraints);
                Assert.Equal(2, result.Mismatches.Total);
            }
        }

        static string ReplaceFirst(string text, string oldValue, string newValue)
        {
            var index = text.IndexOf(oldValue, StringComparison.Ordinal);
            Assert.True(index >= 0, "Required semantic mutation must target the actual source CHECK expression.");
            return text[..index] + newValue + text[(index + oldValue.Length)..];
        }
    }

    private static async Task<string> PickingSnapshot(AppDbContext db, int id)
    {
        db.ChangeTracker.Clear();
        return JsonSerializer.Serialize(new
        {
            Order = await db.DeliveryOrders.AsNoTracking().Where(x => x.Id == id).Select(x => new { x.Revision, x.State, x.RowVersion }).SingleAsync(),
            Work = await db.DeliveryPickingWorks.AsNoTracking().Where(x => x.DeliveryOrderId == id).ToArrayAsync(),
            Lines = await db.DeliveryPickingLines.AsNoTracking().Where(x => x.DeliveryOrderId == id).OrderBy(x => x.Id)
                .Select(x => new { x.Id, x.StoreId, x.DeliveryOrderId, x.DeliveryOrderLineId, x.IsActive, x.PlannedQuantity, x.PlannedOriginalCoverage,
                    x.ReportedQuantity, x.ReporterUserId, x.ReportedAtUtc, x.ShortageReason, x.ReportFactKind,
                    x.ApprovedQuantity, x.ApprovedOriginalCoverage, x.ApprovedNet, x.RowVersion }).ToArrayAsync(),
            Quotes = await db.DeliveryOrderLines.AsNoTracking().Where(x => x.DeliveryOrderId == id).OrderBy(x => x.Id)
                .Select(x => new { x.Id, x.SourceOrderLineId, x.OriginalRootLineId, x.ItemName, x.OrderedQuantity, x.UnitPrice, x.Net, x.IsDeleted, x.CreatedAtUtc, x.RowVersion }).ToArrayAsync(),
            Revisions = await db.DeliveryRevisions.AsNoTracking().Where(x => x.DeliveryOrderId == id).OrderBy(x => x.Id).ToArrayAsync(),
            Receipts = await db.DeliveryCommandReceipts.AsNoTracking().Where(x => x.DeliveryOrderId == id).OrderBy(x => x.Id).ToArrayAsync(),
            Outbox = await db.DeliveryOutboxMessages.AsNoTracking().Where(x => x.DeliveryOrderId == id).OrderBy(x => x.Id).ToArrayAsync()
        });
    }

    private static async Task<int> SeedHistoricalD03(InventoryPostingLocalDb sql)
    {
        var seed = await sql.SeedInventoryCatalogAsync();
        await using var db = sql.CreateTenantContext(seed.StoreId);
        var user = new User { UserName = "d04-prefix69", PasswordHash = "fixture-only", FullName = "D03 historical actor" };
        db.UserInStores.Add(new() { StoreId = seed.StoreId, User = user, Role = new Role { StoreId = seed.StoreId, Code = "D04-OLD", Name = "Historical role" } });
        var terminal = new POSTerminal { StoreId = seed.StoreId, Code = "D03-OLD", Name = "D03 original counter" };
        db.POSTerminals.Add(terminal); await db.SaveChangesAsync();
        var shift = new POSShift { StoreId = seed.StoreId, TerminalId = terminal.Id, WarehouseId = seed.WarehouseId, OpenedByUserId = user.Id };
        db.POSShifts.Add(shift); await db.SaveChangesAsync();
        var variant = await db.ProductVariants.SingleAsync(x => x.Id == seed.ProductVariantId);
        var order = new Order { StoreId = seed.StoreId, POSShiftId = shift.Id, Status = OrderStatus.Cancelled,
            Subtotal = 50, GrandTotal = 50, BalanceDue = 50, Note = "[DELIVERY] D03-HISTORICAL",
            Lines = [new OrderLine { StoreId = seed.StoreId, ProductId = variant.ProductId, VariantId = variant.Id,
                ItemName = "D03 historical goods", UnitName = "Gói", Quantity = 2.5m, BaseQuantity = 2.5m, Multiplier = 1, UnitPrice = 20, LineTotal = 50 }] };
        db.Orders.Add(order); await db.SaveChangesAsync();
        var legal = await db.Warehouses.Where(x => x.Id == seed.WarehouseId).Select(x => x.LegalEntityId).SingleAsync();
        var delivery = new DeliveryOrder { StoreId = seed.StoreId, SourceCartId = order.Id, SourceWarehouseId = seed.WarehouseId,
            SourceLegalEntityId = legal, CreatedShiftId = shift.Id, CreatedTerminalId = terminal.Id, CreatedByUserId = user.Id,
            Code = "D03-HISTORICAL", LookupToken = "D03-HISTORICAL-TOKEN", RecipientName = "D03 recipient", RecipientPhone = "0901234567",
            RecipientAddress = "D03 historical address", QuotedTotal = 50 };
        db.DeliveryOrders.Add(delivery); await db.SaveChangesAsync();
        var lineId = order.Lines.Single().Id;
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT DeliveryOrderLines (StoreId,DeliveryOrderId,SourceCartId,SourceOrderLineId,VariantId,ItemName,UnitName,BaseUnitName,OrderedQuantity,BaseMultiplier,UnitPrice,Gross,LineDiscount,AllocatedOrderDiscount,Net,IsDeleted)
            VALUES ({seed.StoreId},{delivery.Id},{order.Id},{lineId},{variant.Id},N'D03 historical goods',N'Gói',N'Gói',2.5,1,20,50,0,0,50,0)
            """);
        var snapshot = "{\"state\":\"Created\",\"quotedTotal\":50,\"sourceStatus\":\"Cancelled\"}";
        db.DeliveryRevisions.Add(new() { StoreId = seed.StoreId, DeliveryOrderId = delivery.Id, Revision = 1,
            ActorUserId = user.Id, Action = "foundation-create", AggregateVersion = Convert.ToBase64String(delivery.RowVersion),
            SnapshotJson = snapshot, SnapshotHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(snapshot))) });
        db.DeliveryCommandReceipts.Add(new() { StoreId = seed.StoreId, DeliveryOrderId = delivery.Id, ActorUserId = user.Id,
            ClientRequestId = Guid.NewGuid(), Operation = "foundation-create", RequestHash = new string('B', 64), OutcomeJson = snapshot });
        db.DeliveryOutboxMessages.Add(new() { StoreId = seed.StoreId, DeliveryOrderId = delivery.Id, EventId = Guid.NewGuid(), Revision = 1,
            Action = "foundation-create", PayloadJson = "{\"state\":\"Created\",\"revision\":1}" });
        await db.SaveChangesAsync();
        return delivery.Id;
    }

    private static async Task<string> HistoricalSnapshot(AppDbContext db)
    {
        db.ChangeTracker.Clear();
        // Explicit projections use only columns actually present in prefix69, never current EF entity materialization.
        return JsonSerializer.Serialize(new
        {
            Orders = await db.Orders.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.POSShiftId, x.Status, x.Note, x.Subtotal, x.GrandTotal, x.PaidTotal, x.RowVersion }).ToArrayAsync(),
            SourceLines = await db.OrderLines.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.Quantity, x.LineTotal, x.RowVersion }).ToArrayAsync(),
            Delivery = await db.DeliveryOrders.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.Code, x.LookupToken, x.SourceCartId, x.CreatedShiftId, x.CreatedTerminalId, x.CreatedByUserId, x.SourceWarehouseId, x.SourceLegalEntityId, x.QuotedTotal, x.RowVersion }).ToArrayAsync(),
            Quotes = await db.DeliveryOrderLines.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.SourceCartId, x.SourceOrderLineId, x.ItemName, x.OrderedQuantity, x.BaseMultiplier, x.UnitPrice, x.Gross, x.LineDiscount, x.AllocatedOrderDiscount, x.Net, x.RowVersion }).ToArrayAsync(),
            History = await db.DeliveryRevisions.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
            Receipts = await db.DeliveryCommandReceipts.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
            Outbox = await db.DeliveryOutboxMessages.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync()
        });
    }
}
