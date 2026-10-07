using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace GaoApp.Infrastructure.Data.Migrations;

public sealed class SqlServerDatabaseBaselinePreflight
    : IDatabaseBaselinePreflight
{
    private static readonly DatabaseObjectIdentity HistoryTable =
        new("dbo", "__EFMigrationsHistory");

    private readonly AppDbContext _db;
    private readonly IDatabaseMigrationCatalog _migrationCatalog;
    private readonly ISqlServerDatabaseObjectInventoryReader _inventoryReader;
    private readonly IDatabaseSchemaManifestCatalog _manifestCatalog;
    private readonly ISqlServerSchemaSnapshotReader _schemaSnapshotReader;

    public SqlServerDatabaseBaselinePreflight(
        AppDbContext db,
        IDatabaseMigrationCatalog migrationCatalog,
        ISqlServerDatabaseObjectInventoryReader inventoryReader,
        IDatabaseSchemaManifestCatalog manifestCatalog,
        ISqlServerSchemaSnapshotReader schemaSnapshotReader)
    {
        _db = db;
        _migrationCatalog = migrationCatalog;
        _inventoryReader = inventoryReader;
        _manifestCatalog = manifestCatalog;
        _schemaSnapshotReader = schemaSnapshotReader;
    }

    public async Task<DatabaseCompatibilityResult> InspectAsync(
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var catalog = _migrationCatalog.CreateSnapshot();
        var sourceMigrationCount = catalog.SourceMigrationIds.Count;

        if (!string.Equals(
                _db.Database.ProviderName,
                "Microsoft.EntityFrameworkCore.SqlServer",
                StringComparison.Ordinal))
        {
            return Rejected(
                DatabaseCompatibilityState.Inaccessible,
                "UnsupportedDatabaseProvider",
                sourceMigrationCount);
        }

        bool databaseExists;

        try
        {
            var databaseCreator =
                _db.GetService<IRelationalDatabaseCreator>();
            databaseExists = await databaseCreator.ExistsAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return Rejected(
                DatabaseCompatibilityState.Inaccessible,
                "DatabaseExistenceCheckFailed",
                sourceMigrationCount);
        }

        if (!databaseExists)
        {
            return Allowed(
                DatabaseCompatibilityState.DatabaseMissing,
                "DatabaseMissing",
                sourceMigrationCount);
        }

        var wasOpen =
            _db.Database.GetDbConnection().State == ConnectionState.Open;

        try
        {
            if (!wasOpen)
            {
                await _db.Database.OpenConnectionAsync(ct);
            }

            var inventory = await _inventoryReader.ReadAsync(ct);
            var historyExists = inventory.Tables.Contains(HistoryTable);
            var userTableCount = inventory.Tables.Count
                - (historyExists ? 1 : 0);
            var structuralObjectCount =
                inventory.TotalStructuralObjectCount
                - (historyExists ? 1 : 0);
            var securityMetadataCounts =
                inventory.SecurityMetadata.Counts;

            if (!historyExists)
            {
                if (userTableCount > 0)
                {
                    return Rejected(
                        DatabaseCompatibilityState
                            .ExistingSchemaWithoutHistory,
                        "ExistingSchemaWithoutMigrationHistory",
                        sourceMigrationCount,
                        userTableCount: userTableCount,
                        structuralObjectCount: structuralObjectCount,
                        securityMetadataCounts: securityMetadataCounts);
                }

                return inventory.StructuralNonTableObjectCount == 0
                    ? Allowed(
                        DatabaseCompatibilityState.ExistingEmpty,
                        "ExistingEmptyDatabase",
                        sourceMigrationCount,
                        structuralObjectCount: structuralObjectCount,
                        securityMetadataCounts: securityMetadataCounts)
                    : Rejected(
                        DatabaseCompatibilityState.UnexpectedUserObjects,
                        "UnexpectedUserObjectsWithoutMigrationHistory",
                        sourceMigrationCount,
                        structuralObjectCount: structuralObjectCount,
                        securityMetadataCounts: securityMetadataCounts);
            }

            var appliedMigrationIds = (await _db.Database
                    .GetAppliedMigrationsAsync(ct))
                .ToList();

            if (!catalog.IsAppliedHistoryPrefix(appliedMigrationIds))
            {
                return Rejected(
                    DatabaseCompatibilityState.UnknownMigrationHistory,
                    "UnsupportedMigrationHistory",
                    sourceMigrationCount,
                    appliedMigrationIds.Count,
                    userTableCount,
                    structuralObjectCount,
                    securityMetadataCounts: securityMetadataCounts);
            }

            if (appliedMigrationIds.Count == 0)
            {
                return Rejected(
                    DatabaseCompatibilityState.UnsupportedMigrationPrefix,
                    "EmptyHistoryTableUnsupported",
                    sourceMigrationCount,
                    userTableCount: userTableCount,
                    structuralObjectCount: structuralObjectCount,
                    securityMetadataCounts: securityMetadataCounts);
            }

            if (!_manifestCatalog.TryGetManifestForAppliedMigrationPrefix(
                    appliedMigrationIds,
                    out var expectedManifest))
            {
                return Rejected(
                    DatabaseCompatibilityState.UnsupportedMigrationPrefix,
                    "SupportedPrefixManifestMissing",
                    sourceMigrationCount,
                    appliedMigrationIds.Count,
                    userTableCount,
                    structuralObjectCount,
                    securityMetadataCounts: securityMetadataCounts);
            }

            var expectedSchemas = expectedManifest.Tables
                .Select(table => table.Identity.Schema)
                .Concat(
                    expectedManifest.Sequences.Select(
                        sequence => sequence.Identity.Schema))
                .ToHashSet(StringComparer.Ordinal);
            var expectedTriggers = expectedManifest.Tables.SelectMany(table => table.Triggers
                .Select(trigger => new DatabaseObjectIdentity(table.Identity.Schema, trigger.Name))).ToHashSet();
            var unexpectedStructuralObjectCount =
                inventory.Views.Count
                + inventory.Procedures.Count
                + inventory.Functions.Count
                + inventory.Synonyms.Count
                + inventory.UserDefinedTypes.Count
                + inventory.OtherStructuralObjects.Count(x => !expectedTriggers.Contains(x))
                + inventory.UserSchemas.Count(
                    schema => !expectedSchemas.Contains(schema));

            if (unexpectedStructuralObjectCount > 0)
            {
                return Rejected(
                    DatabaseCompatibilityState.UnexpectedUserObjects,
                    "UnexpectedUserObjects",
                    sourceMigrationCount,
                    appliedMigrationIds.Count,
                    userTableCount,
                    structuralObjectCount,
                    securityMetadataCounts: securityMetadataCounts);
            }

            var actualManifest = await _schemaSnapshotReader.ReadAsync(
                appliedMigrationIds,
                ct);
            var comparison = DatabaseSchemaComparer.Compare(
                KnownImportJournalSchema.IncludePresentJournals(expectedManifest, actualManifest),
                actualManifest);

            if (!comparison.IsMatch)
            {
                return Rejected(
                    DatabaseCompatibilityState.PartialOrCorruptBaseline,
                    "StructuralSchemaMismatch",
                    sourceMigrationCount,
                    appliedMigrationIds.Count,
                    userTableCount,
                    structuralObjectCount,
                    CountMismatchCategories(comparison.Mismatches),
                    comparison.Mismatches,
                    securityMetadataCounts);
            }

            return appliedMigrationIds.Count < sourceMigrationCount
                ? Allowed(
                    DatabaseCompatibilityState.SupportedPendingUpgrade,
                    "SupportedMigrationPrefix",
                    sourceMigrationCount,
                    appliedMigrationIds.Count,
                    userTableCount,
                    structuralObjectCount,
                    securityMetadataCounts: securityMetadataCounts)
                : Allowed(
                    DatabaseCompatibilityState.CurrentBaseline,
                    "CurrentBaseline",
                    sourceMigrationCount,
                    appliedMigrationIds.Count,
                    userTableCount,
                    structuralObjectCount,
                    securityMetadataCounts: securityMetadataCounts);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return Rejected(
                DatabaseCompatibilityState.Inaccessible,
                "DatabaseMetadataReadFailed",
                sourceMigrationCount);
        }
        finally
        {
            if (!wasOpen)
            {
                await _db.Database.CloseConnectionAsync();
            }
        }
    }

    private static int CountMismatchCategories(
        DatabaseSchemaMismatchCounts mismatches)
        => new[]
        {
            mismatches.Tables,
            mismatches.Columns,
            mismatches.PrimaryKeys,
            mismatches.ForeignKeys,
            mismatches.Indexes,
            mismatches.CheckConstraints,
            mismatches.Sequences,
            mismatches.Triggers
        }.Count(count => count > 0);

    private static DatabaseCompatibilityResult Allowed(
        DatabaseCompatibilityState state,
        string safeReasonCode,
        int sourceMigrationCount,
        int appliedMigrationCount = 0,
        int userTableCount = 0,
        int structuralObjectCount = 0,
        DatabaseSecurityMetadataCounts? securityMetadataCounts = null)
        => new(
            state,
            true,
            safeReasonCode,
            sourceMigrationCount,
            appliedMigrationCount,
            userTableCount,
            structuralObjectCount,
            SecurityMetadataCounts: securityMetadataCounts);

    private static DatabaseCompatibilityResult Rejected(
        DatabaseCompatibilityState state,
        string safeReasonCode,
        int sourceMigrationCount,
        int appliedMigrationCount = 0,
        int userTableCount = 0,
        int structuralObjectCount = 0,
        int schemaMismatchCategoryCount = 0,
        DatabaseSchemaMismatchCounts? schemaMismatches = null,
        DatabaseSecurityMetadataCounts? securityMetadataCounts = null)
        => new(
            state,
            false,
            safeReasonCode,
            sourceMigrationCount,
            appliedMigrationCount,
            userTableCount,
            structuralObjectCount,
            schemaMismatchCategoryCount,
            schemaMismatches,
            securityMetadataCounts);
}
