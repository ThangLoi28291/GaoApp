using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GaoApp.Infrastructure.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261014020000_AddReceiptGlobalDiscountAmount")]
public sealed class AddReceiptGlobalDiscountAmount : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>(name: "GlobalDiscountAmount", table: "PurchaseReceiptPricingPlan",
            type: "decimal(18,2)", nullable: false, defaultValue: 0m);
        migrationBuilder.AddCheckConstraint(name: "CK_PurchaseReceiptPricingPlan_GlobalDiscount", table: "PurchaseReceiptPricingPlan",
            sql: "[GlobalDiscountAmount] >= 0 AND ([GlobalDiscountAmount] = 0 OR [GlobalDiscountPercent] = 0)");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(name: "CK_PurchaseReceiptPricingPlan_GlobalDiscount", table: "PurchaseReceiptPricingPlan");
        migrationBuilder.DropColumn(name: "GlobalDiscountAmount", table: "PurchaseReceiptPricingPlan");
    }
}
