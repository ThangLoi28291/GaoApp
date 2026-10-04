namespace GaoApp.Infrastructure.Data.Migrations;

/// <summary>
/// Optional receipts created by the reviewed initial-import and product-images runners.
/// Include their exact expected shape when present; never suppress differences by table name.
/// </summary>
public static class KnownImportJournalSchema
{
    public static DatabaseSchemaManifest IncludePresentJournals(DatabaseSchemaManifest expected, DatabaseSchemaManifest actual)
    {
        var optional = new[]
        {
            Table("GaoStoreMigrationRunsV2", "packageid",
                Column("PackageId", "nvarchar(100)", 100), Column("SourceDatabase", "sysname"),
                Column("SqlSha256", "char(64)", 64), Column("SourceHashes", "nvarchar(max)", -1),
                Column("TargetHashes", "nvarchar(max)", -1), Column("CommittedAtUtc", "datetime2")),
            Table("GaoStoreProductImageRunsV1", "storeid",
                Column("StoreId", "int"), Column("SourceDatabase", "sysname"),
                Column("PreviewManifestSha256", "char(64)", 64), Column("SqlSha256", "char(64)", 64),
                Column("ProductVariantBeforeHash", "varchar(100)", 100), Column("ProductVariantAfterHash", "varchar(100)", 100),
                Column("ProductVariantOtherColumnsHash", "varchar(100)", 100), Column("MediaAssetsHash", "varchar(100)", 100),
                Column("ProductImagesHash", "varchar(100)", 100), Column("CommittedAtUtc", "datetime2"))
        };
        var present = optional.Where(x => actual.Tables.Any(t => t.Identity == x.Identity)
            && !expected.Tables.Any(t => t.Identity == x.Identity));
        return DatabaseSchemaCanonicalizer.WithFingerprint(expected.AppliedMigrationIds,
            expected.Tables.Concat(present).ToArray(), expected.Sequences);
    }

    private static DatabaseColumnSchema Column(string name, string type, int? length = null) =>
        new(name.ToLowerInvariant(), type, length, null, null, false, false, false, false, false, null, null);

    private static DatabaseTableSchema Table(string name, string key, params DatabaseColumnSchema[] columns) =>
        new(new("dbo", name), columns, new([key], true, false), [], [], []);
}
