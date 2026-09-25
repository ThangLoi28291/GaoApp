using FluentAssertions;
using GaoApp.Infrastructure.Data.Migrations;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Configuration;

[Collection("R1FinalDatabasePreflight")]
public sealed class C2SchemaIndexDiagnosticTests
{
    [Fact]
    public async Task C2_current_schema_index_manifest_should_match_live_sql_server()
    {
        await using var database = new PreflightAcceptanceDatabase();
        await using var db = database.CreateContext();

        await new EfCoreDatabaseMigrationExecutor(db)
            .MigrateAsync();

        var history =
            await database.ReadMigrationHistoryAsync();

        var expected =
            new EfCoreDatabaseSchemaManifestCatalog(db)
                .GetCurrentManifest();

        await db.Database.OpenConnectionAsync();

        DatabaseSchemaManifest actual;

        try
        {
            actual =
                await new SqlServerSchemaSnapshotReader(db)
                    .ReadAsync(history);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }

        var expectedIndexes =
            DatabaseSchemaCanonicalizer
                .CreateCategoryRecords(expected)
                .Indexes;

        var actualIndexes =
            DatabaseSchemaCanonicalizer
                .CreateCategoryRecords(actual)
                .Indexes;

        var expectedOnly =
            expectedIndexes
                .Except(
                    actualIndexes,
                    StringComparer.Ordinal)
                .OrderBy(
                    x => x,
                    StringComparer.Ordinal)
                .ToArray();

        var actualOnly =
            actualIndexes
                .Except(
                    expectedIndexes,
                    StringComparer.Ordinal)
                .OrderBy(
                    x => x,
                    StringComparer.Ordinal)
                .ToArray();

        var message =
            "EXPECTED_ONLY:\n" +
            string.Join(
                "\n",
                expectedOnly) +
            "\n\nACTUAL_ONLY:\n" +
            string.Join(
                "\n",
                actualOnly);

        expectedOnly.Should()
            .BeEmpty(message);

        actualOnly.Should()
            .BeEmpty(message);
    }
}