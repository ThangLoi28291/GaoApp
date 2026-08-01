using FluentAssertions;
using GaoApp.Infrastructure.Data.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Configuration;

[Collection("R1FinalDatabasePreflight")]
public sealed class InventoryPostingMigrationTests
{
    private const string BaselineMigrationId =
        "20260726073029_InitialProductionBaseline";

    private const string InventoryPostingMigrationId =
        "20260801110856_AddInventoryPostingIdempotency";

    [Fact]
    public async Task Fresh_database_should_apply_nullable_key_and_filtered_unique_index()
    {
        await using var database = new InventoryPostingLocalDb();

        await database.MigrateAsync();

        await using var db = database.CreateHostContext();
        (await db.Database.GetAppliedMigrationsAsync())
            .Should().Equal(
                BaselineMigrationId,
                InventoryPostingMigrationId);
        db.Database.HasPendingModelChanges().Should().BeFalse();
        var manifest = new EfCoreDatabaseSchemaManifestCatalog(db)
            .GetCurrentManifest();
        var inventoryTransactions = manifest.Tables.Single(
            x => x.Identity.Name == "inventorytransactions");
        inventoryTransactions.Columns.Should().ContainSingle(
            x => x.Name == "idempotencykey"
                && x.StoreType == "varbinary(32)"
                && x.IsNullable
                && !x.HasDefault);
        var idempotencyIndex = inventoryTransactions.Indexes
            .Should().ContainSingle(
            x => x.Name
                    == "ux_inventorytransactions_storeid_idempotencykey_active"
                && x.IsUnique)
            .Which;
        idempotencyIndex.KeyColumns
            .Select(column => column.Name)
            .Should().Equal("storeid", "idempotencykey");
        idempotencyIndex.Filter.Should()
            .Be("idempotencykeyisnotnullandisdeleted=0");
        (await database.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM sys.columns
            WHERE object_id = OBJECT_ID(N'[dbo].[InventoryTransactions]')
              AND name = N'IdempotencyKey'
              AND is_nullable = 1
              AND max_length = 32;
            """)).Should().Be(1);
        (await database.ExecuteScalarAsync<string>(
            """
            SELECT filter_definition
            FROM sys.indexes
            WHERE object_id = OBJECT_ID(N'[dbo].[InventoryTransactions]')
              AND name =
                  N'UX_InventoryTransactions_StoreId_IdempotencyKey_Active'
              AND is_unique = 1;
            """)).Should().Be(
                "([IdempotencyKey] IS NOT NULL AND [IsDeleted]=(0))");
    }

    [Fact]
    public async Task Baseline_database_should_upgrade_without_backfilling_legacy_rows()
    {
        await using var database = new InventoryPostingLocalDb();
        await database.MigrateAsync(BaselineMigrationId);
        var seed = await database.SeedInventoryCatalogAsync();

        (await database.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM sys.columns
            WHERE object_id = OBJECT_ID(N'[dbo].[InventoryTransactions]')
              AND name = N'IdempotencyKey';
            """)).Should().Be(0);

        await database.ExecuteAsync(
            $"""
             INSERT INTO [dbo].[InventoryTransactions]
             (
                 [WarehouseId],
                 [ProductVariantId],
                 [TransactionType],
                 [ReferenceType],
                 [ReferenceId],
                 [ReferenceLineId],
                 [QuantityChange],
                 [BeforeQty],
                 [AfterQty],
                 [UnitCostSnapshot],
                 [TotalCost],
                 [BeforeInventoryValue],
                 [AfterInventoryValue],
                 [RunningAverageUnitCostAfter],
                 [CostSourceType],
                 [IsProvisionalCost],
                 [OccurredAtUtc],
                 [CreatedAtUtc],
                 [IsDeleted],
                 [StoreId]
             )
             VALUES
             (
                 {seed.WarehouseId},
                 {seed.ProductVariantId},
                 1,
                 1,
                 N'R2-LEGACY',
                 1,
                 2,
                 0,
                 2,
                 10,
                 20,
                 0,
                 20,
                 10,
                 1,
                 0,
                 SYSUTCDATETIME(),
                 SYSUTCDATETIME(),
                 0,
                 {seed.StoreId}
             );
             """);

        await database.MigrateAsync();

        await using var db = database.CreateHostContext();
        (await db.Database.GetAppliedMigrationsAsync())
            .Should().Equal(
                BaselineMigrationId,
                InventoryPostingMigrationId);
        (await database.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM [dbo].[InventoryTransactions]
            WHERE [ReferenceId] = N'R2-LEGACY'
              AND [IdempotencyKey] IS NULL;
            """)).Should().Be(1);

        await database.ExecuteAsync(
            CreateDuplicateKeyInsertSql("R2-KEYED-ONE"));

        var duplicate = () => database.ExecuteAsync(
            CreateDuplicateKeyInsertSql("R2-KEYED-TWO"));
        var duplicateException = await duplicate.Should()
            .ThrowAsync<SqlException>();
        duplicateException.Which.Number
            .Should().BeOneOf(2601, 2627);
    }

    private static string CreateDuplicateKeyInsertSql(
        string referenceId)
        =>
            $"""
             INSERT INTO [dbo].[InventoryTransactions]
             (
                 [WarehouseId],
                 [ProductVariantId],
                 [IdempotencyKey],
                 [TransactionType],
                 [ReferenceType],
                 [ReferenceId],
                 [ReferenceLineId],
                 [QuantityChange],
                 [BeforeQty],
                 [AfterQty],
                 [UnitCostSnapshot],
                 [TotalCost],
                 [BeforeInventoryValue],
                 [AfterInventoryValue],
                 [RunningAverageUnitCostAfter],
                 [CostSourceType],
                 [IsProvisionalCost],
                 [OccurredAtUtc],
                 [CreatedAtUtc],
                 [IsDeleted],
                 [StoreId]
             )
             SELECT
                 [WarehouseId],
                 [ProductVariantId],
                 0x000102030405060708090A0B0C0D0E0F101112131415161718191A1B1C1D1E1F,
                 [TransactionType],
                 [ReferenceType],
                 N'{referenceId}',
                 [ReferenceLineId],
                 [QuantityChange],
                 [BeforeQty],
                 [AfterQty],
                 [UnitCostSnapshot],
                 [TotalCost],
                 [BeforeInventoryValue],
                 [AfterInventoryValue],
                 [RunningAverageUnitCostAfter],
                 [CostSourceType],
                 [IsProvisionalCost],
                 [OccurredAtUtc],
                 SYSUTCDATETIME(),
                 0,
                 [StoreId]
             FROM [dbo].[InventoryTransactions]
             WHERE [ReferenceId] = N'R2-LEGACY';
             """;
}
