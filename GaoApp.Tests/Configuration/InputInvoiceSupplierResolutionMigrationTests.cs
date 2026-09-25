using FluentAssertions;
using GaoApp.Infrastructure.Migrations;
using GaoApp.Infrastructure.Data.Migrations;
using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GaoApp.Tests.Configuration;

[Collection("R1FinalDatabasePreflight")]
public sealed class InputInvoiceSupplierResolutionMigrationTests
{
    private const string PreviousMigrationId =
        "20260817150000_AddInputInvoiceIdentityUniqueness";

    [Fact]
    public async Task Current_migration_manifest_should_match_the_actual_localdb_schema()
    {
        await using var database = new PreflightAcceptanceDatabase();
        await using var db = database.CreateContext();
        await new EfCoreDatabaseMigrationExecutor(db).MigrateAsync();
        var ids = (await db.Database.GetAppliedMigrationsAsync()).ToList();
        var expected = new EfCoreDatabaseSchemaManifestCatalog(db).GetCurrentManifest();
        await db.Database.OpenConnectionAsync();
        var actual = await new SqlServerSchemaSnapshotReader(db).ReadAsync(ids);
        var expectedColumns = DatabaseSchemaCanonicalizer.CreateCategoryRecords(expected).Columns;
        var actualColumns = DatabaseSchemaCanonicalizer.CreateCategoryRecords(actual).Columns;
        var differences = expectedColumns
            .Except(actualColumns, StringComparer.Ordinal)
            .Select(x => "EXPECTED " + x)
            .Concat(actualColumns.Except(expectedColumns, StringComparer.Ordinal)
                .Select(x => "ACTUAL " + x))
            .ToArray();

        differences.Should().BeEmpty(string.Join(Environment.NewLine, differences));
    }

    [Fact]
    public void Migration_should_be_single_hand_authored_source_with_no_designer()
    {
        var migration = new AddInputInvoiceSupplierResolution();
        migration.Should().BeAssignableTo<Migration>();

        var root = FindRepositoryRoot();
        File.Exists(Path.Combine(
                root,
                "GaoApp.Infrastructure",
                "Migrations",
                "20260822090000_AddInputInvoiceSupplierResolution.Designer.cs"))
            .Should().BeFalse();
    }

    [Fact]
    public async Task Upgrade_should_preserve_legacy_duplicates_without_backfill_and_down_should_reverse_schema()
    {
        await using var database = new InventoryPostingLocalDb();
        await database.MigrateAsync(PreviousMigrationId);
        int storeId;
        await using (var host = database.CreateHostContext())
        {
            var subdomain = $"resolution-migration-{Guid.NewGuid():N}";
            var store = new Store
            {
                Name = "Resolution migration",
                SubDomain = subdomain,
                SubDomainNormalized = subdomain.ToUpperInvariant(),
                IsActive = true
            };
            await LegacyStoreSeed.InsertAsync(host, store);
            storeId = store.Id;
        }
        await database.ExecuteAsync(
            $"""
             INSERT INTO [dbo].[Suppliers]
                 ([Code], [Name], [TaxCode], [IsActive], [SortOrder],
                  [CreatedAtUtc], [IsDeleted], [StoreId])
             VALUES
                 (N'LEGACY-A', N'Legacy A', N'031.277-0607', 1, 0, SYSUTCDATETIME(), 0, {storeId}),
                 (N'LEGACY-B', N'Legacy B', N'031-277 0607', 1, 0, SYSUTCDATETIME(), 0, {storeId});

             INSERT INTO [dbo].[InputInvoiceHead]
                 ([InvoiceSeries], [InvoiceNumber], [InvoiceDate], [SellerTaxCode],
                  [NormalizedSellerTaxCode], [NormalizedInvoiceSeries], [NormalizedInvoiceNumber],
                  [InvoiceIdentityDate], [TotalBeforeTax], [TotalTaxAmount], [TotalPaymentAmount],
                  [XmlHash], [CreatedAtUtc], [IsDeleted], [StoreId])
             VALUES
                 (N'C26TAA', N'9001', CONVERT(datetime2, N'2026-08-22', 126), N'0312770607',
                  N'0312770607', N'C26TAA', N'9001', CONVERT(date, N'2026-08-22'),
                  10, 1, 11, N'RESOLUTION-MIGRATION-LEGACY', SYSUTCDATETIME(), 0, {storeId});
             """);

        await database.MigrateAsync();

        (await database.ExecuteScalarAsync<int>(
            $"""
             SELECT COUNT(*)
             FROM [dbo].[Suppliers]
             WHERE [StoreId] = {storeId}
               AND [NormalizedTaxCode] = N'0312770607'
               AND [IsDeleted] = 0
               AND [IsActive] = 1;
             """))
            .Should().Be(2, "the migration must not rewrite ambiguous legacy master data");
        (await database.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM [dbo].[InputInvoiceHead]
            WHERE [XmlHash] = N'RESOLUTION-MIGRATION-LEGACY'
              AND [ResolvedSupplierId] IS NULL
              AND [SupplierResolutionStatus] = 0
              AND [SupplierResolutionUpdatedAtUtc] IS NULL;
            """))
            .Should().Be(1, "existing invoices must remain NotEvaluated without guessed canonical data");

        await database.MigrateAsync(PreviousMigrationId);

        (await database.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM sys.columns
            WHERE [object_id] IN
                (OBJECT_ID(N'[dbo].[Suppliers]'), OBJECT_ID(N'[dbo].[InputInvoiceHead]'))
              AND [name] IN
                (N'NormalizedTaxCode', N'ResolvedSupplierId',
                 N'SupplierResolutionStatus', N'SupplierResolutionUpdatedAtUtc');
            """))
            .Should().Be(0);
        (await database.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.tables WHERE [name] = N'InputInvoiceSupplierResolutionEvent';"))
            .Should().Be(0);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "GaoApp.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Repository root was not found.");
    }
}
