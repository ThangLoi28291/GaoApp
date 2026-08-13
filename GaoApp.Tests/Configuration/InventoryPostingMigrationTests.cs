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
    public async Task Inventory_connections_should_pool_and_bound_login_retries()
    {
        await using var database = new InventoryPostingLocalDb();
        var settings = new SqlConnectionStringBuilder(
            database.ConnectionString);
        using var connection = LocalDbSqlConnectionFactory.Create(
            database.ConnectionString);

        settings.Pooling.Should().BeTrue();
        settings.ConnectTimeout.Should().Be(30);
        connection.RetryLogicProvider.RetryLogic.NumberOfTries
            .Should().Be(2);
    }

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
                 [ReferenceSubKey],
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
                 [CostFinalizedAtUtc],
                 [OccurredAtUtc],
                 [Note],
                 [CreatedAtUtc],
                 [CreatedBy],
                 [UpdatedAtUtc],
                 [UpdatedBy],
                 [IsDeleted],
                 [DeletedAtUtc],
                 [DeletedBy],
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
                 N'LEGACY-LINE-1',
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
                 CONVERT(datetime2, N'2026-07-30T09:11:12.1234567', 126),
                 CONVERT(datetime2, N'2026-07-30T09:10:11.1234567', 126),
                 N'Legacy movement before durable key migration',
                 CONVERT(datetime2, N'2026-07-29T08:09:10.1234567', 126),
                 41,
                 CONVERT(datetime2, N'2026-07-31T10:11:12.1234567', 126),
                 42,
                 0,
                 NULL,
                 NULL,
                 {seed.StoreId}
             );
             """);

        var beforeRowCount = await ReadLegacyRowCountAsync(database);
        beforeRowCount.Should().Be(1);
        var beforeSignature = await ReadLegacySignatureAsync(database);
        var beforeDependents =
            await ReadHistoricalDependentCountsAsync(database);

        await database.MigrateAsync();

        await using var db = database.CreateHostContext();
        (await db.Database.GetAppliedMigrationsAsync())
            .Should().Equal(
                BaselineMigrationId,
                InventoryPostingMigrationId);
        var afterRowCount = await ReadLegacyRowCountAsync(database);
        afterRowCount.Should().Be(beforeRowCount);
        var afterSignature = await ReadLegacySignatureAsync(database);
        afterSignature.Should().BeEquivalentTo(beforeSignature);
        afterSignature.RowVersion.Should()
            .Equal(beforeSignature.RowVersion);
        (await database.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM [dbo].[InventoryTransactions]
            WHERE [ReferenceId] = N'R2-LEGACY'
              AND [IdempotencyKey] IS NULL;
            """)).Should().Be(1);
        var afterDependents =
            await ReadHistoricalDependentCountsAsync(database);
        afterDependents.Should().BeEquivalentTo(beforeDependents);

        await database.ExecuteAsync(
            CreateDuplicateKeyInsertSql("R2-KEYED-ONE"));

        var duplicate = () => database.ExecuteAsync(
            CreateDuplicateKeyInsertSql("R2-KEYED-TWO"));
        var duplicateException = await duplicate.Should()
            .ThrowAsync<SqlException>();
        duplicateException.Which.Number
            .Should().BeOneOf(2601, 2627);
    }

    private static Task<int> ReadLegacyRowCountAsync(
        InventoryPostingLocalDb database)
        => database.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM [dbo].[InventoryTransactions]
            WHERE [ReferenceId] = N'R2-LEGACY';
            """);

    private static async Task<LegacyTransactionSignature>
        ReadLegacySignatureAsync(InventoryPostingLocalDb database)
    {
        await using var connection =
            LocalDbSqlConnectionFactory.Create(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandTimeout = 30;
        command.CommandText =
            """
            SELECT
                [Id],
                [StoreId],
                [WarehouseId],
                [ProductVariantId],
                [TransactionType],
                [ReferenceType],
                [ReferenceId],
                [ReferenceLineId],
                [ReferenceSubKey],
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
                [CostFinalizedAtUtc],
                [OccurredAtUtc],
                [Note],
                [CreatedAtUtc],
                [CreatedBy],
                [UpdatedAtUtc],
                [UpdatedBy],
                [IsDeleted],
                [DeletedAtUtc],
                [DeletedBy],
                [RowVersion]
            FROM [dbo].[InventoryTransactions]
            WHERE [ReferenceId] = N'R2-LEGACY';
            """;
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            throw new InvalidOperationException(
                "The legacy inventory transaction was not found.");
        }

        var signature = new LegacyTransactionSignature(
            reader.GetInt32(0),
            reader.GetInt32(1),
            reader.GetInt32(2),
            reader.GetInt32(3),
            reader.GetInt32(4),
            reader.GetInt32(5),
            reader.GetString(6),
            ReadNullableInt32(reader, 7),
            ReadNullableString(reader, 8),
            reader.GetDecimal(9),
            reader.GetDecimal(10),
            reader.GetDecimal(11),
            reader.GetDecimal(12),
            reader.GetDecimal(13),
            reader.GetDecimal(14),
            reader.GetDecimal(15),
            reader.GetDecimal(16),
            reader.GetInt32(17),
            reader.GetBoolean(18),
            ReadNullableDateTime(reader, 19),
            reader.GetDateTime(20),
            ReadNullableString(reader, 21),
            reader.GetDateTime(22),
            ReadNullableInt32(reader, 23),
            ReadNullableDateTime(reader, 24),
            ReadNullableInt32(reader, 25),
            reader.GetBoolean(26),
            ReadNullableDateTime(reader, 27),
            ReadNullableInt32(reader, 28),
            reader.GetFieldValue<byte[]>(29));

        if (await reader.ReadAsync())
        {
            throw new InvalidOperationException(
                "The legacy inventory transaction identity is not unique.");
        }

        return signature;
    }

    private static async Task<HistoricalDependentCounts>
        ReadHistoricalDependentCountsAsync(
            InventoryPostingLocalDb database)
        => new(
            await database.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM [dbo].[InventoryValuationEntries];"),
            await database.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM [dbo].[InventoryCostLayers];"),
            await database.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM [dbo].[InventoryBalances];"));

    private static int? ReadNullableInt32(
        SqlDataReader reader,
        int ordinal)
        => reader.IsDBNull(ordinal)
            ? null
            : reader.GetInt32(ordinal);

    private static DateTime? ReadNullableDateTime(
        SqlDataReader reader,
        int ordinal)
        => reader.IsDBNull(ordinal)
            ? null
            : reader.GetDateTime(ordinal);

    private static string? ReadNullableString(
        SqlDataReader reader,
        int ordinal)
        => reader.IsDBNull(ordinal)
            ? null
            : reader.GetString(ordinal);

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

    private sealed record LegacyTransactionSignature(
        int Id,
        int StoreId,
        int WarehouseId,
        int ProductVariantId,
        int TransactionType,
        int ReferenceType,
        string ReferenceId,
        int? ReferenceLineId,
        string? ReferenceSubKey,
        decimal QuantityChange,
        decimal BeforeQty,
        decimal AfterQty,
        decimal UnitCostSnapshot,
        decimal TotalCost,
        decimal BeforeInventoryValue,
        decimal AfterInventoryValue,
        decimal RunningAverageUnitCostAfter,
        int CostSourceType,
        bool IsProvisionalCost,
        DateTime? CostFinalizedAtUtc,
        DateTime OccurredAtUtc,
        string? Note,
        DateTime CreatedAtUtc,
        int? CreatedBy,
        DateTime? UpdatedAtUtc,
        int? UpdatedBy,
        bool IsDeleted,
        DateTime? DeletedAtUtc,
        int? DeletedBy,
        byte[] RowVersion);

    private sealed record HistoricalDependentCounts(
        int ValuationEntries,
        int CostLayers,
        int InventoryBalances);
}
