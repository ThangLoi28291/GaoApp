using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Data.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GaoApp.Infrastructure.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration(DeliveryOpeningSourceSchemaSqlCatalog.MigrationId)]
public sealed class AllowDeliveryOpeningCostSources : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(DeliveryOpeningSourceSchemaSqlCatalog.DropSql);
        migrationBuilder.Sql(DeliveryOpeningSourceSchemaSqlCatalog.TriggerSql);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(DeliveryOpeningSourceSchemaSqlCatalog.DownGuardSql);
        migrationBuilder.Sql(DeliveryOpeningSourceSchemaSqlCatalog.DropSql);
        migrationBuilder.Sql(DeliverySchemaSqlCatalog.InventoryLayerTriggerSql);
    }
}
