using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPOSPaymentAdjustmentRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "POSPaymentAdjustmentRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClientRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PaymentId = table.Column<int>(type: "int", nullable: false),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    POSShiftId = table.Column<int>(type: "int", nullable: false),
                    RequestedByUserId = table.Column<int>(type: "int", nullable: false),
                    RequestedByName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    RequestReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    PaymentVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    OrderVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    OldMethod = table.Column<int>(type: "int", nullable: false),
                    NewMethod = table.Column<int>(type: "int", nullable: false),
                    OldReference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    NewReference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    OldProvider = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ExpectedDelta = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
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
                    table.PrimaryKey("PK_POSPaymentAdjustmentRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_POSPaymentAdjustmentRequests_OrderPayments_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "OrderPayments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_POSPaymentAdjustmentRequests_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_POSPaymentAdjustmentRequests_POSShifts_POSShiftId",
                        column: x => x.POSShiftId,
                        principalTable: "POSShifts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_POSPaymentAdjustmentRequests_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_POSPaymentAdjustmentRequests_OrderId",
                table: "POSPaymentAdjustmentRequests",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_POSPaymentAdjustmentRequests_PaymentId",
                table: "POSPaymentAdjustmentRequests",
                column: "PaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_POSPaymentAdjustmentRequests_POSShiftId",
                table: "POSPaymentAdjustmentRequests",
                column: "POSShiftId");

            migrationBuilder.CreateIndex(
                name: "IX_POSPaymentAdjustmentRequests_StoreId_ClientRequestId",
                table: "POSPaymentAdjustmentRequests",
                columns: new[] { "StoreId", "ClientRequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_POSPaymentAdjustmentRequests_StoreId_OrderId",
                table: "POSPaymentAdjustmentRequests",
                columns: new[] { "StoreId", "OrderId" },
                unique: true,
                filter: "[Status] = 0 AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_POSPaymentAdjustmentRequests_StoreId_POSShiftId_Id",
                table: "POSPaymentAdjustmentRequests",
                columns: new[] { "StoreId", "POSShiftId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_POSPaymentAdjustmentRequests_StoreId_RequestedByUserId_Id",
                table: "POSPaymentAdjustmentRequests",
                columns: new[] { "StoreId", "RequestedByUserId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_POSPaymentAdjustmentRequests_StoreId_Status_Id",
                table: "POSPaymentAdjustmentRequests",
                columns: new[] { "StoreId", "Status", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "POSPaymentAdjustmentRequests");
        }
    }
}
