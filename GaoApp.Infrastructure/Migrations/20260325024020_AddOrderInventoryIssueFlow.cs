using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderInventoryIssueFlow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "HasInventoryIssue",
                table: "Orders",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "InventoryIssueApprovedAtUtc",
                table: "Orders",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "InventoryIssueOpenedAtUtc",
                table: "Orders",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "InventoryResolutionStatus",
                table: "Orders",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "OrderInventoryIssues",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Severity = table.Column<int>(type: "int", nullable: false),
                    OpenedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DueAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReadyForApprovalAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ApprovedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ApprovedByUserId = table.Column<int>(type: "int", nullable: true),
                    RejectedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RejectedByUserId = table.Column<int>(type: "int", nullable: true),
                    ReasonType = table.Column<int>(type: "int", nullable: false),
                    InternalNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    IsOverdue = table.Column<bool>(type: "bit", nullable: false),
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
                    table.PrimaryKey("PK_OrderInventoryIssues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderInventoryIssues_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrderInventoryIssues_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OrderInventoryIssues_Users_ApprovedByUserId",
                        column: x => x.ApprovedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrderInventoryIssues_Users_RejectedByUserId",
                        column: x => x.RejectedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OrderInventoryIssueActions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderInventoryIssueId = table.Column<int>(type: "int", nullable: false),
                    ActionType = table.Column<int>(type: "int", nullable: false),
                    ActorUserId = table.Column<int>(type: "int", nullable: true),
                    ActionAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReferenceType = table.Column<int>(type: "int", nullable: false),
                    ReferenceId = table.Column<int>(type: "int", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
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
                    table.PrimaryKey("PK_OrderInventoryIssueActions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderInventoryIssueActions_OrderInventoryIssues_OrderInventoryIssueId",
                        column: x => x.OrderInventoryIssueId,
                        principalTable: "OrderInventoryIssues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrderInventoryIssueActions_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OrderInventoryIssueActions_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OrderInventoryIssueLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderInventoryIssueId = table.Column<int>(type: "int", nullable: false),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    OrderLineId = table.Column<int>(type: "int", nullable: false),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    ProductVariantId = table.Column<int>(type: "int", nullable: false),
                    ProductUnitConversionId = table.Column<int>(type: "int", nullable: true),
                    BarcodeId = table.Column<int>(type: "int", nullable: true),
                    OrderedQty = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    StockBefore = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    StockAfter = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    NegativeQty = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    ProvisionalUnitCost = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    ProvisionalCostAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    RevaluationAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    IsResolved = table.Column<bool>(type: "bit", nullable: false),
                    ResolvedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
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
                    table.PrimaryKey("PK_OrderInventoryIssueLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderInventoryIssueLines_OrderInventoryIssues_OrderInventoryIssueId",
                        column: x => x.OrderInventoryIssueId,
                        principalTable: "OrderInventoryIssues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrderInventoryIssueLines_OrderLines_OrderLineId",
                        column: x => x.OrderLineId,
                        principalTable: "OrderLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrderInventoryIssueLines_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrderInventoryIssueLines_ProductUnitConversion_ProductUnitConversionId",
                        column: x => x.ProductUnitConversionId,
                        principalTable: "ProductUnitConversion",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrderInventoryIssueLines_ProductVariantUnitBarcode_BarcodeId",
                        column: x => x.BarcodeId,
                        principalTable: "ProductVariantUnitBarcode",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrderInventoryIssueLines_ProductVariant_ProductVariantId",
                        column: x => x.ProductVariantId,
                        principalTable: "ProductVariant",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrderInventoryIssueLines_Product_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Product",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrderInventoryIssueLines_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_StoreId_HasInventoryIssue_InventoryResolutionStatus_IsDeleted",
                table: "Orders",
                columns: new[] { "StoreId", "HasInventoryIssue", "InventoryResolutionStatus", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_StoreId_InventoryIssueOpenedAtUtc_IsDeleted",
                table: "Orders",
                columns: new[] { "StoreId", "InventoryIssueOpenedAtUtc", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderInventoryIssueActions_ActorUserId",
                table: "OrderInventoryIssueActions",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderInventoryIssueActions_OrderInventoryIssueId_ActionAtUtc_IsDeleted",
                table: "OrderInventoryIssueActions",
                columns: new[] { "OrderInventoryIssueId", "ActionAtUtc", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderInventoryIssueActions_StoreId_ActionType_ActionAtUtc_IsDeleted",
                table: "OrderInventoryIssueActions",
                columns: new[] { "StoreId", "ActionType", "ActionAtUtc", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderInventoryIssueActions_StoreId_ReferenceType_ReferenceId_IsDeleted",
                table: "OrderInventoryIssueActions",
                columns: new[] { "StoreId", "ReferenceType", "ReferenceId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderInventoryIssueLines_BarcodeId",
                table: "OrderInventoryIssueLines",
                column: "BarcodeId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderInventoryIssueLines_OrderId_IsDeleted",
                table: "OrderInventoryIssueLines",
                columns: new[] { "OrderId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderInventoryIssueLines_OrderInventoryIssueId_IsDeleted",
                table: "OrderInventoryIssueLines",
                columns: new[] { "OrderInventoryIssueId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderInventoryIssueLines_OrderLineId_IsDeleted",
                table: "OrderInventoryIssueLines",
                columns: new[] { "OrderLineId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderInventoryIssueLines_ProductId",
                table: "OrderInventoryIssueLines",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderInventoryIssueLines_ProductUnitConversionId",
                table: "OrderInventoryIssueLines",
                column: "ProductUnitConversionId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderInventoryIssueLines_ProductVariantId",
                table: "OrderInventoryIssueLines",
                column: "ProductVariantId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderInventoryIssueLines_StoreId_ProductUnitConversionId_IsResolved_IsDeleted",
                table: "OrderInventoryIssueLines",
                columns: new[] { "StoreId", "ProductUnitConversionId", "IsResolved", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderInventoryIssueLines_StoreId_ProductVariantId_IsResolved_IsDeleted",
                table: "OrderInventoryIssueLines",
                columns: new[] { "StoreId", "ProductVariantId", "IsResolved", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderInventoryIssues_ApprovedByUserId",
                table: "OrderInventoryIssues",
                column: "ApprovedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderInventoryIssues_OrderId",
                table: "OrderInventoryIssues",
                column: "OrderId",
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_OrderInventoryIssues_RejectedByUserId",
                table: "OrderInventoryIssues",
                column: "RejectedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderInventoryIssues_StoreId_Code",
                table: "OrderInventoryIssues",
                columns: new[] { "StoreId", "Code" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_OrderInventoryIssues_StoreId_DueAtUtc_Status_IsDeleted",
                table: "OrderInventoryIssues",
                columns: new[] { "StoreId", "DueAtUtc", "Status", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderInventoryIssues_StoreId_IsOverdue_Status_IsDeleted",
                table: "OrderInventoryIssues",
                columns: new[] { "StoreId", "IsOverdue", "Status", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderInventoryIssues_StoreId_OpenedAtUtc_IsDeleted",
                table: "OrderInventoryIssues",
                columns: new[] { "StoreId", "OpenedAtUtc", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderInventoryIssues_StoreId_Status_IsDeleted",
                table: "OrderInventoryIssues",
                columns: new[] { "StoreId", "Status", "IsDeleted" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrderInventoryIssueActions");

            migrationBuilder.DropTable(
                name: "OrderInventoryIssueLines");

            migrationBuilder.DropTable(
                name: "OrderInventoryIssues");

            migrationBuilder.DropIndex(
                name: "IX_Orders_StoreId_HasInventoryIssue_InventoryResolutionStatus_IsDeleted",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_Orders_StoreId_InventoryIssueOpenedAtUtc_IsDeleted",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "HasInventoryIssue",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "InventoryIssueApprovedAtUtc",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "InventoryIssueOpenedAtUtc",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "InventoryResolutionStatus",
                table: "Orders");
        }
    }
}
