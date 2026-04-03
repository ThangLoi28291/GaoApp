using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class editfifoinventorycostlayer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "RemainingOpenProvisionalQty",
                table: "InventoryCostLayers",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ResolvedProvisionalQty",
                table: "InventoryCostLayers",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ResolvedAmount",
                table: "InventoryCostLayerAllocations",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ResolvedQuantity",
                table: "InventoryCostLayerAllocations",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RemainingOpenProvisionalQty",
                table: "InventoryCostLayers");

            migrationBuilder.DropColumn(
                name: "ResolvedProvisionalQty",
                table: "InventoryCostLayers");

            migrationBuilder.DropColumn(
                name: "ResolvedAmount",
                table: "InventoryCostLayerAllocations");

            migrationBuilder.DropColumn(
                name: "ResolvedQuantity",
                table: "InventoryCostLayerAllocations");
        }
    }
}
