using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Add_Order_POSShiftresume2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_OrderLines_Orders_OrderId",
                table: "OrderLines");

            migrationBuilder.DropForeignKey(
                name: "FK_OrderPayments_Orders_OrderId",
                table: "OrderPayments");

            migrationBuilder.DropForeignKey(
                name: "FK_Orders_Customers_CustomerId",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_POSShifts_StoreId_Status",
                table: "POSShifts");

            migrationBuilder.Sql(@"
IF EXISTS (
    SELECT 1
    FROM sys.indexes i
    JOIN sys.tables t ON i.object_id = t.object_id
    WHERE i.name = 'IX_Orders_CompletedAtUtc' AND t.name = 'Orders'
)
BEGIN
    DROP INDEX [IX_Orders_CompletedAtUtc] ON [Orders];
END
");

            migrationBuilder.DropIndex(
                name: "IX_Orders_StoreId_OrderNumber",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_Orders_StoreId_POSShiftId",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_MediaAssets_StoreId_TempToken",
                table: "MediaAssets");

            migrationBuilder.AlterColumn<byte>(
                name: "Status",
                table: "POSShifts",
                type: "tinyint",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldDefaultValue: 1);

            migrationBuilder.AlterColumn<byte>(
                name: "Status",
                table: "Orders",
                type: "tinyint",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AlterColumn<byte>(
                name: "PaymentStatus",
                table: "Orders",
                type: "tinyint",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.CreateIndex(
                name: "IX_POSShifts_StoreId_ShiftCode",
                table: "POSShifts",
                columns: new[] { "StoreId", "ShiftCode" },
                unique: true,
                filter: "[ShiftCode] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_POSShifts_StoreId_Status",
                table: "POSShifts",
                columns: new[] { "StoreId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_StoreId_CompletedAtUtc",
                table: "Orders",
                columns: new[] { "StoreId", "CompletedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_StoreId_OrderNumber",
                table: "Orders",
                columns: new[] { "StoreId", "OrderNumber" },
                unique: true,
                filter: "[OrderNumber] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_StoreId_POSShiftId_Status",
                table: "Orders",
                columns: new[] { "StoreId", "POSShiftId", "Status" });

            migrationBuilder.AddForeignKey(
                name: "FK_OrderLines_Orders_OrderId",
                table: "OrderLines",
                column: "OrderId",
                principalTable: "Orders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_OrderPayments_Orders_OrderId",
                table: "OrderPayments",
                column: "OrderId",
                principalTable: "Orders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Orders_Customers_CustomerId",
                table: "Orders",
                column: "CustomerId",
                principalTable: "Customers",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_OrderLines_Orders_OrderId",
                table: "OrderLines");

            migrationBuilder.DropForeignKey(
                name: "FK_OrderPayments_Orders_OrderId",
                table: "OrderPayments");

            migrationBuilder.DropForeignKey(
                name: "FK_Orders_Customers_CustomerId",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_POSShifts_StoreId_ShiftCode",
                table: "POSShifts");

            migrationBuilder.DropIndex(
                name: "IX_POSShifts_StoreId_Status",
                table: "POSShifts");

            migrationBuilder.DropIndex(
                name: "IX_Orders_StoreId_CompletedAtUtc",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_Orders_StoreId_OrderNumber",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_Orders_StoreId_POSShiftId_Status",
                table: "Orders");

            migrationBuilder.AlterColumn<int>(
                name: "Status",
                table: "POSShifts",
                type: "int",
                nullable: false,
                defaultValue: 1,
                oldClrType: typeof(byte),
                oldType: "tinyint");

            migrationBuilder.AlterColumn<int>(
                name: "Status",
                table: "Orders",
                type: "int",
                nullable: false,
                oldClrType: typeof(byte),
                oldType: "tinyint");

            migrationBuilder.AlterColumn<int>(
                name: "PaymentStatus",
                table: "Orders",
                type: "int",
                nullable: false,
                oldClrType: typeof(byte),
                oldType: "tinyint");

            migrationBuilder.CreateIndex(
                name: "IX_POSShifts_StoreId_Status",
                table: "POSShifts",
                columns: new[] { "StoreId", "Status" },
                unique: true,
                filter: "[Status] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_CompletedAtUtc",
                table: "Orders",
                column: "CompletedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_StoreId_OrderNumber",
                table: "Orders",
                columns: new[] { "StoreId", "OrderNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_StoreId_POSShiftId",
                table: "Orders",
                columns: new[] { "StoreId", "POSShiftId" });

            migrationBuilder.CreateIndex(
                name: "IX_MediaAssets_StoreId_TempToken",
                table: "MediaAssets",
                columns: new[] { "StoreId", "TempToken" },
                unique: true,
                filter: "[TempToken] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_OrderLines_Orders_OrderId",
                table: "OrderLines",
                column: "OrderId",
                principalTable: "Orders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_OrderPayments_Orders_OrderId",
                table: "OrderPayments",
                column: "OrderId",
                principalTable: "Orders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Orders_Customers_CustomerId",
                table: "Orders",
                column: "CustomerId",
                principalTable: "Customers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
