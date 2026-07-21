using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddVoucherRelationToRewardLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_CustomerRewardLedgers_StoreId_VoucherId",
                table: "CustomerRewardLedgers",
                columns: new[] { "StoreId", "VoucherId" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerRewardLedgers_VoucherId",
                table: "CustomerRewardLedgers",
                column: "VoucherId");

            migrationBuilder.AddForeignKey(
                name: "FK_CustomerRewardLedgers_CustomerRewardVouchers_VoucherId",
                table: "CustomerRewardLedgers",
                column: "VoucherId",
                principalTable: "CustomerRewardVouchers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CustomerRewardLedgers_CustomerRewardVouchers_VoucherId",
                table: "CustomerRewardLedgers");

            migrationBuilder.DropIndex(
                name: "IX_CustomerRewardLedgers_StoreId_VoucherId",
                table: "CustomerRewardLedgers");

            migrationBuilder.DropIndex(
                name: "IX_CustomerRewardLedgers_VoucherId",
                table: "CustomerRewardLedgers");
        }
    }
}
