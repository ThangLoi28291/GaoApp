using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FixOrderRewardVoucherIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OrderRewardVouchers_StoreId_VoucherId",
                table: "OrderRewardVouchers");

            migrationBuilder.CreateIndex(
                name: "IX_OrderRewardVouchers_StoreId_VoucherId",
                table: "OrderRewardVouchers",
                columns: new[] { "StoreId", "VoucherId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OrderRewardVouchers_StoreId_VoucherId",
                table: "OrderRewardVouchers");

            migrationBuilder.CreateIndex(
                name: "IX_OrderRewardVouchers_StoreId_VoucherId",
                table: "OrderRewardVouchers",
                columns: new[] { "StoreId", "VoucherId" },
                unique: true,
                filter: "[IsDeleted] = 0");
        }
    }
}
