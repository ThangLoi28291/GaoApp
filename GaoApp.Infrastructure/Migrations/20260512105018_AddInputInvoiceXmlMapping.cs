using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInputInvoiceXmlMapping : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InputInvoiceHead",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InvoiceTemplateCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    InvoiceSeries = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    InvoiceNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    InvoiceDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TaxAuthorityCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SellerTaxCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    SellerName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    SellerAddress = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    BuyerTaxCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    BuyerName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    BuyerAddress = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    TotalBeforeTax = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TotalTaxAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TotalPaymentAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    OriginalFileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: true),
                    XmlFilePath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    XmlHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
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
                    table.PrimaryKey("PK_InputInvoiceHead", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InputInvoiceHead_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "InputInvoiceDetail",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InputInvoiceHeadId = table.Column<int>(type: "int", nullable: false),
                    LineNo = table.Column<int>(type: "int", nullable: false),
                    ItemName = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    UnitName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Quantity = table.Column<decimal>(type: "decimal(18,3)", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    LineAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    VatRate = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    VatAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<int>(type: "int", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<int>(type: "int", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedBy = table.Column<int>(type: "int", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InputInvoiceDetail", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InputInvoiceDetail_InputInvoiceHead_InputInvoiceHeadId",
                        column: x => x.InputInvoiceHeadId,
                        principalTable: "InputInvoiceHead",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StockDocumentInputInvoiceMap",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StockDocumentId = table.Column<int>(type: "int", nullable: false),
                    InputInvoiceHeadId = table.Column<int>(type: "int", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
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
                    table.PrimaryKey("PK_StockDocumentInputInvoiceMap", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockDocumentInputInvoiceMap_InputInvoiceHead_InputInvoiceHeadId",
                        column: x => x.InputInvoiceHeadId,
                        principalTable: "InputInvoiceHead",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockDocumentInputInvoiceMap_StockDocument_StockDocumentId",
                        column: x => x.StockDocumentId,
                        principalTable: "StockDocument",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockDocumentInputInvoiceMap_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "StockDocumentLineInputInvoiceMap",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StockDocumentId = table.Column<int>(type: "int", nullable: false),
                    StockDocumentLineId = table.Column<int>(type: "int", nullable: false),
                    InputInvoiceDetailId = table.Column<int>(type: "int", nullable: true),
                    UseInputInvoice = table.Column<bool>(type: "bit", nullable: false),
                    MatchStatus = table.Column<int>(type: "int", nullable: false),
                    QuantityDifference = table.Column<decimal>(type: "decimal(18,3)", nullable: false),
                    AmountDifference = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
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
                    table.PrimaryKey("PK_StockDocumentLineInputInvoiceMap", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockDocumentLineInputInvoiceMap_InputInvoiceDetail_InputInvoiceDetailId",
                        column: x => x.InputInvoiceDetailId,
                        principalTable: "InputInvoiceDetail",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockDocumentLineInputInvoiceMap_StockDocumentLine_StockDocumentLineId",
                        column: x => x.StockDocumentLineId,
                        principalTable: "StockDocumentLine",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockDocumentLineInputInvoiceMap_StockDocument_StockDocumentId",
                        column: x => x.StockDocumentId,
                        principalTable: "StockDocument",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockDocumentLineInputInvoiceMap_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_InputInvoiceDetail_InputInvoiceHeadId_LineNo",
                table: "InputInvoiceDetail",
                columns: new[] { "InputInvoiceHeadId", "LineNo" });

            migrationBuilder.CreateIndex(
                name: "IX_InputInvoiceHead_StoreId_SellerTaxCode_InvoiceTemplateCode_InvoiceSeries_InvoiceNumber",
                table: "InputInvoiceHead",
                columns: new[] { "StoreId", "SellerTaxCode", "InvoiceTemplateCode", "InvoiceSeries", "InvoiceNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_InputInvoiceHead_StoreId_XmlHash",
                table: "InputInvoiceHead",
                columns: new[] { "StoreId", "XmlHash" });

            migrationBuilder.CreateIndex(
                name: "IX_StockDocumentInputInvoiceMap_InputInvoiceHeadId",
                table: "StockDocumentInputInvoiceMap",
                column: "InputInvoiceHeadId");

            migrationBuilder.CreateIndex(
                name: "IX_StockDocumentInputInvoiceMap_StockDocumentId",
                table: "StockDocumentInputInvoiceMap",
                column: "StockDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_StockDocumentInputInvoiceMap_StoreId_StockDocumentId_InputInvoiceHeadId",
                table: "StockDocumentInputInvoiceMap",
                columns: new[] { "StoreId", "StockDocumentId", "InputInvoiceHeadId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockDocumentLineInputInvoiceMap_InputInvoiceDetailId",
                table: "StockDocumentLineInputInvoiceMap",
                column: "InputInvoiceDetailId");

            migrationBuilder.CreateIndex(
                name: "IX_StockDocumentLineInputInvoiceMap_StockDocumentId",
                table: "StockDocumentLineInputInvoiceMap",
                column: "StockDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_StockDocumentLineInputInvoiceMap_StockDocumentLineId",
                table: "StockDocumentLineInputInvoiceMap",
                column: "StockDocumentLineId");

            migrationBuilder.CreateIndex(
                name: "IX_StockDocumentLineInputInvoiceMap_StoreId_StockDocumentId_UseInputInvoice",
                table: "StockDocumentLineInputInvoiceMap",
                columns: new[] { "StoreId", "StockDocumentId", "UseInputInvoice" });

            migrationBuilder.CreateIndex(
                name: "IX_StockDocumentLineInputInvoiceMap_StoreId_StockDocumentLineId",
                table: "StockDocumentLineInputInvoiceMap",
                columns: new[] { "StoreId", "StockDocumentLineId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StockDocumentInputInvoiceMap");

            migrationBuilder.DropTable(
                name: "StockDocumentLineInputInvoiceMap");

            migrationBuilder.DropTable(
                name: "InputInvoiceDetail");

            migrationBuilder.DropTable(
                name: "InputInvoiceHead");
        }
    }
}
