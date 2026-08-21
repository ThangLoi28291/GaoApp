using FluentAssertions;
using GaoApp.Application.Services.Inventory;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Configuration;

[Collection("R1FinalDatabasePreflight")]
public sealed class InputInvoiceIdentityMigrationTests
{
    private const string PreviousMigrationId =
        "20260817090000_AddPurchaseReceiptCostCapitalizationPolicy";
    private const string IdentityMigrationId =
        "20260817150000_AddInputInvoiceIdentityUniqueness";

    [Fact]
    public async Task Upgrade_should_backfill_normalized_identity_and_enforce_both_unique_keys()
    {
        await using var database = new InventoryPostingLocalDb();
        await database.MigrateAsync(PreviousMigrationId);

        var seed = await database.SeedInventoryCatalogAsync();

        await InsertLegacyInvoiceAsync(
            database,
            seed.StoreId,
            "031.277.0607-001",
            " c26 taa ",
            " 00 0123 ",
            "2026-08-17T23:59:58",
            "HASH-ONE");

        await database.MigrateAsync();

        (await database.ExecuteScalarAsync<string>(
            "SELECT [NormalizedSellerTaxCode] FROM [dbo].[InputInvoiceHead];"))
            .Should().Be("0312770607001");

        (await database.ExecuteScalarAsync<string>(
            "SELECT [NormalizedInvoiceSeries] FROM [dbo].[InputInvoiceHead];"))
            .Should().Be("C26TAA");

        (await database.ExecuteScalarAsync<string>(
            "SELECT [NormalizedInvoiceNumber] FROM [dbo].[InputInvoiceHead];"))
            .Should().Be("000123");

        (await database.ExecuteScalarAsync<DateTime>(
            "SELECT [InvoiceIdentityDate] FROM [dbo].[InputInvoiceHead];"))
            .Should().Be(new DateTime(2026, 8, 17));

        await AssertIdentityMigrationArtifactsPresentAsync(database);

        var businessDuplicate = () => InsertNormalizedInvoiceAsync(
            database,
            seed.StoreId,
            "HASH-TWO");

        (await businessDuplicate.Should().ThrowAsync<SqlException>())
            .Which.Number.Should().BeOneOf(2601, 2627);

        var hashDuplicate = () => InsertNormalizedInvoiceAsync(
            database,
            seed.StoreId,
            "HASH-ONE",
            invoiceNumber: "DIFFERENT");

        (await hashDuplicate.Should().ThrowAsync<SqlException>())
            .Which.Number.Should().BeOneOf(2601, 2627);
    }

    [Fact]
    public async Task Upgrade_should_fail_closed_when_legacy_business_identity_is_duplicated()
    {
        await using var database = new InventoryPostingLocalDb();
        await database.MigrateAsync(PreviousMigrationId);

        var seed = await database.SeedInventoryCatalogAsync();

        await InsertLegacyInvoiceAsync(
            database,
            seed.StoreId,
            "0312770607",
            "C26TAA",
            "123",
            "2026-08-17T00:00:00",
            "HASH-A");

        await InsertLegacyInvoiceAsync(
            database,
            seed.StoreId,
            "031.277.0607",
            " c26 taa ",
            "1 23",
            "2026-08-17T22:00:00",
            "HASH-B");

        var migrate = () => database.MigrateAsync();

        var exception = await migrate
            .Should()
            .ThrowAsync<SqlException>();

        exception.Which.Number.Should().Be(51001);

        await AssertIdentityMigrationArtifactsAbsentAsync(database);
    }

    [Fact]
    public async Task Upgrade_should_fail_closed_when_legacy_xml_hash_is_duplicated()
    {
        await using var database = new InventoryPostingLocalDb();
        await database.MigrateAsync(PreviousMigrationId);

        var seed = await database.SeedInventoryCatalogAsync();

        await InsertLegacyInvoiceAsync(
            database,
            seed.StoreId,
            "0312770607",
            "C26TAA",
            "123",
            "2026-08-17T00:00:00",
            "SAME-HASH");

        await InsertLegacyInvoiceAsync(
            database,
            seed.StoreId,
            "0312770607",
            "C26TAA",
            "124",
            "2026-08-18T00:00:00",
            "SAME-HASH");

        var migrate = () => database.MigrateAsync();

        var exception = await migrate
            .Should()
            .ThrowAsync<SqlException>();

        exception.Which.Number.Should().Be(51002);

        await AssertIdentityMigrationArtifactsAbsentAsync(database);
    }

    [Fact]
    public async Task Upgrade_should_preserve_incomplete_legacy_rows_without_claiming_identity()
    {
        await using var database = new InventoryPostingLocalDb();
        await database.MigrateAsync(PreviousMigrationId);

        var seed = await database.SeedInventoryCatalogAsync();

        await InsertLegacyInvoiceAsync(
            database,
            seed.StoreId,
            string.Empty,
            string.Empty,
            string.Empty,
            "2026-08-17T00:00:00",
            "LEGACY-INCOMPLETE");

        await database.MigrateAsync();

        (await database.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM [dbo].[InputInvoiceHead]
            WHERE [XmlHash] = N'LEGACY-INCOMPLETE'
              AND [NormalizedSellerTaxCode] IS NULL
              AND [NormalizedInvoiceSeries] IS NULL
              AND [NormalizedInvoiceNumber] IS NULL;
            """))
            .Should().Be(1);

        (await database.ExecuteScalarAsync<DateTime>(
            """
            SELECT [InvoiceIdentityDate]
            FROM [dbo].[InputInvoiceHead]
            WHERE [XmlHash] = N'LEGACY-INCOMPLETE';
            """))
            .Should().Be(new DateTime(2026, 8, 17));
    }

    [Fact]
    public async Task Upgrade_should_match_supported_csharp_and_sql_normalization_rules()
    {
        await using var database = new InventoryPostingLocalDb();
        await database.MigrateAsync(PreviousMigrationId);

        var seed = await database.SeedInventoryCatalogAsync();

        await InsertLegacyInvoiceAsync(
            database,
            seed.StoreId,
            "placeholder",
            "placeholder",
            "placeholder",
            "2026-08-17T23:59:58",
            "PARITY-HASH");

        await database.ExecuteAsync(
            """
            UPDATE [dbo].[InputInvoiceHead]
            SET
                [SellerTaxCode] =
                    N' 031.' + NCHAR(9)
                    + N'277-0607' + NCHAR(13)
                    + NCHAR(10) + N'-001 ',

                [InvoiceSeries] =
                    N' C26/' + NCHAR(9)
                    + N'TAA-A ',

                [InvoiceNumber] =
                    N' 00/01' + NCHAR(13)
                    + NCHAR(10) + N'23-A '

            WHERE [XmlHash] = N'PARITY-HASH';
            """);

        await database.MigrateAsync();

        var sellerInput =
            " 031.\t277-0607\r\n-001 ";

        var seriesInput =
            " C26/\tTAA-A ";

        var numberInput =
            " 00/01\r\n23-A ";

        (await database.ExecuteScalarAsync<string>(
            """
            SELECT [NormalizedSellerTaxCode]
            FROM [dbo].[InputInvoiceHead]
            WHERE [XmlHash] = N'PARITY-HASH';
            """))
            .Should().Be(
                InputInvoiceIdentityPolicy.NormalizeTaxCode(
                    sellerInput));

        (await database.ExecuteScalarAsync<string>(
            """
            SELECT [NormalizedInvoiceSeries]
            FROM [dbo].[InputInvoiceHead]
            WHERE [XmlHash] = N'PARITY-HASH';
            """))
            .Should().Be(
                InputInvoiceIdentityPolicy.NormalizeIdentityToken(
                    seriesInput));

        (await database.ExecuteScalarAsync<string>(
            """
            SELECT [NormalizedInvoiceNumber]
            FROM [dbo].[InputInvoiceHead]
            WHERE [XmlHash] = N'PARITY-HASH';
            """))
            .Should().Be(
                InputInvoiceIdentityPolicy.NormalizeIdentityToken(
                    numberInput));

        (await database.ExecuteScalarAsync<DateTime>(
            """
            SELECT [InvoiceIdentityDate]
            FROM [dbo].[InputInvoiceHead]
            WHERE [XmlHash] = N'PARITY-HASH';
            """))
            .Should().Be(new DateTime(2026, 8, 17));
    }

    [Fact]
    public async Task Upgrade_should_allow_same_business_identity_and_hash_in_different_stores()
    {
        await using var database = new InventoryPostingLocalDb();
        await database.MigrateAsync(PreviousMigrationId);

        var firstSeed = await database.SeedInventoryCatalogAsync();
        var secondSeed = await database.SeedInventoryCatalogAsync();

        await InsertLegacyInvoiceAsync(
            database,
            firstSeed.StoreId,
            "0312770607",
            "C26TAA",
            "000123",
            "2026-08-17T00:00:00",
            "SAME-CROSS-STORE-HASH");

        await InsertLegacyInvoiceAsync(
            database,
            secondSeed.StoreId,
            "0312770607",
            "C26TAA",
            "000123",
            "2026-08-17T00:00:00",
            "SAME-CROSS-STORE-HASH");

        await database.MigrateAsync();

        (await database.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM [dbo].[InputInvoiceHead]
            WHERE [XmlHash] = N'SAME-CROSS-STORE-HASH'
              AND [NormalizedSellerTaxCode] = N'0312770607'
              AND [NormalizedInvoiceSeries] = N'C26TAA'
              AND [NormalizedInvoiceNumber] = N'000123'
              AND [InvoiceIdentityDate]
                    = CONVERT(date, N'2026-08-17');
            """))
            .Should().Be(2);

        await AssertIdentityMigrationArtifactsPresentAsync(database);
    }

    [Fact]
    public async Task Upgrade_should_exclude_soft_deleted_rows_from_active_uniqueness()
    {
        await using var database = new InventoryPostingLocalDb();
        await database.MigrateAsync(PreviousMigrationId);

        var seed = await database.SeedInventoryCatalogAsync();

        await InsertLegacyInvoiceAsync(
            database,
            seed.StoreId,
            "0312770607",
            "C26TAA",
            "000123",
            "2026-08-17T00:00:00",
            "SOFT-DELETE-HASH");

        await InsertLegacyInvoiceAsync(
            database,
            seed.StoreId,
            "0312770607",
            "C26TAA",
            "000123",
            "2026-08-17T00:00:00",
            "SOFT-DELETE-HASH",
            isDeleted: true);

        await database.MigrateAsync();

        (await database.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM [dbo].[InputInvoiceHead]
            WHERE [IsDeleted] = 0
              AND [XmlHash] = N'SOFT-DELETE-HASH'
              AND [NormalizedSellerTaxCode] = N'0312770607'
              AND [NormalizedInvoiceSeries] = N'C26TAA'
              AND [NormalizedInvoiceNumber] = N'000123'
              AND [InvoiceIdentityDate]
                    = CONVERT(date, N'2026-08-17');
            """))
            .Should().Be(1);

        (await database.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM [dbo].[InputInvoiceHead]
            WHERE [IsDeleted] = 1
              AND [XmlHash] = N'SOFT-DELETE-HASH'
              AND [NormalizedSellerTaxCode] IS NULL
              AND [NormalizedInvoiceSeries] IS NULL
              AND [NormalizedInvoiceNumber] IS NULL
              AND [InvoiceIdentityDate] IS NULL;
            """))
            .Should().Be(1);

        await AssertIdentityMigrationArtifactsPresentAsync(database);
    }

    [Fact]
    public async Task Failed_upgrade_should_apply_after_duplicate_data_is_remediated()
    {
        await using var database = new InventoryPostingLocalDb();
        await database.MigrateAsync(PreviousMigrationId);

        var seed = await database.SeedInventoryCatalogAsync();

        await InsertLegacyInvoiceAsync(
            database,
            seed.StoreId,
            "0312770607",
            "C26TAA",
            "000123",
            "2026-08-17T00:00:00",
            "RETRY-A");

        await InsertLegacyInvoiceAsync(
            database,
            seed.StoreId,
            "031.277.0607",
            " c26 taa ",
            "00 0123",
            "2026-08-17T22:00:00",
            "RETRY-B");

        var firstAttempt = () => database.MigrateAsync();

        var exception = await firstAttempt
            .Should()
            .ThrowAsync<SqlException>();

        exception.Which.Number.Should().Be(51001);

        await AssertIdentityMigrationArtifactsAbsentAsync(database);

        await database.ExecuteAsync(
            """
            DELETE FROM [dbo].[InputInvoiceHead]
            WHERE [Id] =
            (
                SELECT MAX([Id])
                FROM [dbo].[InputInvoiceHead]
                WHERE [XmlHash]
                    IN (N'RETRY-A', N'RETRY-B')
            );
            """);

        await database.MigrateAsync();

        await AssertIdentityMigrationArtifactsPresentAsync(database);

        (await database.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM [dbo].[InputInvoiceHead]
            WHERE [NormalizedSellerTaxCode] = N'0312770607'
              AND [NormalizedInvoiceSeries] = N'C26TAA'
              AND [NormalizedInvoiceNumber] = N'000123'
              AND [InvoiceIdentityDate]
                    = CONVERT(date, N'2026-08-17');
            """))
            .Should().Be(1);
    }

    [Fact]
    public async Task Down_should_restore_previous_schema_without_identity_artifacts()
    {
        await using var database = new InventoryPostingLocalDb();

        await database.MigrateAsync();
        await AssertIdentityMigrationArtifactsPresentAsync(database);

        await database.MigrateAsync(PreviousMigrationId);
        await AssertIdentityMigrationArtifactsAbsentAsync(database);
    }

    private static async Task AssertIdentityMigrationArtifactsAbsentAsync(
        InventoryPostingLocalDb database)
    {
        (await database.ExecuteScalarAsync<int>(
            $"""
             SELECT COUNT(*)
             FROM [dbo].[__EFMigrationsHistory]
             WHERE [MigrationId] = N'{IdentityMigrationId}';
             """))
            .Should().Be(0);

        (await database.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM sys.columns
            WHERE object_id =
                OBJECT_ID(N'[dbo].[InputInvoiceHead]')
              AND name IN
              (
                  N'NormalizedSellerTaxCode',
                  N'NormalizedInvoiceSeries',
                  N'NormalizedInvoiceNumber',
                  N'InvoiceIdentityDate'
              );
            """))
            .Should().Be(0);

        (await database.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM sys.indexes
            WHERE object_id =
                OBJECT_ID(N'[dbo].[InputInvoiceHead]')
              AND name IN
              (
                  N'UX_InputInvoiceHead_StoreId_BusinessIdentity_Active',
                  N'UX_InputInvoiceHead_StoreId_XmlHash_Active'
              );
            """))
            .Should().Be(0);
    }

    private static async Task AssertIdentityMigrationArtifactsPresentAsync(
        InventoryPostingLocalDb database)
    {
        (await database.ExecuteScalarAsync<int>(
            $"""
             SELECT COUNT(*)
             FROM [dbo].[__EFMigrationsHistory]
             WHERE [MigrationId] = N'{IdentityMigrationId}';
             """))
            .Should().Be(1);

        (await database.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM sys.columns
            WHERE object_id =
                OBJECT_ID(N'[dbo].[InputInvoiceHead]')
              AND name IN
              (
                  N'NormalizedSellerTaxCode',
                  N'NormalizedInvoiceSeries',
                  N'NormalizedInvoiceNumber',
                  N'InvoiceIdentityDate'
              );
            """))
            .Should().Be(4);

        (await database.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM sys.indexes
            WHERE object_id =
                OBJECT_ID(N'[dbo].[InputInvoiceHead]')
              AND name IN
              (
                  N'UX_InputInvoiceHead_StoreId_BusinessIdentity_Active',
                  N'UX_InputInvoiceHead_StoreId_XmlHash_Active'
              );
            """))
            .Should().Be(2);
    }

    private static Task InsertLegacyInvoiceAsync(
        InventoryPostingLocalDb database,
        int storeId,
        string sellerTaxCode,
        string invoiceSeries,
        string invoiceNumber,
        string invoiceDate,
        string xmlHash,
        bool isDeleted = false)
        => database.ExecuteAsync(
            $"""
             INSERT INTO [dbo].[InputInvoiceHead]
                 ([InvoiceSeries], [InvoiceNumber], [InvoiceDate], [SellerTaxCode],
                  [TotalBeforeTax], [TotalTaxAmount], [TotalPaymentAmount], [XmlHash],
                  [CreatedAtUtc], [IsDeleted], [StoreId])
             VALUES
                 (N'{invoiceSeries}', N'{invoiceNumber}', CONVERT(datetime2, N'{invoiceDate}', 126),
                  N'{sellerTaxCode}', 10, 1, 11, N'{xmlHash}', SYSUTCDATETIME(),
                  {(isDeleted ? 1 : 0)}, {storeId});
             """);

    private static Task InsertNormalizedInvoiceAsync(
        InventoryPostingLocalDb database,
        int storeId,
        string xmlHash,
        string invoiceNumber = "000123")
        => database.ExecuteAsync(
            $"""
             INSERT INTO [dbo].[InputInvoiceHead]
                 ([InvoiceSeries], [InvoiceNumber], [InvoiceDate], [SellerTaxCode],
                  [NormalizedSellerTaxCode], [NormalizedInvoiceSeries], [NormalizedInvoiceNumber],
                  [InvoiceIdentityDate], [TotalBeforeTax], [TotalTaxAmount], [TotalPaymentAmount],
                  [XmlHash], [CreatedAtUtc], [IsDeleted], [StoreId])
             VALUES
                 (N'C26TAA', N'{invoiceNumber}', CONVERT(datetime2, N'2026-08-17', 126), N'0312770607001',
                  N'0312770607001', N'C26TAA', N'{invoiceNumber}', CONVERT(date, N'2026-08-17'),
                  10, 1, 11, N'{xmlHash}', SYSUTCDATETIME(), 0, {storeId});
             """);
}