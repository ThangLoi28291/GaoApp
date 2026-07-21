using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStockDocumentRevisionRequestFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "HasRevisionRequest",
                table: "StockDocument",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "RevisionRequestNote",
                table: "StockDocument",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RevisionRequestedAtUtc",
                table: "StockDocument",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RevisionRequestedByUserId",
                table: "StockDocument",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RevisionResolvedAtUtc",
                table: "StockDocument",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RevisionResolvedByUserId",
                table: "StockDocument",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockDocument_HasRevisionRequest",
                table: "StockDocument",
                column: "HasRevisionRequest");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StockDocument_HasRevisionRequest",
                table: "StockDocument");

            migrationBuilder.DropColumn(
                name: "HasRevisionRequest",
                table: "StockDocument");

            migrationBuilder.DropColumn(
                name: "RevisionRequestNote",
                table: "StockDocument");

            migrationBuilder.DropColumn(
                name: "RevisionRequestedAtUtc",
                table: "StockDocument");

            migrationBuilder.DropColumn(
                name: "RevisionRequestedByUserId",
                table: "StockDocument");

            migrationBuilder.DropColumn(
                name: "RevisionResolvedAtUtc",
                table: "StockDocument");

            migrationBuilder.DropColumn(
                name: "RevisionResolvedByUserId",
                table: "StockDocument");
        }
    }
}
