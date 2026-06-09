using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPOSShiftClosingSlips : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "POSShiftClosingSlips",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    POSShiftId = table.Column<int>(type: "int", nullable: false),
                    SlipCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    BarcodeValue = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    OpenedByUserId = table.Column<int>(type: "int", nullable: false),
                    ClosedByUserId = table.Column<int>(type: "int", nullable: false),
                    OpenedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ClosedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    OpeningCash = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CashSalesTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    NonCashSalesTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CashRefundTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    NonCashRefundTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    RefundCount = table.Column<int>(type: "int", nullable: false),
                    VoidCount = table.Column<int>(type: "int", nullable: false),
                    CashInTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CashOutTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ClosingCashExpected = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ClosingCashActual = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CashDifference = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CloseNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    PrintedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
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
                    table.PrimaryKey("PK_POSShiftClosingSlips", x => x.Id);
                    table.ForeignKey(
                        name: "FK_POSShiftClosingSlips_POSShifts_POSShiftId",
                        column: x => x.POSShiftId,
                        principalTable: "POSShifts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_POSShiftClosingSlips_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "POSShiftClosingSlipDenominations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    POSShiftClosingSlipId = table.Column<int>(type: "int", nullable: false),
                    DenominationValue = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
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
                    table.PrimaryKey("PK_POSShiftClosingSlipDenominations", x => x.Id);
                    table.CheckConstraint("CK_POSShiftClosingSlipDenominations_Amount_NonNegative", "[Amount] >= 0");
                    table.CheckConstraint("CK_POSShiftClosingSlipDenominations_DenominationValue_NonNegative", "[DenominationValue] >= 0");
                    table.CheckConstraint("CK_POSShiftClosingSlipDenominations_Quantity_NonNegative", "[Quantity] >= 0");
                    table.ForeignKey(
                        name: "FK_POSShiftClosingSlipDenominations_POSShiftClosingSlips_POSShiftClosingSlipId",
                        column: x => x.POSShiftClosingSlipId,
                        principalTable: "POSShiftClosingSlips",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_POSShiftClosingSlipDenominations_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_POSShiftClosingSlipDenominations_POSShiftClosingSlipId",
                table: "POSShiftClosingSlipDenominations",
                column: "POSShiftClosingSlipId");

            migrationBuilder.CreateIndex(
                name: "IX_POSShiftClosingSlipDenominations_StoreId_POSShiftClosingSlipId_DenominationValue",
                table: "POSShiftClosingSlipDenominations",
                columns: new[] { "StoreId", "POSShiftClosingSlipId", "DenominationValue" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_POSShiftClosingSlips_POSShiftId",
                table: "POSShiftClosingSlips",
                column: "POSShiftId");

            migrationBuilder.CreateIndex(
                name: "IX_POSShiftClosingSlips_StoreId_BarcodeValue",
                table: "POSShiftClosingSlips",
                columns: new[] { "StoreId", "BarcodeValue" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_POSShiftClosingSlips_StoreId_POSShiftId",
                table: "POSShiftClosingSlips",
                columns: new[] { "StoreId", "POSShiftId" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_POSShiftClosingSlips_StoreId_SlipCode",
                table: "POSShiftClosingSlips",
                columns: new[] { "StoreId", "SlipCode" },
                unique: true,
                filter: "[IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "POSShiftClosingSlipDenominations");

            migrationBuilder.DropTable(
                name: "POSShiftClosingSlips");
        }
    }
}
