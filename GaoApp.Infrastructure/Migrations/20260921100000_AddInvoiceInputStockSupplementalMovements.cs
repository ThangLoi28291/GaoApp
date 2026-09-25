using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260921100000_AddInvoiceInputStockSupplementalMovements")]
public sealed class AddInvoiceInputStockSupplementalMovements : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "InvoiceInputStockSupplementalMovements",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                WarehouseId = table.Column<int>(type: "int", nullable: false),
                ProductVariantId = table.Column<int>(type: "int", nullable: false),
                EffectiveAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                QuantityChange = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                MovementType = table.Column<int>(type: "int", nullable: false),
                LegacySourceKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                SourcePeriod = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
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
                table.PrimaryKey("PK_InvoiceInputStockSupplementalMovements", x => x.Id);
                table.ForeignKey(
                    name: "FK_InvoiceInputStockSupplementalMovements_ProductVariant_ProductVariantId",
                    column: x => x.ProductVariantId,
                    principalTable: "ProductVariant",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_InvoiceInputStockSupplementalMovements_Stores_StoreId",
                    column: x => x.StoreId,
                    principalTable: "Stores",
                    principalColumn: "Id");
                table.ForeignKey(
                    name: "FK_InvoiceInputStockSupplementalMovements_Warehouses_WarehouseId",
                    column: x => x.WarehouseId,
                    principalTable: "Warehouses",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_InvoiceInputStockSupplementalMovements_ProductVariantId",
            table: "InvoiceInputStockSupplementalMovements",
            column: "ProductVariantId");

        migrationBuilder.CreateIndex(
            name: "IX_InvoiceInputStockSupplementalMovements_WarehouseId",
            table: "InvoiceInputStockSupplementalMovements",
            column: "WarehouseId");

        migrationBuilder.CreateIndex(
            name: "IX_InvoiceInputStockSupplementalMovements_Store_Warehouse_Variant_EffectiveAt",
            table: "InvoiceInputStockSupplementalMovements",
            columns: new[] { "StoreId", "WarehouseId", "ProductVariantId", "EffectiveAtUtc" });

        migrationBuilder.CreateIndex(
            name: "UX_InvoiceInputStockSupplementalMovements_Store_LegacySourceKey",
            table: "InvoiceInputStockSupplementalMovements",
            columns: new[] { "StoreId", "LegacySourceKey" },
            unique: true,
            filter: "[StoreId] IS NOT NULL AND [LegacySourceKey] IS NOT NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "InvoiceInputStockSupplementalMovements");
    }
}
