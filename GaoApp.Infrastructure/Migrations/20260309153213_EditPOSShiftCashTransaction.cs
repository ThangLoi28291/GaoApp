using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class EditPOSShiftCashTransaction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_POSShiftCashTransactions_StoreId_POSShiftId_CreatedAtUtc",
                table: "POSShiftCashTransactions");

            migrationBuilder.RenameColumn(
                name: "Note",
                table: "POSShifts",
                newName: "OpenNote");

            migrationBuilder.AlterColumn<decimal>(
                name: "OpeningCash",
                table: "POSShifts",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)",
                oldPrecision: 18,
                oldScale: 2);

            migrationBuilder.AlterColumn<decimal>(
                name: "NonCashSalesTotal",
                table: "POSShifts",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)",
                oldPrecision: 18,
                oldScale: 2);

            migrationBuilder.AlterColumn<decimal>(
                name: "ClosingCashExpected",
                table: "POSShifts",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)",
                oldPrecision: 18,
                oldScale: 2);

            migrationBuilder.AlterColumn<decimal>(
                name: "CashSalesTotal",
                table: "POSShifts",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)",
                oldPrecision: 18,
                oldScale: 2);

            migrationBuilder.AlterColumn<decimal>(
                name: "CashOutTotal",
                table: "POSShifts",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)",
                oldPrecision: 18,
                oldScale: 2);

            migrationBuilder.AlterColumn<decimal>(
                name: "CashInTotal",
                table: "POSShifts",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)",
                oldPrecision: 18,
                oldScale: 2);

            migrationBuilder.AddColumn<string>(
                name: "CloseNote",
                table: "POSShifts",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "Type",
                table: "POSShiftCashTransactions",
                type: "int",
                nullable: false,
                oldClrType: typeof(byte),
                oldType: "tinyint");

            migrationBuilder.AlterColumn<string>(
                name: "Note",
                table: "POSShiftCashTransactions",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(300)",
                oldMaxLength: 300,
                oldNullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CreatedByUserId",
                table: "POSShiftCashTransactions",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Reason",
                table: "POSShiftCashTransactions",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_POSShifts_StoreId_ClosedAtUtc",
                table: "POSShifts",
                columns: new[] { "StoreId", "ClosedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_POSShifts_StoreId_OpenedAtUtc",
                table: "POSShifts",
                columns: new[] { "StoreId", "OpenedAtUtc" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_POSShifts_CashInTotal_NonNegative",
                table: "POSShifts",
                sql: "[CashInTotal] >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_POSShifts_CashOutTotal_NonNegative",
                table: "POSShifts",
                sql: "[CashOutTotal] >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_POSShifts_CashSalesTotal_NonNegative",
                table: "POSShifts",
                sql: "[CashSalesTotal] >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_POSShifts_ClosingCashActual_NonNegative",
                table: "POSShifts",
                sql: "[ClosingCashActual] IS NULL OR [ClosingCashActual] >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_POSShifts_ClosingCashExpected_NonNegative",
                table: "POSShifts",
                sql: "[ClosingCashExpected] >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_POSShifts_NonCashSalesTotal_NonNegative",
                table: "POSShifts",
                sql: "[NonCashSalesTotal] >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_POSShifts_OpeningCash_NonNegative",
                table: "POSShifts",
                sql: "[OpeningCash] >= 0");

            migrationBuilder.CreateIndex(
                name: "IX_POSShiftCashTransactions_CreatedAtUtc",
                table: "POSShiftCashTransactions",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_POSShiftCashTransactions_StoreId_POSShiftId",
                table: "POSShiftCashTransactions",
                columns: new[] { "StoreId", "POSShiftId" });

            migrationBuilder.CreateIndex(
                name: "IX_POSShiftCashTransactions_StoreId_Type",
                table: "POSShiftCashTransactions",
                columns: new[] { "StoreId", "Type" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_POSShifts_StoreId_ClosedAtUtc",
                table: "POSShifts");

            migrationBuilder.DropIndex(
                name: "IX_POSShifts_StoreId_OpenedAtUtc",
                table: "POSShifts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_POSShifts_CashInTotal_NonNegative",
                table: "POSShifts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_POSShifts_CashOutTotal_NonNegative",
                table: "POSShifts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_POSShifts_CashSalesTotal_NonNegative",
                table: "POSShifts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_POSShifts_ClosingCashActual_NonNegative",
                table: "POSShifts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_POSShifts_ClosingCashExpected_NonNegative",
                table: "POSShifts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_POSShifts_NonCashSalesTotal_NonNegative",
                table: "POSShifts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_POSShifts_OpeningCash_NonNegative",
                table: "POSShifts");

            migrationBuilder.DropIndex(
                name: "IX_POSShiftCashTransactions_CreatedAtUtc",
                table: "POSShiftCashTransactions");

            migrationBuilder.DropIndex(
                name: "IX_POSShiftCashTransactions_StoreId_POSShiftId",
                table: "POSShiftCashTransactions");

            migrationBuilder.DropIndex(
                name: "IX_POSShiftCashTransactions_StoreId_Type",
                table: "POSShiftCashTransactions");

            migrationBuilder.DropColumn(
                name: "CloseNote",
                table: "POSShifts");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "POSShiftCashTransactions");

            migrationBuilder.DropColumn(
                name: "Reason",
                table: "POSShiftCashTransactions");

            migrationBuilder.RenameColumn(
                name: "OpenNote",
                table: "POSShifts",
                newName: "Note");

            migrationBuilder.AlterColumn<decimal>(
                name: "OpeningCash",
                table: "POSShifts",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)",
                oldPrecision: 18,
                oldScale: 2,
                oldDefaultValue: 0m);

            migrationBuilder.AlterColumn<decimal>(
                name: "NonCashSalesTotal",
                table: "POSShifts",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)",
                oldPrecision: 18,
                oldScale: 2,
                oldDefaultValue: 0m);

            migrationBuilder.AlterColumn<decimal>(
                name: "ClosingCashExpected",
                table: "POSShifts",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)",
                oldPrecision: 18,
                oldScale: 2,
                oldDefaultValue: 0m);

            migrationBuilder.AlterColumn<decimal>(
                name: "CashSalesTotal",
                table: "POSShifts",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)",
                oldPrecision: 18,
                oldScale: 2,
                oldDefaultValue: 0m);

            migrationBuilder.AlterColumn<decimal>(
                name: "CashOutTotal",
                table: "POSShifts",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)",
                oldPrecision: 18,
                oldScale: 2,
                oldDefaultValue: 0m);

            migrationBuilder.AlterColumn<decimal>(
                name: "CashInTotal",
                table: "POSShifts",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)",
                oldPrecision: 18,
                oldScale: 2,
                oldDefaultValue: 0m);

            migrationBuilder.AlterColumn<byte>(
                name: "Type",
                table: "POSShiftCashTransactions",
                type: "tinyint",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AlterColumn<string>(
                name: "Note",
                table: "POSShiftCashTransactions",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(500)",
                oldMaxLength: 500,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_POSShiftCashTransactions_StoreId_POSShiftId_CreatedAtUtc",
                table: "POSShiftCashTransactions",
                columns: new[] { "StoreId", "POSShiftId", "CreatedAtUtc" });
        }
    }
}
