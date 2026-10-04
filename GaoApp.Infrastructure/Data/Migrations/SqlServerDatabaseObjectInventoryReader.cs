using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GaoApp.Infrastructure.Data.Migrations;

public sealed class SqlServerDatabaseObjectInventoryReader
    : ISqlServerDatabaseObjectInventoryReader
{
    private static readonly HashSet<string> SystemSchemaAllowlist =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "dbo",
            "guest",
            "sys",
            "INFORMATION_SCHEMA",
            "db_owner",
            "db_accessadmin",
            "db_securityadmin",
            "db_ddladmin",
            "db_backupoperator",
            "db_datareader",
            "db_datawriter",
            "db_denydatareader",
            "db_denydatawriter"
        };

    private readonly AppDbContext _db;

    public SqlServerDatabaseObjectInventoryReader(AppDbContext db)
    {
        _db = db;
    }

    public async Task<DatabaseObjectInventory> ReadAsync(
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        const string sql = """
            SELECT [schema].[name], [table].[name]
            FROM [sys].[tables] AS [table]
            INNER JOIN [sys].[schemas] AS [schema]
                ON [schema].[schema_id] = [table].[schema_id]
            WHERE [table].[is_ms_shipped] = 0;

            SELECT [schema].[name], [view].[name]
            FROM [sys].[views] AS [view]
            INNER JOIN [sys].[schemas] AS [schema]
                ON [schema].[schema_id] = [view].[schema_id]
            WHERE [view].[is_ms_shipped] = 0;

            SELECT [schema].[name], [procedure].[name]
            FROM [sys].[procedures] AS [procedure]
            INNER JOIN [sys].[schemas] AS [schema]
                ON [schema].[schema_id] = [procedure].[schema_id]
            WHERE [procedure].[is_ms_shipped] = 0;

            SELECT [schema].[name], [object].[name]
            FROM [sys].[objects] AS [object]
            INNER JOIN [sys].[schemas] AS [schema]
                ON [schema].[schema_id] = [object].[schema_id]
            WHERE [object].[is_ms_shipped] = 0
              AND [object].[type] IN ('FN', 'IF', 'TF', 'FS', 'FT');

            SELECT [schema].[name], [sequence].[name]
            FROM [sys].[sequences] AS [sequence]
            INNER JOIN [sys].[schemas] AS [schema]
                ON [schema].[schema_id] = [sequence].[schema_id];

            SELECT [schema].[name], [synonym].[name]
            FROM [sys].[synonyms] AS [synonym]
            INNER JOIN [sys].[schemas] AS [schema]
                ON [schema].[schema_id] = [synonym].[schema_id];

            SELECT [schema].[name]
            FROM [sys].[schemas] AS [schema]
            WHERE [schema].[name] NOT IN
            (
                N'dbo',
                N'guest',
                N'sys',
                N'INFORMATION_SCHEMA',
                N'db_owner',
                N'db_accessadmin',
                N'db_securityadmin',
                N'db_ddladmin',
                N'db_backupoperator',
                N'db_datareader',
                N'db_datawriter',
                N'db_denydatareader',
                N'db_denydatawriter'
            );

            SELECT [schema].[name], [type].[name]
            FROM [sys].[types] AS [type]
            INNER JOIN [sys].[schemas] AS [schema]
                ON [schema].[schema_id] = [type].[schema_id]
            WHERE [type].[is_user_defined] = 1;

            SELECT [schema].[name], [object].[name]
            FROM [sys].[objects] AS [object]
            INNER JOIN [sys].[schemas] AS [schema]
                ON [schema].[schema_id] = [object].[schema_id]
            WHERE [object].[is_ms_shipped] = 0
              AND [object].[type] NOT IN
              (
                  'U', 'V', 'P', 'PC',
                  'FN', 'IF', 'TF', 'FS', 'FT',
                  'SO', 'SN',
                  'C', 'D', 'F', 'PK', 'UQ',
                  'IT', 'S'
              )
            UNION ALL
            SELECT N'database', [trigger].[name]
            FROM [sys].[triggers] AS [trigger]
            WHERE [trigger].[is_ms_shipped] = 0
              AND [trigger].[parent_class] = 0
            UNION ALL
            SELECT N'assembly', [assembly].[name]
            FROM [sys].[assemblies] AS [assembly]
            WHERE [assembly].[is_user_defined] = 1
            UNION ALL
            SELECT N'xml-schema', [collection].[name]
            FROM [sys].[xml_schema_collections] AS [collection]
            WHERE [collection].[xml_collection_id] > 1
            UNION ALL
            SELECT N'partition-function', [function].[name]
            FROM [sys].[partition_functions] AS [function]
            UNION ALL
            SELECT N'partition-scheme', [scheme].[name]
            FROM [sys].[partition_schemes] AS [scheme]
            UNION ALL
            SELECT N'fulltext-catalog', [catalog].[name]
            FROM [sys].[fulltext_catalogs] AS [catalog]
            ;

            SELECT [principal].[name]
            FROM [sys].[database_principals] AS [principal]
            WHERE [principal].[is_fixed_role] = 0
              AND [principal].[type] NOT IN ('R', 'A')
              AND [principal].[name] NOT IN
              (
                  N'dbo', N'guest', N'sys',
                  N'INFORMATION_SCHEMA', N'public'
              )
              AND [principal].[name] NOT LIKE N'##MS[_]%';

            SELECT [principal].[name]
            FROM [sys].[database_principals] AS [principal]
            WHERE [principal].[is_fixed_role] = 0
              AND [principal].[type] IN ('R', 'A')
              AND [principal].[name] <> N'public'
              AND [principal].[name] NOT LIKE N'##MS[_]%';

            SELECT [role].[name], [member].[name]
            FROM [sys].[database_role_members] AS [membership]
            INNER JOIN [sys].[database_principals] AS [role]
                ON [role].[principal_id] = [membership].[role_principal_id]
            INNER JOIN [sys].[database_principals] AS [member]
                ON [member].[principal_id] = [membership].[member_principal_id]
            WHERE [member].[name] NOT IN
              (
                  N'dbo', N'guest', N'sys',
                  N'INFORMATION_SCHEMA', N'public'
              )
              AND [member].[name] NOT LIKE N'##MS[_]%';

            SELECT [certificate].[name]
            FROM [sys].[certificates] AS [certificate]
            WHERE [certificate].[name] NOT LIKE N'##MS[_]%';

            SELECT [key].[name]
            FROM [sys].[asymmetric_keys] AS [key]
            WHERE [key].[name] NOT LIKE N'##MS[_]%';

            SELECT [key].[name]
            FROM [sys].[symmetric_keys] AS [key]
            WHERE [key].[name] NOT LIKE N'##MS[_]%';

            SELECT [credential].[name]
            FROM [sys].[database_scoped_credentials] AS [credential];
            """;

        await using var command = _db.Database
            .GetDbConnection()
            .CreateCommand();
        command.CommandText = sql;
        command.CommandType = CommandType.Text;
        command.Transaction = _db.Database.CurrentTransaction?.GetDbTransaction();

        await using var reader = await command.ExecuteReaderAsync(ct);

        var tables = await ReadIdentitiesAsync(reader, ct);
        await reader.NextResultAsync(ct);
        var views = await ReadIdentitiesAsync(reader, ct);
        await reader.NextResultAsync(ct);
        var procedures = await ReadIdentitiesAsync(reader, ct);
        await reader.NextResultAsync(ct);
        var functions = await ReadIdentitiesAsync(reader, ct);
        await reader.NextResultAsync(ct);
        var sequences = await ReadIdentitiesAsync(reader, ct);
        await reader.NextResultAsync(ct);
        var synonyms = await ReadIdentitiesAsync(reader, ct);
        await reader.NextResultAsync(ct);

        var userSchemas = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync(ct))
        {
            var schema = reader.GetString(0);
            if (!SystemSchemaAllowlist.Contains(schema))
            {
                userSchemas.Add(
                    DatabaseSchemaNormalization.NormalizeIdentifier(
                        schema));
            }
        }

        await reader.NextResultAsync(ct);
        var userDefinedTypes = await ReadIdentitiesAsync(reader, ct);
        await reader.NextResultAsync(ct);
        var otherStructuralObjects = await ReadIdentitiesAsync(reader, ct);
        await reader.NextResultAsync(ct);
        var databaseUsers = await ReadNamesAsync(reader, ct);
        await reader.NextResultAsync(ct);
        var customDatabaseRoles = await ReadNamesAsync(reader, ct);
        await reader.NextResultAsync(ct);
        var roleMemberships = await ReadRoleMembershipsAsync(reader, ct);
        await reader.NextResultAsync(ct);
        var certificates = await ReadNamesAsync(reader, ct);
        await reader.NextResultAsync(ct);
        var asymmetricKeys = await ReadNamesAsync(reader, ct);
        await reader.NextResultAsync(ct);
        var symmetricKeys = await ReadNamesAsync(reader, ct);
        await reader.NextResultAsync(ct);
        var databaseScopedCredentials = await ReadNamesAsync(reader, ct);

        return new DatabaseObjectInventory(
            tables,
            views,
            procedures,
            functions,
            sequences,
            synonyms,
            userSchemas,
            userDefinedTypes,
            otherStructuralObjects,
            new DatabaseSecurityMetadataInventory(
                databaseUsers,
                customDatabaseRoles,
                roleMemberships,
                certificates,
                asymmetricKeys,
                symmetricKeys,
                databaseScopedCredentials));
    }

    private static async Task<HashSet<DatabaseObjectIdentity>>
        ReadIdentitiesAsync(
            System.Data.Common.DbDataReader reader,
            CancellationToken ct)
    {
        var result = new HashSet<DatabaseObjectIdentity>();

        while (await reader.ReadAsync(ct))
        {
            result.Add(new DatabaseObjectIdentity(
                reader.GetString(0),
                reader.GetString(1)));
        }

        return result;
    }

    private static async Task<HashSet<string>> ReadNamesAsync(
        System.Data.Common.DbDataReader reader,
        CancellationToken ct)
    {
        var result = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        while (await reader.ReadAsync(ct))
        {
            result.Add(
                DatabaseSchemaNormalization.NormalizeIdentifier(
                    reader.GetString(0)));
        }

        return result;
    }

    private static async Task<HashSet<DatabaseRoleMembershipIdentity>>
        ReadRoleMembershipsAsync(
            System.Data.Common.DbDataReader reader,
            CancellationToken ct)
    {
        var result = new HashSet<DatabaseRoleMembershipIdentity>();

        while (await reader.ReadAsync(ct))
        {
            result.Add(new DatabaseRoleMembershipIdentity(
                reader.GetString(0),
                reader.GetString(1)));
        }

        return result;
    }
}
