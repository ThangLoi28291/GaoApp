using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUniqueOpenPOSShiftPerTerminal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_POSShifts_StoreId_TerminalId_Status",
                table: "POSShifts",
                columns: new[] { "StoreId", "TerminalId", "Status" },
                unique: true,
                filter: "[Status] = 1 AND [IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_POSShifts_StoreId_TerminalId_Status",
                table: "POSShifts");
        }
    }
}
