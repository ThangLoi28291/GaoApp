using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260817090000_AddPurchaseReceiptCostCapitalizationPolicy")]
public sealed class AddPurchaseReceiptCostCapitalizationPolicy : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "CapitalizeFreightInInventoryCost",
            table: "StockDocument",
            type: "bit",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<bool>(
            name: "IncludeVatInInventoryCost",
            table: "StockDocument",
            type: "bit",
            nullable: false,
            defaultValue: false);

        // Confirmed receipts must continue to describe the cost that was
        // historically posted (the legacy behavior included VAT and freight).
        migrationBuilder.Sql(
            """
            UPDATE [dbo].[StockDocument]
            SET [IncludeVatInInventoryCost] = CASE WHEN [HasVat] = 1 THEN 1 ELSE 0 END,
                [CapitalizeFreightInInventoryCost] = CASE WHEN [HasFreight] = 1 THEN 1 ELSE 0 END
            WHERE [Status] = 3;
            """);

        // Unconfirmed legacy allocations represented the old automatic
        // capitalization default. The new default is explicit opt-in.
        migrationBuilder.Sql(
            """
            UPDATE line
            SET line.[FreightAllocation] = 0
            FROM [dbo].[StockDocumentLine] AS line
            INNER JOIN [dbo].[StockDocument] AS document
                ON document.[Id] = line.[StockDocumentId]
            WHERE document.[Status] <> 3
              AND line.[FreightAllocation] <> 0;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "CapitalizeFreightInInventoryCost",
            table: "StockDocument");

        migrationBuilder.DropColumn(
            name: "IncludeVatInInventoryCost",
            table: "StockDocument");
    }
}
