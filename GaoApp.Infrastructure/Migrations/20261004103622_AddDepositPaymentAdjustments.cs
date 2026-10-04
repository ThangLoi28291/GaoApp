using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDepositPaymentAdjustments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_POSPaymentAdjustmentRequests_StoreId_OrderId",
                table: "POSPaymentAdjustmentRequests");

            migrationBuilder.AddColumn<int>(
                name: "CustomerDepositEntryId",
                table: "POSShiftCashTransactions",
                type: "int",
                nullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "PaymentId",
                table: "POSPaymentAdjustmentRequests",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AlterColumn<int>(
                name: "OrderId",
                table: "POSPaymentAdjustmentRequests",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<int>(
                name: "CashTransactionId",
                table: "POSPaymentAdjustmentRequests",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CashTransactionVersion",
                table: "POSPaymentAdjustmentRequests",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DepositEntryId",
                table: "POSPaymentAdjustmentRequests",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "NewStoreBankAccountId",
                table: "POSPaymentAdjustmentRequests",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OldStoreBankAccountId",
                table: "POSPaymentAdjustmentRequests",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_POSShiftCashTransactions_CustomerDepositEntryId",
                table: "POSShiftCashTransactions",
                column: "CustomerDepositEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_POSShiftCashTransactions_StoreId_CustomerDepositEntryId",
                table: "POSShiftCashTransactions",
                columns: new[] { "StoreId", "CustomerDepositEntryId" },
                unique: true,
                filter: "[CustomerDepositEntryId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_POSPaymentAdjustmentRequests_DepositEntryId",
                table: "POSPaymentAdjustmentRequests",
                column: "DepositEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_POSPaymentAdjustmentRequests_StoreId_DepositEntryId",
                table: "POSPaymentAdjustmentRequests",
                columns: new[] { "StoreId", "DepositEntryId" },
                unique: true,
                filter: "[DepositEntryId] IS NOT NULL AND [Status] = 0 AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_POSPaymentAdjustmentRequests_StoreId_OrderId",
                table: "POSPaymentAdjustmentRequests",
                columns: new[] { "StoreId", "OrderId" },
                unique: true,
                filter: "[OrderId] IS NOT NULL AND [Status] = 0 AND [IsDeleted] = 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_POSPaymentAdjustmentRequests_Target",
                table: "POSPaymentAdjustmentRequests",
                sql: "([DepositEntryId] IS NOT NULL AND [PaymentId] IS NULL AND [OrderId] IS NULL) OR ([DepositEntryId] IS NULL AND [PaymentId] IS NOT NULL AND [OrderId] IS NOT NULL)");

            migrationBuilder.AddForeignKey(
                name: "FK_POSPaymentAdjustmentRequests_CustomerDepositEntries_DepositEntryId",
                table: "POSPaymentAdjustmentRequests",
                column: "DepositEntryId",
                principalTable: "CustomerDepositEntries",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_POSShiftCashTransactions_CustomerDepositEntries_CustomerDepositEntryId",
                table: "POSShiftCashTransactions",
                column: "CustomerDepositEntryId",
                principalTable: "CustomerDepositEntries",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM [POSPaymentAdjustmentRequests] WHERE [DepositEntryId] IS NOT NULL) THROW 51000, 'Deposit correction history exists. Preserve it before reverting this migration.', 1;");
            migrationBuilder.DropForeignKey(
                name: "FK_POSPaymentAdjustmentRequests_CustomerDepositEntries_DepositEntryId",
                table: "POSPaymentAdjustmentRequests");

            migrationBuilder.DropForeignKey(
                name: "FK_POSShiftCashTransactions_CustomerDepositEntries_CustomerDepositEntryId",
                table: "POSShiftCashTransactions");

            migrationBuilder.DropIndex(
                name: "IX_POSShiftCashTransactions_CustomerDepositEntryId",
                table: "POSShiftCashTransactions");

            migrationBuilder.DropIndex(
                name: "IX_POSShiftCashTransactions_StoreId_CustomerDepositEntryId",
                table: "POSShiftCashTransactions");

            migrationBuilder.DropIndex(
                name: "IX_POSPaymentAdjustmentRequests_DepositEntryId",
                table: "POSPaymentAdjustmentRequests");

            migrationBuilder.DropIndex(
                name: "IX_POSPaymentAdjustmentRequests_StoreId_DepositEntryId",
                table: "POSPaymentAdjustmentRequests");

            migrationBuilder.DropIndex(
                name: "IX_POSPaymentAdjustmentRequests_StoreId_OrderId",
                table: "POSPaymentAdjustmentRequests");

            migrationBuilder.DropCheckConstraint(
                name: "CK_POSPaymentAdjustmentRequests_Target",
                table: "POSPaymentAdjustmentRequests");

            migrationBuilder.DropColumn(
                name: "CustomerDepositEntryId",
                table: "POSShiftCashTransactions");

            migrationBuilder.DropColumn(
                name: "CashTransactionId",
                table: "POSPaymentAdjustmentRequests");

            migrationBuilder.DropColumn(
                name: "CashTransactionVersion",
                table: "POSPaymentAdjustmentRequests");

            migrationBuilder.DropColumn(
                name: "DepositEntryId",
                table: "POSPaymentAdjustmentRequests");

            migrationBuilder.DropColumn(
                name: "NewStoreBankAccountId",
                table: "POSPaymentAdjustmentRequests");

            migrationBuilder.DropColumn(
                name: "OldStoreBankAccountId",
                table: "POSPaymentAdjustmentRequests");

            migrationBuilder.AlterColumn<int>(
                name: "PaymentId",
                table: "POSPaymentAdjustmentRequests",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "OrderId",
                table: "POSPaymentAdjustmentRequests",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_POSPaymentAdjustmentRequests_StoreId_OrderId",
                table: "POSPaymentAdjustmentRequests",
                columns: new[] { "StoreId", "OrderId" },
                unique: true,
                filter: "[Status] = 0 AND [IsDeleted] = 0");
        }
    }
}
