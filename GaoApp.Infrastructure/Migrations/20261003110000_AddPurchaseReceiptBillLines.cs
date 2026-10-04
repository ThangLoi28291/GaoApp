using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPurchaseReceiptBillLines : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PurchaseReceiptPricingRuleSource_StoreId_PricingRuleId_PricingPlanLineId",
                table: "PurchaseReceiptPricingRuleSource");

            migrationBuilder.AddColumn<int>(
                name: "BillLineId",
                table: "PurchaseReceiptPricingRuleSource",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ParticipatingQuantity",
                table: "PurchaseReceiptPricingRuleSource",
                type: "decimal(28,9)",
                precision: 28,
                scale: 9,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GiftBillLineId",
                table: "PurchaseReceiptPricingRule",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Name",
                table: "PurchaseReceiptPricingRule",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ProgramKey",
                table: "PurchaseReceiptPricingRule",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "BillLayoutVersion",
                table: "PurchaseReceiptPricingPlan",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "PurchaseReceiptBillLine",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PricingPlanId = table.Column<int>(type: "int", nullable: false),
                    BillLineKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    LineNo = table.Column<int>(type: "int", nullable: false),
                    ProductVariantIdSnapshot = table.Column<int>(type: "int", nullable: false),
                    BillUnitId = table.Column<int>(type: "int", nullable: false),
                    BillUnitName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    BillConversionId = table.Column<int>(type: "int", nullable: false),
                    BillFactor = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    BillQuantity = table.Column<decimal>(type: "decimal(28,9)", precision: 28, scale: 9, nullable: false),
                    BillUnitPriceBeforeVat = table.Column<decimal>(type: "decimal(28,12)", precision: 28, scale: 12, nullable: false),
                    IsGift = table.Column<bool>(type: "bit", nullable: false),
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
                    table.PrimaryKey("PK_PurchaseReceiptBillLine", x => x.Id);
                    table.UniqueConstraint("AK_PurchaseReceiptBillLine_StoreId_PricingPlanId_Id", x => new { x.StoreId, x.PricingPlanId, x.Id });
                    table.CheckConstraint("CK_PurchaseReceiptBillLine_Input", "[LineNo] > 0 AND [BillFactor] > 0 AND [BillQuantity] > 0 AND ([IsGift] = 1 AND [BillUnitPriceBeforeVat] = 0 OR [IsGift] = 0 AND [BillUnitPriceBeforeVat] > 0)");
                    table.ForeignKey(
                        name: "FK_PurchaseReceiptBillLine_PurchaseReceiptPricingPlan_StoreId_PricingPlanId",
                        columns: x => new { x.StoreId, x.PricingPlanId },
                        principalTable: "PurchaseReceiptPricingPlan",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseReceiptBillLine_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReceiptPricingRuleSource_StoreId_PricingPlanId_BillLineId",
                table: "PurchaseReceiptPricingRuleSource",
                columns: new[] { "StoreId", "PricingPlanId", "BillLineId" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReceiptPricingRuleSource_StoreId_PricingRuleId_BillLineId",
                table: "PurchaseReceiptPricingRuleSource",
                columns: new[] { "StoreId", "PricingRuleId", "BillLineId" },
                unique: true,
                filter: "[IsDeleted] = 0 AND [BillLineId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReceiptPricingRuleSource_StoreId_PricingRuleId_PricingPlanLineId",
                table: "PurchaseReceiptPricingRuleSource",
                columns: new[] { "StoreId", "PricingRuleId", "PricingPlanLineId" },
                unique: true,
                filter: "[IsDeleted] = 0 AND [BillLineId] IS NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PurchaseReceiptPricingRuleSource_Quantity",
                table: "PurchaseReceiptPricingRuleSource",
                sql: "[BillLineId] IS NULL AND [ParticipatingQuantity] IS NULL OR [BillLineId] IS NOT NULL AND [ParticipatingQuantity] > 0");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReceiptPricingRule_StoreId_PricingPlanId_GiftBillLineId",
                table: "PurchaseReceiptPricingRule",
                columns: new[] { "StoreId", "PricingPlanId", "GiftBillLineId" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReceiptBillLine_StoreId_PricingPlanId_BillLineKey",
                table: "PurchaseReceiptBillLine",
                columns: new[] { "StoreId", "PricingPlanId", "BillLineKey" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReceiptBillLine_StoreId_PricingPlanId_LineNo",
                table: "PurchaseReceiptBillLine",
                columns: new[] { "StoreId", "PricingPlanId", "LineNo" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseReceiptPricingRule_PurchaseReceiptBillLine_StoreId_PricingPlanId_GiftBillLineId",
                table: "PurchaseReceiptPricingRule",
                columns: new[] { "StoreId", "PricingPlanId", "GiftBillLineId" },
                principalTable: "PurchaseReceiptBillLine",
                principalColumns: new[] { "StoreId", "PricingPlanId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseReceiptPricingRuleSource_PurchaseReceiptBillLine_StoreId_PricingPlanId_BillLineId",
                table: "PurchaseReceiptPricingRuleSource",
                columns: new[] { "StoreId", "PricingPlanId", "BillLineId" },
                principalTable: "PurchaseReceiptBillLine",
                principalColumns: new[] { "StoreId", "PricingPlanId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseReceiptPricingRule_PurchaseReceiptBillLine_StoreId_PricingPlanId_GiftBillLineId",
                table: "PurchaseReceiptPricingRule");

            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseReceiptPricingRuleSource_PurchaseReceiptBillLine_StoreId_PricingPlanId_BillLineId",
                table: "PurchaseReceiptPricingRuleSource");

            migrationBuilder.DropTable(
                name: "PurchaseReceiptBillLine");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseReceiptPricingRuleSource_StoreId_PricingPlanId_BillLineId",
                table: "PurchaseReceiptPricingRuleSource");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseReceiptPricingRuleSource_StoreId_PricingRuleId_BillLineId",
                table: "PurchaseReceiptPricingRuleSource");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseReceiptPricingRuleSource_StoreId_PricingRuleId_PricingPlanLineId",
                table: "PurchaseReceiptPricingRuleSource");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PurchaseReceiptPricingRuleSource_Quantity",
                table: "PurchaseReceiptPricingRuleSource");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseReceiptPricingRule_StoreId_PricingPlanId_GiftBillLineId",
                table: "PurchaseReceiptPricingRule");

            migrationBuilder.DropColumn(
                name: "BillLineId",
                table: "PurchaseReceiptPricingRuleSource");

            migrationBuilder.DropColumn(
                name: "ParticipatingQuantity",
                table: "PurchaseReceiptPricingRuleSource");

            migrationBuilder.DropColumn(
                name: "GiftBillLineId",
                table: "PurchaseReceiptPricingRule");

            migrationBuilder.DropColumn(
                name: "Name",
                table: "PurchaseReceiptPricingRule");

            migrationBuilder.DropColumn(
                name: "ProgramKey",
                table: "PurchaseReceiptPricingRule");

            migrationBuilder.DropColumn(
                name: "BillLayoutVersion",
                table: "PurchaseReceiptPricingPlan");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReceiptPricingRuleSource_StoreId_PricingRuleId_PricingPlanLineId",
                table: "PurchaseReceiptPricingRuleSource",
                columns: new[] { "StoreId", "PricingRuleId", "PricingPlanLineId" },
                unique: true,
                filter: "[IsDeleted] = 0");
        }
    }
}
