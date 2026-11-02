using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GaoApp.Infrastructure.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261022090000_AddDurableAuditOutbox")]
public sealed class AddDurableAuditOutbox : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(name: "EventId", table: "AuditLogs", type: "uniqueidentifier", nullable: true);
        migrationBuilder.CreateIndex(name: "IX_AuditLogs_EventId", table: "AuditLogs", column: "EventId",
            unique: true, filter: "[EventId] IS NOT NULL");
        migrationBuilder.CreateTable(name: "AuditOutbox", columns: table => new
        {
            EventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
            StoreId = table.Column<int>(type: "int", nullable: false),
            PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
            CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
            NextAttemptAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
            ProcessedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
            AttemptCount = table.Column<int>(type: "int", nullable: false),
            LastErrorType = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_AuditOutbox", x => x.EventId);
            table.CheckConstraint("CK_AuditOutbox_StoreId", "[StoreId] > 0");
        });
        migrationBuilder.CreateIndex(name: "IX_AuditOutbox_StoreId_ProcessedAtUtc_NextAttemptAtUtc_CreatedAtUtc",
            table: "AuditOutbox", columns: new[] { "StoreId", "ProcessedAtUtc", "NextAttemptAtUtc", "CreatedAtUtc" });
        migrationBuilder.CreateIndex(name: "IX_AuditOutbox_ProcessedAtUtc_NextAttemptAtUtc_CreatedAtUtc",
            table: "AuditOutbox", columns: new[] { "ProcessedAtUtc", "NextAttemptAtUtc", "CreatedAtUtc" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Never silently discard pending evidence in a rollback. Export/drain and remove the table explicitly first.
        migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM [AuditOutbox] WHERE [ProcessedAtUtc] IS NULL) THROW 51010, 'Pending audit evidence prevents downgrade.', 1;");
        migrationBuilder.DropTable(name: "AuditOutbox");
        migrationBuilder.DropIndex(name: "IX_AuditLogs_EventId", table: "AuditLogs");
        migrationBuilder.DropColumn(name: "EventId", table: "AuditLogs");
    }
}
