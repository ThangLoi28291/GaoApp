using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddKioskStations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "KioskStations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TerminalId = table.Column<int>(type: "int", nullable: false),
                    WarehouseId = table.Column<int>(type: "int", nullable: false),
                    SystemUserId = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsPaused = table.Column<bool>(type: "bit", nullable: false),
                    ActivationHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ActivationExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeviceHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    LastSeenAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    HelpRequestedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    OrderId = table.Column<int>(type: "int", nullable: true),
                    SessionKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    LastCommandId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LastCommandHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    CheckoutKey = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CartTouchedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CustomerId = table.Column<int>(type: "int", nullable: true),
                    CustomerExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
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
                    table.PrimaryKey("PK_KioskStations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_KioskStations_POSTerminals_TerminalId",
                        column: x => x.TerminalId,
                        principalTable: "POSTerminals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_KioskStations_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_KioskStations_Users_SystemUserId",
                        column: x => x.SystemUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_KioskStations_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_KioskStations_ActivationHash",
                table: "KioskStations",
                column: "ActivationHash",
                unique: true,
                filter: "[ActivationHash] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_KioskStations_DeviceHash",
                table: "KioskStations",
                column: "DeviceHash",
                unique: true,
                filter: "[DeviceHash] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_KioskStations_StoreId_TerminalId",
                table: "KioskStations",
                columns: new[] { "StoreId", "TerminalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_KioskStations_SystemUserId",
                table: "KioskStations",
                column: "SystemUserId");

            migrationBuilder.CreateIndex(
                name: "IX_KioskStations_TerminalId",
                table: "KioskStations",
                column: "TerminalId");

            migrationBuilder.CreateIndex(
                name: "IX_KioskStations_WarehouseId",
                table: "KioskStations",
                column: "WarehouseId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "KioskStations");
        }
    }
}
