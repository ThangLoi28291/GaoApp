using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPOSShiftCashReceipt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CashReceiptNote",
                table: "POSShifts",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CashReceivedAmount",
                table: "POSShifts",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CashReceivedAtUtc",
                table: "POSShifts",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CashReceivedByUserId",
                table: "POSShifts",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_POSShifts_StoreId_CashReceivedAtUtc",
                table: "POSShifts",
                columns: new[] { "StoreId", "CashReceivedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_POSShifts_StoreId_CashReceivedAtUtc",
                table: "POSShifts");

            migrationBuilder.DropColumn(
                name: "CashReceiptNote",
                table: "POSShifts");

            migrationBuilder.DropColumn(
                name: "CashReceivedAmount",
                table: "POSShifts");

            migrationBuilder.DropColumn(
                name: "CashReceivedAtUtc",
                table: "POSShifts");

            migrationBuilder.DropColumn(
                name: "CashReceivedByUserId",
                table: "POSShifts");
        }
    }
}
