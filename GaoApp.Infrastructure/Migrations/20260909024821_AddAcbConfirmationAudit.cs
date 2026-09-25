using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAcbConfirmationAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ConfirmationCallbackReceiptId",
                table: "AcbQrSessions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ConfirmationSource",
                table: "AcbQrSessions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ConfirmedAtUtc",
                table: "AcbQrSessions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ConfirmedByUserId",
                table: "AcbQrSessions",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AcbQrSessions_ConfirmationCallbackReceiptId",
                table: "AcbQrSessions",
                column: "ConfirmationCallbackReceiptId");

            migrationBuilder.AddForeignKey(
                name: "FK_AcbQrSessions_AcbCallbackReceipts_ConfirmationCallbackReceiptId",
                table: "AcbQrSessions",
                column: "ConfirmationCallbackReceiptId",
                principalTable: "AcbCallbackReceipts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AcbQrSessions_AcbCallbackReceipts_ConfirmationCallbackReceiptId",
                table: "AcbQrSessions");

            migrationBuilder.DropIndex(
                name: "IX_AcbQrSessions_ConfirmationCallbackReceiptId",
                table: "AcbQrSessions");

            migrationBuilder.DropColumn(
                name: "ConfirmationCallbackReceiptId",
                table: "AcbQrSessions");

            migrationBuilder.DropColumn(
                name: "ConfirmationSource",
                table: "AcbQrSessions");

            migrationBuilder.DropColumn(
                name: "ConfirmedAtUtc",
                table: "AcbQrSessions");

            migrationBuilder.DropColumn(
                name: "ConfirmedByUserId",
                table: "AcbQrSessions");
        }
    }
}
