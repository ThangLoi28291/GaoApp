using System.Reflection;
using GaoApp.Infrastructure.Data.Migrations;
using GaoApp.Infrastructure.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GaoApp.Tests.Configuration;

public sealed class BankNotificationSchemaSqlCatalogTests
{
    [Theory]
    [InlineData("DROP TABLE [Orders];", false)]
    [InlineData("CREATE INDEX [Unexpected] ON [BankNotificationReceipts] ([StoreId]);", false)]
    [InlineData("UPDATE [Stores] SET [Name]='changed';", false)]
    [InlineData("", true)]
    public void Notification_schema_manifest_rejects_unreviewed_or_nontransactional_sql(string sql, bool suppressTransaction)
    {
        if (sql.Length == 0) sql = new AddBankNotificationBridge().UpOperations.OfType<SqlOperation>().First().Sql;
        var method = typeof(EfCoreDatabaseSchemaManifestCatalog).Assembly
            .GetType("GaoApp.Infrastructure.Data.Migrations.BankNotificationSchemaSqlCatalog")!
            .GetMethod("TryGetOperation", BindingFlags.Public | BindingFlags.Static)!;
        var error = Assert.Throws<TargetInvocationException>(() => method.Invoke(null,
            new object?[] { "20261014140000_AddBankNotificationBridge", new SqlOperation { Sql = sql, SuppressTransaction = suppressTransaction }, null }));
        Assert.IsType<InvalidOperationException>(error.InnerException);
    }
}
