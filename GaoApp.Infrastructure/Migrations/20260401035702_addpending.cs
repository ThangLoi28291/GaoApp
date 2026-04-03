using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class addpending : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastOverdueNotifiedAtUtc",
                table: "OrderInventoryIssues",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "OverdueSinceUtc",
                table: "OrderInventoryIssues",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrderInventoryIssues_StoreId_IsOverdue_LastOverdueNotifiedAtUtc_IsDeleted",
                table: "OrderInventoryIssues",
                columns: new[] { "StoreId", "IsOverdue", "LastOverdueNotifiedAtUtc", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderInventoryIssues_StoreId_IsOverdue_OverdueSinceUtc_IsDeleted",
                table: "OrderInventoryIssues",
                columns: new[] { "StoreId", "IsOverdue", "OverdueSinceUtc", "IsDeleted" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OrderInventoryIssues_StoreId_IsOverdue_LastOverdueNotifiedAtUtc_IsDeleted",
                table: "OrderInventoryIssues");

            migrationBuilder.DropIndex(
                name: "IX_OrderInventoryIssues_StoreId_IsOverdue_OverdueSinceUtc_IsDeleted",
                table: "OrderInventoryIssues");

            migrationBuilder.DropColumn(
                name: "LastOverdueNotifiedAtUtc",
                table: "OrderInventoryIssues");

            migrationBuilder.DropColumn(
                name: "OverdueSinceUtc",
                table: "OrderInventoryIssues");
        }
    }
}
