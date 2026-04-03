using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSourceTraceToInventoryValuationEntry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InventoryValuationEntries_StoreId_ReferenceType_ReferenceId_ReferenceLineId_EntryType",
                table: "InventoryValuationEntries");

            migrationBuilder.AddColumn<string>(
                name: "ReferenceSubKey",
                table: "InventoryValuationEntries",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceReferenceSubKey",
                table: "InventoryValuationEntries",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SourceValuationEntryId",
                table: "InventoryValuationEntries",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryValuationEntries_SourceValuationEntryId",
                table: "InventoryValuationEntries",
                column: "SourceValuationEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryValuationEntries_StoreId_ReferenceType_ReferenceId_ReferenceLineId_ReferenceSubKey_EntryType",
                table: "InventoryValuationEntries",
                columns: new[] { "StoreId", "ReferenceType", "ReferenceId", "ReferenceLineId", "ReferenceSubKey", "EntryType" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryValuationEntries_StoreId_SourceValuationEntryId",
                table: "InventoryValuationEntries",
                columns: new[] { "StoreId", "SourceValuationEntryId" });

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryValuationEntries_InventoryValuationEntries_SourceValuationEntryId",
                table: "InventoryValuationEntries",
                column: "SourceValuationEntryId",
                principalTable: "InventoryValuationEntries",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_InventoryValuationEntries_InventoryValuationEntries_SourceValuationEntryId",
                table: "InventoryValuationEntries");

            migrationBuilder.DropIndex(
                name: "IX_InventoryValuationEntries_SourceValuationEntryId",
                table: "InventoryValuationEntries");

            migrationBuilder.DropIndex(
                name: "IX_InventoryValuationEntries_StoreId_ReferenceType_ReferenceId_ReferenceLineId_ReferenceSubKey_EntryType",
                table: "InventoryValuationEntries");

            migrationBuilder.DropIndex(
                name: "IX_InventoryValuationEntries_StoreId_SourceValuationEntryId",
                table: "InventoryValuationEntries");

            migrationBuilder.DropColumn(
                name: "ReferenceSubKey",
                table: "InventoryValuationEntries");

            migrationBuilder.DropColumn(
                name: "SourceReferenceSubKey",
                table: "InventoryValuationEntries");

            migrationBuilder.DropColumn(
                name: "SourceValuationEntryId",
                table: "InventoryValuationEntries");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryValuationEntries_StoreId_ReferenceType_ReferenceId_ReferenceLineId_EntryType",
                table: "InventoryValuationEntries",
                columns: new[] { "StoreId", "ReferenceType", "ReferenceId", "ReferenceLineId", "EntryType" });
        }
    }
}
