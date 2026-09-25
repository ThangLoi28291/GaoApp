using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProvisionalReceivingItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_StockDocument_ActiveReceivingDraft",
                table: "StockDocument");

            migrationBuilder.AlterColumn<int>(
                name: "StockDocumentLineId",
                table: "PurchaseReceivingActions",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AlterColumn<int>(
                name: "ProductVariantId",
                table: "PurchaseReceivingActions",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AlterColumn<int>(
                name: "ProductUnitConversionId",
                table: "PurchaseReceivingActions",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<string>(
                name: "AfterProvisionalStateJson",
                table: "PurchaseReceivingActions",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BeforeProvisionalStateJson",
                table: "PurchaseReceivingActions",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CommandPayloadHash",
                table: "PurchaseReceivingActions",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StockDocumentProvisionalItemId",
                table: "PurchaseReceivingActions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EditablePurchaseReceiptKey",
                table: "StockDocument",
                type: "int",
                nullable: false,
                computedColumnSql: "CASE WHEN [IsDeleted] = 0 AND [Type] = 1 AND [ReceiptSource] = 2 AND ([Status] = 4 OR [Status] = 1) AND [PurchaseOrderId] IS NOT NULL THEN [PurchaseOrderId] ELSE -[Id] END",
                stored: true);

            migrationBuilder.AddColumn<int>(
                name: "StockDocumentProvisionalItemId",
                table: "PurchaseReceiptAuditEvents",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "StockDocumentProvisionalItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StockDocumentId = table.Column<int>(type: "int", nullable: false),
                    NameSnapshot = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    RawBarcodeSnapshot = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    NormalizedBarcode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    UnitId = table.Column<int>(type: "int", nullable: true),
                    UnitNameSnapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    NormalizedUnitNameSnapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Quantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ResolutionMethod = table.Column<int>(type: "int", nullable: true),
                    ResolvedStockDocumentLineId = table.Column<int>(type: "int", nullable: true),
                    ResolvedProductVariantId = table.Column<int>(type: "int", nullable: true),
                    ResolvedProductUnitConversionId = table.Column<int>(type: "int", nullable: true),
                    ResolvedByUserId = table.Column<int>(type: "int", nullable: true),
                    ResolvedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RawBarcodeRemembered = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBarcodeId = table.Column<int>(type: "int", nullable: true),
                    RemovedByUserId = table.Column<int>(type: "int", nullable: true),
                    RemovedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SupersededByProvisionalItemId = table.Column<int>(type: "int", nullable: true),
                    ReceivingRevision = table.Column<int>(type: "int", nullable: false),
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
                    table.PrimaryKey("PK_StockDocumentProvisionalItems", x => x.Id);
                    table.CheckConstraint("CK_StockDocumentProvisionalItem_Quantity", "[Quantity] > 0");
                    table.CheckConstraint("CK_StockDocumentProvisionalItem_State", "[Status] = 0 AND [ResolvedStockDocumentLineId] IS NULL AND [ResolutionMethod] IS NULL AND [RemovedAtUtc] IS NULL OR [Status] = 1 AND [ResolvedStockDocumentLineId] IS NOT NULL AND [ResolvedProductVariantId] IS NOT NULL AND [ResolvedProductUnitConversionId] IS NOT NULL AND [ResolutionMethod] IS NOT NULL AND [ResolvedAtUtc] IS NOT NULL OR [Status] = 2 AND [ResolvedStockDocumentLineId] IS NULL AND [RemovedAtUtc] IS NOT NULL");
                    table.ForeignKey(
                        name: "FK_StockDocumentProvisionalItems_ProductUnitConversion_ResolvedProductUnitConversionId",
                        column: x => x.ResolvedProductUnitConversionId,
                        principalTable: "ProductUnitConversion",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockDocumentProvisionalItems_ProductVariantUnitBarcode_CreatedBarcodeId",
                        column: x => x.CreatedBarcodeId,
                        principalTable: "ProductVariantUnitBarcode",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockDocumentProvisionalItems_ProductVariant_ResolvedProductVariantId",
                        column: x => x.ResolvedProductVariantId,
                        principalTable: "ProductVariant",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockDocumentProvisionalItems_StockDocumentLine_ResolvedStockDocumentLineId",
                        column: x => x.ResolvedStockDocumentLineId,
                        principalTable: "StockDocumentLine",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockDocumentProvisionalItems_StockDocumentProvisionalItems_SupersededByProvisionalItemId",
                        column: x => x.SupersededByProvisionalItemId,
                        principalTable: "StockDocumentProvisionalItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockDocumentProvisionalItems_StockDocument_StockDocumentId",
                        column: x => x.StockDocumentId,
                        principalTable: "StockDocument",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockDocumentProvisionalItems_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_StockDocumentProvisionalItems_Unit_UnitId",
                        column: x => x.UnitId,
                        principalTable: "Unit",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "UX_StockDocument_EditablePurchaseReceipt",
                table: "StockDocument",
                columns: new[] { "StoreId", "EditablePurchaseReceiptKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReceivingActions_StockDocumentProvisionalItemId",
                table: "PurchaseReceivingActions",
                column: "StockDocumentProvisionalItemId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PurchaseReceivingActions_Target",
                table: "PurchaseReceivingActions",
                sql: "[StockDocumentLineId] IS NOT NULL AND [StockDocumentProvisionalItemId] IS NULL AND [ProductVariantId] IS NOT NULL AND [ProductUnitConversionId] IS NOT NULL OR [StockDocumentLineId] IS NULL AND [StockDocumentProvisionalItemId] IS NOT NULL AND [ProductVariantId] IS NULL AND [ProductUnitConversionId] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReceiptAuditEvents_StockDocumentProvisionalItemId",
                table: "PurchaseReceiptAuditEvents",
                column: "StockDocumentProvisionalItemId");

            migrationBuilder.CreateIndex(
                name: "IX_StockDocumentProvisionalItems_CreatedBarcodeId",
                table: "StockDocumentProvisionalItems",
                column: "CreatedBarcodeId");

            migrationBuilder.CreateIndex(
                name: "IX_StockDocumentProvisionalItems_Document_Status",
                table: "StockDocumentProvisionalItems",
                columns: new[] { "StoreId", "StockDocumentId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_StockDocumentProvisionalItems_ResolvedProductUnitConversionId",
                table: "StockDocumentProvisionalItems",
                column: "ResolvedProductUnitConversionId");

            migrationBuilder.CreateIndex(
                name: "IX_StockDocumentProvisionalItems_ResolvedProductVariantId",
                table: "StockDocumentProvisionalItems",
                column: "ResolvedProductVariantId");

            migrationBuilder.CreateIndex(
                name: "IX_StockDocumentProvisionalItems_ResolvedStockDocumentLineId",
                table: "StockDocumentProvisionalItems",
                column: "ResolvedStockDocumentLineId");

            migrationBuilder.CreateIndex(
                name: "IX_StockDocumentProvisionalItems_SupersededByProvisionalItemId",
                table: "StockDocumentProvisionalItems",
                column: "SupersededByProvisionalItemId");

            migrationBuilder.CreateIndex(
                name: "IX_StockDocumentProvisionalItems_UnitId",
                table: "StockDocumentProvisionalItems",
                column: "UnitId");

            migrationBuilder.CreateIndex(
                name: "UX_StockDocumentProvisionalItems_Barcode_Unit",
                table: "StockDocumentProvisionalItems",
                columns: new[] { "StockDocumentId", "NormalizedBarcode", "UnitId" },
                unique: true,
                filter: "[IsDeleted] = 0 AND [Status] = 0 AND [NormalizedBarcode] IS NOT NULL AND [UnitId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_StockDocumentProvisionalItems_Barcode_UnitSnapshot",
                table: "StockDocumentProvisionalItems",
                columns: new[] { "StockDocumentId", "NormalizedBarcode", "NormalizedUnitNameSnapshot" },
                unique: true,
                filter: "[IsDeleted] = 0 AND [Status] = 0 AND [NormalizedBarcode] IS NOT NULL AND [UnitId] IS NULL AND [NormalizedUnitNameSnapshot] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseReceiptAuditEvents_StockDocumentProvisionalItems_StockDocumentProvisionalItemId",
                table: "PurchaseReceiptAuditEvents",
                column: "StockDocumentProvisionalItemId",
                principalTable: "StockDocumentProvisionalItems",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseReceivingActions_StockDocumentProvisionalItems_StockDocumentProvisionalItemId",
                table: "PurchaseReceivingActions",
                column: "StockDocumentProvisionalItemId",
                principalTable: "StockDocumentProvisionalItems",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseReceiptAuditEvents_StockDocumentProvisionalItems_StockDocumentProvisionalItemId",
                table: "PurchaseReceiptAuditEvents");

            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseReceivingActions_StockDocumentProvisionalItems_StockDocumentProvisionalItemId",
                table: "PurchaseReceivingActions");

            migrationBuilder.DropTable(
                name: "StockDocumentProvisionalItems");

            migrationBuilder.DropIndex(
                name: "UX_StockDocument_EditablePurchaseReceipt",
                table: "StockDocument");

            migrationBuilder.DropColumn(
                name: "EditablePurchaseReceiptKey",
                table: "StockDocument");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseReceivingActions_StockDocumentProvisionalItemId",
                table: "PurchaseReceivingActions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PurchaseReceivingActions_Target",
                table: "PurchaseReceivingActions");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseReceiptAuditEvents_StockDocumentProvisionalItemId",
                table: "PurchaseReceiptAuditEvents");

            migrationBuilder.DropColumn(
                name: "AfterProvisionalStateJson",
                table: "PurchaseReceivingActions");

            migrationBuilder.DropColumn(
                name: "BeforeProvisionalStateJson",
                table: "PurchaseReceivingActions");

            migrationBuilder.DropColumn(
                name: "CommandPayloadHash",
                table: "PurchaseReceivingActions");

            migrationBuilder.DropColumn(
                name: "StockDocumentProvisionalItemId",
                table: "PurchaseReceivingActions");

            migrationBuilder.DropColumn(
                name: "StockDocumentProvisionalItemId",
                table: "PurchaseReceiptAuditEvents");

            migrationBuilder.AlterColumn<int>(
                name: "StockDocumentLineId",
                table: "PurchaseReceivingActions",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "ProductVariantId",
                table: "PurchaseReceivingActions",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "ProductUnitConversionId",
                table: "PurchaseReceivingActions",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "UX_StockDocument_ActiveReceivingDraft",
                table: "StockDocument",
                columns: new[] { "StoreId", "PurchaseOrderId" },
                unique: true,
                filter: "[IsDeleted] = 0 AND [Type] = 1 AND [ReceiptSource] = 2 AND [ReceivingSessionState] = 1 AND [PurchaseOrderId] IS NOT NULL");
        }
    }
}
