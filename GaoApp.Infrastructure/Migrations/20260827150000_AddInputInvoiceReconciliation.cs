using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInputInvoiceReconciliation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExclusionReason",
                table: "StockDocumentLineInputInvoiceMap",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "StockDocumentInputInvoiceReconciliation",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StockDocumentInputInvoiceMapId = table.Column<int>(type: "int", nullable: false),
                    StockDocumentId = table.Column<int>(type: "int", nullable: false),
                    InputInvoiceHeadId = table.Column<int>(type: "int", nullable: false),
                    OverallState = table.Column<int>(type: "int", nullable: false),
                    EvidenceFingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    LastCalculatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TotalDetailCount = table.Column<int>(type: "int", nullable: false),
                    MatchedDetailCount = table.Column<int>(type: "int", nullable: false),
                    MismatchDetailCount = table.Column<int>(type: "int", nullable: false),
                    UnmatchedDetailCount = table.Column<int>(type: "int", nullable: false),
                    IgnoredDetailCount = table.Column<int>(type: "int", nullable: false),
                    ExcludedLineCount = table.Column<int>(type: "int", nullable: false),
                    IncompleteCount = table.Column<int>(type: "int", nullable: false),
                    ReceiptSubtotalBeforeVat = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    XmlTotalBeforeTax = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    SubtotalDifference = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ReceiptVatAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    XmlTaxAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    VatDifference = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ReceiptGoodsTotal = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    XmlPaymentAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    PaymentDifference = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    HasUnsupportedHeader = table.Column<bool>(type: "bit", nullable: false),
                    UnsupportedHeaderReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    AcceptedEvidenceFingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    AcceptanceReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    AcceptedByUserId = table.Column<int>(type: "int", nullable: true),
                    AcceptedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
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
                    table.PrimaryKey("PK_StockDocumentInputInvoiceReconciliation", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockDocumentInputInvoiceReconciliation_InputInvoiceHead_InputInvoiceHeadId",
                        column: x => x.InputInvoiceHeadId,
                        principalTable: "InputInvoiceHead",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockDocumentInputInvoiceReconciliation_StockDocumentInputInvoiceMap_StockDocumentInputInvoiceMapId",
                        column: x => x.StockDocumentInputInvoiceMapId,
                        principalTable: "StockDocumentInputInvoiceMap",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockDocumentInputInvoiceReconciliation_StockDocument_StockDocumentId",
                        column: x => x.StockDocumentId,
                        principalTable: "StockDocument",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockDocumentInputInvoiceReconciliation_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "StockDocumentInputInvoiceDetailReconciliation",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StockDocumentInputInvoiceReconciliationId = table.Column<int>(type: "int", nullable: false),
                    StockDocumentId = table.Column<int>(type: "int", nullable: false),
                    InputInvoiceHeadId = table.Column<int>(type: "int", nullable: false),
                    InputInvoiceDetailId = table.Column<int>(type: "int", nullable: false),
                    DetailState = table.Column<int>(type: "int", nullable: false),
                    ProductVariantId = table.Column<int>(type: "int", nullable: true),
                    ProductUnitConversionId = table.Column<int>(type: "int", nullable: true),
                    ConfirmedUnitId = table.Column<int>(type: "int", nullable: true),
                    ConfirmedBaseUnitId = table.Column<int>(type: "int", nullable: true),
                    ConfirmedFactor = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    XmlQuantity = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    DerivedBaseQuantity = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    ReceiptBaseQuantity = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    QuantityDifference = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    ReceiptBeforeVatAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    XmlBeforeVatAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    AmountDifference = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ReceiptVatRate = table.Column<decimal>(type: "decimal(9,4)", nullable: true),
                    XmlVatRate = table.Column<decimal>(type: "decimal(9,4)", nullable: true),
                    VatComparable = table.Column<bool>(type: "bit", nullable: false),
                    VatComparisonReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ReceiptVatAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    XmlVatAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    VatAmountDifference = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    IsIgnored = table.Column<bool>(type: "bit", nullable: false),
                    IgnoreReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
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
                    table.PrimaryKey("PK_StockDocumentInputInvoiceDetailReconciliation", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockDocumentInputInvoiceDetailReconciliation_InputInvoiceDetail_InputInvoiceDetailId",
                        column: x => x.InputInvoiceDetailId,
                        principalTable: "InputInvoiceDetail",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockDocumentInputInvoiceDetailReconciliation_StockDocumentInputInvoiceReconciliation_StockDocumentInputInvoiceReconciliatio~",
                        column: x => x.StockDocumentInputInvoiceReconciliationId,
                        principalTable: "StockDocumentInputInvoiceReconciliation",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockDocumentInputInvoiceDetailReconciliation_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_InputInvoiceDetailReconciliation_Receipt_State",
                table: "StockDocumentInputInvoiceDetailReconciliation",
                columns: new[] { "StoreId", "StockDocumentId", "InputInvoiceHeadId", "DetailState" });

            migrationBuilder.CreateIndex(
                name: "IX_StockDocumentInputInvoiceDetailReconciliation_InputInvoiceDetailId",
                table: "StockDocumentInputInvoiceDetailReconciliation",
                column: "InputInvoiceDetailId");

            migrationBuilder.CreateIndex(
                name: "IX_StockDocumentInputInvoiceDetailReconciliation_StockDocumentInputInvoiceReconciliationId",
                table: "StockDocumentInputInvoiceDetailReconciliation",
                column: "StockDocumentInputInvoiceReconciliationId");

            migrationBuilder.CreateIndex(
                name: "UX_InputInvoiceDetailReconciliation_Detail_Active",
                table: "StockDocumentInputInvoiceDetailReconciliation",
                columns: new[] { "StoreId", "StockDocumentInputInvoiceReconciliationId", "InputInvoiceDetailId" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_InputInvoiceReconciliation_Receipt_State",
                table: "StockDocumentInputInvoiceReconciliation",
                columns: new[] { "StoreId", "StockDocumentId", "OverallState" });

            migrationBuilder.CreateIndex(
                name: "IX_StockDocumentInputInvoiceReconciliation_InputInvoiceHeadId",
                table: "StockDocumentInputInvoiceReconciliation",
                column: "InputInvoiceHeadId");

            migrationBuilder.CreateIndex(
                name: "IX_StockDocumentInputInvoiceReconciliation_StockDocumentId",
                table: "StockDocumentInputInvoiceReconciliation",
                column: "StockDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_StockDocumentInputInvoiceReconciliation_StockDocumentInputInvoiceMapId",
                table: "StockDocumentInputInvoiceReconciliation",
                column: "StockDocumentInputInvoiceMapId");

            migrationBuilder.CreateIndex(
                name: "UX_InputInvoiceReconciliation_Link_Active",
                table: "StockDocumentInputInvoiceReconciliation",
                columns: new[] { "StoreId", "StockDocumentInputInvoiceMapId" },
                unique: true,
                filter: "[IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StockDocumentInputInvoiceDetailReconciliation");

            migrationBuilder.DropTable(
                name: "StockDocumentInputInvoiceReconciliation");

            migrationBuilder.DropColumn(
                name: "ExclusionReason",
                table: "StockDocumentLineInputInvoiceMap");
        }
    }
}
