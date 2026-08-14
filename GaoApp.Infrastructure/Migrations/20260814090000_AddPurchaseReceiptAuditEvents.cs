using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPurchaseReceiptAuditEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PurchaseReceiptAuditEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StoreId = table.Column<int>(type: "int", nullable: false),
                    StockDocumentId = table.Column<int>(type: "int", nullable: false),
                    StockDocumentLineId = table.Column<int>(type: "int", nullable: true),
                    EventType = table.Column<int>(type: "int", nullable: false),
                    ActorUserId = table.Column<int>(type: "int", nullable: false),
                    ActorUserName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ChangedFieldsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OldValuesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    NewValuesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TraceId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    IsSuccess = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseReceiptAuditEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseReceiptAuditEvents_StockDocumentLine_StockDocumentLineId",
                        column: x => x.StockDocumentLineId,
                        principalTable: "StockDocumentLine",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PurchaseReceiptAuditEvents_StockDocument_StockDocumentId",
                        column: x => x.StockDocumentId,
                        principalTable: "StockDocument",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PurchaseReceiptAuditEvents_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReceiptAuditEvents_StockDocumentLineId",
                table: "PurchaseReceiptAuditEvents",
                column: "StockDocumentLineId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReceiptAuditEvents_StockDocumentId",
                table: "PurchaseReceiptAuditEvents",
                column: "StockDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReceiptAuditEvents_Store_Document_Occurred_Id",
                table: "PurchaseReceiptAuditEvents",
                columns: new[] { "StoreId", "StockDocumentId", "OccurredAtUtc", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PurchaseReceiptAuditEvents");
        }
    }
}
