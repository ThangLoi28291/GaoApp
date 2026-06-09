using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderRewardVoucher : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "VoucherDiscountTotal",
                table: "Orders",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "OrderRewardVouchers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    VoucherId = table.Column<int>(type: "int", nullable: false),
                    VoucherValue = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<int>(type: "int", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<int>(type: "int", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedBy = table.Column<int>(type: "int", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    StoreId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderRewardVouchers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderRewardVouchers_CustomerRewardVouchers_VoucherId",
                        column: x => x.VoucherId,
                        principalTable: "CustomerRewardVouchers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OrderRewardVouchers_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OrderRewardVouchers_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrderRewardVouchers_OrderId",
                table: "OrderRewardVouchers",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderRewardVouchers_StoreId_OrderId",
                table: "OrderRewardVouchers",
                columns: new[] { "StoreId", "OrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderRewardVouchers_StoreId_VoucherId",
                table: "OrderRewardVouchers",
                columns: new[] { "StoreId", "VoucherId" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_OrderRewardVouchers_VoucherId",
                table: "OrderRewardVouchers",
                column: "VoucherId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrderRewardVouchers");

            migrationBuilder.DropColumn(
                name: "VoucherDiscountTotal",
                table: "Orders");
        }
    }
}
