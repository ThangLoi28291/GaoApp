using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations;

public partial class AddMixedQuantityPromotions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<byte>(
            name: "ComboPricingMode", table: "Promotions", type: "tinyint",
            nullable: false, defaultValue: (byte)1);
        migrationBuilder.AddColumn<decimal>(
            name: "ComboQuantity", table: "Promotions", type: "decimal(18,4)",
            precision: 18, scale: 4, nullable: true);
        migrationBuilder.AddColumn<int>(
            name: "ComboBaseUnitId", table: "Promotions", type: "int", nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "ComboBaseUnitId", table: "Promotions");
        migrationBuilder.DropColumn(name: "ComboQuantity", table: "Promotions");
        migrationBuilder.DropColumn(name: "ComboPricingMode", table: "Promotions");
    }
}
