using System.Data;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GaoApp.Infrastructure.Data.Migrations;

public sealed class SqlServerSchemaSnapshotReader
    : ISqlServerSchemaSnapshotReader
{
    private readonly AppDbContext _db;

    public SqlServerSchemaSnapshotReader(AppDbContext db)
    {
        _db = db;
    }

    public async Task<DatabaseSchemaManifest> ReadAsync(
        IReadOnlyList<string> appliedMigrationIds,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var tables = await ReadTablesAndColumnsAsync(ct);
        await ReadPrimaryKeysAsync(tables, ct);
        await ReadForeignKeysAsync(tables, ct);
        await ReadIndexesAsync(tables, ct);
        await ReadCheckConstraintsAsync(tables, ct);
        var sequences = await ReadSequencesAsync(ct);

        return DatabaseSchemaCanonicalizer.WithFingerprint(
            appliedMigrationIds,
            tables.Values
                .Select(table => table.ToSchema())
                .ToArray(),
            sequences);
    }

    private async Task<Dictionary<DatabaseObjectIdentity, MutableTable>>
        ReadTablesAndColumnsAsync(CancellationToken ct)
    {
        const string sql = """
            SELECT
                [schema].[name],
                [table].[name],
                [column].[name],
                [type].[name],
                [column].[max_length],
                [column].[precision],
                [column].[scale],
                [column].[is_nullable],
                [column].[is_identity],
                [column].[is_computed],
                [column].[system_type_id],
                [default].[definition],
                [computed].[definition]
            FROM [sys].[tables] AS [table]
            INNER JOIN [sys].[schemas] AS [schema]
                ON [schema].[schema_id] = [table].[schema_id]
            INNER JOIN [sys].[columns] AS [column]
                ON [column].[object_id] = [table].[object_id]
            INNER JOIN [sys].[types] AS [type]
                ON [type].[user_type_id] = [column].[user_type_id]
            LEFT JOIN [sys].[default_constraints] AS [default]
                ON [default].[object_id] = [column].[default_object_id]
            LEFT JOIN [sys].[computed_columns] AS [computed]
                ON [computed].[object_id] = [column].[object_id]
               AND [computed].[column_id] = [column].[column_id]
            WHERE [table].[is_ms_shipped] = 0
              AND NOT
              (
                  [schema].[name] = N'dbo'
                  AND [table].[name] = N'__EFMigrationsHistory'
              )
            ORDER BY
                [schema].[name],
                [table].[name],
                [column].[column_id];
            """;

        await using var command = CreateCommand(sql);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var tables = new Dictionary<
            DatabaseObjectIdentity,
            MutableTable>();

        while (await reader.ReadAsync(ct))
        {
            var identity = new DatabaseObjectIdentity(
                reader.GetString(0),
                reader.GetString(1));
            if (!tables.TryGetValue(identity, out var table))
            {
                table = new MutableTable(identity);
                tables.Add(identity, table);
            }

            var typeName =
                DatabaseSchemaNormalization.NormalizeStoreType(
                    reader.GetString(3));
            var maxLength = reader.GetInt16(4);
            var precision = reader.GetByte(5);
            var scale = reader.GetByte(6);
            var type = CreateStoreType(
                typeName,
                maxLength,
                precision,
                scale);
            var hasDefault = !reader.IsDBNull(11);

            table.Columns.Add(new DatabaseColumnSchema(
                DatabaseSchemaNormalization.NormalizeIdentifier(
                    reader.GetString(2)),
                type.StoreType,
                type.MaxLength,
                type.Precision,
                type.Scale,
                reader.GetBoolean(7),
                reader.GetBoolean(8),
                reader.GetBoolean(9),
                reader.GetByte(10) == 189
                    || type.StoreType == "rowversion",
                hasDefault,
                hasDefault
                    ? DatabaseSchemaNormalization
                        .NormalizeDefaultExpression(
                            reader.GetString(11))
                    : null,
                reader.IsDBNull(12)
                    ? null
                    : DatabaseSchemaNormalization
                        .NormalizeSqlExpression(
                            reader.GetString(12))));
        }

        return tables;
    }

    private async Task ReadPrimaryKeysAsync(
        IDictionary<DatabaseObjectIdentity, MutableTable> tables,
        CancellationToken ct)
    {
        const string sql = """
            SELECT
                [constraint].[parent_object_id],
                [schema].[name],
                [table].[name],
                [column].[name],
                [index_column].[key_ordinal],
                CASE WHEN [index].[type] = 1 THEN CAST(1 AS bit)
                     ELSE CAST(0 AS bit) END,
                [index].[is_disabled]
            FROM [sys].[key_constraints] AS [constraint]
            INNER JOIN [sys].[tables] AS [table]
                ON [table].[object_id] = [constraint].[parent_object_id]
            INNER JOIN [sys].[schemas] AS [schema]
                ON [schema].[schema_id] = [table].[schema_id]
            INNER JOIN [sys].[indexes] AS [index]
                ON [index].[object_id] = [constraint].[parent_object_id]
               AND [index].[index_id] = [constraint].[unique_index_id]
            INNER JOIN [sys].[index_columns] AS [index_column]
                ON [index_column].[object_id] = [index].[object_id]
               AND [index_column].[index_id] = [index].[index_id]
               AND [index_column].[key_ordinal] > 0
            INNER JOIN [sys].[columns] AS [column]
                ON [column].[object_id] = [index_column].[object_id]
               AND [column].[column_id] = [index_column].[column_id]
            WHERE [constraint].[type] = 'PK'
              AND [table].[is_ms_shipped] = 0
              AND NOT
              (
                  [schema].[name] = N'dbo'
                  AND [table].[name] = N'__EFMigrationsHistory'
              )
            ORDER BY
                [constraint].[parent_object_id],
                [index_column].[key_ordinal];
            """;

        await using var command = CreateCommand(sql);
        await using var reader = await command.ExecuteReaderAsync(ct);
        DatabaseObjectIdentity? currentIdentity = null;
        var columns = new List<string>();
        var isClustered = false;
        var isDisabled = false;

        while (await reader.ReadAsync(ct))
        {
            var identity = new DatabaseObjectIdentity(
                reader.GetString(1),
                reader.GetString(2));
            if (currentIdentity is { } current
                && current != identity)
            {
                SetPrimaryKey(
                    tables,
                    current,
                    columns,
                    isClustered,
                    isDisabled);
                columns = [];
            }

            currentIdentity = identity;
            columns.Add(
                DatabaseSchemaNormalization.NormalizeIdentifier(
                    reader.GetString(3)));
            isClustered = reader.GetBoolean(5);
            isDisabled = reader.GetBoolean(6);
        }

        if (currentIdentity is { } finalIdentity)
        {
            SetPrimaryKey(
                tables,
                finalIdentity,
                columns,
                isClustered,
                isDisabled);
        }
    }

    private async Task ReadForeignKeysAsync(
        IDictionary<DatabaseObjectIdentity, MutableTable> tables,
        CancellationToken ct)
    {
        const string sql = """
            SELECT
                [foreign_key].[object_id],
                [dependent_schema].[name],
                [dependent_table].[name],
                [dependent_column].[name],
                [principal_schema].[name],
                [principal_table].[name],
                [principal_column].[name],
                [foreign_key].[delete_referential_action_desc],
                [foreign_key].[is_disabled],
                [foreign_key].[is_not_trusted],
                [foreign_key_column].[constraint_column_id]
            FROM [sys].[foreign_keys] AS [foreign_key]
            INNER JOIN [sys].[foreign_key_columns] AS [foreign_key_column]
                ON [foreign_key_column].[constraint_object_id] =
                    [foreign_key].[object_id]
            INNER JOIN [sys].[tables] AS [dependent_table]
                ON [dependent_table].[object_id] =
                    [foreign_key].[parent_object_id]
            INNER JOIN [sys].[schemas] AS [dependent_schema]
                ON [dependent_schema].[schema_id] =
                    [dependent_table].[schema_id]
            INNER JOIN [sys].[columns] AS [dependent_column]
                ON [dependent_column].[object_id] =
                    [foreign_key_column].[parent_object_id]
               AND [dependent_column].[column_id] =
                    [foreign_key_column].[parent_column_id]
            INNER JOIN [sys].[tables] AS [principal_table]
                ON [principal_table].[object_id] =
                    [foreign_key].[referenced_object_id]
            INNER JOIN [sys].[schemas] AS [principal_schema]
                ON [principal_schema].[schema_id] =
                    [principal_table].[schema_id]
            INNER JOIN [sys].[columns] AS [principal_column]
                ON [principal_column].[object_id] =
                    [foreign_key_column].[referenced_object_id]
               AND [principal_column].[column_id] =
                    [foreign_key_column].[referenced_column_id]
            WHERE [dependent_table].[is_ms_shipped] = 0
            ORDER BY
                [foreign_key].[object_id],
                [foreign_key_column].[constraint_column_id];
            """;

        await using var command = CreateCommand(sql);
        await using var reader = await command.ExecuteReaderAsync(ct);
        int? currentId = null;
        DatabaseObjectIdentity dependentTable = default;
        DatabaseObjectIdentity principalTable = default;
        var dependentColumns = new List<string>();
        var principalColumns = new List<string>();
        var deleteAction = string.Empty;
        var isDisabled = false;
        var isNotTrusted = false;

        while (await reader.ReadAsync(ct))
        {
            var id = reader.GetInt32(0);
            if (currentId.HasValue && currentId.Value != id)
            {
                AddForeignKey(
                    tables,
                    dependentTable,
                    dependentColumns,
                    principalTable,
                    principalColumns,
                    deleteAction,
                    isDisabled,
                    isNotTrusted);
                dependentColumns = [];
                principalColumns = [];
            }

            currentId = id;
            dependentTable = new DatabaseObjectIdentity(
                reader.GetString(1),
                reader.GetString(2));
            dependentColumns.Add(
                DatabaseSchemaNormalization.NormalizeIdentifier(
                    reader.GetString(3)));
            principalTable = new DatabaseObjectIdentity(
                reader.GetString(4),
                reader.GetString(5));
            principalColumns.Add(
                DatabaseSchemaNormalization.NormalizeIdentifier(
                    reader.GetString(6)));
            deleteAction =
                DatabaseSchemaNormalization.NormalizeDeleteAction(
                    reader.GetString(7));
            isDisabled = reader.GetBoolean(8);
            isNotTrusted = reader.GetBoolean(9);
        }

        if (currentId.HasValue)
        {
            AddForeignKey(
                tables,
                dependentTable,
                dependentColumns,
                principalTable,
                principalColumns,
                deleteAction,
                isDisabled,
                isNotTrusted);
        }
    }

    private async Task ReadIndexesAsync(
        IDictionary<DatabaseObjectIdentity, MutableTable> tables,
        CancellationToken ct)
    {
        const string sql = """
            SELECT
                [index].[object_id],
                [index].[index_id],
                [schema].[name],
                [table].[name],
                [index].[name],
                [column].[name],
                [index_column].[key_ordinal],
                [index_column].[is_descending_key],
                [index_column].[is_included_column],
                [index].[is_unique],
                [index].[is_unique_constraint],
                CASE WHEN [index].[type] = 1 THEN CAST(1 AS bit)
                     ELSE CAST(0 AS bit) END,
                [index].[is_disabled],
                [index].[filter_definition],
                [index_column].[index_column_id]
            FROM [sys].[indexes] AS [index]
            INNER JOIN [sys].[tables] AS [table]
                ON [table].[object_id] = [index].[object_id]
            INNER JOIN [sys].[schemas] AS [schema]
                ON [schema].[schema_id] = [table].[schema_id]
            INNER JOIN [sys].[index_columns] AS [index_column]
                ON [index_column].[object_id] = [index].[object_id]
               AND [index_column].[index_id] = [index].[index_id]
            INNER JOIN [sys].[columns] AS [column]
                ON [column].[object_id] = [index_column].[object_id]
               AND [column].[column_id] = [index_column].[column_id]
            WHERE [table].[is_ms_shipped] = 0
              AND [index].[index_id] > 0
              AND [index].[is_hypothetical] = 0
              AND [index].[is_primary_key] = 0
              AND NOT
              (
                  [schema].[name] = N'dbo'
                  AND [table].[name] = N'__EFMigrationsHistory'
              )
            ORDER BY
                [index].[object_id],
                [index].[index_id],
                CASE WHEN [index_column].[is_included_column] = 0
                     THEN 0 ELSE 1 END,
                [index_column].[key_ordinal],
                [index_column].[index_column_id];
            """;

        await using var command = CreateCommand(sql);
        await using var reader = await command.ExecuteReaderAsync(ct);
        (int ObjectId, int IndexId)? currentId = null;
        DatabaseObjectIdentity tableIdentity = default;
        var indexName = string.Empty;
        var keyColumns = new List<DatabaseIndexColumnSchema>();
        var includedColumns = new List<string>();
        var isUnique = false;
        var isUniqueConstraint = false;
        var isClustered = false;
        var isDisabled = false;
        string? filter = null;

        while (await reader.ReadAsync(ct))
        {
            var id = (reader.GetInt32(0), reader.GetInt32(1));
            if (currentId.HasValue && currentId.Value != id)
            {
                AddIndex(
                    tables,
                    tableIdentity,
                    indexName,
                    keyColumns,
                    includedColumns,
                    isUnique,
                    isUniqueConstraint,
                    isClustered,
                    isDisabled,
                    filter);
                keyColumns = [];
                includedColumns = [];
            }

            currentId = id;
            tableIdentity = new DatabaseObjectIdentity(
                reader.GetString(2),
                reader.GetString(3));
            indexName =
                DatabaseSchemaNormalization.NormalizeIdentifier(
                    reader.GetString(4));
            var columnName =
                DatabaseSchemaNormalization.NormalizeIdentifier(
                    reader.GetString(5));
            if (reader.GetBoolean(8))
            {
                includedColumns.Add(columnName);
            }
            else
            {
                keyColumns.Add(new DatabaseIndexColumnSchema(
                    columnName,
                    reader.GetBoolean(7)));
            }

            isUnique = reader.GetBoolean(9);
            isUniqueConstraint = reader.GetBoolean(10);
            isClustered = reader.GetBoolean(11);
            isDisabled = reader.GetBoolean(12);
            filter = reader.IsDBNull(13)
                ? null
                : DatabaseSchemaNormalization.NormalizeSqlExpression(
                    reader.GetString(13));
        }

        if (currentId.HasValue)
        {
            AddIndex(
                tables,
                tableIdentity,
                indexName,
                keyColumns,
                includedColumns,
                isUnique,
                isUniqueConstraint,
                isClustered,
                isDisabled,
                filter);
        }
    }

    private async Task ReadCheckConstraintsAsync(
        IDictionary<DatabaseObjectIdentity, MutableTable> tables,
        CancellationToken ct)
    {
        const string sql = """
            SELECT
                [schema].[name],
                [table].[name],
                [check].[name],
                [check].[definition],
                [check].[is_disabled],
                [check].[is_not_trusted]
            FROM [sys].[check_constraints] AS [check]
            INNER JOIN [sys].[tables] AS [table]
                ON [table].[object_id] = [check].[parent_object_id]
            INNER JOIN [sys].[schemas] AS [schema]
                ON [schema].[schema_id] = [table].[schema_id]
            WHERE [table].[is_ms_shipped] = 0
            ORDER BY
                [schema].[name],
                [table].[name],
                [check].[name];
            """;

        await using var command = CreateCommand(sql);
        await using var reader = await command.ExecuteReaderAsync(ct);

        while (await reader.ReadAsync(ct))
        {
            var identity = new DatabaseObjectIdentity(
                reader.GetString(0),
                reader.GetString(1));
            if (!tables.TryGetValue(identity, out var table))
            {
                continue;
            }

            table.CheckConstraints.Add(
                new DatabaseCheckConstraintSchema(
                    DatabaseSchemaNormalization.NormalizeIdentifier(
                        reader.GetString(2)),
                    DatabaseSchemaNormalization.NormalizeCheckConstraintExpression(
                        reader.GetString(3))
                    ?? string.Empty,
                    reader.GetBoolean(4),
                    reader.GetBoolean(5)));
        }
    }

    private async Task<IReadOnlyList<DatabaseSequenceSchema>>
        ReadSequencesAsync(CancellationToken ct)
    {
        const string sql = """
            SELECT
                [schema].[name],
                [sequence].[name],
                [type].[name],
                [sequence].[start_value],
                [sequence].[increment],
                [sequence].[minimum_value],
                [sequence].[maximum_value],
                [sequence].[is_cycling]
            FROM [sys].[sequences] AS [sequence]
            INNER JOIN [sys].[schemas] AS [schema]
                ON [schema].[schema_id] = [sequence].[schema_id]
            INNER JOIN [sys].[types] AS [type]
                ON [type].[user_type_id] = [sequence].[user_type_id]
            ORDER BY [schema].[name], [sequence].[name];
            """;

        await using var command = CreateCommand(sql);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var sequences = new List<DatabaseSequenceSchema>();

        while (await reader.ReadAsync(ct))
        {
            sequences.Add(new DatabaseSequenceSchema(
                new DatabaseObjectIdentity(
                    reader.GetString(0),
                    reader.GetString(1)),
                DatabaseSchemaNormalization.NormalizeStoreType(
                    reader.GetString(2)),
                Convert.ToInt64(
                    reader.GetValue(3),
                    CultureInfo.InvariantCulture),
                Convert.ToInt32(
                    reader.GetValue(4),
                    CultureInfo.InvariantCulture),
                reader.IsDBNull(5)
                    ? null
                    : Convert.ToInt64(
                        reader.GetValue(5),
                        CultureInfo.InvariantCulture),
                reader.IsDBNull(6)
                    ? null
                    : Convert.ToInt64(
                        reader.GetValue(6),
                        CultureInfo.InvariantCulture),
                reader.GetBoolean(7)));
        }

        return sequences;
    }

    private System.Data.Common.DbCommand CreateCommand(string sql)
    {
        var command = _db.Database
            .GetDbConnection()
            .CreateCommand();
        command.CommandText = sql;
        command.CommandType = CommandType.Text;
        command.Transaction = _db.Database.CurrentTransaction?.GetDbTransaction();
        return command;
    }

    private static StoreTypeFacets CreateStoreType(
        string typeName,
        short maxLength,
        byte precision,
        byte scale)
    {
        if (typeName is
            "varchar" or "nvarchar" or "char" or "nchar"
            or "binary" or "varbinary")
        {
            var normalizedLength = maxLength == -1
                ? -1
                : typeName is "nvarchar" or "nchar"
                    ? maxLength / 2
                    : maxLength;
            var lengthText = normalizedLength == -1
                ? "max"
                : normalizedLength.ToString(
                    CultureInfo.InvariantCulture);
            return new StoreTypeFacets(
                $"{typeName}({lengthText})",
                normalizedLength,
                null,
                null);
        }

        if (typeName is "decimal" or "numeric")
        {
            return new StoreTypeFacets(
                $"{typeName}({precision},{scale})",
                null,
                precision,
                scale);
        }

        if (typeName is "datetime2" or "datetimeoffset" or "time")
        {
            return scale == 7
                ? new StoreTypeFacets(
                    typeName,
                    null,
                    null,
                    null)
                : new StoreTypeFacets(
                    $"{typeName}({scale})",
                    null,
                    null,
                    scale);
        }

        return new StoreTypeFacets(
            typeName,
            null,
            null,
            null);
    }

    private static void SetPrimaryKey(
        IDictionary<DatabaseObjectIdentity, MutableTable> tables,
        DatabaseObjectIdentity identity,
        IReadOnlyList<string> columns,
        bool isClustered,
        bool isDisabled)
    {
        if (tables.TryGetValue(identity, out var table))
        {
            table.PrimaryKey = new DatabasePrimaryKeySchema(
                columns.ToArray(),
                isClustered,
                isDisabled);
        }
    }

    private static void AddForeignKey(
        IDictionary<DatabaseObjectIdentity, MutableTable> tables,
        DatabaseObjectIdentity dependentTable,
        IReadOnlyList<string> dependentColumns,
        DatabaseObjectIdentity principalTable,
        IReadOnlyList<string> principalColumns,
        string deleteAction,
        bool isDisabled,
        bool isNotTrusted)
    {
        if (tables.TryGetValue(dependentTable, out var table))
        {
            table.ForeignKeys.Add(new DatabaseForeignKeySchema(
                dependentColumns.ToArray(),
                principalTable,
                principalColumns.ToArray(),
                deleteAction,
                isDisabled,
                isNotTrusted));
        }
    }

    private static void AddIndex(
        IDictionary<DatabaseObjectIdentity, MutableTable> tables,
        DatabaseObjectIdentity tableIdentity,
        string indexName,
        IReadOnlyList<DatabaseIndexColumnSchema> keyColumns,
        IReadOnlyList<string> includedColumns,
        bool isUnique,
        bool isUniqueConstraint,
        bool isClustered,
        bool isDisabled,
        string? filter)
    {
        if (tables.TryGetValue(tableIdentity, out var table))
        {
            table.Indexes.Add(new DatabaseIndexSchema(
                indexName,
                keyColumns.ToArray(),
                includedColumns.ToArray(),
                isUnique,
                isUniqueConstraint,
                isClustered,
                isDisabled,
                filter));
        }
    }

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
        string StoreType,
        int? MaxLength,
        int? Precision,
        int? Scale);
}
