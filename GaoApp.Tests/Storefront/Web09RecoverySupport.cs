using System.Data;
using System.Text.Json;
using System.Text.RegularExpressions;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Storefront;
using GaoApp.Infrastructure.Data;
using GaoApp.Tests.Configuration;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Storefront;

internal static class Web09RecoverySupport
{
    private static string Guard(InventoryPostingLocalDb database)
    {
        var cs = new SqlConnectionStringBuilder(database.ConnectionString);
        Assert.Equal(SqlTestDataSource.Current, cs.DataSource);
        Assert.Matches("^GaoApp_R2_InventoryPosting_[A-F0-9]{32}$", cs.InitialCatalog); return cs.InitialCatalog;
    }
    private static string Literal(string value) => "N'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
    internal static void Plan(InventoryPostingLocalDb database, string purpose)
    {
        var name = Guard(database); File.WriteAllText(Web09Support.Artifact("ownership/database-plan-" + name + ".json"),
            JsonSerializer.Serialize(new { AtUtc = DateTime.UtcNow, Database = name, Owner = Web09Support.Owner(), Purpose = purpose,
                CreationDeferredToOrdinaryFixture = true, NeverUserDatabase = true }));
    }
    internal static async Task<string> DigestAsync(AppDbContext db)
    {
        Type[] types = [typeof(StorefrontSettings), typeof(StorefrontQuote), typeof(StorefrontOrder), typeof(StorefrontOrderLine),
            typeof(StorefrontCommandReceipt), typeof(StorefrontEvent), typeof(StorefrontOutboxMessage), typeof(StorefrontFulfillmentSource),
            typeof(StorefrontReservationLink), typeof(InventoryReservation), typeof(InventoryBalance), typeof(Order), typeof(OrderPayment), typeof(AdminMenuItem)];
        return await DigestTypesAsync(db, types);
    }
    internal static async Task<string> BankDigestAsync(AppDbContext db)
    {
        Type[] types = [typeof(StorefrontPaymentIntent), typeof(StorefrontQrProcessing), typeof(StorefrontBankReceipt), typeof(StorefrontPaymentAllocation),
            typeof(StorefrontRefund), typeof(StorefrontRefundFunding), typeof(StorefrontRefundDecision), typeof(StorefrontRefundEvidence),
            typeof(StorefrontBankScanAttempt), typeof(StorefrontBankScanCompletion), typeof(StorefrontBankReviewRestriction),
            typeof(BankVerifiedReceipt), typeof(BankReceiptConsumption), typeof(TreasuryEntry), typeof(StoreAcbSettings), typeof(StoreBankAccount), typeof(StorefrontBankCollectionAuthority)];
        return await DigestTypesAsync(db, types);
    }
    private static async Task<string> DigestTypesAsync(AppDbContext db, Type[] types)
    {
        var rows = new SortedDictionary<string, object>();
        await using var sql = new SqlConnection(db.Database.GetConnectionString()); await sql.OpenAsync();
        foreach (var type in types)
        {
            var entity = db.Model.FindEntityType(type)!; var table = entity.GetTableName()!; Assert.Matches("^[A-Za-z0-9_]+$", table);
            var scope = entity.FindProperty("OriginStoreId") is not null ? "OriginStoreId" : "StoreId"; Assert.NotNull(entity.FindProperty(scope));
            await using var command = new SqlCommand($"SELECT COUNT_BIG(*),HASHBYTES('SHA2_256',(SELECT * FROM dbo.[{table}] WHERE [{scope}]=@store ORDER BY Id FOR JSON PATH,INCLUDE_NULL_VALUES)) FROM dbo.[{table}] WHERE [{scope}]=@store", sql);
            command.Parameters.AddWithValue("@store", db.CurrentStoreId!.Value);
            await using var reader = await command.ExecuteReaderAsync(); Assert.True(await reader.ReadAsync());
            rows[table] = new { Count = reader.GetInt64(0), SHA256 = reader.IsDBNull(1) ? null : Convert.ToHexString((byte[])reader[1]) };
        }
        return JsonSerializer.Serialize(rows);
    }
    internal static async Task BackupRestoreAsync(InventoryPostingLocalDb source, InventoryPostingLocalDb target, string directory)
    {
        var sourceName = Guard(source); var targetName = Guard(target); Assert.NotEqual(sourceName, targetName);
        var backup = Path.GetFullPath(Path.Combine(directory, "owned-source-copy-only.bak")); Web09Support.NoReparse(backup);
        Assert.False(File.Exists(backup));
        File.WriteAllText(Path.Combine(directory, "backup-before.json"), JsonSerializer.Serialize(new { AtUtc = DateTime.UtcNow,
            Source = sourceName, Target = targetName, Path = backup, InitiallyAbsent = true, Owner = Web09Support.Owner(), RetainBackup = true,
            SourceRequestsQuiescent = true, NoPrivateRingCopy = true, NoAclChange = true }));
        var cs = new SqlConnectionStringBuilder(source.ConnectionString) { InitialCatalog = "master", Pooling = false };
        await using var master = new SqlConnection(cs.ConnectionString); await master.OpenAsync();
        async Task Execute(string sql)
        { await using var command = new SqlCommand(sql, master) { CommandTimeout = 120 }; await command.ExecuteNonQueryAsync(); }
        // Fresh path only. No automatic service-account ACL or fallback path.
        await Execute($"BACKUP DATABASE [{sourceName}] TO DISK={Literal(backup)} WITH COPY_ONLY,CHECKSUM");
        await Execute($"RESTORE VERIFYONLY FROM DISK={Literal(backup)} WITH CHECKSUM");
        var header = new Dictionary<string, object?>();
        await using (var command = new SqlCommand($"RESTORE HEADERONLY FROM DISK={Literal(backup)}", master))
        await using (var reader = await command.ExecuteReaderAsync())
        {
            Assert.True(await reader.ReadAsync());
            foreach (var field in new[] { "DatabaseName", "BackupStartDate", "BackupFinishDate", "FirstLSN", "LastLSN", "CheckpointLSN", "DatabaseBackupLSN", "IsCopyOnly", "HasBackupChecksums" })
                header[field] = reader[field] is DBNull ? null : reader[field];
            Assert.Equal(sourceName, header["DatabaseName"]); Assert.Equal(true, header["IsCopyOnly"]); Assert.Equal(true, header["HasBackupChecksums"]); Assert.False(await reader.ReadAsync());
        }
        var logical = new Dictionary<string, string>();
        await using (var command = new SqlCommand($"RESTORE FILELISTONLY FROM DISK={Literal(backup)}", master))
        await using (var reader = await command.ExecuteReaderAsync())
            while (await reader.ReadAsync()) Assert.True(logical.TryAdd(reader["Type"].ToString()!, reader["LogicalName"].ToString()!));
        Assert.Equal(new[] { "D", "L" }, logical.Keys.Order().ToArray());
        var targetFiles = new Dictionary<string, string>();
        await using (var targetSql = new SqlConnection(target.ConnectionString))
        {
            await targetSql.OpenAsync(); await using var command = new SqlCommand("SELECT type,physical_name FROM sys.database_files ORDER BY type", targetSql);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var kind = reader.GetByte(0) switch { 0 => "D", 1 => "L", _ => throw new InvalidOperationException("Unexpected own target file type") };
                var path = reader.GetString(1); Assert.StartsWith(targetName, Path.GetFileName(path), StringComparison.OrdinalIgnoreCase);
                Assert.True(targetFiles.TryAdd(kind, path));
            }
        }
        Assert.Equal(new[] { "D", "L" }, targetFiles.Keys.Order().ToArray());
        using (var poolIdentity = new SqlConnection(target.ConnectionString)) SqlConnection.ClearPool(poolIdentity);
        var started = DateTime.UtcNow;
        try
        {
            await Execute($"ALTER DATABASE [{targetName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE");
            await Execute($"RESTORE DATABASE [{targetName}] FROM DISK={Literal(backup)} WITH REPLACE,CHECKSUM,MOVE {Literal(logical["D"])} TO {Literal(targetFiles["D"])},MOVE {Literal(logical["L"])} TO {Literal(targetFiles["L"])}");
        }
        finally { await Execute($"ALTER DATABASE [{targetName}] SET MULTI_USER"); }
        File.WriteAllText(Path.Combine(directory, "backup-restore-completed.json"), JsonSerializer.Serialize(new { AtUtc = DateTime.UtcNow,
            Owner = Web09Support.Owner(), Source = sourceName, Target = targetName, BackupPath = backup,
            BackupBytes = new FileInfo(backup).Length, Header = header, Moves = targetFiles, RestoreStartedUtc = started,
            RestoreEndedUtc = DateTime.UtcNow, NewOwnTargetOnly = true, NoAclChange = true, NoRingBytesCopied = true, NoProductionRpoRtoClaim = true }));
    }
}
