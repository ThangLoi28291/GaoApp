using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260919173000_MakePurchaseOrderSupplierOptional")]
public sealed class MakePurchaseOrderSupplierOptional : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_PurchaseOrders_Suppliers_SupplierId",
            table: "PurchaseOrders");

        migrationBuilder.AlterColumn<int>(
            name: "SupplierId",
            table: "PurchaseOrders",
            type: "int",
            nullable: true,
            oldClrType: typeof(int),
            oldType: "int");

        migrationBuilder.AddForeignKey(
            name: "FK_PurchaseOrders_Suppliers_SupplierId",
            table: "PurchaseOrders",
            column: "SupplierId",
            principalTable: "Suppliers",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF EXISTS (SELECT 1 FROM [PurchaseOrders] WHERE [SupplierId] IS NULL)
                THROW 51000, N'Không thể quay lui: còn đơn đặt hàng chưa chọn nhà cung cấp.', 1;
            """);

        migrationBuilder.DropForeignKey(
            name: "FK_PurchaseOrders_Suppliers_SupplierId",
            table: "PurchaseOrders");

        migrationBuilder.AlterColumn<int>(
            name: "SupplierId",
            table: "PurchaseOrders",
            type: "int",
            nullable: false,
            oldClrType: typeof(int),
            oldType: "int",
            oldNullable: true);

        migrationBuilder.AddForeignKey(
            name: "FK_PurchaseOrders_Suppliers_SupplierId",
            table: "PurchaseOrders",
            column: "SupplierId",
            principalTable: "Suppliers",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);
    }
}
