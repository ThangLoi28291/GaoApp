using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations;

public partial class EnableSnapshotProfitReads : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Opt-in transaction snapshots only; READ_COMMITTED_SNAPSHOT is not changed.
        migrationBuilder.Sql("ALTER DATABASE CURRENT SET ALLOW_SNAPSHOT_ISOLATION ON;", suppressTransaction: true);
        migrationBuilder.CreateIndex(name: "IX_SalesReturns_StoreId_CompletedAtUtc", table: "SalesReturns",
            columns: new[] { "StoreId", "CompletedAtUtc" });
        migrationBuilder.CreateIndex(name: "IX_InventoryValuationEntries_StoreId_EntryType_OccurredAtUtc", table: "InventoryValuationEntries",
            columns: new[] { "StoreId", "EntryType", "OccurredAtUtc" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "IX_SalesReturns_StoreId_CompletedAtUtc", table: "SalesReturns");
        migrationBuilder.DropIndex(name: "IX_InventoryValuationEntries_StoreId_EntryType_OccurredAtUtc", table: "InventoryValuationEntries");
        // Leave the opt-in database setting enabled: other readers may also depend on it.
        // An operator may turn it off after stopping all snapshot readers, if required.
    }
}
