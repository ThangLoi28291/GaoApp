using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOperationsReportsAndTreasury : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DueDate",
                table: "PurchasePayables",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CustomerDebtReceiptId",
                table: "POSShiftCashTransactions",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TreasuryEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClientRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Fund = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    TargetFund = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    OperatingExpenseId = table.Column<int>(type: "int", nullable: true),
                    PurchasePayableId = table.Column<int>(type: "int", nullable: true),
                    POSShiftCashTransactionId = table.Column<int>(type: "int", nullable: true),
                    IsVoucherLink = table.Column<bool>(type: "bit", nullable: false),
                    ReversalOfId = table.Column<int>(type: "int", nullable: true),
                    ReversedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
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
                    table.PrimaryKey("PK_TreasuryEntries", x => x.Id);
                    table.CheckConstraint("CK_TreasuryEntries_Amount", "[Amount] <> 0");
                    table.CheckConstraint("CK_TreasuryEntries_Transfer", "[TargetFund] IS NULL OR (([TargetFund] <> [Fund] OR ([Fund] = 'cash' AND [IsVoucherLink] = 1)) AND [OperatingExpenseId] IS NULL AND [PurchasePayableId] IS NULL)");
                    table.ForeignKey(
                        name: "FK_TreasuryEntries_OperatingExpenses_OperatingExpenseId",
                        column: x => x.OperatingExpenseId,
                        principalTable: "OperatingExpenses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TreasuryEntries_POSShiftCashTransactions_POSShiftCashTransactionId",
                        column: x => x.POSShiftCashTransactionId,
                        principalTable: "POSShiftCashTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TreasuryEntries_PurchasePayables_PurchasePayableId",
                        column: x => x.PurchasePayableId,
                        principalTable: "PurchasePayables",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TreasuryEntries_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TreasuryEntries_TreasuryEntries_ReversalOfId",
                        column: x => x.ReversalOfId,
                        principalTable: "TreasuryEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TreasuryOpeningBalances",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Fund = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    AsOfDate = table.Column<DateTime>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
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
                    table.PrimaryKey("PK_TreasuryOpeningBalances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TreasuryOpeningBalances_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_POSShiftCashTransactions_CustomerDebtReceiptId",
                table: "POSShiftCashTransactions",
                column: "CustomerDebtReceiptId");

            migrationBuilder.CreateIndex(
                name: "IX_TreasuryEntries_OperatingExpenseId",
                table: "TreasuryEntries",
                column: "OperatingExpenseId");

            migrationBuilder.CreateIndex(
                name: "IX_TreasuryEntries_POSShiftCashTransactionId",
                table: "TreasuryEntries",
                column: "POSShiftCashTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_TreasuryEntries_PurchasePayableId",
                table: "TreasuryEntries",
                column: "PurchasePayableId");

            migrationBuilder.CreateIndex(
                name: "IX_TreasuryEntries_ReversalOfId",
                table: "TreasuryEntries",
                column: "ReversalOfId");

            migrationBuilder.CreateIndex(
                name: "IX_TreasuryEntries_StoreId_ClientRequestId",
                table: "TreasuryEntries",
                columns: new[] { "StoreId", "ClientRequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TreasuryEntries_StoreId_OccurredAtUtc",
                table: "TreasuryEntries",
                columns: new[] { "StoreId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_TreasuryEntries_StoreId_POSShiftCashTransactionId",
                table: "TreasuryEntries",
                columns: new[] { "StoreId", "POSShiftCashTransactionId" },
                unique: true,
                filter: "[POSShiftCashTransactionId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TreasuryEntries_StoreId_ReversalOfId",
                table: "TreasuryEntries",
                columns: new[] { "StoreId", "ReversalOfId" },
                unique: true,
                filter: "[ReversalOfId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TreasuryOpeningBalances_StoreId_Fund",
                table: "TreasuryOpeningBalances",
                columns: new[] { "StoreId", "Fund" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_POSShiftCashTransactions_CustomerDebtReceipts_CustomerDebtReceiptId",
                table: "POSShiftCashTransactions",
                column: "CustomerDebtReceiptId",
                principalTable: "CustomerDebtReceipts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_POSShiftCashTransactions_CustomerDebtReceipts_CustomerDebtReceiptId",
                table: "POSShiftCashTransactions");

            migrationBuilder.DropTable(
                name: "TreasuryEntries");

            migrationBuilder.DropTable(
                name: "TreasuryOpeningBalances");

            migrationBuilder.DropIndex(
                name: "IX_POSShiftCashTransactions_CustomerDebtReceiptId",
                table: "POSShiftCashTransactions");

            migrationBuilder.DropColumn(
                name: "DueDate",
                table: "PurchasePayables");

            migrationBuilder.DropColumn(
                name: "CustomerDebtReceiptId",
                table: "POSShiftCashTransactions");
        }
    }
}
