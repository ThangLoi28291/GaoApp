using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProductBarcodeVerificationRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProductBarcodeVerificationRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StoreId = table.Column<int>(type: "int", nullable: false),
                    ProductVariantId = table.Column<int>(type: "int", nullable: false),
                    ProductUnitConversionId = table.Column<int>(type: "int", nullable: false),
                    StockDocumentId = table.Column<int>(type: "int", nullable: true),
                    ProductNameSnapshot = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    UnitNameSnapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    FactorSnapshot = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    SuggestedBarcode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    RequestType = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    EmployeeNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ManagerNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RequestedByUserId = table.Column<int>(type: "int", nullable: false),
                    RequestedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ResolvedByUserId = table.Column<int>(type: "int", nullable: true),
                    ResolvedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBarcodeId = table.Column<int>(type: "int", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductBarcodeVerificationRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductBarcodeVerificationRequests_ProductUnitConversion_ProductUnitConversionId",
                        column: x => x.ProductUnitConversionId,
                        principalTable: "ProductUnitConversion",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductBarcodeVerificationRequests_ProductVariantUnitBarcode_CreatedBarcodeId",
                        column: x => x.CreatedBarcodeId,
                        principalTable: "ProductVariantUnitBarcode",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductBarcodeVerificationRequests_ProductVariant_ProductVariantId",
                        column: x => x.ProductVariantId,
                        principalTable: "ProductVariant",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductBarcodeVerificationRequests_StockDocument_StockDocumentId",
                        column: x => x.StockDocumentId,
                        principalTable: "StockDocument",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductBarcodeVerificationRequests_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProductBarcodeVerificationRequests_CreatedBarcodeId",
                table: "ProductBarcodeVerificationRequests",
                column: "CreatedBarcodeId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductBarcodeVerificationRequests_ProductUnitConversionId",
                table: "ProductBarcodeVerificationRequests",
                column: "ProductUnitConversionId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductBarcodeVerificationRequests_ProductVariantId",
                table: "ProductBarcodeVerificationRequests",
                column: "ProductVariantId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductBarcodeVerificationRequests_StockDocumentId_IsDeleted",
                table: "ProductBarcodeVerificationRequests",
                columns: new[] { "StockDocumentId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductBarcodeVerificationRequests_StoreId_ProductUnitConversionId_Status_IsDeleted",
                table: "ProductBarcodeVerificationRequests",
                columns: new[] { "StoreId", "ProductUnitConversionId", "Status", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductBarcodeVerificationRequests_StoreId_SuggestedBarcode_Status_IsDeleted",
                table: "ProductBarcodeVerificationRequests",
                columns: new[] { "StoreId", "SuggestedBarcode", "Status", "IsDeleted" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProductBarcodeVerificationRequests");
        }
    }
}
