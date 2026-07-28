namespace GaoApp.Infrastructure.Data.Migrations;

public enum DatabaseCompatibilityState
{
    DatabaseMissing = 0,
    ExistingEmpty = 1,
    CurrentBaseline = 2,
    SupportedPendingUpgrade = 3,
    UnknownMigrationHistory = 4,
    ExistingSchemaWithoutHistory = 5,
    PartialOrCorruptBaseline = 6,
    UnexpectedUserTables = 7,
    Inaccessible = 8,
    UnexpectedUserObjects = 9,
    UnsupportedMigrationPrefix = 10
}

public sealed record DatabaseCompatibilityResult(
    DatabaseCompatibilityState State,
    bool IsAllowed,
    string SafeReasonCode,
    int SourceMigrationCount,
    int AppliedMigrationCount,
    int UserTableCount,
    int StructuralObjectCount = 0,
    int SchemaMismatchCategoryCount = 0,
    DatabaseSchemaMismatchCounts? SchemaMismatches = null,
    DatabaseSecurityMetadataCounts? SecurityMetadataCounts = null);

public sealed class DatabaseCompatibilityException : Exception
{
    public DatabaseCompatibilityException(DatabaseCompatibilityResult result)
        : base(
            $"Database compatibility preflight rejected the target. ReasonCode={result.SafeReasonCode}. No schema or data changes were applied.")
    {
        Result = result;
    }

    public DatabaseCompatibilityResult Result { get; }
}

public interface IDatabaseBaselinePreflight
{
    Task<DatabaseCompatibilityResult> InspectAsync(
        CancellationToken ct = default);
}

public interface IDatabaseMigrationExecutor
{
    Task MigrateAsync(CancellationToken ct = default);
}

public sealed record DatabaseMigrationCatalogSnapshot(
    IReadOnlyList<string> SourceMigrationIds)
{
    public bool IsAppliedHistoryPrefix(IReadOnlyList<string> appliedMigrationIds)
    {
        if (appliedMigrationIds.Count > SourceMigrationIds.Count)
        {
            return false;
        }

        for (var index = 0; index < appliedMigrationIds.Count; index++)
        {
            if (!string.Equals(
                    appliedMigrationIds[index],
                    SourceMigrationIds[index],
                    StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }
}

public interface IDatabaseMigrationCatalog
{
    DatabaseMigrationCatalogSnapshot CreateSnapshot();
}
