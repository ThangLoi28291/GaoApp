using GaoApp.Infrastructure.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GaoApp.Infrastructure.Data.Migrations;

internal static class BankNotificationSchemaSqlCatalog
{
    private const string MigrationId = "20261014140000_AddBankNotificationBridge";

    public static bool TryGetOperation(string migrationId, SqlOperation sql, out MigrationOperation operation)
    {
        operation = null!;
        if (!string.Equals(migrationId, MigrationId, StringComparison.Ordinal)) return false;
        var statements = BankNotificationSchemaV1.CreateSql().ToArray();
        var operations = BankNotificationSchemaV1.ManifestOperations().ToArray();
        var position = Array.IndexOf(statements, sql.Sql);
        if (sql.SuppressTransaction || position < 0 || statements.Length != operations.Length)
            throw new InvalidOperationException("The bank notification migration contains an unreviewed schema SQL operation.");
        operation = operations[position];
        return true;
    }
}
