using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase225OrderLegalEntityAllocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "HasMultipleLegalEntities",
                table: "Orders",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "LegalEntityAllocatedAtUtc",
                table: "Orders",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LegalEntityCount",
                table: "Orders",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "OrderLegalEntityAllocations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    OrderLineId = table.Column<int>(type: "int", nullable: false),
                    ProductVariantId = table.Column<int>(type: "int", nullable: false),
                    ProductUnitConversionId = table.Column<int>(type: "int", nullable: true),
                    LegalEntityId = table.Column<int>(type: "int", nullable: false),
                    WarehouseId = table.Column<int>(type: "int", nullable: false),
                    InventoryTransactionId = table.Column<int>(type: "int", nullable: true),
                    SalePriority = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    BaseQuantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    LineTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    DiscountAllocated = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    PromotionDiscountAllocated = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ComboDiscountAllocated = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    OrderDiscountAllocated = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    VoucherDiscountAllocated = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    NetAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    AllocationSource = table.Column<byte>(type: "tinyint", nullable: false),
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
                    table.PrimaryKey("PK_OrderLegalEntityAllocations", x => x.Id);
                    table.CheckConstraint("CK_OrderLegalEntityAllocations_Amounts_NonNegative", "[LineTotal] >= 0 AND [DiscountAllocated] >= 0 AND [PromotionDiscountAllocated] >= 0 AND [ComboDiscountAllocated] >= 0 AND [OrderDiscountAllocated] >= 0 AND [VoucherDiscountAllocated] >= 0 AND [NetAmount] >= 0");
                    table.CheckConstraint("CK_OrderLegalEntityAllocations_Quantity_Positive", "[Quantity] > 0 AND [BaseQuantity] > 0");
                    table.CheckConstraint("CK_OrderLegalEntityAllocations_SalePriority_Positive", "[SalePriority] > 0");
                    table.ForeignKey(
                        name: "FK_OrderLegalEntityAllocations_InventoryTransactions_InventoryTransactionId",
                        column: x => x.InventoryTransactionId,
                        principalTable: "InventoryTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrderLegalEntityAllocations_LegalEntities_StoreId_LegalEntityId",
                        columns: x => new { x.StoreId, x.LegalEntityId },
                        principalTable: "LegalEntities",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrderLegalEntityAllocations_OrderLines_OrderLineId",
                        column: x => x.OrderLineId,
                        principalTable: "OrderLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrderLegalEntityAllocations_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrderLegalEntityAllocations_ProductUnitConversion_ProductUnitConversionId",
                        column: x => x.ProductUnitConversionId,
                        principalTable: "ProductUnitConversion",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrderLegalEntityAllocations_ProductVariant_ProductVariantId",
                        column: x => x.ProductVariantId,
                        principalTable: "ProductVariant",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrderLegalEntityAllocations_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OrderLegalEntityAllocations_Warehouses_StoreId_WarehouseId",
                        columns: x => new { x.StoreId, x.WarehouseId },
                        principalTable: "Warehouses",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_StoreId_HasMultipleLegalEntities_LegalEntityAllocatedAtUtc_IsDeleted",
                table: "Orders",
                columns: new[] { "StoreId", "HasMultipleLegalEntities", "LegalEntityAllocatedAtUtc", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderLegalEntityAllocations_InventoryTransactionId",
                table: "OrderLegalEntityAllocations",
                column: "InventoryTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderLegalEntityAllocations_OrderId",
                table: "OrderLegalEntityAllocations",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderLegalEntityAllocations_OrderLineId",
                table: "OrderLegalEntityAllocations",
                column: "OrderLineId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderLegalEntityAllocations_ProductUnitConversionId",
                table: "OrderLegalEntityAllocations",
                column: "ProductUnitConversionId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderLegalEntityAllocations_ProductVariantId",
                table: "OrderLegalEntityAllocations",
                column: "ProductVariantId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderLegalEntityAllocations_StoreId_InventoryTransactionId",
                table: "OrderLegalEntityAllocations",
                columns: new[] { "StoreId", "InventoryTransactionId" },
                unique: true,
                filter: "[InventoryTransactionId] IS NOT NULL AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_OrderLegalEntityAllocations_StoreId_LegalEntityId_OrderId_IsDeleted",
                table: "OrderLegalEntityAllocations",
                columns: new[] { "StoreId", "LegalEntityId", "OrderId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderLegalEntityAllocations_StoreId_OrderId_IsDeleted",
                table: "OrderLegalEntityAllocations",
                columns: new[] { "StoreId", "OrderId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderLegalEntityAllocations_StoreId_OrderId_OrderLineId_LegalEntityId_WarehouseId",
                table: "OrderLegalEntityAllocations",
                columns: new[] { "StoreId", "OrderId", "OrderLineId", "LegalEntityId", "WarehouseId" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_OrderLegalEntityAllocations_StoreId_WarehouseId_ProductVariantId_IsDeleted",
                table: "OrderLegalEntityAllocations",
                columns: new[] { "StoreId", "WarehouseId", "ProductVariantId", "IsDeleted" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrderLegalEntityAllocations");

            migrationBuilder.DropIndex(
                name: "IX_Orders_StoreId_HasMultipleLegalEntities_LegalEntityAllocatedAtUtc_IsDeleted",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "HasMultipleLegalEntities",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "LegalEntityAllocatedAtUtc",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "LegalEntityCount",
                table: "Orders");
        }
    }
}
