using System.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Data.Migrations;

public sealed record DatabaseSchemaInspectionReport(
    string Status,
    IReadOnlyList<string> SourceMigrations,
    IReadOnlyList<string> AppliedMigrations,
    IReadOnlyList<string> PendingMigrations,
    DatabaseSchemaMismatchCounts? MismatchCounts,
    IReadOnlyList<string> ExpectedOnly,
    IReadOnlyList<string> ActualOnly);

/// <summary>Read-only schema diagnostics. Never migrates, seeds, or repairs history.</summary>
public sealed class DatabaseSchemaInspection(AppDbContext db)
{
    public async Task<DatabaseSchemaInspectionReport> ReadAsync(CancellationToken ct = default)
    {
        var source = db.Database.GetMigrations().ToArray();
        var wasOpen = db.Database.GetDbConnection().State == ConnectionState.Open;
        try
        {
            if (!wasOpen) await db.Database.OpenConnectionAsync(ct);
            var applied = (await db.Database.GetAppliedMigrationsAsync(ct)).ToArray();
            var catalog = new EfCoreDatabaseSchemaManifestCatalog(db);
            if (!catalog.TryGetManifestForAppliedMigrationPrefix(applied, out var expected))
                return new("UnsupportedHistory", source, applied, [], null, [], []);

            var actual = await new SqlServerSchemaSnapshotReader(db).ReadAsync(applied, ct);
            expected = KnownImportJournalSchema.IncludePresentJournals(expected, actual);
            var comparison = DatabaseSchemaComparer.Compare(expected, actual);
            var expectedRecords = DatabaseSchemaCanonicalizer.CreateCategoryRecords(expected).All;
            var actualRecords = DatabaseSchemaCanonicalizer.CreateCategoryRecords(actual).All;
            var pending = source.Skip(applied.Length).ToArray();
            return new(comparison.IsMatch ? (pending.Length > 0 ? "MatchingAppliedPrefix" : "MatchingCurrentSchema") : "StructuralSchemaMismatch",
                source, applied, pending, comparison.Mismatches,
                expectedRecords.Except(actualRecords, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
                actualRecords.Except(expectedRecords, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());
        }
        finally
        {
            if (!wasOpen) await db.Database.CloseConnectionAsync();
        }
    }
}
