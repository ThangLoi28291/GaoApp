using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerReceivables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CreditDueDate",
                table: "Orders",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CreditInitialBalance",
                table: "Orders",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "CreditNote",
                table: "Orders",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CreditRequestId",
                table: "Orders",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsCreditSale",
                table: "Orders",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsDebtCollection",
                table: "OrderPayments",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "CustomerDebtReceipts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClientRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerId = table.Column<int>(type: "int", nullable: false),
                    POSShiftId = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Method = table.Column<int>(type: "int", nullable: false),
                    StoreBankAccountId = table.Column<int>(type: "int", nullable: true),
                    Reference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RequestJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
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
                    table.PrimaryKey("PK_CustomerDebtReceipts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerDebtReceipts_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerDebtReceipts_POSShifts_POSShiftId",
                        column: x => x.POSShiftId,
                        principalTable: "POSShifts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerDebtReceipts_StoreBankAccounts_StoreBankAccountId",
                        column: x => x.StoreBankAccountId,
                        principalTable: "StoreBankAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerDebtReceipts_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "CustomerReceivableEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CustomerId = table.Column<int>(type: "int", nullable: false),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    ReceiptId = table.Column<int>(type: "int", nullable: true),
                    SalesReturnId = table.Column<int>(type: "int", nullable: true),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
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
                    table.PrimaryKey("PK_CustomerReceivableEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerReceivableEntries_CustomerDebtReceipts_ReceiptId",
                        column: x => x.ReceiptId,
                        principalTable: "CustomerDebtReceipts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerReceivableEntries_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerReceivableEntries_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerReceivableEntries_SalesReturns_SalesReturnId",
                        column: x => x.SalesReturnId,
                        principalTable: "SalesReturns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerReceivableEntries_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerDebtReceipts_CustomerId",
                table: "CustomerDebtReceipts",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerDebtReceipts_POSShiftId",
                table: "CustomerDebtReceipts",
                column: "POSShiftId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerDebtReceipts_StoreBankAccountId",
                table: "CustomerDebtReceipts",
                column: "StoreBankAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerDebtReceipts_StoreId_ClientRequestId",
                table: "CustomerDebtReceipts",
                columns: new[] { "StoreId", "ClientRequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerDebtReceipts_StoreId_CustomerId_Id",
                table: "CustomerDebtReceipts",
                columns: new[] { "StoreId", "CustomerId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerReceivableEntries_CustomerId",
                table: "CustomerReceivableEntries",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerReceivableEntries_OrderId",
                table: "CustomerReceivableEntries",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerReceivableEntries_ReceiptId",
                table: "CustomerReceivableEntries",
                column: "ReceiptId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerReceivableEntries_SalesReturnId",
                table: "CustomerReceivableEntries",
                column: "SalesReturnId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerReceivableEntries_StoreId_CustomerId_Id",
                table: "CustomerReceivableEntries",
                columns: new[] { "StoreId", "CustomerId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerReceivableEntries_StoreId_OrderId_Kind",
                table: "CustomerReceivableEntries",
                columns: new[] { "StoreId", "OrderId", "Kind" },
                unique: true,
                filter: "[Kind] IN ('Sale','Void')");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerReceivableEntries_StoreId_OrderId_ReceiptId",
                table: "CustomerReceivableEntries",
                columns: new[] { "StoreId", "OrderId", "ReceiptId" },
                unique: true,
                filter: "[ReceiptId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerReceivableEntries_StoreId_SalesReturnId",
                table: "CustomerReceivableEntries",
                columns: new[] { "StoreId", "SalesReturnId" },
                unique: true,
                filter: "[SalesReturnId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CustomerReceivableEntries");

            migrationBuilder.DropTable(
                name: "CustomerDebtReceipts");

            migrationBuilder.DropColumn(
                name: "CreditDueDate",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "CreditInitialBalance",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "CreditNote",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "CreditRequestId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "IsCreditSale",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "IsDebtCollection",
                table: "OrderPayments");
        }
    }
}
