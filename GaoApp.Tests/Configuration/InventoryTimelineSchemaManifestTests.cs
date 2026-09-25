using System.Collections;
using System.Reflection;
using System.Runtime.ExceptionServices;
using FluentAssertions;
using GaoApp.Infrastructure.Data.Migrations;
using GaoApp.Infrastructure.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GaoApp.Tests.Configuration;

public sealed class InventoryTimelineSchemaManifestTests
{
    private const string MigrationId = "20260920093000_OptimizeInventoryLedgerTimeline";
    private const string IndexName = "ix_inventorytransactions_ledgertimeline";

    [Fact]
    public void Supplier_optional_prefix_replaces_fk_without_duplicating_it()
    {
        using var db = new PreflightAcceptanceDatabase().CreateContext();
        var catalog = new EfCoreDatabaseSchemaManifestCatalog(db);
        var ids = db.Database.GetMigrations().ToArray();
        var position = Array.IndexOf(ids, "20260919173000_MakePurchaseOrderSupplierOptional");
        position.Should().BeGreaterThan(0);
        catalog.TryGetManifestForAppliedMigrationPrefix(ids[..position], out var before).Should().BeTrue();
        catalog.TryGetManifestForAppliedMigrationPrefix(ids[..(position + 1)], out var after).Should().BeTrue();
        var oldTable = before.Tables.Single(x => x.Identity == new DatabaseObjectIdentity("dbo", "PurchaseOrders"));
        var newTable = after.Tables.Single(x => x.Identity == oldTable.Identity);
        oldTable.Columns.Single(x => x.Name == "supplierid").IsNullable.Should().BeFalse();
        newTable.Columns.Single(x => x.Name == "supplierid").IsNullable.Should().BeTrue();
        newTable.ForeignKeys.Count.Should().Be(oldTable.ForeignKeys.Count);
        var supplierFk = newTable.ForeignKeys.Single(x => x.Columns.SequenceEqual(new[] { "supplierid" }));
        supplierFk.PrincipalTable.Should().Be(new DatabaseObjectIdentity("dbo", "Suppliers"));
        supplierFk.PrincipalColumns.Should().Equal("id");
        supplierFk.DeleteAction.Should().Be(DatabaseSchemaNormalization.NormalizeDeleteAction("Restrict"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Foreign_key_created_inline_or_added_can_be_dropped_and_readded(bool inline)
    {
        var operations = new OperationHarness();
        var create = new CreateTableOperation { Name = "InventoryTransactions", Schema = "dbo" };
        if (inline) create.ForeignKeys.Add(ForeignKey("FK_Test"));
        operations.Apply("fixture", create);
        if (!inline) operations.Apply("fixture", ForeignKey("FK_Test"));
        operations.Apply("fixture", ForeignKey("FK_Other"));
        operations.Apply("fixture", new DropForeignKeyOperation { Table = "InventoryTransactions", Name = "FK_Test" });
        operations.Table().ForeignKeys.Should().ContainSingle();
        var replacement = ForeignKey("FK_Test");
        replacement.OnDelete = ReferentialAction.Cascade;
        operations.Apply("fixture", replacement);
        operations.Table().ForeignKeys.Should().HaveCount(2);
        operations.Table().ForeignKeys.Count(x => x.DeleteAction == "cascade").Should().Be(1);
    }

    [Theory]
    [InlineData("dbo", "InventoryTransactions", "Missing")]
    [InlineData("other", "InventoryTransactions", "FK_Test")]
    [InlineData("dbo", "Missing", "FK_Test")]
    public void Missing_or_wrong_scope_foreign_key_drop_is_rejected(string schema, string table, string name)
    {
        var operations = new OperationHarness();
        operations.AddTable();
        operations.Apply("fixture", ForeignKey("FK_Test"));
        var action = () => operations.Apply("fixture", new DropForeignKeyOperation
        {
            Schema = schema, Table = table, Name = name
        });
        action.Should().Throw<InvalidOperationException>();
        operations.Table().ForeignKeys.Should().ContainSingle();
    }

    [Fact]
    public void Duplicate_fk_name_and_repeated_drop_are_rejected()
    {
        var operations = new OperationHarness();
        operations.AddTable();
        operations.Apply("fixture", ForeignKey("FK_Test"));
        var duplicate = () => operations.Apply("fixture", ForeignKey("FK_Test"));
        duplicate.Should().Throw<InvalidOperationException>().WithMessage("*duplicates*");
        operations.Table().ForeignKeys.Should().ContainSingle();
        var drop = new DropForeignKeyOperation { Table = "InventoryTransactions", Name = "FK_Test" };
        operations.Apply("fixture", drop);
        var repeated = () => operations.Apply("fixture", drop);
        repeated.Should().Throw<InvalidOperationException>();
        operations.Table().ForeignKeys.Should().BeEmpty();
    }

    private static AddForeignKeyOperation ForeignKey(string name) => new()
    {
        Name = name, Table = "InventoryTransactions", Columns = ["SupplierId"],
        PrincipalTable = "Suppliers", PrincipalColumns = ["Id"], OnDelete = ReferentialAction.Restrict
    };

    [Fact]
    public void Current_and_prefix_manifests_include_index_only_after_its_migration()
    {
        // Creating this context does not create/open a database. Only EF metadata is read.
        using var db = new PreflightAcceptanceDatabase().CreateContext();
        var catalog = new EfCoreDatabaseSchemaManifestCatalog(db);
        var ids = db.Database.GetMigrations().ToArray();
        var position = Array.IndexOf(ids, MigrationId);
        position.Should().BeGreaterThan(0);
        catalog.TryGetManifestForAppliedMigrationPrefix(ids[..position], out var before)
            .Should().BeTrue();
        TimelineTable(before).Indexes.Should().NotContain(x => x.Name == IndexName);
        catalog.TryGetManifestForAppliedMigrationPrefix(ids[..(position + 1)], out var after)
            .Should().BeTrue();
        AssertIndex(TimelineTable(after).Indexes.Single(x => x.Name == IndexName));
        AssertIndex(TimelineTable(catalog.GetCurrentManifest()).Indexes.Single(x => x.Name == IndexName));
    }

    [Fact]
    public void Immutable_migration_sql_populates_the_complete_index_metadata()
    {
        var operations = new OperationHarness();
        operations.AddTable();
        operations.Apply(MigrationId, TimelineSql());
        AssertIndex(operations.Table().Indexes.Single());
    }

    [Fact]
    public void Whitespace_only_variation_is_accepted()
    {
        var operations = new OperationHarness();
        operations.AddTable();
        var sql = TimelineSql();
        sql.Sql = "\r\n\t" + sql.Sql.Replace(" ", "  ") + "\n";
        operations.Apply(MigrationId, sql);
        AssertIndex(operations.Table().Indexes.Single());
    }

    [Theory]
    [InlineData("DESC", "ASC")]
    [InlineData("[StoreId], [OccurredAtUtc]", "[OccurredAtUtc], [StoreId]")]
    [InlineData("[QuantityChange], [AfterQty]", "[QuantityChange]")]
    [InlineData("[IsDeleted] = 0", "[IsDeleted] = 1")]
    [InlineData("[dbo]", "[other]")]
    [InlineData("CREATE INDEX", "CREATE UNIQUE INDEX")]
    [InlineData(";", "; DROP TABLE [dbo].[InventoryTransactions];")]
    public void Changed_schema_sql_is_rejected(string oldText, string newText)
    {
        var operations = new OperationHarness();
        operations.AddTable();
        var sql = TimelineSql();
        sql.Sql.Should().Contain(oldText);
        sql.Sql = sql.Sql.Replace(oldText, newText);
        var action = () => operations.Apply(MigrationId, sql);
        action.Should().Throw<InvalidOperationException>().WithMessage("*unreviewed*");
        operations.Table().Indexes.Should().BeEmpty();
    }

    [Theory]
    [InlineData("20260920093001_OptimizeInventoryLedgerTimeline")]
    [InlineData("20260827150000_AddInputInvoiceReconciliation")]
    public void Same_sql_under_another_migration_id_is_rejected(string migrationId)
    {
        var operations = new OperationHarness();
        operations.AddTable();
        var action = () => operations.Apply(migrationId, TimelineSql());
        action.Should().Throw<InvalidOperationException>();
        operations.Table().Indexes.Should().BeEmpty();
    }

    [Fact]
    public void Unknown_sql_and_changed_transaction_semantics_are_rejected()
    {
        var operations = new OperationHarness();
        operations.AddTable();
        var unknown = () => operations.Apply("unknown", new SqlOperation { Sql = "SELECT 1;" });
        unknown.Should().Throw<InvalidOperationException>();
        var sql = TimelineSql();
        sql.SuppressTransaction = true;
        var suppressed = () => operations.Apply(MigrationId, sql);
        suppressed.Should().Throw<InvalidOperationException>();
        operations.Table().Indexes.Should().BeEmpty();
    }

    [Fact]
    public void Missing_table_and_duplicate_index_are_rejected()
    {
        var operations = new OperationHarness();
        var missing = () => operations.Apply(MigrationId, TimelineSql());
        missing.Should().Throw<InvalidOperationException>().WithMessage("*table absent*");
        operations.AddTable();
        operations.Apply(MigrationId, TimelineSql());
        var duplicate = () => operations.Apply(MigrationId, TimelineSql());
        duplicate.Should().Throw<InvalidOperationException>().WithMessage("*already exists*");
        operations.Table().Indexes.Should().ContainSingle();
    }

    private static SqlOperation TimelineSql()
        => (SqlOperation)new OptimizeInventoryLedgerTimeline().UpOperations.Single();

    private static DatabaseTableSchema TimelineTable(DatabaseSchemaManifest manifest)
        => manifest.Tables.Single(x => x.Identity == new DatabaseObjectIdentity("dbo", "InventoryTransactions"));

    private static void AssertIndex(DatabaseIndexSchema index)
    {
        index.Name.Should().Be(IndexName);
        index.KeyColumns.Should().Equal(
            new DatabaseIndexColumnSchema("storeid", false),
            new DatabaseIndexColumnSchema("occurredatutc", true),
            new DatabaseIndexColumnSchema("id", true));
        index.IncludedColumns.Should().BeEquivalentTo(new[] { "quantitychange", "afterqty" });
        index.Filter.Should().Be(DatabaseSchemaNormalization.NormalizeSqlExpression("[IsDeleted] = 0"));
        index.IsUnique.Should().BeFalse();
        index.IsUniqueConstraint.Should().BeFalse();
        index.IsClustered.Should().BeFalse();
        index.IsDisabled.Should().BeFalse();
    }

    // Exercise the actual operation dispatcher without changing production visibility
    // or adding synthetic migrations to the application's migration assembly.
    private sealed class OperationHarness
    {
        private static readonly Type Catalog = typeof(EfCoreDatabaseSchemaManifestCatalog);
        private static readonly Type TableType = Catalog.GetNestedType("MutableTable", BindingFlags.NonPublic)!;
        private readonly IDictionary _tables = (IDictionary)Activator.CreateInstance(
            typeof(Dictionary<,>).MakeGenericType(typeof(DatabaseObjectIdentity), TableType))!;

        public void AddTable() => Apply("fixture", new CreateTableOperation
        {
            Name = "InventoryTransactions", Schema = "dbo"
        });

        public void Apply(string migrationId, MigrationOperation operation)
        {
            try
            {
                Catalog.GetMethod("ApplyOperation", BindingFlags.NonPublic | BindingFlags.Static)!
                    .Invoke(null, new object[]
                    {
                        migrationId, operation, _tables,
                        new Dictionary<DatabaseObjectIdentity, DatabaseSequenceSchema>(), "dbo"
                    });
            }
            catch (TargetInvocationException exception) when (exception.InnerException is not null)
            {
                ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            }
        }

        public DatabaseTableSchema Table()
            => (DatabaseTableSchema)TableType.GetMethod("ToSchema")!
                .Invoke(_tables.Values.Cast<object>().Single(), null)!;
    }
}
