using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data.Migrations;
using GaoApp.Infrastructure.Migrations;
using GaoApp.Tests.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GaoApp.Tests.Suppliers;

[Collection("R1FinalDatabasePreflight")]
public sealed class SupplierBankFieldsMigrationTests
{
    private const string MigrationId = "20260909100000_AddSupplierBankFields";
    private const string PreviousId = "20260909080000_AddPosCollectionIdempotency";
    private static readonly Dictionary<string, int> Fields = new()
    {
        ["BankAccountNumber"] = 50, ["BankAccountName"] = 250, ["BankName"] = 250
    };

    [Fact]
    public async Task Model_snapshot_manifest_and_exact_three_column_migration_align()
    {
        await using var database = new PreflightAcceptanceDatabase();
        await using var db = database.CreateContext();
        var migrations = db.GetService<IMigrationsAssembly>();
        Assert.Single(db.Database.GetMigrations(), x => x.EndsWith("_AddSupplierBankFields"));
        var migrationIds = db.Database.GetMigrations().ToList();
        Assert.Equal(PreviousId, migrationIds[migrationIds.IndexOf(MigrationId) - 1]);
        var migration = migrations.CreateMigration(migrations.Migrations[MigrationId], db.Database.ProviderName!);
        Assert.Equal(3, migration.UpOperations.Count);
        Assert.Equal(3, migration.DownOperations.Count);
        foreach (var operation in migration.UpOperations)
        {
            var column = Assert.IsType<AddColumnOperation>(operation);
            Assert.Equal("Suppliers", column.Table);
            Assert.Equal(typeof(string), column.ClrType);
            Assert.Equal(Fields[column.Name], column.MaxLength);
            Assert.Equal($"nvarchar({Fields[column.Name]})", column.ColumnType);
            Assert.True(column.IsNullable);
            Assert.Null(column.DefaultValue); Assert.Null(column.DefaultValueSql);
        }
        Assert.Equal(Fields.Keys.Order(), migration.DownOperations.Select(o =>
        {
            var column = Assert.IsType<DropColumnOperation>(o);
            Assert.Equal("Suppliers", column.Table);
            return column.Name;
        }).Order());
        var model = db.GetService<IDesignTimeModel>().Model;
        var entity = model.FindEntityType(typeof(Supplier))!;
        foreach (var (name, length) in Fields)
        {
            var property = entity.FindProperty(name)!;
            Assert.True(property.IsNullable); Assert.Equal(length, property.GetMaxLength());
            Assert.Equal($"nvarchar({length})", property.GetColumnType());
        }
        Assert.DoesNotContain(entity.GetIndexes(), i => i.Properties.Any(p => Fields.ContainsKey(p.Name)));
        Assert.True(entity.FindProperty("RowVersion")!.IsConcurrencyToken);
        var initializer = db.GetService<IModelRuntimeInitializer>();
        var snapshot = initializer.Initialize(migrations.ModelSnapshot!.Model, designTime: true).GetRelationalModel();
        var differ = db.GetService<IMigrationsModelDiffer>();
        Assert.Empty(differ.GetDifferences(snapshot, model.GetRelationalModel()));
        var previous = migrations.CreateMigration(migrations.Migrations[PreviousId], db.Database.ProviderName!);
        var previousModel = initializer.Initialize(previous.TargetModel, designTime: true).GetRelationalModel();
        var targetModel = initializer.Initialize(migration.TargetModel, designTime: true).GetRelationalModel();
        var delta = differ.GetDifferences(previousModel, targetModel);
        Assert.Equal(3, delta.Count);
        Assert.All(delta, o => Assert.IsType<AddColumnOperation>(o));
        var manifest = new EfCoreDatabaseSchemaManifestCatalog(db).GetCurrentManifest();
        var supplier = Assert.Single(manifest.Tables, t => string.Equals(t.Identity.Name, "Suppliers", StringComparison.OrdinalIgnoreCase));
        foreach (var (name, length) in Fields)
        {
            var column = Assert.Single(supplier.Columns, c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
            Assert.True(column.IsNullable);
            Assert.Equal($"nvarchar({length})", column.StoreType);
        }
    }

    [Fact]
    public async Task Migration_preserves_legacy_row_and_sql_roundtrip_and_rollback_on_disposable_database()
    {
        await using var database = new PreflightAcceptanceDatabase();
        await database.CreateDatabaseAsync();
        await using var db = database.CreateContext();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(PreviousId);
        var store = new Store { Name = "Synthetic bank test", SubDomain = "banktest", SubDomainNormalized = "BANKTEST", IsActive = true };
        await LegacyStoreSeed.InsertAsync(db, store);
        // Synthetic row inserted using SQL because the current EF model includes new columns.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO Suppliers (StoreId, Code, Name, IsActive, SortOrder, CreatedAtUtc, IsDeleted)
            VALUES ({store.Id}, N'LEGACY', N'Synthetic legacy supplier', 1, 0, SYSUTCDATETIME(), 0);
            """);
        await migrator.MigrateAsync(MigrationId);
        var row = await db.Suppliers.SingleAsync();
        Assert.Null(row.BankAccountNumber); Assert.Null(row.BankAccountName); Assert.Null(row.BankName);
        row.BankAccountNumber = "0012 034567"; row.BankAccountName = "Tên Synthetic"; row.BankName = "Test bank";
        await db.SaveChangesAsync();
        var originalVersion = row.RowVersion.ToArray();
        await using var staleDb = database.CreateContext();
        var stale = await staleDb.Suppliers.SingleAsync();
        row.BankName = "New bank"; await db.SaveChangesAsync();
        Assert.False(originalVersion.SequenceEqual(row.RowVersion));
        stale.BankName = "Stale bank";
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => staleDb.SaveChangesAsync());
        db.ChangeTracker.Clear();
        row = await db.Suppliers.SingleAsync();
        Assert.Equal("0012 034567", row.BankAccountNumber); Assert.Equal("Tên Synthetic", row.BankAccountName);
        row.BankAccountNumber = null; row.BankAccountName = null; row.BankName = null;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        Assert.Null((await db.Suppliers.SingleAsync()).BankName);
        await migrator.MigrateAsync(PreviousId);
        var count = await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM Suppliers WHERE Code=N'LEGACY'").SingleAsync();
        Assert.Equal(1, count);
        var bankColumns = await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM sys.columns WHERE object_id=OBJECT_ID(N'Suppliers') AND name IN (N'BankName',N'BankAccountName',N'BankAccountNumber')").SingleAsync();
        Assert.Equal(0, bankColumns);
    }
}
