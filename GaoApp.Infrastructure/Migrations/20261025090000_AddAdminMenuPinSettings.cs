using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GaoApp.Infrastructure.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261025090000_AddAdminMenuPinSettings")]
public sealed class AddAdminMenuPinSettings : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(name: "AdminMenuPinSettings", columns: table => new
        {
            Id = table.Column<int>(type: "int", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
            StoreId = table.Column<int>(type: "int", nullable: false),
            PinnedMenuIdsJson = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
            CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
            CreatedBy = table.Column<int>(type: "int", nullable: true),
            UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
            UpdatedBy = table.Column<int>(type: "int", nullable: true),
            IsDeleted = table.Column<bool>(type: "bit", nullable: false),
            DeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
            DeletedBy = table.Column<int>(type: "int", nullable: true),
            RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_AdminMenuPinSettings", x => x.Id);
            table.ForeignKey("FK_AdminMenuPinSettings_Stores_StoreId", x => x.StoreId, "Stores", "Id");
        });
        migrationBuilder.CreateIndex(name: "IX_AdminMenuPinSettings_StoreId", table: "AdminMenuPinSettings", column: "StoreId", unique: true,
            filter: "[StoreId] IS NOT NULL");
        // Schema only: no menu creation and no automatically selected pins.
    }

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable("AdminMenuPinSettings");
}
