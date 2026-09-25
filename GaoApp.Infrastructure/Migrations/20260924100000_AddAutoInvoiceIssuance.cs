using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GaoApp.Infrastructure.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260924100000_AddAutoInvoiceIssuance")]
public sealed class AddAutoInvoiceIssuance : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint("CK_InvoiceHeads_LegacyOrder", "InvoiceHeads");
        migrationBuilder.AddColumn<bool>("IsAutoInvoiceGroup", "InvoiceHeads", "bit", nullable: false, defaultValue: false);
        migrationBuilder.AddCheckConstraint(
    "CK_InvoiceHeads_LegacyOrder",
    "InvoiceHeads",
    "[OrderId] IS NOT NULL OR [LegacySourceId] IS NOT NULL AND [LegacyReadOnly] = 1 OR [IsAutoInvoiceGroup] = 1");
        migrationBuilder.CreateIndex("IX_InvoiceHeads_StoreId_IsAutoInvoiceGroup_IsDeleted", "InvoiceHeads", new[] { "StoreId", "IsAutoInvoiceGroup", "IsDeleted" });

        migrationBuilder.AddColumn<int>("AutoInvoiceOperationId", "InvoiceIntegrationLogs", "int", nullable: true);
        migrationBuilder.CreateIndex("IX_InvoiceIntegrationLogs_StoreId_AutoInvoiceOperationId_StartedAtUtc", "InvoiceIntegrationLogs", new[] { "StoreId", "AutoInvoiceOperationId", "StartedAtUtc" });

        migrationBuilder.CreateTable(
            name: "AutoInvoiceSettings",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                MinimumAgeMinutes = table.Column<int>(type: "int", nullable: false),
                SeparateAmountThreshold = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                GroupTargetAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                SendIntervalSeconds = table.Column<int>(type: "int", nullable: false),
                ClosingTimeLocal = table.Column<TimeSpan>(type: "time", nullable: false),
                IssueOldDayRemainder = table.Column<bool>(type: "bit", nullable: false),
                ScopeMode = table.Column<byte>(type: "tinyint", nullable: false),
                ScopeStartDateLocal = table.Column<DateTime>(type: "datetime2", nullable: true),
                ScopeEndDateLocal = table.Column<DateTime>(type: "datetime2", nullable: true),
                TimeZoneId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                LastEnabledAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                LastDisabledAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
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
                table.PrimaryKey("PK_AutoInvoiceSettings", x => x.Id);
                table.ForeignKey("FK_AutoInvoiceSettings_Stores_StoreId", x => x.StoreId, "Stores", "Id");
            });

        migrationBuilder.CreateTable(
            name: "AutoInvoiceOperations",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                Kind = table.Column<byte>(type: "tinyint", nullable: false),
                Status = table.Column<byte>(type: "tinyint", nullable: false),
                InvoiceHeadId = table.Column<int>(type: "int", nullable: true),
                GroupKey = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                SaleDateLocal = table.Column<DateTime>(type: "datetime2", nullable: false),
                TransactionUuid = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: true),
                AttemptCount = table.Column<int>(type: "int", nullable: false),
                IsManual = table.Column<bool>(type: "bit", nullable: false),
                RequestedByUserId = table.Column<int>(type: "int", nullable: true),
                RequestedByUserName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                CorrelationId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                ClaimedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                SubmittedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                NextAttemptAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                ErrorCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                ErrorMessage = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
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
                table.PrimaryKey("PK_AutoInvoiceOperations", x => x.Id);
                table.ForeignKey("FK_AutoInvoiceOperations_InvoiceHeads_InvoiceHeadId", x => x.InvoiceHeadId, "InvoiceHeads", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_AutoInvoiceOperations_Stores_StoreId", x => x.StoreId, "Stores", "Id");
            });

        migrationBuilder.CreateTable(
            name: "AutoInvoiceWorkerStates",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                WorkerName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                WorkerInstanceId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                IsRunning = table.Column<bool>(type: "bit", nullable: false),
                StartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                StoppedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                LastHeartbeatAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                LastScanAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                CurrentOperationId = table.Column<int>(type: "int", nullable: true),
                CurrentInvoiceHeadId = table.Column<int>(type: "int", nullable: true),
                NextRunAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                LastErrorCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                LastErrorMessage = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                LastResult = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
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
                table.PrimaryKey("PK_AutoInvoiceWorkerStates", x => x.Id);
                table.ForeignKey("FK_AutoInvoiceWorkerStates_Stores_StoreId", x => x.StoreId, "Stores", "Id");
            });

        migrationBuilder.CreateTable(
            name: "AutoInvoiceOperationSources",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                AutoInvoiceOperationId = table.Column<int>(type: "int", nullable: false),
                InvoiceHeadId = table.Column<int>(type: "int", nullable: false),
                OrderId = table.Column<int>(type: "int", nullable: true),
                InvoiceDetailId = table.Column<int>(type: "int", nullable: true),
                OrderLineId = table.Column<int>(type: "int", nullable: true),
                Status = table.Column<byte>(type: "tinyint", nullable: false),
                IsActive = table.Column<bool>(type: "bit", nullable: false),
                SourceSnapshotJson = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                ErrorCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                ErrorMessage = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
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
                table.PrimaryKey("PK_AutoInvoiceOperationSources", x => x.Id);
                table.ForeignKey("FK_AutoInvoiceOperationSources_AutoInvoiceOperations_AutoInvoiceOperationId", x => x.AutoInvoiceOperationId, "AutoInvoiceOperations", "Id", onDelete: ReferentialAction.Cascade);
                table.ForeignKey("FK_AutoInvoiceOperationSources_InvoiceHeads_InvoiceHeadId", x => x.InvoiceHeadId, "InvoiceHeads", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_AutoInvoiceOperationSources_Stores_StoreId", x => x.StoreId, "Stores", "Id");
            });
        migrationBuilder.CreateIndex(
    name: "IX_AutoInvoiceOperations_InvoiceHeadId",
    table: "AutoInvoiceOperations",
    column: "InvoiceHeadId");

        migrationBuilder.CreateIndex(
            name: "IX_AutoInvoiceOperationSources_AutoInvoiceOperationId",
            table: "AutoInvoiceOperationSources",
            column: "AutoInvoiceOperationId");

        migrationBuilder.CreateIndex(
            name: "IX_AutoInvoiceOperationSources_InvoiceHeadId",
            table: "AutoInvoiceOperationSources",
            column: "InvoiceHeadId");
        migrationBuilder.CreateIndex("UX_AutoInvoiceSettings_Store_Active", "AutoInvoiceSettings", new[] { "StoreId", "IsDeleted" }, unique: true, filter: "[IsDeleted] = 0");
        migrationBuilder.CreateIndex("IX_AutoInvoiceOperations_Store_Status_NextAttempt", "AutoInvoiceOperations", new[] { "StoreId", "Status", "NextAttemptAtUtc" });
        migrationBuilder.CreateIndex("IX_AutoInvoiceOperations_Store_Invoice_IsDeleted", "AutoInvoiceOperations", new[] { "StoreId", "InvoiceHeadId", "IsDeleted" });
        migrationBuilder.CreateIndex(
    name: "UX_AutoInvoiceWorkerStates_Store_Name",
    table: "AutoInvoiceWorkerStates",
    columns: new[] { "StoreId", "WorkerName" },
    unique: true,
    filter: "[StoreId] IS NOT NULL AND [WorkerName] IS NOT NULL");
        migrationBuilder.CreateIndex("UX_AutoInvoiceOperationSources_ActiveInvoice", "AutoInvoiceOperationSources", new[] { "StoreId", "InvoiceHeadId" }, unique: true, filter: "[IsActive] = 1 AND [IsDeleted] = 0");
        migrationBuilder.CreateIndex("IX_AutoInvoiceOperationSources_Operation_Status", "AutoInvoiceOperationSources", new[] { "StoreId", "AutoInvoiceOperationId", "Status" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "AutoInvoiceOperationSources");
        migrationBuilder.DropTable(name: "AutoInvoiceWorkerStates");
        migrationBuilder.DropTable(name: "AutoInvoiceOperations");
        migrationBuilder.DropTable(name: "AutoInvoiceSettings");
        migrationBuilder.DropIndex("IX_InvoiceIntegrationLogs_StoreId_AutoInvoiceOperationId_StartedAtUtc", "InvoiceIntegrationLogs");
        migrationBuilder.DropColumn("AutoInvoiceOperationId", "InvoiceIntegrationLogs");
        migrationBuilder.DropIndex("IX_InvoiceHeads_StoreId_IsAutoInvoiceGroup_IsDeleted", "InvoiceHeads");
        migrationBuilder.DropCheckConstraint("CK_InvoiceHeads_LegacyOrder", "InvoiceHeads");
        migrationBuilder.DropColumn("IsAutoInvoiceGroup", "InvoiceHeads");
        migrationBuilder.AddCheckConstraint("CK_InvoiceHeads_LegacyOrder", "InvoiceHeads", "[OrderId] IS NOT NULL OR ([LegacySourceId] IS NOT NULL AND [LegacyReadOnly] = 1)");
    }
}
