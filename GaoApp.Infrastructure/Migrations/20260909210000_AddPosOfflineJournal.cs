using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace GaoApp.Infrastructure.Migrations;

public partial class AddPosOfflineJournal : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable("PosOperationReceipts", columns: table => new
        {
            Id = table.Column<int>(type: "int", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
            OperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
            StoreId = table.Column<int>(type: "int", nullable: false),
            TerminalId = table.Column<int>(type: "int", nullable: false),
            UserId = table.Column<int>(type: "int", nullable: false),
            RequestHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
            ResponseJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
            WasOffline = table.Column<bool>(type: "bit", nullable: false),
            OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
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
            table.PrimaryKey("PK_PosOperationReceipts", x => x.Id);
            table.ForeignKey("FK_PosOperationReceipts_Stores_StoreId", x => x.StoreId, "Stores", "Id");
        });
        migrationBuilder.CreateIndex("IX_PosOperationReceipts_StoreId_OperationId", "PosOperationReceipts", new[] { "StoreId", "OperationId" }, unique: true);
        migrationBuilder.CreateIndex("IX_PosOperationReceipts_StoreId_TerminalId_CreatedAtUtc", "PosOperationReceipts", new[] { "StoreId", "TerminalId", "CreatedAtUtc" });
    }
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable("PosOperationReceipts");
}
