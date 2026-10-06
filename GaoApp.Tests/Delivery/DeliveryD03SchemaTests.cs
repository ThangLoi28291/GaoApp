using System.Collections;
using System.Reflection;
using System.Runtime.ExceptionServices;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Data.Migrations;
using GaoApp.Tests.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GaoApp.Tests.Delivery;

[Collection("R1FinalDatabasePreflight"), Trait("Category", "DeliveryD03Schema")]
public sealed class DeliveryD03SchemaTests : IClassFixture<DeliveryD03SchemaFixture>
{
    private const string Foundation = "20261006153000_AddDeliveryFoundation";
    private const string Protection = "20261006163000_ProtectDeliverySourceCarts";
    private readonly DeliveryD03SchemaFixture fixture;
    public DeliveryD03SchemaTests(DeliveryD03SchemaFixture fixture) => this.fixture = fixture;

    [Fact]
    public void Migration_prefix_manifests_include_only_applied_triggers_and_unique_constraints()
    {
        using var db = fixture.Database.CreateHostContext();
        var ids = db.Database.GetMigrations().ToArray();
        var catalog = new EfCoreDatabaseSchemaManifestCatalog(db);
        var position = Array.IndexOf(ids, Foundation);
        Assert.True(catalog.TryGetManifestForAppliedMigrationPrefix(ids[..position], out var before));
        Assert.Empty(before.Tables.SelectMany(x => x.Triggers));
        Assert.DoesNotContain(before.Tables.Single(x => x.Identity.Name == "orders").Indexes,
            x => x.Name == "ak_orders_storeid_id");
        Assert.True(catalog.TryGetManifestForAppliedMigrationPrefix(ids[..(position + 1)], out var foundation));
        Assert.Equal(7, foundation.Tables.Sum(x => x.Triggers.Count));
        var unique = Assert.Single(foundation.Tables.Single(x => x.Identity.Name == "orders").Indexes,
            x => x.Name == "ak_orders_storeid_id");
        Assert.True(unique.IsUnique && unique.IsUniqueConstraint && !unique.IsClustered);
        Assert.Equal(new[] { "storeid", "id" }, unique.KeyColumns.Select(x => x.Name));
        Assert.Equal(10, catalog.GetCurrentManifest().Tables.Sum(x => x.Triggers.Count));
    }

    [Fact]
    public async Task Fresh_sql_schema_and_real_trigger_definitions_match_manifest()
    {
        await using var db = fixture.Database.CreateHostContext();
        var result = await Preflight(db).InspectAsync();
        var expected = new EfCoreDatabaseSchemaManifestCatalog(db).GetCurrentManifest();
        await db.Database.OpenConnectionAsync();
        var actual = await new SqlServerSchemaSnapshotReader(db).ReadAsync((await db.Database.GetAppliedMigrationsAsync()).ToArray());
        Assert.Equal(10, actual.Tables.Sum(x => x.Triggers.Count));
        var expectedRecords = DatabaseSchemaCanonicalizer.CreateCategoryRecords(expected).All;
        var actualRecords = DatabaseSchemaCanonicalizer.CreateCategoryRecords(actual).All;
        Assert.True(DatabaseSchemaComparer.Compare(expected, actual).IsMatch,
            string.Join("\n", expectedRecords.Except(actualRecords).Select(x => "EXPECTED " + x)
                .Concat(actualRecords.Except(expectedRecords).Select(x => "ACTUAL " + x))));
        Assert.True(result.IsAllowed, result.SafeReasonCode);
        Assert.Equal(DatabaseCompatibilityState.CurrentBaseline, result.State);
    }

    [Theory]
    [InlineData("DROP TRIGGER [TR_Orders_DeliverySource];")]
    [InlineData("DISABLE TRIGGER [TR_Orders_DeliverySource] ON [Orders];")]
    [InlineData("ALTER TRIGGER [TR_Orders_DeliverySource] ON [Orders] AFTER UPDATE AS BEGIN SET NOCOUNT ON; RETURN; END;")]
    [InlineData("DROP TRIGGER [TR_DeliveryRevisions_Immutable];")]
    [InlineData("DISABLE TRIGGER [TR_DeliveryRevisions_Immutable] ON [DeliveryRevisions];")]
    public async Task Missing_disabled_or_modified_delivery_trigger_is_rejected(string sql)
    {
        await using var db = fixture.Database.CreateHostContext();
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlRawAsync(sql);
        var result = await Preflight(db).InspectAsync();
        Assert.False(result.IsAllowed);
        Assert.Equal("StructuralSchemaMismatch", result.SafeReasonCode);
        Assert.True(result.SchemaMismatches!.Triggers > 0);
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task Known_trigger_name_on_wrong_table_is_rejected()
    {
        await using var db = fixture.Database.CreateHostContext();
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlRawAsync("DROP TRIGGER [TR_Orders_DeliverySource];");
        await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER [TR_Orders_DeliverySource] ON [OrderLines] AFTER UPDATE AS BEGIN RETURN; END;");
        var result = await Preflight(db).InspectAsync();
        Assert.False(result.IsAllowed);
        Assert.Equal("StructuralSchemaMismatch", result.SafeReasonCode);
        Assert.True(result.SchemaMismatches!.Triggers > 0);
        await transaction.RollbackAsync();
    }

    [Theory]
    [InlineData("ALTER TABLE [DeliveryOrders] DROP CONSTRAINT [CK_DeliveryOrders_State]; ALTER TABLE [DeliveryOrders] ADD CONSTRAINT [CK_DeliveryOrders_State] CHECK ([State] BETWEEN 0 AND 11 AND [Revision]>0);")]
    [InlineData("ALTER TABLE [DeliveryJournalEntries] DROP CONSTRAINT [CK_DeliveryJournalEntries_Money]; ALTER TABLE [DeliveryJournalEntries] ADD CONSTRAINT [CK_DeliveryJournalEntries_Money] CHECK ([Kind] BETWEEN 1 AND 8 AND [MoneyAmount]>=0 AND [MoneyAmount]=ROUND([MoneyAmount],0));")]
    [InlineData("ALTER TABLE [DeliveryOrderLines] DROP CONSTRAINT [CK_DeliveryOrderLines_Quote]; ALTER TABLE [DeliveryOrderLines] ADD CONSTRAINT [CK_DeliveryOrderLines_Quote] CHECK ([Net]=[Gross]-([LineDiscount]-[AllocatedOrderDiscount]));")]
    [InlineData("ALTER TABLE [OperatingExpenses] DROP CONSTRAINT [CK_OperatingExpenses_Status]; ALTER TABLE [OperatingExpenses] ADD CONSTRAINT [CK_OperatingExpenses_Status] CHECK ([Status] IN ('draft','confirmed'));")]
    [InlineData("ALTER TABLE [TreasuryEntries] DROP CONSTRAINT [CK_TreasuryEntries_Transfer]; ALTER TABLE [TreasuryEntries] ADD CONSTRAINT [CK_TreasuryEntries_Transfer] CHECK (([TargetFund] IS NULL OR [TargetFund]<>[Fund] OR [Fund]='cash' AND [IsVoucherLink]=1) AND [OperatingExpenseId] IS NULL AND [PurchasePayableId] IS NULL);")]
    public async Task Changed_check_bounds_arithmetic_or_boolean_grouping_are_rejected(string sql)
    {
        await using var db = fixture.Database.CreateHostContext();
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlRawAsync(sql);
        var result = await Preflight(db).InspectAsync();
        Assert.False(result.IsAllowed);
        Assert.Equal("StructuralSchemaMismatch", result.SafeReasonCode);
        Assert.True(result.SchemaMismatches!.CheckConstraints > 0);
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task Additional_unrecognized_trigger_is_rejected()
    {
        await using var db = fixture.Database.CreateHostContext();
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER [TR_D03_Unexpected] ON [Orders] AFTER INSERT AS BEGIN RETURN; END;");
        var result = await Preflight(db).InspectAsync();
        Assert.False(result.IsAllowed);
        Assert.Equal("UnexpectedUserObjects", result.SafeReasonCode);
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task Changed_unique_constraint_columns_are_rejected()
    {
        await using var db = fixture.Database.CreateHostContext();
        await using var transaction = await db.Database.BeginTransactionAsync();
        // Remove only its dependent FK first; recreate both against a wrong key.
        await db.Database.ExecuteSqlRawAsync("""
            ALTER TABLE [DeliveryDispatchCostFragments] DROP CONSTRAINT [FK_DeliveryDispatchCostFragments_InventoryCostLayerAllocations_StoreId_InventoryCostLayerAllocationId];
            ALTER TABLE [InventoryCostLayerAllocations] DROP CONSTRAINT [AK_InventoryCostLayerAllocations_StoreId_Id];
            ALTER TABLE [InventoryCostLayerAllocations] ADD CONSTRAINT [AK_InventoryCostLayerAllocations_StoreId_Id] UNIQUE ([Id]);
            """);
        var result = await Preflight(db).InspectAsync();
        Assert.False(result.IsAllowed);
        Assert.Equal("StructuralSchemaMismatch", result.SafeReasonCode);
        Assert.True(result.SchemaMismatches!.Indexes > 0);
        await transaction.RollbackAsync();
    }

    [Theory]
    [InlineData(Foundation)]
    [InlineData(Protection)]
    public void Changing_trigger_sql_or_suppressing_transaction_is_rejected(string migrationId)
    {
        using var db = fixture.Database.CreateHostContext();
        var assembly = db.GetService<IMigrationsAssembly>();
        var migration = assembly.CreateMigration(assembly.Migrations[migrationId], db.Database.ProviderName!);
        var sql = migration.UpOperations.OfType<SqlOperation>().First();
        var catalog = typeof(EfCoreDatabaseSchemaManifestCatalog);
        var mutableTable = catalog.GetNestedType("MutableTable", BindingFlags.NonPublic)!;
        var tables = (IDictionary)Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(typeof(DatabaseObjectIdentity), mutableTable))!;
        void Apply(string id, MigrationOperation operation)
        {
            try
            {
                catalog.GetMethod("ApplyOperation", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null,
                    new object[] { id, operation, tables, new Dictionary<DatabaseObjectIdentity, DatabaseSequenceSchema>(), "dbo" });
            }
            catch (TargetInvocationException ex) when (ex.InnerException is not null)
            { ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); }
        }
        Assert.Throws<InvalidOperationException>(() => Apply(migrationId, new SqlOperation { Sql = sql.Sql.Replace("THROW", "PRINT") }));
        Assert.Throws<InvalidOperationException>(() => Apply(migrationId, new SqlOperation { Sql = sql.Sql, SuppressTransaction = true }));
    }

    private static SqlServerDatabaseBaselinePreflight Preflight(AppDbContext db) => new(db,
        new EfCoreDatabaseMigrationCatalog(db), new SqlServerDatabaseObjectInventoryReader(db),
        new EfCoreDatabaseSchemaManifestCatalog(db), new SqlServerSchemaSnapshotReader(db));
}

public sealed class DeliveryD03SchemaFixture : IAsyncLifetime
{
    internal InventoryPostingLocalDb Database { get; } = new();
    public async Task InitializeAsync()
    {
        await Database.MigrateAsync();
    }
    public async Task DisposeAsync() => await Database.DisposeAsync();
}
