using System.Data;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GaoApp.Infrastructure.Data.Migrations;

public sealed class EfCoreDatabaseSchemaManifestCatalog
    : IDatabaseSchemaManifestCatalog
{
    private const string SqlServerValueGenerationStrategy =
        "SqlServer:ValueGenerationStrategy";
    private const string SqlServerIdentity = "SqlServer:Identity";
    private const string SqlServerClustered = "SqlServer:Clustered";
    private const string SqlServerInclude = "SqlServer:Include";

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
        var current = GetCurrentManifest();
        if (appliedMigrationIds.SequenceEqual(
            current.AppliedMigrationIds,
            StringComparer.Ordinal))
        {
            manifest = current;
            return true;
        }

        manifest = null!;
        return false;
    }

    private DatabaseSchemaManifest BuildCurrentManifest()
    {
        var sourceMigrationIds = _db.Database.GetMigrations().ToList();
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

            case AddForeignKeyOperation addForeignKey:
                ApplyAddForeignKey(
                    addForeignKey,
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

            default:
                throw new InvalidOperationException(
                    $"Current schema manifest does not support migration operation {operation.GetType().Name}. Add an explicit, reviewed manifest handler before deployment.");
        }
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

        table.ForeignKeys.Add(new DatabaseForeignKeySchema(
            NormalizeIdentifiers(operation.Columns),
            new DatabaseObjectIdentity(
                operation.PrincipalSchema ?? defaultSchema,
                operation.PrincipalTable),
            NormalizeIdentifiers(operation.PrincipalColumns ?? []),
            DatabaseSchemaNormalization.NormalizeDeleteAction(
                operation.OnDelete.ToString()),
            IsDisabled: false,
            IsNotTrusted: false));
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
            table.ForeignKeys.Add(new DatabaseForeignKeySchema(
                NormalizeIdentifiers(foreignKey.Columns),
                new DatabaseObjectIdentity(
                    foreignKey.PrincipalSchema ?? defaultSchema,
                    foreignKey.PrincipalTable),
                NormalizeIdentifiers(
                    foreignKey.PrincipalColumns ?? []),
                DatabaseSchemaNormalization.NormalizeDeleteAction(
                    foreignKey.OnDelete.ToString()),
                IsDisabled: false,
                IsNotTrusted: false));
        }

        foreach (var check in operation.CheckConstraints)
        {
            table.CheckConstraints.Add(
                new DatabaseCheckConstraintSchema(
                    DatabaseSchemaNormalization.NormalizeIdentifier(
                        check.Name),
                    DatabaseSchemaNormalization.NormalizeSqlExpression(
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

    private static DatabaseColumnSchema CreateColumn(
        AddColumnOperation operation)
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

        return new DatabaseColumnSchema(
            DatabaseSchemaNormalization.NormalizeIdentifier(
                operation.Name),
            storeType,
            facets.MaxLength ?? operation.MaxLength,
            facets.Precision ?? operation.Precision,
            facets.Scale ?? operation.Scale,
            operation.IsNullable,
            hasIdentityAnnotation
                || generationStrategy?.Contains(
                    "IdentityColumn",
                    StringComparison.OrdinalIgnoreCase) == true,
            !string.IsNullOrWhiteSpace(operation.ComputedColumnSql),
            operation.IsRowVersion
                || storeType is "rowversion" or "timestamp",
            hasDefault,
            defaultExpression,
            DatabaseSchemaNormalization.NormalizeSqlExpression(
                operation.ComputedColumnSql));
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
        public List<DatabaseForeignKeySchema> ForeignKeys { get; } = [];
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
                ForeignKeys
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
