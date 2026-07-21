using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPOSShiftHandoverSlips : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "POSShiftHandoverSlips",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SlipCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    BarcodeValue = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    TerminalId = table.Column<int>(type: "int", nullable: true),
                    WarehouseId = table.Column<int>(type: "int", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: false),
                    AssignedToUserId = table.Column<int>(type: "int", nullable: true),
                    OpeningCashTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false, defaultValue: 0m),
                    UsedPOSShiftId = table.Column<int>(type: "int", nullable: true),
                    PrintedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UsedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UsedByUserId = table.Column<int>(type: "int", nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelledByUserId = table.Column<int>(type: "int", nullable: true),
                    CancelReason = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
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
                    table.PrimaryKey("PK_POSShiftHandoverSlips", x => x.Id);
                    table.CheckConstraint("CK_POSShiftHandoverSlips_OpeningCashTotal_NonNegative", "[OpeningCashTotal] >= 0");
                    table.ForeignKey(
                        name: "FK_POSShiftHandoverSlips_POSShifts_UsedPOSShiftId",
                        column: x => x.UsedPOSShiftId,
                        principalTable: "POSShifts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_POSShiftHandoverSlips_POSTerminals_TerminalId",
                        column: x => x.TerminalId,
                        principalTable: "POSTerminals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_POSShiftHandoverSlips_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_POSShiftHandoverSlips_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "POSShiftHandoverSlipDenominations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    POSShiftHandoverSlipId = table.Column<int>(type: "int", nullable: false),
                    DenominationValue = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false, defaultValue: 0m),
                    Note = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
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
                    table.PrimaryKey("PK_POSShiftHandoverSlipDenominations", x => x.Id);
                    table.CheckConstraint("CK_POSShiftHandoverSlipDenominations_Amount_NonNegative", "[Amount] >= 0");
                    table.CheckConstraint("CK_POSShiftHandoverSlipDenominations_DenominationValue_NonNegative", "[DenominationValue] >= 0");
                    table.CheckConstraint("CK_POSShiftHandoverSlipDenominations_Quantity_NonNegative", "[Quantity] >= 0");
                    table.ForeignKey(
                        name: "FK_POSShiftHandoverSlipDenominations_POSShiftHandoverSlips_POSShiftHandoverSlipId",
                        column: x => x.POSShiftHandoverSlipId,
                        principalTable: "POSShiftHandoverSlips",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_POSShiftHandoverSlipDenominations_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_POSShiftHandoverSlipDenominations_POSShiftHandoverSlipId",
                table: "POSShiftHandoverSlipDenominations",
                column: "POSShiftHandoverSlipId");

            migrationBuilder.CreateIndex(
                name: "IX_POSShiftHandoverSlipDenominations_StoreId_POSShiftHandoverSlipId",
                table: "POSShiftHandoverSlipDenominations",
                columns: new[] { "StoreId", "POSShiftHandoverSlipId" });

            migrationBuilder.CreateIndex(
                name: "IX_POSShiftHandoverSlipDenominations_StoreId_POSShiftHandoverSlipId_DenominationValue",
                table: "POSShiftHandoverSlipDenominations",
                columns: new[] { "StoreId", "POSShiftHandoverSlipId", "DenominationValue" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_POSShiftHandoverSlips_StoreId_BarcodeValue",
                table: "POSShiftHandoverSlips",
                columns: new[] { "StoreId", "BarcodeValue" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_POSShiftHandoverSlips_StoreId_CreatedAtUtc",
                table: "POSShiftHandoverSlips",
                columns: new[] { "StoreId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_POSShiftHandoverSlips_StoreId_SlipCode",
                table: "POSShiftHandoverSlips",
                columns: new[] { "StoreId", "SlipCode" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_POSShiftHandoverSlips_StoreId_Status",
                table: "POSShiftHandoverSlips",
                columns: new[] { "StoreId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_POSShiftHandoverSlips_StoreId_TerminalId",
                table: "POSShiftHandoverSlips",
                columns: new[] { "StoreId", "TerminalId" });

            migrationBuilder.CreateIndex(
                name: "IX_POSShiftHandoverSlips_StoreId_WarehouseId",
                table: "POSShiftHandoverSlips",
                columns: new[] { "StoreId", "WarehouseId" });

            migrationBuilder.CreateIndex(
                name: "IX_POSShiftHandoverSlips_TerminalId",
                table: "POSShiftHandoverSlips",
                column: "TerminalId");

            migrationBuilder.CreateIndex(
                name: "IX_POSShiftHandoverSlips_UsedPOSShiftId",
                table: "POSShiftHandoverSlips",
                column: "UsedPOSShiftId");

            migrationBuilder.CreateIndex(
                name: "IX_POSShiftHandoverSlips_WarehouseId",
                table: "POSShiftHandoverSlips",
                column: "WarehouseId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "POSShiftHandoverSlipDenominations");

            migrationBuilder.DropTable(
                name: "POSShiftHandoverSlips");
        }
    }
}
