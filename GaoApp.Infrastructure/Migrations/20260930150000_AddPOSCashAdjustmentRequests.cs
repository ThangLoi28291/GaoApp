using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPOSCashAdjustmentRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "NeedsCashReconciliation",
                table: "POSShifts",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "POSCashAdjustmentRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClientRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TransactionId = table.Column<int>(type: "int", nullable: false),
                    POSShiftId = table.Column<int>(type: "int", nullable: false),
                    RequestedByUserId = table.Column<int>(type: "int", nullable: false),
                    RequestedByName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IsCancellation = table.Column<bool>(type: "bit", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    RequestReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    TransactionVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    OldType = table.Column<int>(type: "int", nullable: false),
                    OldAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    OldReason = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    OldNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    NewType = table.Column<int>(type: "int", nullable: false),
                    NewAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    NewReason = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    NewNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ReviewedByUserId = table.Column<int>(type: "int", nullable: true),
                    ReviewedByName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ReviewedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReviewNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    BeforeShiftJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AfterShiftJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AppliedToClosedShift = table.Column<bool>(type: "bit", nullable: false),
                    ReconciledAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReconciledByUserId = table.Column<int>(type: "int", nullable: true),
                    ReconciledByName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ReconciliationNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
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
                    table.PrimaryKey("PK_POSCashAdjustmentRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_POSCashAdjustmentRequests_POSShiftCashTransactions_TransactionId",
                        column: x => x.TransactionId,
                        principalTable: "POSShiftCashTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_POSCashAdjustmentRequests_POSShifts_POSShiftId",
                        column: x => x.POSShiftId,
                        principalTable: "POSShifts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_POSCashAdjustmentRequests_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_POSCashAdjustmentRequests_POSShiftId",
                table: "POSCashAdjustmentRequests",
                column: "POSShiftId");

            migrationBuilder.CreateIndex(
                name: "IX_POSCashAdjustmentRequests_StoreId_ClientRequestId",
                table: "POSCashAdjustmentRequests",
                columns: new[] { "StoreId", "ClientRequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_POSCashAdjustmentRequests_StoreId_RequestedByUserId_Id",
                table: "POSCashAdjustmentRequests",
                columns: new[] { "StoreId", "RequestedByUserId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_POSCashAdjustmentRequests_StoreId_Status_Id",
                table: "POSCashAdjustmentRequests",
                columns: new[] { "StoreId", "Status", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_POSCashAdjustmentRequests_StoreId_TransactionId",
                table: "POSCashAdjustmentRequests",
                columns: new[] { "StoreId", "TransactionId" },
                unique: true,
                filter: "[Status] = 0 AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_POSCashAdjustmentRequests_TransactionId",
                table: "POSCashAdjustmentRequests",
                column: "TransactionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "POSCashAdjustmentRequests");

            migrationBuilder.DropColumn(
                name: "NeedsCashReconciliation",
                table: "POSShifts");
        }
    }
}
