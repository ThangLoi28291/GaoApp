using GaoApp.Infrastructure.Data.Migrations;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace GaoApp.Infrastructure.Migrations;

public partial class AddStorefrontCheckout : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(StorefrontCheckoutSchemaSqlCatalog.PreflightSql);
        migrationBuilder.Sql(StorefrontCheckoutSchemaSqlCatalog.PermissionSql);
        migrationBuilder.AddCheckConstraint("CK_InventoryTransactions_NoStorefrontPhysical", "InventoryTransactions", "[ReferenceType]<>12");
        migrationBuilder.AddColumn<int>("StorefrontOrderId", "InventoryReservations", type: "int", nullable: true);
        migrationBuilder.AddColumn<int>("StorefrontOrderLineId", "InventoryReservations", type: "int", nullable: true);
        migrationBuilder.AddColumn<int>("StorefrontFulfillmentSourceId", "InventoryReservations", type: "int", nullable: true);
        migrationBuilder.AddColumn<decimal>("RoundingAdjustment", "StorefrontOrderLines", type: "decimal(18,2)", precision: 18, scale: 2, nullable: false, defaultValue: 0m);
        migrationBuilder.AlterColumn<string>("UnitName", "StorefrontOrderLines", type: "nvarchar(200)", maxLength: 200, nullable: false,
            oldClrType: typeof(string), oldType: "nvarchar(100)", oldMaxLength: 100);
        migrationBuilder.AddUniqueConstraint("AK_StorefrontOrderLines_StoreId_StorefrontOrderId_Id_VariantId", "StorefrontOrderLines", ["StoreId", "StorefrontOrderId", "Id", "VariantId"]);
        migrationBuilder.AddUniqueConstraint("AK_StorefrontFulfillmentSources_StoreId_StorefrontOrderId_Id_SourceWarehouseId_SourceLegalEntityId", "StorefrontFulfillmentSources",
            ["StoreId", "StorefrontOrderId", "Id", "SourceWarehouseId", "SourceLegalEntityId"]);
        migrationBuilder.CreateIndex("UX_InventoryReservations_StorefrontLineSource", "InventoryReservations", ["StoreId", "StorefrontOrderLineId", "StorefrontFulfillmentSourceId"],
            unique: true, filter: "[ReferenceType]=12 AND [IsDeleted]=0");
        migrationBuilder.CreateIndex("IX_InventoryReservations_StoreId_StorefrontOrderId_StorefrontOrderLineId_ProductVariantId", "InventoryReservations",
            ["StoreId", "StorefrontOrderId", "StorefrontOrderLineId", "ProductVariantId"]);
        migrationBuilder.CreateIndex("IX_InventoryReservations_StoreId_StorefrontOrderId_StorefrontFulfillmentSourceId_WarehouseId_SourceLegalEntityId", "InventoryReservations",
            ["StoreId", "StorefrontOrderId", "StorefrontFulfillmentSourceId", "WarehouseId", "SourceLegalEntityId"]);
        migrationBuilder.AddForeignKey("FK_InventoryReservations_StorefrontOrderLine", "InventoryReservations",
            ["StoreId", "StorefrontOrderId", "StorefrontOrderLineId", "ProductVariantId"], "StorefrontOrderLines", principalColumns: ["StoreId", "StorefrontOrderId", "Id", "VariantId"], onDelete: ReferentialAction.Restrict);
        migrationBuilder.AddForeignKey("FK_InventoryReservations_StorefrontFulfillmentSource", "InventoryReservations",
            ["StoreId", "StorefrontOrderId", "StorefrontFulfillmentSourceId", "WarehouseId", "SourceLegalEntityId"], "StorefrontFulfillmentSources",
            principalColumns: ["StoreId", "StorefrontOrderId", "Id", "SourceWarehouseId", "SourceLegalEntityId"], onDelete: ReferentialAction.Restrict);
        migrationBuilder.DropCheckConstraint("CK_InventoryReservations_DeliverySource", "InventoryReservations");
        migrationBuilder.AddCheckConstraint("CK_InventoryReservations_DeliverySource", "InventoryReservations", StorefrontCheckoutSchemaSqlCatalog.DeliveryConstraint);
        migrationBuilder.AddCheckConstraint("CK_InventoryReservations_StorefrontSource", "InventoryReservations", StorefrontCheckoutSchemaSqlCatalog.ReservationConstraint);
        migrationBuilder.DropCheckConstraint("CK_StorefrontOrderLines_Price", "StorefrontOrderLines");
        migrationBuilder.AddCheckConstraint("CK_StorefrontOrderLines_Price", "StorefrontOrderLines", StorefrontCheckoutSchemaSqlCatalog.LinePriceConstraint);
        migrationBuilder.AddCheckConstraint("CK_StorefrontOrderLines_RoundingAdjustment", "StorefrontOrderLines", "[RoundingAdjustment] BETWEEN -201 AND 201 AND [RoundingAdjustment] = ROUND([RoundingAdjustment],0)");
        foreach (var trigger in StorefrontCheckoutSchemaSqlCatalog.Triggers) migrationBuilder.Sql(trigger.Sql);
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(StorefrontCheckoutSchemaSqlCatalog.DownGuardSql);
        migrationBuilder.DropCheckConstraint("CK_InventoryTransactions_NoStorefrontPhysical", "InventoryTransactions");
        foreach (var trigger in StorefrontCheckoutSchemaSqlCatalog.Triggers) migrationBuilder.Sql($"DROP TRIGGER [dbo].[{trigger.Name}];");
        migrationBuilder.DropForeignKey("FK_InventoryReservations_StorefrontOrderLine", "InventoryReservations");
        migrationBuilder.DropForeignKey("FK_InventoryReservations_StorefrontFulfillmentSource", "InventoryReservations");
        migrationBuilder.DropIndex("UX_InventoryReservations_StorefrontLineSource", "InventoryReservations");
        migrationBuilder.DropIndex("IX_InventoryReservations_StoreId_StorefrontOrderId_StorefrontOrderLineId_ProductVariantId", "InventoryReservations");
        migrationBuilder.DropIndex("IX_InventoryReservations_StoreId_StorefrontOrderId_StorefrontFulfillmentSourceId_WarehouseId_SourceLegalEntityId", "InventoryReservations");
        migrationBuilder.DropUniqueConstraint("AK_StorefrontOrderLines_StoreId_StorefrontOrderId_Id_VariantId", "StorefrontOrderLines");
        migrationBuilder.DropUniqueConstraint("AK_StorefrontFulfillmentSources_StoreId_StorefrontOrderId_Id_SourceWarehouseId_SourceLegalEntityId", "StorefrontFulfillmentSources");
        migrationBuilder.DropCheckConstraint("CK_InventoryReservations_DeliverySource", "InventoryReservations");
        migrationBuilder.DropCheckConstraint("CK_InventoryReservations_StorefrontSource", "InventoryReservations");
        migrationBuilder.AddCheckConstraint("CK_InventoryReservations_DeliverySource", "InventoryReservations", StorefrontCheckoutSchemaSqlCatalog.LegacyDeliveryConstraint);
        migrationBuilder.DropCheckConstraint("CK_StorefrontOrderLines_Price", "StorefrontOrderLines");
        migrationBuilder.DropCheckConstraint("CK_StorefrontOrderLines_RoundingAdjustment", "StorefrontOrderLines");
        migrationBuilder.AddCheckConstraint("CK_StorefrontOrderLines_Price", "StorefrontOrderLines", StorefrontCheckoutSchemaSqlCatalog.LegacyLinePriceConstraint);
        migrationBuilder.DropColumn("StorefrontOrderId", "InventoryReservations");
        migrationBuilder.DropColumn("StorefrontOrderLineId", "InventoryReservations");
        migrationBuilder.DropColumn("StorefrontFulfillmentSourceId", "InventoryReservations");
        migrationBuilder.DropColumn("RoundingAdjustment", "StorefrontOrderLines");
        migrationBuilder.AlterColumn<string>("UnitName", "StorefrontOrderLines", type: "nvarchar(100)", maxLength: 100, nullable: false,
            oldClrType: typeof(string), oldType: "nvarchar(200)", oldMaxLength: 200);
    }
}
