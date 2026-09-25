using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAcbQrNotificationReconciliation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AcbCallbackReceipts_StoreId_ClientRequestId_Page",
                table: "AcbCallbackReceipts");

            migrationBuilder.AddColumn<string>(
                name: "RequestCode",
                table: "AcbCallbackReceipts",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "TRANSACTION_UPDATE");

            migrationBuilder.AddColumn<int>(
                name: "TotalPages",
                table: "AcbCallbackReceipts",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateTable(
                name: "AcbQrNotificationItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReceiptId = table.Column<int>(type: "int", nullable: false),
                    Position = table.Column<int>(type: "int", nullable: false),
                    ProviderOrderId = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    RequestCode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    BusinessDate = table.Column<DateTime>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TransactionStatus = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    DebitOrCredit = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Content = table.Column<string>(type: "nvarchar(max)", nullable: false),
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
                    table.PrimaryKey("PK_AcbQrNotificationItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AcbQrNotificationItems_AcbCallbackReceipts_ReceiptId",
                        column: x => x.ReceiptId,
                        principalTable: "AcbCallbackReceipts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AcbQrNotificationItems_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            // Preserve previously received notifications in the searchable reconciliation history.
            migrationBuilder.Sql(@"
UPDATE r SET TotalPages = COALESCE(TRY_CONVERT(int, JSON_VALUE(j.Payload, '$.requestParameters.request.requestParams.pagination.totalPage')), 1)
FROM AcbCallbackReceipts r
CROSS APPLY (SELECT CASE WHEN ISJSON(r.PayloadJson) = 1 THEN r.PayloadJson ELSE '{}' END AS Payload) j;

INSERT INTO AcbQrNotificationItems
(ReceiptId, Position, ProviderOrderId, RequestCode, BusinessDate, Amount, TransactionStatus, DebitOrCredit, Content,
 CreatedAtUtc, CreatedBy, IsDeleted, StoreId)
SELECT r.Id, CONVERT(int, t.[key]), COALESCE(v.ProviderOrderId, ''), r.RequestCode, TRY_CONVERT(date, v.BusinessDate, 23),
 TRY_CONVERT(decimal(18,2), v.Amount), v.TransactionStatus, COALESCE(v.DebitOrCredit, ''), COALESCE(v.Content, ''),
 r.CreatedAtUtc, r.CreatedBy, r.IsDeleted, r.StoreId
FROM AcbCallbackReceipts r
CROSS APPLY OPENJSON(CASE WHEN ISJSON(r.PayloadJson) = 1 THEN r.PayloadJson ELSE '{}' END,
 '$.requestParameters.request.requestParams.transactions') t
CROSS APPLY OPENJSON(t.value) WITH (
 ProviderOrderId nvarchar(max) '$.transactionEntityAttribute.custom4',
 BusinessDate nvarchar(100) '$.effectiveDate', Amount nvarchar(100) '$.amount',
 TransactionStatus nvarchar(100) '$.transactionStatus', DebitOrCredit nvarchar(100) '$.debitOrCredit',
 Content nvarchar(max) '$.transactionContent') v
WHERE TRY_CONVERT(date, v.BusinessDate, 23) IS NOT NULL AND TRY_CONVERT(decimal(18,2), v.Amount) IS NOT NULL
 AND LEN(COALESCE(v.ProviderOrderId, '')) <= 300 AND LEN(COALESCE(v.DebitOrCredit, '')) <= 10
 AND v.TransactionStatus IN ('COMPLETED', 'ERRORCORRECTED');");

            migrationBuilder.CreateIndex(
                name: "IX_AcbCallbackReceipts_StoreId_RequestCode_ClientRequestId_Page",
                table: "AcbCallbackReceipts",
                columns: new[] { "StoreId", "RequestCode", "ClientRequestId", "Page" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AcbQrNotificationItems_ReceiptId_Position",
                table: "AcbQrNotificationItems",
                columns: new[] { "ReceiptId", "Position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AcbQrNotificationItems_StoreId_BusinessDate_RequestCode_ProviderOrderId",
                table: "AcbQrNotificationItems",
                columns: new[] { "StoreId", "BusinessDate", "RequestCode", "ProviderOrderId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM AcbCallbackReceipts GROUP BY StoreId, ClientRequestId, Page HAVING COUNT(*) > 1)
    THROW 51000, 'Cannot downgrade: distinct notification types share receipt identifiers. Preserve callback evidence before downgrade.', 1;");
            migrationBuilder.DropTable(
                name: "AcbQrNotificationItems");

            migrationBuilder.DropIndex(
                name: "IX_AcbCallbackReceipts_StoreId_RequestCode_ClientRequestId_Page",
                table: "AcbCallbackReceipts");

            migrationBuilder.DropColumn(
                name: "RequestCode",
                table: "AcbCallbackReceipts");

            migrationBuilder.DropColumn(
                name: "TotalPages",
                table: "AcbCallbackReceipts");

            migrationBuilder.CreateIndex(
                name: "IX_AcbCallbackReceipts_StoreId_ClientRequestId_Page",
                table: "AcbCallbackReceipts",
                columns: new[] { "StoreId", "ClientRequestId", "Page" },
                unique: true);
        }
    }
}
