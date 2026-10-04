using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPurchaseReceiptPricingPlans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PurchaseReceiptPricingPlan",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StockDocumentId = table.Column<int>(type: "int", nullable: false),
                    State = table.Column<int>(type: "int", nullable: false),
                    ActualBillTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    GlobalDiscountPercent = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    SystemTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ReceiptRowVersionSnapshot = table.Column<byte[]>(type: "varbinary(8)", maxLength: 8, nullable: false),
                    PhysicalDependencyHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    AppliedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ConfirmedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
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
                    table.PrimaryKey("PK_PurchaseReceiptPricingPlan", x => x.Id);
                    table.UniqueConstraint("AK_PurchaseReceiptPricingPlan_StoreId_Id", x => new { x.StoreId, x.Id });
                    table.CheckConstraint("CK_PurchaseReceiptPricingPlan_Money", "[ActualBillTotal] > 0 AND [SystemTotal] > 0 AND [GlobalDiscountPercent] >= 0 AND [GlobalDiscountPercent] < 100");
                    table.CheckConstraint("CK_PurchaseReceiptPricingPlan_State", "[State] = 1 OR [State] = 2 OR [State] = 3");
                    table.ForeignKey(
                        name: "FK_PurchaseReceiptPricingPlan_StockDocument_StockDocumentId",
                        column: x => x.StockDocumentId,
                        principalTable: "StockDocument",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseReceiptPricingPlan_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "PurchaseReceiptGiftValuation",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PricingPlanId = table.Column<int>(type: "int", nullable: false),
                    ProductVariantId = table.Column<int>(type: "int", nullable: false),
                    UnitId = table.Column<int>(type: "int", nullable: false),
                    ConversionId = table.Column<int>(type: "int", nullable: false),
                    Factor = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    UnitValueBeforeVat = table.Column<decimal>(type: "decimal(28,12)", precision: 28, scale: 12, nullable: false),
                    Source = table.Column<int>(type: "int", nullable: false),
                    HistoricalReceiptLineId = table.Column<int>(type: "int", nullable: true),
                    HistoricalConfirmedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
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
                    table.PrimaryKey("PK_PurchaseReceiptGiftValuation", x => x.Id);
                    table.CheckConstraint("CK_PurchaseReceiptGiftValuation_Value", "[Factor] > 0 AND [UnitValueBeforeVat] > 0 AND ([Source] = 1 OR [Source] = 2 OR [Source] = 3)");
                    table.ForeignKey(
                        name: "FK_PurchaseReceiptGiftValuation_PurchaseReceiptPricingPlan_StoreId_PricingPlanId",
                        columns: x => new { x.StoreId, x.PricingPlanId },
                        principalTable: "PurchaseReceiptPricingPlan",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseReceiptGiftValuation_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "PurchaseReceiptPricingPlanLine",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PricingPlanId = table.Column<int>(type: "int", nullable: false),
                    StockDocumentLineId = table.Column<int>(type: "int", nullable: false),
                    LineNo = table.Column<int>(type: "int", nullable: false),
                    ProductVariantIdSnapshot = table.Column<int>(type: "int", nullable: false),
                    ProductIdSnapshot = table.Column<int>(type: "int", nullable: false),
                    PhysicalUnitIdSnapshot = table.Column<int>(type: "int", nullable: false),
                    PhysicalConversionIdSnapshot = table.Column<int>(type: "int", nullable: true),
                    PhysicalFactorSnapshot = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    PhysicalQuantitySnapshot = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    PhysicalBaseQuantitySnapshot = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    ReceiptLineRowVersionSnapshot = table.Column<byte[]>(type: "varbinary(8)", maxLength: 8, nullable: false),
                    BillUnitId = table.Column<int>(type: "int", nullable: false),
                    BillConversionId = table.Column<int>(type: "int", nullable: false),
                    BillFactor = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    BillQuantity = table.Column<decimal>(type: "decimal(28,9)", precision: 28, scale: 9, nullable: false),
                    BillUnitPriceBeforeVat = table.Column<decimal>(type: "decimal(28,12)", precision: 28, scale: 12, nullable: false),
                    PurchasedBaseQuantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    GiftBaseQuantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    BaselineAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    BaselineResidual = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    GiftBurden = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    PurchasedAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    GiftAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    FinalAmountBeforeVat = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    EffectiveUnitPriceBeforeVat = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
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
                    table.PrimaryKey("PK_PurchaseReceiptPricingPlanLine", x => x.Id);
                    table.UniqueConstraint("AK_PurchaseReceiptPricingPlanLine_StoreId_PricingPlanId_Id", x => new { x.StoreId, x.PricingPlanId, x.Id });
                    table.CheckConstraint("CK_PurchaseReceiptPricingPlanLine_Money", "[BillUnitPriceBeforeVat] >= 0 AND [BaselineAmount] >= 0 AND [GiftBurden] >= 0 AND [PurchasedAmount] >= 0 AND [GiftAmount] >= 0 AND [FinalAmountBeforeVat] > 0");
                    table.CheckConstraint("CK_PurchaseReceiptPricingPlanLine_Quantity", "[PhysicalQuantitySnapshot] > 0 AND [PhysicalBaseQuantitySnapshot] > 0 AND [PhysicalFactorSnapshot] > 0 AND [BillFactor] > 0 AND [BillQuantity] >= 0 AND [PurchasedBaseQuantity] >= 0 AND [GiftBaseQuantity] >= 0");
                    table.ForeignKey(
                        name: "FK_PurchaseReceiptPricingPlanLine_PurchaseReceiptPricingPlan_StoreId_PricingPlanId",
                        columns: x => new { x.StoreId, x.PricingPlanId },
                        principalTable: "PurchaseReceiptPricingPlan",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseReceiptPricingPlanLine_StockDocumentLine_StockDocumentLineId",
                        column: x => x.StockDocumentLineId,
                        principalTable: "StockDocumentLine",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseReceiptPricingPlanLine_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "PurchaseReceiptPricingRule",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PricingPlanId = table.Column<int>(type: "int", nullable: false),
                    RuleKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    DiscountPercent = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    GiftMode = table.Column<int>(type: "int", nullable: true),
                    GiftPlanLineId = table.Column<int>(type: "int", nullable: true),
                    GiftUnitId = table.Column<int>(type: "int", nullable: true),
                    GiftConversionId = table.Column<int>(type: "int", nullable: true),
                    GiftFactor = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    GiftQuantity = table.Column<decimal>(type: "decimal(28,9)", precision: 28, scale: 9, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
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
                    table.PrimaryKey("PK_PurchaseReceiptPricingRule", x => x.Id);
                    table.UniqueConstraint("AK_PurchaseReceiptPricingRule_StoreId_PricingPlanId_Id", x => new { x.StoreId, x.PricingPlanId, x.Id });
                    table.CheckConstraint("CK_PurchaseReceiptPricingRule_Type", "[Type] = 1 AND ([GiftMode] = 1 OR [GiftMode] = 2) AND [GiftPlanLineId] IS NOT NULL AND [GiftUnitId] IS NOT NULL AND [GiftFactor] > 0 AND [GiftQuantity] > 0 AND [DiscountPercent] = 0 OR [Type] = 2 AND [GiftPlanLineId] IS NULL AND [GiftMode] IS NULL AND [DiscountPercent] > 0 AND [DiscountPercent] < 100");
                    table.ForeignKey(
                        name: "FK_PurchaseReceiptPricingRule_PurchaseReceiptPricingPlanLine_StoreId_PricingPlanId_GiftPlanLineId",
                        columns: x => new { x.StoreId, x.PricingPlanId, x.GiftPlanLineId },
                        principalTable: "PurchaseReceiptPricingPlanLine",
                        principalColumns: new[] { "StoreId", "PricingPlanId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseReceiptPricingRule_PurchaseReceiptPricingPlan_StoreId_PricingPlanId",
                        columns: x => new { x.StoreId, x.PricingPlanId },
                        principalTable: "PurchaseReceiptPricingPlan",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseReceiptPricingRule_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "PurchaseReceiptPricingRuleSource",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PricingPlanId = table.Column<int>(type: "int", nullable: false),
                    PricingRuleId = table.Column<int>(type: "int", nullable: false),
                    PricingPlanLineId = table.Column<int>(type: "int", nullable: false),
                    BaselineAmountSnapshot = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ResidualAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
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
                    table.PrimaryKey("PK_PurchaseReceiptPricingRuleSource", x => x.Id);
                    table.CheckConstraint("CK_PurchaseReceiptPricingRuleSource_Money", "[BaselineAmountSnapshot] > 0 AND [Amount] >= 0 AND [ResidualAmount] >= 0");
                    table.ForeignKey(
                        name: "FK_PurchaseReceiptPricingRuleSource_PurchaseReceiptPricingPlanLine_StoreId_PricingPlanId_PricingPlanLineId",
                        columns: x => new { x.StoreId, x.PricingPlanId, x.PricingPlanLineId },
                        principalTable: "PurchaseReceiptPricingPlanLine",
                        principalColumns: new[] { "StoreId", "PricingPlanId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseReceiptPricingRuleSource_PurchaseReceiptPricingRule_StoreId_PricingPlanId_PricingRuleId",
                        columns: x => new { x.StoreId, x.PricingPlanId, x.PricingRuleId },
                        principalTable: "PurchaseReceiptPricingRule",
                        principalColumns: new[] { "StoreId", "PricingPlanId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseReceiptPricingRuleSource_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReceiptGiftValuation_StoreId_PricingPlanId_ProductVariantId",
                table: "PurchaseReceiptGiftValuation",
                columns: new[] { "StoreId", "PricingPlanId", "ProductVariantId" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReceiptPricingPlan_StockDocumentId",
                table: "PurchaseReceiptPricingPlan",
                column: "StockDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReceiptPricingPlan_StoreId_StockDocumentId",
                table: "PurchaseReceiptPricingPlan",
                columns: new[] { "StoreId", "StockDocumentId" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReceiptPricingPlanLine_StockDocumentLineId",
                table: "PurchaseReceiptPricingPlanLine",
                column: "StockDocumentLineId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReceiptPricingPlanLine_StoreId_PricingPlanId_StockDocumentLineId",
                table: "PurchaseReceiptPricingPlanLine",
                columns: new[] { "StoreId", "PricingPlanId", "StockDocumentLineId" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReceiptPricingRule_StoreId_PricingPlanId_GiftPlanLineId",
                table: "PurchaseReceiptPricingRule",
                columns: new[] { "StoreId", "PricingPlanId", "GiftPlanLineId" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReceiptPricingRule_StoreId_PricingPlanId_RuleKey",
                table: "PurchaseReceiptPricingRule",
                columns: new[] { "StoreId", "PricingPlanId", "RuleKey" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReceiptPricingRuleSource_StoreId_PricingPlanId_PricingPlanLineId",
                table: "PurchaseReceiptPricingRuleSource",
                columns: new[] { "StoreId", "PricingPlanId", "PricingPlanLineId" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReceiptPricingRuleSource_StoreId_PricingPlanId_PricingRuleId",
                table: "PurchaseReceiptPricingRuleSource",
                columns: new[] { "StoreId", "PricingPlanId", "PricingRuleId" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReceiptPricingRuleSource_StoreId_PricingRuleId_PricingPlanLineId",
                table: "PurchaseReceiptPricingRuleSource",
                columns: new[] { "StoreId", "PricingRuleId", "PricingPlanLineId" },
                unique: true,
                filter: "[IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PurchaseReceiptGiftValuation");

            migrationBuilder.DropTable(
                name: "PurchaseReceiptPricingRuleSource");

            migrationBuilder.DropTable(
                name: "PurchaseReceiptPricingRule");

            migrationBuilder.DropTable(
                name: "PurchaseReceiptPricingPlanLine");

            migrationBuilder.DropTable(
                name: "PurchaseReceiptPricingPlan");
        }
    }
}
