using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Fix_SalesReturnPayment_Amount_Precision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SalesReturnPayments_StoreId",
                table: "SalesReturnPayments");

            migrationBuilder.AlterColumn<decimal>(
                name: "NonCashRefundTotal",
                table: "POSShifts",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)");

            migrationBuilder.AlterColumn<decimal>(
                name: "CashRefundTotal",
                table: "POSShifts",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)");

            migrationBuilder.AlterColumn<decimal>(
                name: "AutoDetectedInboundQty",
                table: "OrderInventoryIssueLines",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)");

            migrationBuilder.CreateIndex(
                name: "IX_SalesReturnPayments_StoreId_PaidAtUtc",
                table: "SalesReturnPayments",
                columns: new[] { "StoreId", "PaidAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SalesReturnPayments_StoreId_SalesReturnId",
                table: "SalesReturnPayments",
                columns: new[] { "StoreId", "SalesReturnId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SalesReturnPayments_StoreId_PaidAtUtc",
                table: "SalesReturnPayments");

            migrationBuilder.DropIndex(
                name: "IX_SalesReturnPayments_StoreId_SalesReturnId",
                table: "SalesReturnPayments");

            migrationBuilder.AlterColumn<decimal>(
                name: "NonCashRefundTotal",
                table: "POSShifts",
                type: "decimal(18,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)",
                oldPrecision: 18,
                oldScale: 2,
                oldDefaultValue: 0m);

            migrationBuilder.AlterColumn<decimal>(
                name: "CashRefundTotal",
                table: "POSShifts",
                type: "decimal(18,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)",
                oldPrecision: 18,
                oldScale: 2,
                oldDefaultValue: 0m);

            migrationBuilder.AlterColumn<decimal>(
                name: "AutoDetectedInboundQty",
                table: "OrderInventoryIssueLines",
                type: "decimal(18,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)",
                oldPrecision: 18,
                oldScale: 4,
                oldDefaultValue: 0m);

            migrationBuilder.CreateIndex(
                name: "IX_SalesReturnPayments_StoreId",
                table: "SalesReturnPayments",
                column: "StoreId");
        }
    }
}
