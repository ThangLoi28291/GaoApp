using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260920093000_OptimizeInventoryLedgerTimeline")]
public sealed class OptimizeInventoryLedgerTimeline : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE INDEX [IX_InventoryTransactions_LedgerTimeline]
            ON [dbo].[InventoryTransactions]
                ([StoreId], [OccurredAtUtc] DESC, [Id] DESC)
            INCLUDE ([QuantityChange], [AfterQty])
            WHERE [IsDeleted] = 0;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_InventoryTransactions_LedgerTimeline",
            table: "InventoryTransactions");
    }
}
