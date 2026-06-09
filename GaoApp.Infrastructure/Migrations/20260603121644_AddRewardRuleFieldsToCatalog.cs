using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRewardRuleFieldsToCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsRewardEligibleOverride",
                table: "Products",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "RewardBulkExcludeQuantity",
                table: "Products",
                type: "decimal(18,4)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsRewardEligible",
                table: "Category",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsRewardEligibleOverride",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "RewardBulkExcludeQuantity",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "IsRewardEligible",
                table: "Category");
        }
    }
}
