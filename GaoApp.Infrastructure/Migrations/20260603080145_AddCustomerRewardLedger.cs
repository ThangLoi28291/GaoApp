using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerRewardLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CustomerRewardLedgers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CustomerId = table.Column<int>(type: "int", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    OrderId = table.Column<int>(type: "int", nullable: true),
                    SalesReturnId = table.Column<int>(type: "int", nullable: true),
                    VoucherId = table.Column<int>(type: "int", nullable: true),
                    ReferenceCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
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
                    table.PrimaryKey("PK_CustomerRewardLedgers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerRewardLedgers_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerRewardLedgers_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerRewardLedgers_SalesReturns_SalesReturnId",
                        column: x => x.SalesReturnId,
                        principalTable: "SalesReturns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerRewardLedgers_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerRewardLedgers_CustomerId",
                table: "CustomerRewardLedgers",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerRewardLedgers_OrderId",
                table: "CustomerRewardLedgers",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerRewardLedgers_SalesReturnId",
                table: "CustomerRewardLedgers",
                column: "SalesReturnId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerRewardLedgers_StoreId_CustomerId_CreatedAtUtc",
                table: "CustomerRewardLedgers",
                columns: new[] { "StoreId", "CustomerId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerRewardLedgers_StoreId_OrderId",
                table: "CustomerRewardLedgers",
                columns: new[] { "StoreId", "OrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerRewardLedgers_StoreId_ReferenceCode",
                table: "CustomerRewardLedgers",
                columns: new[] { "StoreId", "ReferenceCode" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerRewardLedgers_StoreId_SalesReturnId",
                table: "CustomerRewardLedgers",
                columns: new[] { "StoreId", "SalesReturnId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CustomerRewardLedgers");
        }
    }
}
