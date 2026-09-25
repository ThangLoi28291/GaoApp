using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPosQrInstallmentLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ClientRequestId",
                table: "PosPaymentQrRequests",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PaymentId",
                table: "PosPaymentQrRequests",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PrintClaimedAtUtc",
                table: "PosPaymentQrRequests",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PosPaymentQrRequests_PaymentId",
                table: "PosPaymentQrRequests",
                column: "PaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_PosPaymentQrRequests_StoreId_OrderId_ClientRequestId",
                table: "PosPaymentQrRequests",
                columns: new[] { "StoreId", "OrderId", "ClientRequestId" },
                unique: true,
                filter: "[ClientRequestId] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_PosPaymentQrRequests_OrderPayments_PaymentId",
                table: "PosPaymentQrRequests",
                column: "PaymentId",
                principalTable: "OrderPayments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PosPaymentQrRequests_OrderPayments_PaymentId",
                table: "PosPaymentQrRequests");

            migrationBuilder.DropIndex(
                name: "IX_PosPaymentQrRequests_PaymentId",
                table: "PosPaymentQrRequests");

            migrationBuilder.DropIndex(
                name: "IX_PosPaymentQrRequests_StoreId_OrderId_ClientRequestId",
                table: "PosPaymentQrRequests");

            migrationBuilder.DropColumn(
                name: "ClientRequestId",
                table: "PosPaymentQrRequests");

            migrationBuilder.DropColumn(
                name: "PaymentId",
                table: "PosPaymentQrRequests");

            migrationBuilder.DropColumn(
                name: "PrintClaimedAtUtc",
                table: "PosPaymentQrRequests");
        }
    }
}
