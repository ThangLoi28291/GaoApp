using GaoApp.Domain.Entities;
using GaoApp.Tests.Configuration;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Inventory;

[Collection("R1FinalDatabasePreflight")]
public sealed class InputInvoiceReconciliationSqlServerConcurrencyTests
{
    [Fact]
    public async Task Sql_server_model_enforces_one_active_snapshot_per_receipt_invoice_link()
    {
        await using var database = new InventoryPostingLocalDb();
        await database.MigrateAsync();
        await using var db = database.CreateHostContext();

        var entity = db.Model.FindEntityType(
            typeof(StockDocumentInputInvoiceReconciliation));
        Assert.NotNull(entity);
        var index = Assert.Single(entity!.GetIndexes(), x => x.IsUnique);
        Assert.Equal(
            new[] { "StoreId", "StockDocumentInputInvoiceMapId" },
            index.Properties.Select(x => x.Name));
        Assert.Contains("IsDeleted", index.GetFilter(), StringComparison.Ordinal);
        Assert.True(entity.FindProperty("RowVersion")?.IsConcurrencyToken);
    }
}
