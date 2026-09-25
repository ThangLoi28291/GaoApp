using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260919174500_AllowPurchaseOrderVariantMultipleUnits")]
public sealed class AllowPurchaseOrderVariantMultipleUnits : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "UX_PurchaseOrderLines_Store_Order_ProductVariant",
            table: "PurchaseOrderLines");

        migrationBuilder.CreateIndex(
            name: "UX_PurchaseOrderLines_Store_Order_ProductUnitConversion",
            table: "PurchaseOrderLines",
            columns: new[] { "StoreId", "PurchaseOrderId", "ProductUnitConversionId" },
            unique: true,
            filter: "[IsDeleted] = 0 AND [ProductUnitConversionId] IS NOT NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "UX_PurchaseOrderLines_Store_Order_ProductUnitConversion",
            table: "PurchaseOrderLines");

        migrationBuilder.CreateIndex(
            name: "UX_PurchaseOrderLines_Store_Order_ProductVariant",
            table: "PurchaseOrderLines",
            columns: new[] { "StoreId", "PurchaseOrderId", "ProductVariantId" },
            unique: true,
            filter: "[IsDeleted] = 0 AND [ProductVariantId] IS NOT NULL");
    }
}
