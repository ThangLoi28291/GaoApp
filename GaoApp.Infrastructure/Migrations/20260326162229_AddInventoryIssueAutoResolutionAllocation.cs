using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryIssueAutoResolutionAllocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AutoResolvedLineCount",
                table: "OrderInventoryIssues",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastAutoResolvedAtUtc",
                table: "OrderInventoryIssues",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AutoDetectedCostResolved",
                table: "OrderInventoryIssueLines",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "AutoDetectedDocumentResolved",
                table: "OrderInventoryIssueLines",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "AutoDetectedInboundQty",
                table: "OrderInventoryIssueLines",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "AutoDetectedRevaluationAmount",
                table: "OrderInventoryIssueLines",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AutoResolveNote",
                table: "OrderInventoryIssueLines",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastAutoResolvedAtUtc",
                table: "OrderInventoryIssueLines",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "OrderInventoryIssueLineAllocations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderInventoryIssueId = table.Column<int>(type: "int", nullable: false),
                    OrderInventoryIssueLineId = table.Column<int>(type: "int", nullable: false),
                    SourceReferenceType = table.Column<int>(type: "int", nullable: false),
                    SourceReferenceId = table.Column<int>(type: "int", nullable: false),
                    SourceReferenceLineId = table.Column<int>(type: "int", nullable: true),
                    InventoryTransactionId = table.Column<int>(type: "int", nullable: false),
                    AllocatedQuantity = table.Column<decimal>(type: "decimal(18,3)", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<int>(type: "int", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<int>(type: "int", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedBy = table.Column<int>(type: "int", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    StoreId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderInventoryIssueLineAllocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderInventoryIssueLineAllocations_InventoryTransactions_InventoryTransactionId",
                        column: x => x.InventoryTransactionId,
                        principalTable: "InventoryTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrderInventoryIssueLineAllocations_OrderInventoryIssueLines_OrderInventoryIssueLineId",
                        column: x => x.OrderInventoryIssueLineId,
                        principalTable: "OrderInventoryIssueLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrderInventoryIssueLineAllocations_OrderInventoryIssues_OrderInventoryIssueId",
                        column: x => x.OrderInventoryIssueId,
                        principalTable: "OrderInventoryIssues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrderInventoryIssueLineAllocations_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrderInventoryIssueLineAllocations_InventoryTransactionId",
                table: "OrderInventoryIssueLineAllocations",
                column: "InventoryTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderInventoryIssueLineAllocations_OrderInventoryIssueId_OrderInventoryIssueLineId_IsDeleted",
                table: "OrderInventoryIssueLineAllocations",
                columns: new[] { "OrderInventoryIssueId", "OrderInventoryIssueLineId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderInventoryIssueLineAllocations_OrderInventoryIssueLineId",
                table: "OrderInventoryIssueLineAllocations",
                column: "OrderInventoryIssueLineId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderInventoryIssueLineAllocations_StoreId",
                table: "OrderInventoryIssueLineAllocations",
                column: "StoreId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrderInventoryIssueLineAllocations");

            migrationBuilder.DropColumn(
                name: "AutoResolvedLineCount",
                table: "OrderInventoryIssues");

            migrationBuilder.DropColumn(
                name: "LastAutoResolvedAtUtc",
                table: "OrderInventoryIssues");

            migrationBuilder.DropColumn(
                name: "AutoDetectedCostResolved",
                table: "OrderInventoryIssueLines");

            migrationBuilder.DropColumn(
                name: "AutoDetectedDocumentResolved",
                table: "OrderInventoryIssueLines");

            migrationBuilder.DropColumn(
                name: "AutoDetectedInboundQty",
                table: "OrderInventoryIssueLines");

            migrationBuilder.DropColumn(
                name: "AutoDetectedRevaluationAmount",
                table: "OrderInventoryIssueLines");

            migrationBuilder.DropColumn(
                name: "AutoResolveNote",
                table: "OrderInventoryIssueLines");

            migrationBuilder.DropColumn(
                name: "LastAutoResolvedAtUtc",
                table: "OrderInventoryIssueLines");
        }
    }
}
