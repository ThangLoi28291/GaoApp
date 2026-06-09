using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDisplayPromotionPriority : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CountdownToUtc",
                table: "DisplayPromotions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsFlashSale",
                table: "DisplayPromotions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsFullscreen",
                table: "DisplayPromotions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "Priority",
                table: "DisplayPromotions",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CountdownToUtc",
                table: "DisplayPromotions");

            migrationBuilder.DropColumn(
                name: "IsFlashSale",
                table: "DisplayPromotions");

            migrationBuilder.DropColumn(
                name: "IsFullscreen",
                table: "DisplayPromotions");

            migrationBuilder.DropColumn(
                name: "Priority",
                table: "DisplayPromotions");
        }
    }
}
