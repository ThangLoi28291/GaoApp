using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerDeposits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CustomerDepositId",
                table: "Orders",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DepositAmount",
                table: "Orders",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "CustomerDeposits",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CustomerId = table.Column<int>(type: "int", nullable: false),
                    Purpose = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ExpectedDeliveryDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Balance = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
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
                    table.PrimaryKey("PK_CustomerDeposits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerDeposits_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerDeposits_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "CustomerDepositEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CustomerDepositId = table.Column<int>(type: "int", nullable: false),
                    OrderId = table.Column<int>(type: "int", nullable: true),
                    POSShiftId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Method = table.Column<int>(type: "int", nullable: true),
                    StoreBankAccountId = table.Column<int>(type: "int", nullable: true),
                    Reference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ClientRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RequestJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
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
                    table.PrimaryKey("PK_CustomerDepositEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerDepositEntries_CustomerDeposits_CustomerDepositId",
                        column: x => x.CustomerDepositId,
                        principalTable: "CustomerDeposits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerDepositEntries_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerDepositEntries_POSShifts_POSShiftId",
                        column: x => x.POSShiftId,
                        principalTable: "POSShifts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerDepositEntries_StoreBankAccounts_StoreBankAccountId",
                        column: x => x.StoreBankAccountId,
                        principalTable: "StoreBankAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerDepositEntries_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_CustomerDepositId",
                table: "Orders",
                column: "CustomerDepositId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerDepositEntries_CustomerDepositId",
                table: "CustomerDepositEntries",
                column: "CustomerDepositId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerDepositEntries_OrderId",
                table: "CustomerDepositEntries",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerDepositEntries_POSShiftId",
                table: "CustomerDepositEntries",
                column: "POSShiftId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerDepositEntries_StoreBankAccountId",
                table: "CustomerDepositEntries",
                column: "StoreBankAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerDepositEntries_StoreId_ClientRequestId",
                table: "CustomerDepositEntries",
                columns: new[] { "StoreId", "ClientRequestId" },
                unique: true,
                filter: "[ClientRequestId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerDepositEntries_StoreId_CustomerDepositId_Id",
                table: "CustomerDepositEntries",
                columns: new[] { "StoreId", "CustomerDepositId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerDepositEntries_StoreId_OrderId_Kind",
                table: "CustomerDepositEntries",
                columns: new[] { "StoreId", "OrderId", "Kind" },
                unique: true,
                filter: "[OrderId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerDeposits_CustomerId",
                table: "CustomerDeposits",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerDeposits_StoreId_CustomerId",
                table: "CustomerDeposits",
                columns: new[] { "StoreId", "CustomerId" });

            migrationBuilder.AddForeignKey(
                name: "FK_Orders_CustomerDeposits_CustomerDepositId",
                table: "Orders",
                column: "CustomerDepositId",
                principalTable: "CustomerDeposits",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Orders_CustomerDeposits_CustomerDepositId",
                table: "Orders");

            migrationBuilder.DropTable(
                name: "CustomerDepositEntries");

            migrationBuilder.DropTable(
                name: "CustomerDeposits");

            migrationBuilder.DropIndex(
                name: "IX_Orders_CustomerDepositId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "CustomerDepositId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "DepositAmount",
                table: "Orders");
        }
    }
}
