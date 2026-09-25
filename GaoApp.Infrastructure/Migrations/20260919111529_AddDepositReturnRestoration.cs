using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDepositReturnRestoration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CustomerDepositEntries_StoreId_OrderId_Kind",
                table: "CustomerDepositEntries");

            migrationBuilder.AddColumn<decimal>(
                name: "DepositRestoredTotal",
                table: "SalesReturns",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "SalesReturnId",
                table: "CustomerDepositEntries",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerDepositEntries_SalesReturnId",
                table: "CustomerDepositEntries",
                column: "SalesReturnId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerDepositEntries_StoreId_OrderId_Kind",
                table: "CustomerDepositEntries",
                columns: new[] { "StoreId", "OrderId", "Kind" },
                unique: true,
                filter: "[OrderId] IS NOT NULL AND [Kind] IN ('Apply','Void')");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerDepositEntries_StoreId_SalesReturnId",
                table: "CustomerDepositEntries",
                columns: new[] { "StoreId", "SalesReturnId" },
                unique: true,
                filter: "[SalesReturnId] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_CustomerDepositEntries_SalesReturns_SalesReturnId",
                table: "CustomerDepositEntries",
                column: "SalesReturnId",
                principalTable: "SalesReturns",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CustomerDepositEntries_SalesReturns_SalesReturnId",
                table: "CustomerDepositEntries");

            migrationBuilder.DropIndex(
                name: "IX_CustomerDepositEntries_SalesReturnId",
                table: "CustomerDepositEntries");

            migrationBuilder.DropIndex(
                name: "IX_CustomerDepositEntries_StoreId_OrderId_Kind",
                table: "CustomerDepositEntries");

            migrationBuilder.DropIndex(
                name: "IX_CustomerDepositEntries_StoreId_SalesReturnId",
                table: "CustomerDepositEntries");

            migrationBuilder.DropColumn(
                name: "DepositRestoredTotal",
                table: "SalesReturns");

            migrationBuilder.DropColumn(
                name: "SalesReturnId",
                table: "CustomerDepositEntries");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerDepositEntries_StoreId_OrderId_Kind",
                table: "CustomerDepositEntries",
                columns: new[] { "StoreId", "OrderId", "Kind" },
                unique: true,
                filter: "[OrderId] IS NOT NULL");
        }
    }
}
