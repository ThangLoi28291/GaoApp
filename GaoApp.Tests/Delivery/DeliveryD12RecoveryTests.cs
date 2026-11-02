using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using GaoApp.Tests.Configuration;
using GaoApp.Tests.Security;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Delivery;

[Collection("DeliveryD12"), Trait("Category", "DeliveryD12")]
public sealed class DeliveryD12RecoveryTests(DeliveryD02Fixture fixture)
{
    [Fact]
    public async Task Copy_only_backup_restores_committed_goods_money_and_delivery_history_into_an_owned_database()
    {
        using var c = await DeliveryD08Case.CreateAsync(fixture);
        var ack = await c.ConfirmAsync(await c.RequestAsync(2));
        var before = await FactsAsync(fixture.Web.Database);
        await using var restored = new InventoryPostingLocalDb();
        // Allocates only a new GUID-named fixture database; the restore never names an application DB.
        await restored.MigrateAsync();
        var origin = new SqlConnectionStringBuilder(fixture.Web.Database.ConnectionString);
        var target = new SqlConnectionStringBuilder(restored.ConnectionString);
        foreach (var name in new[] { origin.InitialCatalog, target.InitialCatalog })
            Assert.Matches("^GaoApp_R2_InventoryPosting_[A-F0-9]{32}$", name);
        Assert.NotEqual(origin.InitialCatalog, target.InitialCatalog);
        var output = Path.Combine(FullApplicationFixture.SourceRoot(), "Logs", "delivery-roadmap", "D12", "recovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);
        var backupPath = Path.Combine(output, "owned-test-database.bak");
        origin.InitialCatalog = "master";
        await using var master = new SqlConnection(origin.ConnectionString);
        await master.OpenAsync();
        async Task Execute(string sql)
        {
            await using var command = new SqlCommand(sql, master) { CommandTimeout = 120 };
            command.Parameters.Add("@backup", SqlDbType.NVarChar, 4000).Value = backupPath;
            await command.ExecuteNonQueryAsync();
        }
        var sourceName = new SqlConnectionStringBuilder(fixture.Web.Database.ConnectionString).InitialCatalog;
        await Execute($"BACKUP DATABASE [{sourceName}] TO DISK=@backup WITH COPY_ONLY, CHECKSUM; RESTORE VERIFYONLY FROM DISK=@backup WITH CHECKSUM;");
        var files = new List<(string Logical, string Type, string Physical)>();
        await using (var command = new SqlCommand("SELECT name,type_desc,physical_name FROM sys.master_files WHERE database_id=DB_ID(@target) ORDER BY file_id", master))
        {
            command.Parameters.Add("@target", SqlDbType.NVarChar, 128).Value = target.InitialCatalog;
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) files.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        }
        var dataPath = Assert.Single(files, x => x.Type == "ROWS").Physical;
        var logPath = Assert.Single(files, x => x.Type == "LOG").Physical;
        var backupFiles = new List<(string Logical, string Type)>();
        await using (var command = new SqlCommand("RESTORE FILELISTONLY FROM DISK=@backup", master))
        {
            command.Parameters.Add("@backup", SqlDbType.NVarChar, 4000).Value = backupPath;
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) backupFiles.Add((reader.GetString(reader.GetOrdinal("LogicalName")), reader.GetString(reader.GetOrdinal("Type"))));
        }
        var dataLogical = Assert.Single(backupFiles, x => x.Type == "D").Logical;
        var logLogical = Assert.Single(backupFiles, x => x.Type == "L").Logical;
        using (var pooledTarget = new SqlConnection(restored.ConnectionString)) SqlConnection.ClearPool(pooledTarget);
        await using (var command = new SqlCommand($"ALTER DATABASE [{target.InitialCatalog}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; " +
            $"RESTORE DATABASE [{target.InitialCatalog}] FROM DISK=@backup WITH REPLACE,CHECKSUM,MOVE @dataLogical TO @dataPath,MOVE @logLogical TO @logPath; " +
            $"ALTER DATABASE [{target.InitialCatalog}] SET MULTI_USER; DBCC CHECKDB ([{target.InitialCatalog}]) WITH NO_INFOMSGS;", master) { CommandTimeout = 120 })
        {
            foreach (var pair in new[] { ("@backup", backupPath), ("@dataLogical", dataLogical), ("@logLogical", logLogical),
                ("@dataPath", dataPath), ("@logPath", logPath) }) command.Parameters.AddWithValue(pair.Item1, pair.Item2);
            await command.ExecuteNonQueryAsync();
        }
        var recovered = await FactsAsync(restored);
        Assert.Equal(before, recovered);
        await using (var check = restored.CreateTenantContext(c.Closer.Store.StoreId))
        {
            Assert.Equal("Settled", (await check.DeliveryOrders.SingleAsync(x => x.Id == c.Id)).State.ToString());
            var sale = await check.Orders.Include(x => x.Payments).SingleAsync(x => x.Id == ack.Settlement.SaleOrderId);
            Assert.Equal(40000, sale.GrandTotal); Assert.Equal(40000, Assert.Single(sale.Payments).Amount);
            Assert.Equal(2, await check.DeliveryReturnedCostFragments.Where(x => x.DeliveryOrderId == c.Id).SumAsync(x => x.BaseQuantity));
        }
        var evidence = new { sourceDatabase = sourceName, restoredDatabase = target.InitialCatalog,
            backup = backupPath, backupSha256 = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(backupPath))),
            verifiedBackup = true, checkDb = true, goodsMoneyHistoryEqual = true,
            factsSha256 = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(before))),
            qualification = "Local disposable SQL fixture only; production backup/restore, IIS and external uploads/keys remain operator acceptance." };
        await File.WriteAllTextAsync(Path.Combine(output, "verification.json"), JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static async Task<string> FactsAsync(InventoryPostingLocalDb database)
    {
        await using var db = database.CreateHostContext();
        return JsonSerializer.Serialize(new {
            Migrations = await db.Database.GetAppliedMigrationsAsync(),
            Orders = await db.Orders.IgnoreQueryFilters().AsNoTracking().OrderBy(x => x.Id)
                .Select(x => new { x.Id,x.Status,x.GrandTotal,x.PaidTotal,x.BalanceDue,x.POSShiftId,x.RowVersion }).ToArrayAsync(),
            Lines = await db.OrderLines.IgnoreQueryFilters().AsNoTracking().OrderBy(x => x.Id)
                .Select(x => new { x.Id,x.OrderId,x.Quantity,x.BaseQuantity,x.LineTotal,x.LineCostTotal,x.IsProvisionalCost }).ToArrayAsync(),
            Payments = await db.OrderPayments.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
            Shifts = await db.POSShifts.IgnoreQueryFilters().AsNoTracking().OrderBy(x => x.Id)
                .Select(x => new { x.Id,x.Status,x.CashSalesTotal,x.NonCashSalesTotal,x.CurrentOrderId }).ToArrayAsync(),
            Balances = await db.InventoryBalances.IgnoreQueryFilters().AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
            Movements = await db.InventoryTransactions.IgnoreQueryFilters().AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
            Deliveries = await db.DeliveryOrders.IgnoreQueryFilters().AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
            Journal = await db.DeliveryJournalEntries.IgnoreQueryFilters().AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
            Revisions = await db.DeliveryRevisions.IgnoreQueryFilters().AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
            Returns = await db.DeliveryReturnedCostFragments.IgnoreQueryFilters().AsNoTracking().OrderBy(x => x.Id).ToArrayAsync()
        });
    }
}
