using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase515_PosFinalizePendingApprovaluser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "OrderInventoryIssueLineId",
                table: "OrderInventoryIssueActions",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrderInventoryIssueActions_OrderInventoryIssueLineId",
                table: "OrderInventoryIssueActions",
                column: "OrderInventoryIssueLineId");

            migrationBuilder.AddForeignKey(
                name: "FK_OrderInventoryIssueActions_OrderInventoryIssueLines_OrderInventoryIssueLineId",
                table: "OrderInventoryIssueActions",
                column: "OrderInventoryIssueLineId",
                principalTable: "OrderInventoryIssueLines",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_OrderInventoryIssueActions_OrderInventoryIssueLines_OrderInventoryIssueLineId",
                table: "OrderInventoryIssueActions");

            migrationBuilder.DropIndex(
                name: "IX_OrderInventoryIssueActions_OrderInventoryIssueLineId",
                table: "OrderInventoryIssueActions");

            migrationBuilder.DropColumn(
                name: "OrderInventoryIssueLineId",
                table: "OrderInventoryIssueActions");
        }
    }
}
