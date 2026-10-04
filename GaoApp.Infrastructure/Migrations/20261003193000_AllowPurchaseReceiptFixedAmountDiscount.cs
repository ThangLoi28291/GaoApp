using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations;

public partial class AllowPurchaseReceiptFixedAmountDiscount : Migration
{
    private const string PreviousConstraint = "[Type] = 1 AND ([GiftMode] = 1 OR [GiftMode] = 2) AND [GiftPlanLineId] IS NOT NULL AND [GiftUnitId] IS NOT NULL AND [GiftFactor] > 0 AND [GiftQuantity] > 0 AND [DiscountPercent] = 0 OR [Type] = 2 AND [GiftPlanLineId] IS NULL AND [GiftMode] IS NULL AND [DiscountPercent] > 0 AND [DiscountPercent] < 100";
    private const string CurrentConstraint = PreviousConstraint + " OR [Type] = 3 AND [GiftPlanLineId] IS NULL AND [GiftMode] IS NULL AND [GiftBillLineId] IS NULL AND [GiftUnitId] IS NULL AND [GiftFactor] = 0 AND [GiftQuantity] = 0 AND [DiscountPercent] = 0 AND [Amount] > 0";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint("CK_PurchaseReceiptPricingRule_Type", "PurchaseReceiptPricingRule");
        migrationBuilder.AddCheckConstraint("CK_PurchaseReceiptPricingRule_Type", "PurchaseReceiptPricingRule", CurrentConstraint);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint("CK_PurchaseReceiptPricingRule_Type", "PurchaseReceiptPricingRule");
        migrationBuilder.AddCheckConstraint("CK_PurchaseReceiptPricingRule_Type", "PurchaseReceiptPricingRule", PreviousConstraint);
    }
}
