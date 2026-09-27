using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GaoApp.Infrastructure.Data.Migrations;

public sealed class EfCoreDatabaseSchemaManifestCatalog
    : IDatabaseSchemaManifestCatalog
{
    private const string PurchaseReceiptCostPolicyMigrationId =
        "20260817090000_AddPurchaseReceiptCostCapitalizationPolicy";
    private const string InputInvoiceIdentityMigrationId =
        "20260817150000_AddInputInvoiceIdentityUniqueness";
    private const string InputInvoiceBuyerOwnerGuardMigrationId =
        "20260824150000_AddInputInvoiceBuyerOwnerGuard";
    private const string InputInvoiceBuyerOwnerGuardSqlSha256 =
        "917b4697378ca73b5428b1f607799117249edd7b20b6996a5e5ae8b217146441";
    private const string InputInvoiceItemCatalogMappingMigrationId =
        "20260826150000_AddInputInvoiceItemCatalogMapping";
    private const string InputInvoiceItemCatalogMappingSqlSha256 =
        "8e29947021883255050b73905bc4f02e3dcc6d8188e6d70d5695420cab511875";
    private const string InputInvoiceReconciliationMigrationId =
        "20260827150000_AddInputInvoiceReconciliation";
    private const string ReceivingWorkbenchMigrationId =
        "20260830112901_AddReceivingWorkbench";
    private const string InvoiceIssuanceRoutingMigrationId =
    "20260926113012_AddInvoiceIssuanceRoutingAndBuyerSelfService";

    private const string InvoiceIssuanceRoutingBackfillSqlSha256 =
        "84685fb83dde21db7b797aebd99d418153d71e7ac2e9a42de62770ff9497a2d2";
    private static readonly HashSet<string> ReceivingWorkbenchSqlSha256 =
    [
        "bd1bd38a8320d73b03bd1d15583f11064ebf3e6f4e3de80ad50705a6fc5d8cac",
        "590430dfa5331ca428de54ddb37adee4f6f0d4c5f503dbcf4f5b040f047c7477"
    ];
    private const string SqlServerValueGenerationStrategy =
        "SqlServer:ValueGenerationStrategy";
    private const string SqlServerIdentity = "SqlServer:Identity";
    private const string SqlServerClustered = "SqlServer:Clustered";
    private const string SqlServerInclude = "SqlServer:Include";
    private const string InventoryLedgerTimelineMigrationId =
        "20260920093000_OptimizeInventoryLedgerTimeline";
    private const string InventoryLedgerTimelineIndexName =
        "IX_InventoryTransactions_LedgerTimeline";
    private const string ReviewedInventoryLedgerTimelineSql =
        "CREATE INDEX [IX_InventoryTransactions_LedgerTimeline] ON [dbo].[InventoryTransactions] ([StoreId], [OccurredAtUtc] DESC, [Id] DESC) INCLUDE ([QuantityChange], [AfterQty]) WHERE [IsDeleted] = 0;";

    private readonly AppDbContext _db;
    private DatabaseSchemaManifest? _currentManifest;

    public EfCoreDatabaseSchemaManifestCatalog(AppDbContext db)
    {
        _db = db;
    }

    public DatabaseSchemaManifest GetCurrentManifest()
        => _currentManifest ??= BuildCurrentManifest();

    public bool TryGetManifestForAppliedMigrationPrefix(
        IReadOnlyList<string> appliedMigrationIds,
        out DatabaseSchemaManifest manifest)
    {
        var sourceMigrationIds =
            _db.Database.GetMigrations().ToList();
        if (appliedMigrationIds.Count > 0
            && appliedMigrationIds.Count
                <= sourceMigrationIds.Count
            && appliedMigrationIds.SequenceEqual(
                sourceMigrationIds.Take(
                    appliedMigrationIds.Count),
                StringComparer.Ordinal))
        {
            manifest = appliedMigrationIds.Count
                    == sourceMigrationIds.Count
                ? GetCurrentManifest()
                : BuildManifest(appliedMigrationIds);
            return true;
        }

        manifest = null!;
        return false;
    }

    private DatabaseSchemaManifest BuildCurrentManifest()
    {
        var sourceMigrationIds = _db.Database.GetMigrations().ToList();
        return BuildManifest(sourceMigrationIds);
    }

    private DatabaseSchemaManifest BuildManifest(
        IReadOnlyList<string> sourceMigrationIds)
    {
        var migrationsAssembly = _db.GetService<IMigrationsAssembly>();
        var activeProvider = _db.Database.ProviderName
            ?? throw new InvalidOperationException(
                "Database provider is unavailable.");
        var defaultSchema = _db.Model.GetDefaultSchema() ?? "dbo";
        var tables = new Dictionary<
            DatabaseObjectIdentity,
            MutableTable>();
        var sequences = new Dictionary<
            DatabaseObjectIdentity,
            DatabaseSequenceSchema>();

        foreach (var migrationId in sourceMigrationIds)
        {
            if (!migrationsAssembly.Migrations.TryGetValue(
                    migrationId,
                    out var migrationType))
            {
                throw new InvalidOperationException(
                    "Migration schema catalog is inconsistent.");
            }

            var migration = migrationsAssembly.CreateMigration(
                migrationType,
                activeProvider);

            foreach (var operation in migration.UpOperations)
            {
                ApplyOperation(
                    migrationId,
                    operation,
                    tables,
                    sequences,
                    defaultSchema);
            }
        }

        var tableSchemas = tables.Values
            .Select(table => table.ToSchema())
            .ToArray();

        return DatabaseSchemaCanonicalizer.WithFingerprint(
            sourceMigrationIds,
            tableSchemas,
            sequences.Values.ToArray());
    }

    private static void ApplyOperation(
        string migrationId,
        MigrationOperation operation,
        IDictionary<DatabaseObjectIdentity, MutableTable> tables,
        IDictionary<DatabaseObjectIdentity, DatabaseSequenceSchema>
            sequences,
        string defaultSchema)
    {
        switch (operation)
        {
            case CreateTableOperation createTable:
                ApplyCreateTable(createTable, tables, defaultSchema);
                break;

            case CreateIndexOperation createIndex:
                ApplyCreateIndex(createIndex, tables, defaultSchema);
                break;

            case DropIndexOperation dropIndex:
                ApplyDropIndex(dropIndex, tables, defaultSchema);
                break;

            case AddColumnOperation addColumn:
                ApplyAddColumn(addColumn, tables, defaultSchema);
                break;

            case AlterColumnOperation alterColumn:
                ApplyAlterColumn(alterColumn, tables, defaultSchema);
                break;

            case AddForeignKeyOperation addForeignKey:
                ApplyAddForeignKey(
                    addForeignKey,
                    tables,
                    defaultSchema);
                break;

            case DropForeignKeyOperation dropForeignKey:
                ApplyDropForeignKey(dropForeignKey, tables, defaultSchema);
                break;

            case AddCheckConstraintOperation addCheckConstraint:
                ApplyAddCheckConstraint(
                    addCheckConstraint,
                    tables,
                    defaultSchema);
                break;
            case DropCheckConstraintOperation dropCheckConstraint:
                ApplyDropCheckConstraint(
                    dropCheckConstraint,
                    tables,
                    defaultSchema);
                break;

            case CreateSequenceOperation createSequence:
                var sequenceIdentity = new DatabaseObjectIdentity(
                    createSequence.Schema ?? defaultSchema,
                    createSequence.Name);
                sequences.Add(
                    sequenceIdentity,
                    new DatabaseSequenceSchema(
                        sequenceIdentity,
                        DatabaseSchemaNormalization.NormalizeStoreType(
                            GetSequenceStoreType(createSequence.ClrType)),
                        createSequence.StartValue,
                        createSequence.IncrementBy,
                        createSequence.MinValue,
                        createSequence.MaxValue,
                        createSequence.IsCyclic));
                break;

            case SqlOperation sqlOperation:
                if (!TryApplyReviewedSchemaSql(
                        migrationId, sqlOperation, tables, defaultSchema))
                {
                    EnsureReviewedDataOnlySql(migrationId, sqlOperation.Sql);
                }
                break;

            default:
                throw new InvalidOperationException(
                    $"Current schema manifest does not support migration operation {operation.GetType().Name}. Add an explicit, reviewed manifest handler before deployment.");
        }
    }

    private static bool TryApplyReviewedSchemaSql(
        string migrationId,
        SqlOperation operation,
        IDictionary<DatabaseObjectIdentity, MutableTable> tables,
        string defaultSchema)
    {
        // This immutable migration contains DDL: accepting it as data-only SQL
        // would omit the index from both current and migration-prefix manifests.
        if (!string.Equals(migrationId, InventoryLedgerTimelineMigrationId,
                StringComparison.Ordinal))
            return false;

        var normalized = string.Join(
            ' ', operation.Sql.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (operation.SuppressTransaction || !string.Equals(
                normalized, ReviewedInventoryLedgerTimelineSql, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The inventory timeline migration contains an unreviewed schema SQL operation.");
        }

        var identity = new DatabaseObjectIdentity("dbo", "InventoryTransactions");
        if (tables.TryGetValue(identity, out var table)
            && table.Indexes.Any(index => string.Equals(
                index.Name,
                DatabaseSchemaNormalization.NormalizeIdentifier(InventoryLedgerTimelineIndexName),
                StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                "The inventory timeline index already exists in the schema manifest.");
        }

        var createIndex = new CreateIndexOperation
        {
            Schema = "dbo",
            Table = "InventoryTransactions",
            Name = InventoryLedgerTimelineIndexName,
            Columns = ["StoreId", "OccurredAtUtc", "Id"],
            IsDescending = [false, true, true],
            Filter = "[IsDeleted] = 0"
        };
        createIndex.AddAnnotation(SqlServerInclude, new[] { "QuantityChange", "AfterQty" });
        ApplyCreateIndex(createIndex, tables, defaultSchema);
        return true;
    }

    private static void EnsureReviewedDataOnlySql(string migrationId, string sql)
    {
        if (string.Equals(migrationId, InputInvoiceReconciliationMigrationId,
                StringComparison.Ordinal))
            throw new InvalidOperationException(
                "The reconciliation migration is schema-only and must not contain raw SQL.");
        var normalized = string.Join(
            ' ',
            sql.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        var reviewedCostPolicyStatements = new HashSet<string>(StringComparer.Ordinal)
        {
            "UPDATE [dbo].[StockDocument] SET [IncludeVatInInventoryCost] = CASE WHEN [HasVat] = 1 THEN 1 ELSE 0 END, [CapitalizeFreightInInventoryCost] = CASE WHEN [HasFreight] = 1 THEN 1 ELSE 0 END WHERE [Status] = 3;",
            "UPDATE line SET line.[FreightAllocation] = 0 FROM [dbo].[StockDocumentLine] AS line INNER JOIN [dbo].[StockDocument] AS document ON document.[Id] = line.[StockDocumentId] WHERE document.[Status] <> 3 AND line.[FreightAllocation] <> 0;"
        };
        var reviewedInputInvoiceIdentityStatements =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "UPDATE [dbo].[InputInvoiceHead] SET [NormalizedSellerTaxCode] = NULLIF(UPPER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(LTRIM(RTRIM([SellerTaxCode])), N' ', N''), N'.', N''), N'-', N''), NCHAR(9), N''), NCHAR(13), N''), NCHAR(10), N'')), N''), [NormalizedInvoiceSeries] = NULLIF(UPPER(REPLACE(REPLACE(REPLACE(REPLACE(LTRIM(RTRIM([InvoiceSeries])), N' ', N''), NCHAR(9), N''), NCHAR(13), N''), NCHAR(10), N'')), N''), [NormalizedInvoiceNumber] = NULLIF(UPPER(REPLACE(REPLACE(REPLACE(REPLACE(LTRIM(RTRIM([InvoiceNumber])), N' ', N''), NCHAR(9), N''), NCHAR(13), N''), NCHAR(10), N'')), N''), [InvoiceIdentityDate] = CONVERT(date, [InvoiceDate]) WHERE [IsDeleted] = 0;",
                "IF EXISTS ( SELECT 1 FROM [dbo].[InputInvoiceHead] WHERE [IsDeleted] = 0 AND [NormalizedSellerTaxCode] IS NOT NULL AND [NormalizedInvoiceSeries] IS NOT NULL AND [NormalizedInvoiceNumber] IS NOT NULL AND [InvoiceIdentityDate] IS NOT NULL GROUP BY [StoreId], [NormalizedSellerTaxCode], [NormalizedInvoiceSeries], [NormalizedInvoiceNumber], [InvoiceIdentityDate] HAVING COUNT_BIG(*) > 1 ) THROW 51001, 'Duplicate active input-invoice business identities must be resolved before migration.', 1; IF EXISTS ( SELECT 1 FROM [dbo].[InputInvoiceHead] WHERE [IsDeleted] = 0 AND [XmlHash] IS NOT NULL GROUP BY [StoreId], [XmlHash] HAVING COUNT_BIG(*) > 1 ) THROW 51002, 'Duplicate active input-invoice XML hashes must be resolved before migration.', 1;"
            };
        var isReviewedCostPolicySql = string.Equals(
                migrationId,
                PurchaseReceiptCostPolicyMigrationId,
                StringComparison.Ordinal)
            && reviewedCostPolicyStatements.Contains(normalized);
        var isReviewedInputInvoiceIdentitySql = string.Equals(
                migrationId,
                InputInvoiceIdentityMigrationId,
                StringComparison.Ordinal)
            && reviewedInputInvoiceIdentityStatements.Contains(normalized);
        var isReviewedInputInvoiceBuyerOwnerGuardSql = string.Equals(
                migrationId,
                InputInvoiceBuyerOwnerGuardMigrationId,
                StringComparison.Ordinal)
            && string.Equals(
                Convert.ToHexString(SHA256.HashData(
                        Encoding.UTF8.GetBytes(normalized)))
                    .ToLowerInvariant(),
                InputInvoiceBuyerOwnerGuardSqlSha256,
                StringComparison.Ordinal);
        var isReviewedInputInvoiceItemCatalogMappingSql = string.Equals(
                migrationId,
                InputInvoiceItemCatalogMappingMigrationId,
                StringComparison.Ordinal)
            && string.Equals(
                Convert.ToHexString(SHA256.HashData(
                        Encoding.UTF8.GetBytes(normalized)))
                    .ToLowerInvariant(),
                InputInvoiceItemCatalogMappingSqlSha256,
                StringComparison.Ordinal);
        var isReviewedReceivingWorkbenchSql = string.Equals(
                migrationId,
                ReceivingWorkbenchMigrationId,
                StringComparison.Ordinal)
            && ReceivingWorkbenchSqlSha256.Contains(
                Convert.ToHexString(SHA256.HashData(
                        Encoding.UTF8.GetBytes(normalized)))
                    .ToLowerInvariant());
        var isReviewedInvoiceIssuanceRoutingSql =
    string.Equals(
        migrationId,
        InvoiceIssuanceRoutingMigrationId,
        StringComparison.Ordinal)
    && string.Equals(
        Convert.ToHexString(
            SHA256.HashData(
                Encoding.UTF8.GetBytes(normalized)))
            .ToLowerInvariant(),
        InvoiceIssuanceRoutingBackfillSqlSha256,
        StringComparison.Ordinal);
        // Reviewed ACB backfill copies existing receipt JSON into reconciliation rows; it performs no DDL.
        var isReviewedAcbReconciliationSql = string.Equals(
                migrationId, "20260908192844_AddAcbQrNotificationReconciliation", StringComparison.Ordinal)
            && string.Equals(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant(),
                "7afc10c799b9883d2445ab743cc244f69ea8e466a67637cff6961bd8e91d13a6", StringComparison.Ordinal);
        var isReviewedBankDefaultGuardSql = string.Equals(
                migrationId, "20260909055844_EnforceSingleDefaultBankAccount", StringComparison.Ordinal)
            && string.Equals(normalized,
                "IF EXISTS (SELECT StoreId FROM StoreBankAccounts WHERE IsDefault = 1 AND IsDeleted = 0 GROUP BY StoreId HAVING COUNT(*) > 1) THROW 51001, 'Multiple default bank accounts exist in a store. Select one default per store before retrying migration.', 1;",
                StringComparison.Ordinal);
        var isReviewedSnapshotOptionSql = string.Equals(
                migrationId, "20260909061611_EnableSnapshotProfitReads", StringComparison.Ordinal)
            && string.Equals(normalized, "ALTER DATABASE CURRENT SET ALLOW_SNAPSHOT_ISOLATION ON;", StringComparison.Ordinal);
        if (!isReviewedSnapshotOptionSql
      && !isReviewedAcbReconciliationSql
      && !isReviewedBankDefaultGuardSql
      && !isReviewedCostPolicySql
      && !isReviewedInputInvoiceIdentitySql
      && !isReviewedInputInvoiceBuyerOwnerGuardSql
      && !isReviewedInputInvoiceItemCatalogMappingSql
      && !isReviewedReceivingWorkbenchSql
      && !isReviewedInvoiceIssuanceRoutingSql)
        {
            throw new InvalidOperationException(
                "The schema manifest encountered an unreviewed SQL migration operation.");
        }
    }

    private static void ApplyAddColumn(
        AddColumnOperation operation,
        IDictionary<DatabaseObjectIdentity, MutableTable> tables,
        string defaultSchema)
    {
        var identity = new DatabaseObjectIdentity(
            operation.Schema ?? defaultSchema,
            operation.Table);

        if (!tables.TryGetValue(identity, out var table))
        {
            throw new InvalidOperationException(
                "Migration column references a table absent from the schema manifest.");
        }

        table.Columns.Add(CreateColumn(operation));
    }

    private static void ApplyAlterColumn(
        AlterColumnOperation operation,
        IDictionary<DatabaseObjectIdentity, MutableTable> tables,
        string defaultSchema)
    {
        var identity = new DatabaseObjectIdentity(
            operation.Schema ?? defaultSchema,
            operation.Table);
        if (!tables.TryGetValue(identity, out var table))
            throw new InvalidOperationException(
                "Migration column alteration references a table absent from the schema manifest.");

        var normalizedName = DatabaseSchemaNormalization.NormalizeIdentifier(operation.Name);
        var index = table.Columns.FindIndex(column => string.Equals(
            column.Name, normalizedName, StringComparison.Ordinal));
        if (index < 0 || table.Columns.Count(column => string.Equals(
                column.Name, normalizedName, StringComparison.Ordinal)) != 1)
            throw new InvalidOperationException(
                "Migration column alteration must identify exactly one schema-manifest column.");

        table.Columns[index] = CreateColumn(operation);
    }

    private static void ApplyAddForeignKey(
        AddForeignKeyOperation operation,
        IDictionary<DatabaseObjectIdentity, MutableTable> tables,
        string defaultSchema)
    {
        var identity = new DatabaseObjectIdentity(
            operation.Schema ?? defaultSchema,
            operation.Table);

        if (!tables.TryGetValue(identity, out var table))
        {
            throw new InvalidOperationException(
                "Migration foreign key references a table absent from the schema manifest.");
        }

        AddForeignKeyToTable(table, operation, defaultSchema);
    }

    private static void AddForeignKeyToTable(
        MutableTable table, AddForeignKeyOperation operation, string defaultSchema)
    {
        var name = DatabaseSchemaNormalization.NormalizeIdentifier(operation.Name);
        var foreignKey = new DatabaseForeignKeySchema(
            NormalizeIdentifiers(operation.Columns),
            new DatabaseObjectIdentity(
                operation.PrincipalSchema ?? defaultSchema,
                operation.PrincipalTable),
            NormalizeIdentifiers(operation.PrincipalColumns ?? []),
            DatabaseSchemaNormalization.NormalizeDeleteAction(
                operation.OnDelete.ToString()),
            IsDisabled: false,
            IsNotTrusted: false);
        if (!table.ForeignKeys.TryAdd(name, foreignKey))
        {
            throw new InvalidOperationException(
                "Migration foreign key duplicates a schema-manifest constraint name.");
        }
    }

    private static void ApplyDropForeignKey(
        DropForeignKeyOperation operation,
        IDictionary<DatabaseObjectIdentity, MutableTable> tables,
        string defaultSchema)
    {
        var identity = new DatabaseObjectIdentity(
            operation.Schema ?? defaultSchema, operation.Table);
        if (!tables.TryGetValue(identity, out var table))
        {
            throw new InvalidOperationException(
                "Migration foreign key references a table absent from the schema manifest.");
        }

        var name = DatabaseSchemaNormalization.NormalizeIdentifier(operation.Name);
        if (!table.ForeignKeys.Remove(name))
        {
            throw new InvalidOperationException(
                "Migration foreign key removal must identify exactly one schema-manifest constraint.");
        }
    }

    private static void ApplyAddCheckConstraint(
        AddCheckConstraintOperation operation,
        IDictionary<DatabaseObjectIdentity, MutableTable> tables,
        string defaultSchema)
    {
        var identity = new DatabaseObjectIdentity(
            operation.Schema ?? defaultSchema,
            operation.Table);

        if (!tables.TryGetValue(identity, out var table))
        {
            throw new InvalidOperationException(
                "Migration check constraint references a table absent from the schema manifest.");
        }

        var normalizedName =
            DatabaseSchemaNormalization.NormalizeIdentifier(
                operation.Name);
        if (table.CheckConstraints.Any(check => string.Equals(
                check.Name,
                normalizedName,
                StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                "Migration check constraint duplicates a schema-manifest constraint.");
        }

        table.CheckConstraints.Add(new DatabaseCheckConstraintSchema(
            normalizedName,
            DatabaseSchemaNormalization.NormalizeCheckConstraintExpression(
                operation.Sql)
            ?? string.Empty,
            IsDisabled: false,
            IsNotTrusted: false));
    }
    private static void ApplyDropCheckConstraint(
    DropCheckConstraintOperation operation,
    IDictionary<DatabaseObjectIdentity, MutableTable> tables,
    string defaultSchema)
    {
        var identity = new DatabaseObjectIdentity(
            operation.Schema ?? defaultSchema,
            operation.Table);

        if (!tables.TryGetValue(identity, out var table))
        {
            throw new InvalidOperationException(
                "Migration check constraint references a table absent from the schema manifest.");
        }

        var normalizedName =
            DatabaseSchemaNormalization.NormalizeIdentifier(
                operation.Name);

        var removed = table.CheckConstraints.RemoveAll(
            check => string.Equals(
                check.Name,
                normalizedName,
                StringComparison.Ordinal));

        if (removed != 1)
        {
            throw new InvalidOperationException(
                "Migration check constraint removal must identify exactly one schema-manifest constraint.");
        }
    }
    private static void ApplyCreateTable(
        CreateTableOperation operation,
        IDictionary<DatabaseObjectIdentity, MutableTable> tables,
        string defaultSchema)
    {
        var identity = new DatabaseObjectIdentity(
            operation.Schema ?? defaultSchema,
            operation.Name);
        var table = new MutableTable(identity);

        foreach (var column in operation.Columns)
        {
            table.Columns.Add(CreateColumn(column));
        }

        if (operation.PrimaryKey is { } primaryKey)
        {
            table.PrimaryKey = new DatabasePrimaryKeySchema(
                NormalizeIdentifiers(primaryKey.Columns),
                GetBooleanAnnotation(
                    primaryKey,
                    SqlServerClustered,
                    defaultValue: true),
                IsDisabled: false);
        }

        foreach (var uniqueConstraint in operation.UniqueConstraints)
        {
            table.Indexes.Add(new DatabaseIndexSchema(
                DatabaseSchemaNormalization.NormalizeIdentifier(
                    uniqueConstraint.Name),
                NormalizeIdentifiers(uniqueConstraint.Columns)
                    .Select(column =>
                        new DatabaseIndexColumnSchema(
                            column,
                            IsDescending: false))
                    .ToArray(),
                [],
                IsUnique: true,
                IsUniqueConstraint: true,
                IsClustered: GetBooleanAnnotation(
                    uniqueConstraint,
                    SqlServerClustered,
                    defaultValue: false),
                IsDisabled: false,
                Filter: null));
        }

        foreach (var foreignKey in operation.ForeignKeys)
        {
            AddForeignKeyToTable(table, foreignKey, defaultSchema);
        }

        foreach (var check in operation.CheckConstraints)
        {
            table.CheckConstraints.Add(
                new DatabaseCheckConstraintSchema(
                    DatabaseSchemaNormalization.NormalizeIdentifier(
                        check.Name),
                    DatabaseSchemaNormalization.NormalizeCheckConstraintExpression(
                        check.Sql)
                    ?? string.Empty,
                    IsDisabled: false,
                    IsNotTrusted: false));
        }

        tables.Add(identity, table);
    }

    private static void ApplyCreateIndex(
        CreateIndexOperation operation,
        IDictionary<DatabaseObjectIdentity, MutableTable> tables,
        string defaultSchema)
    {
        var identity = new DatabaseObjectIdentity(
            operation.Schema ?? defaultSchema,
            operation.Table);

        if (!tables.TryGetValue(identity, out var table))
        {
            throw new InvalidOperationException(
                "Migration index references a table absent from the schema manifest.");
        }

        var descending = operation.IsDescending;
        var keyColumns = operation.Columns
            .Select((column, index) =>
                new DatabaseIndexColumnSchema(
                    DatabaseSchemaNormalization.NormalizeIdentifier(
                        column),
                    descending is not null
                    && index < descending.Length
                    && descending[index]))
            .ToArray();
        var includedColumns =
            operation.FindAnnotation(SqlServerInclude)?.Value switch
            {
                string[] values => NormalizeIdentifiers(values),
                IReadOnlyList<string> values =>
                    NormalizeIdentifiers(values),
                _ => []
            };

        table.Indexes.Add(new DatabaseIndexSchema(
            DatabaseSchemaNormalization.NormalizeIdentifier(
                operation.Name),
            keyColumns,
            includedColumns,
            operation.IsUnique,
            IsUniqueConstraint: false,
            IsClustered: GetBooleanAnnotation(
                operation,
                SqlServerClustered,
                defaultValue: false),
            IsDisabled: false,
            DatabaseSchemaNormalization.NormalizeSqlExpression(
                operation.Filter)));
    }

    private static void ApplyDropIndex(
        DropIndexOperation operation,
        IDictionary<DatabaseObjectIdentity, MutableTable> tables,
        string defaultSchema)
    {
        var identity = new DatabaseObjectIdentity(
            operation.Schema ?? defaultSchema,
            operation.Table
            ?? throw new InvalidOperationException(
                "Migration index table is required."));

        if (!tables.TryGetValue(identity, out var table))
        {
            throw new InvalidOperationException(
                "Migration index references a table absent from the schema manifest.");
        }

        var normalizedName =
            DatabaseSchemaNormalization.NormalizeIdentifier(
                operation.Name);
        var matches = table.Indexes
            .Where(index => string.Equals(
                index.Name,
                normalizedName,
                StringComparison.Ordinal))
            .ToArray();
        if (matches.Length != 1)
        {
            throw new InvalidOperationException(
                "Migration index removal must identify exactly one schema-manifest index.");
        }

        table.Indexes.Remove(matches[0]);
    }

    private static DatabaseColumnSchema CreateColumn(
        ColumnOperation operation)
    {
        var storeType =
            DatabaseSchemaNormalization.NormalizeStoreType(
                operation.ColumnType
                ?? throw new InvalidOperationException(
                    "Migration column store type is required."));
        var facets = ParseStoreTypeFacets(storeType);
        var hasDefault =
            operation.DefaultValue is not null
            || !string.IsNullOrWhiteSpace(operation.DefaultValueSql);
        var defaultExpression = operation.DefaultValue is not null
            ? DatabaseSchemaNormalization.NormalizeDefaultValue(
                operation.DefaultValue)
            : DatabaseSchemaNormalization.NormalizeDefaultExpression(
                operation.DefaultValueSql);
        var generationStrategy = operation
            .FindAnnotation(SqlServerValueGenerationStrategy)
            ?.Value
            ?.ToString();
        var hasIdentityAnnotation =
            operation.FindAnnotation(SqlServerIdentity)?.Value is not null;
        var isComputed = !string.IsNullOrWhiteSpace(operation.ComputedColumnSql);

        return new DatabaseColumnSchema(
            DatabaseSchemaNormalization.NormalizeIdentifier(
                operation.Name),
            storeType,
            facets.MaxLength ?? operation.MaxLength,
            facets.Precision ?? operation.Precision,
            facets.Scale ?? operation.Scale,
            isComputed || operation.IsNullable,
            hasIdentityAnnotation
                || generationStrategy?.Contains(
                    "IdentityColumn",
                    StringComparison.OrdinalIgnoreCase) == true,
            isComputed,
            operation.IsRowVersion
                || storeType is "rowversion" or "timestamp",
            hasDefault,
            defaultExpression,
            NormalizeComputedColumnExpression(operation.ComputedColumnSql));
    }

    private static string? NormalizeComputedColumnExpression(string? expression)
    {
        var normalized = DatabaseSchemaNormalization.NormalizeSqlExpression(expression);
        if (normalized is null)
            return null;

        // SQL Server persists NCHAR numeric arguments with an additional set
        // of parentheses. Canonicalize migration-operation metadata to the
        // same representation used by sys.computed_columns.
        return normalized
            .Replace("nchar9", "nchar(9)", StringComparison.Ordinal)
            .Replace("nchar10", "nchar(10)", StringComparison.Ordinal)
            .Replace("nchar13", "nchar(13)", StringComparison.Ordinal);
    }

    private static StoreTypeFacets ParseStoreTypeFacets(
        string storeType)
    {
        var open = storeType.IndexOf('(');
        if (open < 0 || !storeType.EndsWith(')'))
        {
            return default;
        }

        var baseType = storeType[..open];
        var arguments = storeType[(open + 1)..^1]
            .Split(
                ',',
                StringSplitOptions.TrimEntries
                | StringSplitOptions.RemoveEmptyEntries);

        if (baseType is
            "varchar" or "nvarchar" or "char" or "nchar"
            or "binary" or "varbinary")
        {
            return new StoreTypeFacets(
                arguments[0] == "max"
                    ? -1
                    : int.Parse(
                        arguments[0],
                        CultureInfo.InvariantCulture),
                null,
                null);
        }

        if (baseType is "decimal" or "numeric"
            && arguments.Length == 2)
        {
            return new StoreTypeFacets(
                null,
                int.Parse(
                    arguments[0],
                    CultureInfo.InvariantCulture),
                int.Parse(
                    arguments[1],
                    CultureInfo.InvariantCulture));
        }

        if (baseType is "datetime2" or "datetimeoffset" or "time"
            && arguments.Length == 1)
        {
            return new StoreTypeFacets(
                null,
                null,
                int.Parse(
                    arguments[0],
                    CultureInfo.InvariantCulture));
        }

        return default;
    }

    private static bool GetBooleanAnnotation(
        MigrationOperation operation,
        string name,
        bool defaultValue)
        => operation.FindAnnotation(name)?.Value is bool value
            ? value
            : defaultValue;

    private static string[] NormalizeIdentifiers(
        IEnumerable<string> values)
        => values
            .Select(
                DatabaseSchemaNormalization.NormalizeIdentifier)
            .ToArray();

    private static string GetSequenceStoreType(Type clrType)
        => Type.GetTypeCode(clrType) switch
        {
            TypeCode.Byte => "tinyint",
            TypeCode.Int16 => "smallint",
            TypeCode.Int32 => "int",
            TypeCode.Int64 => "bigint",
            TypeCode.Decimal => "decimal(18,2)",
            _ => throw new InvalidOperationException(
                "Unsupported sequence CLR type.")
        };

    private sealed class MutableTable
    {
        public MutableTable(DatabaseObjectIdentity identity)
        {
            Identity = identity;
        }

        public DatabaseObjectIdentity Identity { get; }
        public List<DatabaseColumnSchema> Columns { get; } = [];
        public DatabasePrimaryKeySchema? PrimaryKey { get; set; }
        // Names are needed while replaying DropForeignKey operations, but are
        // deliberately not part of the structural schema fingerprint contract.
        public Dictionary<string, DatabaseForeignKeySchema> ForeignKeys { get; } =
            new(StringComparer.Ordinal);
        public List<DatabaseIndexSchema> Indexes { get; } = [];
        public List<DatabaseCheckConstraintSchema> CheckConstraints { get; } =
            [];

        public DatabaseTableSchema ToSchema()
            => new(
                Identity,
                Columns
                    .OrderBy(column => column.Name, StringComparer.Ordinal)
                    .ToArray(),
                PrimaryKey,
                ForeignKeys.Values
                    .OrderBy(
                        foreignKey =>
                            string.Join(",", foreignKey.Columns),
                        StringComparer.Ordinal)
                    .ToArray(),
                Indexes
                    .OrderBy(index => index.Name, StringComparer.Ordinal)
                    .ToArray(),
                CheckConstraints
                    .OrderBy(
                        check => check.Name,
                        StringComparer.Ordinal)
                    .ToArray());
    }

    private readonly record struct StoreTypeFacets(
        int? MaxLength,
        int? Precision,
        int? Scale);
}
