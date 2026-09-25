using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260826150000_AddInputInvoiceItemCatalogMapping")]
public sealed partial class AddInputInvoiceItemCatalogMapping : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "NormalizedItemName",
            table: "InputInvoiceDetail",
            type: "nvarchar(500)",
            maxLength: 500,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "NormalizedSupplierItemCode",
            table: "InputInvoiceDetail",
            type: "nvarchar(200)",
            maxLength: 200,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "NormalizedUnitName",
            table: "InputInvoiceDetail",
            type: "nvarchar(100)",
            maxLength: 100,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "SupplierItemCode",
            table: "InputInvoiceDetail",
            type: "nvarchar(200)",
            maxLength: 200,
            nullable: true);

        // Deterministic identity evidence only. Historical receipt-line maps are
        // deliberately not used to synthesize reusable Supplier mappings.
        migrationBuilder.Sql("""
            UPDATE [InputInvoiceDetail]
               SET [NormalizedItemName] = NULLIF(UPPER(LTRIM(RTRIM(
                     REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
                       TRANSLATE([ItemName], NCHAR(9)+NCHAR(10)+NCHAR(13)+NCHAR(160), N'    '),
                       N'  ', N' '), N'  ', N' '), N'  ', N' '), N'  ', N' '),
                       N'  ', N' '), N'  ', N' '), N'  ', N' '), N'  ', N' ')
                   ))), N''),
                   [NormalizedUnitName] = NULLIF(UPPER(LTRIM(RTRIM(
                     REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
                       TRANSLATE([UnitName], NCHAR(9)+NCHAR(10)+NCHAR(13)+NCHAR(160), N'    '),
                       N'  ', N' '), N'  ', N' '), N'  ', N' '), N'  ', N' '),
                       N'  ', N' '), N'  ', N' '), N'  ', N' '), N'  ', N' ')
                   ))), N'')
             WHERE [IsDeleted] = 0;
            """);

        migrationBuilder.CreateTable(
            name: "InputInvoiceItemCatalogMap",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                StoreId = table.Column<int>(type: "int", nullable: false),
                SupplierId = table.Column<int>(type: "int", nullable: false),
                SupplierItemCode = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                NormalizedSupplierItemCode = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                SupplierItemName = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                NormalizedSupplierItemName = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                SupplierUnitName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                NormalizedSupplierUnitName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                ProductVariantId = table.Column<int>(type: "int", nullable: false),
                ProductUnitConversionId = table.Column<int>(type: "int", nullable: false),
                ConfirmedUnitId = table.Column<int>(type: "int", nullable: false),
                ConfirmedFactor = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                ConfirmedBaseUnitId = table.Column<int>(type: "int", nullable: false),
                IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
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
                table.PrimaryKey("PK_InputInvoiceItemCatalogMap", x => x.Id);
                table.ForeignKey(
                    name: "FK_InputInvoiceItemCatalogMap_ProductUnitConversion_ProductUnitConversionId",
                    column: x => x.ProductUnitConversionId,
                    principalTable: "ProductUnitConversion",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_InputInvoiceItemCatalogMap_ProductVariant_ProductVariantId",
                    column: x => x.ProductVariantId,
                    principalTable: "ProductVariant",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_InputInvoiceItemCatalogMap_Stores_StoreId",
                    column: x => x.StoreId,
                    principalTable: "Stores",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.NoAction);
                table.ForeignKey(
                    name: "FK_InputInvoiceItemCatalogMap_Suppliers_SupplierId",
                    column: x => x.SupplierId,
                    principalTable: "Suppliers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_InputInvoiceItemCatalogMap_Unit_ConfirmedBaseUnitId",
                    column: x => x.ConfirmedBaseUnitId,
                    principalTable: "Unit",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_InputInvoiceItemCatalogMap_Unit_ConfirmedUnitId",
                    column: x => x.ConfirmedUnitId,
                    principalTable: "Unit",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_InputInvoiceDetail_InputInvoiceHeadId_NormalizedSupplierItemCode_NormalizedUnitName",
            table: "InputInvoiceDetail",
            columns: new[] { "InputInvoiceHeadId", "NormalizedSupplierItemCode", "NormalizedUnitName" });

        migrationBuilder.CreateIndex(
            name: "IX_InputInvoiceItemCatalogMap_ConfirmedBaseUnitId",
            table: "InputInvoiceItemCatalogMap",
            column: "ConfirmedBaseUnitId");

        migrationBuilder.CreateIndex(
            name: "IX_InputInvoiceItemCatalogMap_ConfirmedUnitId",
            table: "InputInvoiceItemCatalogMap",
            column: "ConfirmedUnitId");

        migrationBuilder.CreateIndex(
            name: "IX_InputInvoiceItemCatalogMap_ProductUnitConversionId",
            table: "InputInvoiceItemCatalogMap",
            column: "ProductUnitConversionId");

        migrationBuilder.CreateIndex(
            name: "IX_InputInvoiceItemCatalogMap_ProductVariantId",
            table: "InputInvoiceItemCatalogMap",
            column: "ProductVariantId");

        migrationBuilder.CreateIndex(
            name: "IX_InputInvoiceItemCatalogMap_SupplierId",
            table: "InputInvoiceItemCatalogMap",
            column: "SupplierId");

        migrationBuilder.CreateIndex(
            name: "IX_InputInvoiceItemCatalogMap_StoreId_ProductVariantId_ProductUnitConversionId_IsActive",
            table: "InputInvoiceItemCatalogMap",
            columns: new[] { "StoreId", "ProductVariantId", "ProductUnitConversionId", "IsActive" });

        migrationBuilder.CreateIndex(
            name: "UX_InputInvoiceItemCatalogMap_CodeUnit_Active",
            table: "InputInvoiceItemCatalogMap",
            columns: new[] { "StoreId", "SupplierId", "NormalizedSupplierItemCode", "NormalizedSupplierUnitName" },
            unique: true,
            filter: "[NormalizedSupplierItemCode] IS NOT NULL AND [IsActive] = 1 AND [IsDeleted] = 0");

        migrationBuilder.CreateIndex(
            name: "UX_InputInvoiceItemCatalogMap_NameUnit_Active",
            table: "InputInvoiceItemCatalogMap",
            columns: new[] { "StoreId", "SupplierId", "NormalizedSupplierItemName", "NormalizedSupplierUnitName" },
            unique: true,
            filter: "[NormalizedSupplierItemCode] IS NULL AND [IsActive] = 1 AND [IsDeleted] = 0");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "InputInvoiceItemCatalogMap");
        migrationBuilder.DropIndex(
            name: "IX_InputInvoiceDetail_InputInvoiceHeadId_NormalizedSupplierItemCode_NormalizedUnitName",
            table: "InputInvoiceDetail");
        migrationBuilder.DropColumn(name: "NormalizedItemName", table: "InputInvoiceDetail");
        migrationBuilder.DropColumn(name: "NormalizedSupplierItemCode", table: "InputInvoiceDetail");
        migrationBuilder.DropColumn(name: "NormalizedUnitName", table: "InputInvoiceDetail");
        migrationBuilder.DropColumn(name: "SupplierItemCode", table: "InputInvoiceDetail");
    }
}
