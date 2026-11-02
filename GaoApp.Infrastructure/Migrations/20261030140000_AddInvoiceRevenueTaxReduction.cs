using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261030140000_AddInvoiceRevenueTaxReduction")]
public sealed class AddInvoiceRevenueTaxReduction : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>("RevenueTaxPercentage", "InvoiceHeads", type: "int", nullable: true);
        migrationBuilder.AddColumn<decimal>("RevenueTaxReductionAmount", "InvoiceHeads", type: "decimal(18,2)", nullable: false, defaultValue: 0m);
        migrationBuilder.AddCheckConstraint("CK_InvoiceHeads_RevenueTax", "InvoiceHeads",
            "([RevenueTaxPercentage] IS NULL AND [RevenueTaxReductionAmount] = 0) OR ([RevenueTaxPercentage] IS NOT NULL AND [RevenueTaxPercentage] IN (1,2,3,5) AND [RevenueTaxReductionAmount] >= 0)");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint("CK_InvoiceHeads_RevenueTax", "InvoiceHeads");
        migrationBuilder.DropColumn("RevenueTaxPercentage", "InvoiceHeads");
        migrationBuilder.DropColumn("RevenueTaxReductionAmount", "InvoiceHeads");
    }
}
