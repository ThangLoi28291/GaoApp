using GaoApp.Domain.Entities;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Configuration;

[Collection("R1FinalDatabasePreflight")]
public sealed class ReceiptTemplateLegacyRepairTests
{
    private const string Legacy = "20260909171948_AddPosReceiptTemplates";
    private const string Current = "20260910002000_AddPosReceiptTemplates";

    [Fact]
    public async Task Repair_preserves_template_content_and_identity_and_is_repeatable_before_EF_startup()
    {
        await using var database = new InventoryPostingLocalDb();
        await PrepareLegacyAsync(database);
        var version = await database.ExecuteScalarAsync<string>("SELECT CONVERT(varchar(18), RowVersion, 1) FROM dbo.PosReceiptTemplates");
        await database.ExecuteAsync(RepairSql());
        await database.ExecuteAsync(RepairSql());
        Assert.Equal(-1, await JsonLength(database));
        Assert.Equal(0, await HistoryCount(database, Legacy));
        Assert.Equal(1, await HistoryCount(database, Current));
        Assert.Equal("Mẫu giữ nguyên", await database.ExecuteScalarAsync<string>("SELECT Name FROM dbo.PosReceiptTemplates WHERE Id = 1"));
        Assert.Equal("{\"footerText\":\"Tiếng Việt — giữ dữ liệu\"}", await database.ExecuteScalarAsync<string>("SELECT DefinitionJson FROM dbo.PosReceiptTemplates WHERE Id = 1"));
        Assert.Equal(version, await database.ExecuteScalarAsync<string>("SELECT CONVERT(varchar(18), RowVersion, 1) FROM dbo.PosReceiptTemplates WHERE Id = 1"));
        await database.MigrateAsync(); // Same call that failed during the user's Development startup.
        await using var db = database.CreateHostContext();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
    }

    [Theory]
    [InlineData("columns")]
    [InlineData("missing-history")]
    [InlineData("both-history-ids")]
    public async Task Repair_rejects_unrecognized_states_without_widening_or_changing_history(string state)
    {
        await using var database = new InventoryPostingLocalDb();
        await PrepareLegacyAsync(database);
        await database.ExecuteAsync(state switch
        {
            "columns" => "ALTER TABLE dbo.PosReceiptTemplates ADD Unexpected int NULL;",
            "missing-history" => $"DELETE FROM dbo.__EFMigrationsHistory WHERE MigrationId = N'{Legacy}';",
            _ => $"INSERT dbo.__EFMigrationsHistory VALUES (N'{Current}', N'8.0.29');"
        });
        var beforeLegacy = await HistoryCount(database, Legacy);
        var beforeCurrent = await HistoryCount(database, Current);
        var error = await Assert.ThrowsAsync<SqlException>(() => database.ExecuteAsync(RepairSql()));
        Assert.Equal(51041, error.Number);
        Assert.Equal(8000, await JsonLength(database));
        Assert.Equal(beforeLegacy, await HistoryCount(database, Legacy));
        Assert.Equal(beforeCurrent, await HistoryCount(database, Current));
        Assert.Equal(1, await database.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.PosReceiptTemplates"));
    }

    [Fact]
    public async Task Failure_after_column_widening_rolls_back_schema_and_history_together()
    {
        await using var database = new InventoryPostingLocalDb();
        await PrepareLegacyAsync(database);
        await database.ExecuteAsync("""
            CREATE TRIGGER dbo.RejectReceiptHistoryUpdate ON dbo.__EFMigrationsHistory AFTER UPDATE AS
            THROW 51099, 'Injected history failure', 1;
            """);
        var error = await Assert.ThrowsAsync<SqlException>(() => database.ExecuteAsync(RepairSql()));
        Assert.Equal(51099, error.Number);
        Assert.Equal(8000, await JsonLength(database));
        Assert.Equal(1, await HistoryCount(database, Legacy));
        Assert.Equal(0, await HistoryCount(database, Current));
        Assert.Equal(1, await database.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.PosReceiptTemplates"));
    }

    private static async Task PrepareLegacyAsync(InventoryPostingLocalDb database)
    {
        await database.MigrateAsync();
        await using var db = database.CreateHostContext();
        var store = new Store { Name = "Receipt repair test", SubDomain = "repair", SubDomainNormalized = "REPAIR", IsActive = true };
        db.Stores.Add(store);
        await db.SaveChangesAsync();
        await database.ExecuteAsync($$"""
            ALTER TABLE dbo.PosReceiptTemplates ALTER COLUMN DefinitionJson nvarchar(4000) NOT NULL;
            UPDATE dbo.__EFMigrationsHistory SET MigrationId = N'{{Legacy}}' WHERE MigrationId = N'{{Current}}';
            INSERT dbo.PosReceiptTemplates (Name, DefinitionJson, CreatedAtUtc, IsDeleted, StoreId)
            VALUES (N'Mẫu giữ nguyên', N'{"footerText":"Tiếng Việt — giữ dữ liệu"}', '2026-09-10', 0, {{store.Id}});
            """);
    }

    private static Task<int> JsonLength(InventoryPostingLocalDb database) => database.ExecuteScalarAsync<int>(
        "SELECT COL_LENGTH(N'dbo.PosReceiptTemplates', N'DefinitionJson')");
    private static Task<int> HistoryCount(InventoryPostingLocalDb database, string migration) => database.ExecuteScalarAsync<int>(
        $"SELECT COUNT(*) FROM dbo.__EFMigrationsHistory WHERE MigrationId = N'{migration}'");
    private static string RepairSql()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GaoApp.sln"))) directory = directory.Parent;
        return File.ReadAllText(Path.Combine(directory?.FullName ?? throw new InvalidOperationException("Repository root missing"),
            "docs", "pos-receipt-templates-legacy-repair.sql"));
    }
}
