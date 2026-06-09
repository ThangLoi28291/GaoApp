using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPOSShiftCashDenominations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "POSShiftCashDenominations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    POSShiftId = table.Column<int>(type: "int", nullable: false),
                    EntryType = table.Column<byte>(type: "tinyint", nullable: false),
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
                    table.PrimaryKey("PK_POSShiftCashDenominations", x => x.Id);
                    table.CheckConstraint("CK_POSShiftCashDenominations_Amount_NonNegative", "[Amount] >= 0");
                    table.CheckConstraint("CK_POSShiftCashDenominations_DenominationValue_NonNegative", "[DenominationValue] >= 0");
                    table.CheckConstraint("CK_POSShiftCashDenominations_Quantity_NonNegative", "[Quantity] >= 0");
                    table.ForeignKey(
                        name: "FK_POSShiftCashDenominations_POSShifts_POSShiftId",
                        column: x => x.POSShiftId,
                        principalTable: "POSShifts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_POSShiftCashDenominations_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_POSShiftCashDenominations_POSShiftId",
                table: "POSShiftCashDenominations",
                column: "POSShiftId");

            migrationBuilder.CreateIndex(
                name: "IX_POSShiftCashDenominations_StoreId_POSShiftId_EntryType",
                table: "POSShiftCashDenominations",
                columns: new[] { "StoreId", "POSShiftId", "EntryType" });

            migrationBuilder.CreateIndex(
                name: "IX_POSShiftCashDenominations_StoreId_POSShiftId_EntryType_DenominationValue",
                table: "POSShiftCashDenominations",
                columns: new[] { "StoreId", "POSShiftId", "EntryType", "DenominationValue" },
                unique: true,
                filter: "[IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "POSShiftCashDenominations");
        }
    }
}
