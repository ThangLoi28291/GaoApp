using FluentAssertions;
using GaoApp.Infrastructure.Data.Migrations;
using GaoApp.Infrastructure.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GaoApp.Tests.Configuration;

public sealed class InvoiceInputStockSupplementalMovementMigrationTests
{
    private const string MigrationId =
        "20260921100000_AddInvoiceInputStockSupplementalMovements";
    private const string TableName =
        "InvoiceInputStockSupplementalMovements";

    [Fact]
    public void Migration_up_creates_only_the_c2_table_indexes_and_safe_foreign_keys()
    {
        var migration = new AddInvoiceInputStockSupplementalMovements();
        var operations = migration.UpOperations;

        operations.Should().NotContain(x =>
            x.GetType() == typeof(InsertDataOperation)
            || x.GetType() == typeof(UpdateDataOperation)
            || x.GetType() == typeof(DeleteDataOperation)
            || x.GetType() == typeof(SqlOperation));
        var table = operations.OfType<CreateTableOperation>().Should().ContainSingle().Which;
        table.Name.Should().Be(TableName);
        table.Columns.Select(x => x.Name).Should().BeEquivalentTo(
        [
            "Id", "WarehouseId", "ProductVariantId", "EffectiveAtUtc", "QuantityChange",
            "MovementType", "LegacySourceKey", "SourcePeriod", "Note", "CreatedAtUtc",
            "CreatedBy", "UpdatedAtUtc", "UpdatedBy", "IsDeleted", "DeletedAtUtc",
            "DeletedBy", "RowVersion", "StoreId"
        ]);
        table.Columns.Single(x => x.Name == "QuantityChange").ColumnType.Should().Be("decimal(18,4)");
        table.Columns.Single(x => x.Name == "LegacySourceKey").MaxLength.Should().Be(200);
        table.ForeignKeys.Should().HaveCount(3);
        table.ForeignKeys.Select(x => x.PrincipalTable).Should().BeEquivalentTo(
            ["Stores", "Warehouses", "ProductVariant"]);
        table.ForeignKeys.Where(x => x.PrincipalTable is "Warehouses" or "ProductVariant")
            .Should().OnlyContain(x => x.OnDelete == ReferentialAction.Restrict);

        var indexes = operations.OfType<CreateIndexOperation>().ToList();
        indexes.Should().HaveCount(4);
        var unique = indexes.Single(x => x.Name ==
            "UX_InvoiceInputStockSupplementalMovements_Store_LegacySourceKey");
        unique.IsUnique.Should().BeTrue();
        unique.Columns.Should().Equal("StoreId", "LegacySourceKey");
        unique.Filter.Should().Be(
    "[StoreId] IS NOT NULL AND [LegacySourceKey] IS NOT NULL");
        var read = indexes.Single(x => x.Name ==
            "IX_InvoiceInputStockSupplementalMovements_Store_Warehouse_Variant_EffectiveAt");
        read.Columns.Should().Equal("StoreId", "WarehouseId", "ProductVariantId", "EffectiveAtUtc");
    }

    [Fact]
    public void Migration_down_returns_to_the_prior_schema_without_business_data_operations()
    {
        var migration = new AddInvoiceInputStockSupplementalMovements();

        var drop = migration.DownOperations.OfType<DropTableOperation>().Should().ContainSingle().Which;
        drop.Name.Should().Be(TableName);
        migration.DownOperations.Should().HaveCount(1);
        migration.DownOperations.Should().NotContain(x =>
            x.GetType() == typeof(SqlOperation)
            || x.GetType() == typeof(InsertDataOperation)
            || x.GetType() == typeof(UpdateDataOperation)
            || x.GetType() == typeof(DeleteDataOperation));
    }

    [Fact]
    public void Current_model_and_manifest_include_the_c2_table_indexes_and_migration_once()
    {
        using var db = new PreflightAcceptanceDatabase().CreateContext();
        var migrations = db.Database.GetMigrations().ToList();

        migrations.Should().ContainSingle(x => x == MigrationId);
        migrations.Should().ContainSingle(x => x == "20260923140000_AddInvoiceStockLegacyDocumentReferences");
        db.Model.FindEntityType("GaoApp.Domain.Entities.InvoiceInputStockSupplementalMovement")
            .Should().NotBeNull();

        var manifest = new EfCoreDatabaseSchemaManifestCatalog(db).GetCurrentManifest();
        manifest.AppliedMigrationIds.Should().Contain("20260923140000_AddInvoiceStockLegacyDocumentReferences");
        var table = manifest.Tables.Single(x =>
            x.Identity.Name == "invoiceinputstocksupplementalmovements");
        table.Indexes.Should().ContainSingle(x =>
            x.Name == "ux_invoiceinputstocksupplementalmovements_store_legacysourcekey"
            && x.IsUnique
            && x.KeyColumns.Select(y => y.Name).SequenceEqual(new[] { "storeid", "legacysourcekey" }));
        table.Indexes.Should().ContainSingle(x =>
            x.Name == "ix_invoiceinputstocksupplementalmovements_store_warehouse_variant_effectiveat"
            && x.KeyColumns.Select(y => y.Name).SequenceEqual(
                new[] { "storeid", "warehouseid", "productvariantid", "effectiveatutc" }));
        table.ForeignKeys.Should().HaveCount(3);
    }
}
