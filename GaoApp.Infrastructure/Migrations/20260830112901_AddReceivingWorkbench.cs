using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddReceivingWorkbench : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF EXISTS (
                    SELECT 1
                    FROM [PurchaseOrderLines]
                    WHERE [IsDeleted] = 0 AND [ProductVariantId] IS NOT NULL
                    GROUP BY [StoreId], [PurchaseOrderId], [ProductVariantId]
                    HAVING COUNT_BIG(*) > 1)
                    THROW 51001, 'RW migration preflight: duplicate active ProductVariant rows exist in a Purchase Order.', 1;

                IF EXISTS (
                    SELECT 1
                    FROM [StockDocument]
                    WHERE [IsDeleted] = 0 AND [Type] = 1 AND [ReceiptSource] = 2
                      AND [PurchaseOrderId] IS NOT NULL AND [Status] IN (1, 4)
                    GROUP BY [StoreId], [PurchaseOrderId]
                    HAVING COUNT_BIG(*) > 1)
                    THROW 51002, 'RW migration preflight: multiple editable Purchase Receipts exist for a Purchase Order.', 1;

                IF EXISTS (
                    SELECT 1
                    FROM [StockDocumentLine] l
                    INNER JOIN [StockDocument] d ON d.[Id] = l.[StockDocumentId]
                    LEFT JOIN [PurchaseOrderLines] p ON p.[Id] = l.[PurchaseOrderLineId]
                    WHERE l.[IsDeleted] = 0 AND d.[IsDeleted] = 0
                      AND d.[Type] = 1 AND d.[ReceiptSource] = 2
                      AND (l.[PurchaseOrderLineId] IS NULL OR p.[Id] IS NULL
                           OR p.[PurchaseOrderId] <> d.[PurchaseOrderId]
                           OR p.[StoreId] <> d.[StoreId]
                           OR p.[ProductVariantId] <> l.[ProductVariantId]))
                    THROW 51003, 'RW migration preflight: a PO receipt line has an inconsistent Purchase Order allocation.', 1;

                IF EXISTS (
                    SELECT 1
                    FROM [StockDocumentLine] l
                    INNER JOIN [StockDocument] d ON d.[Id] = l.[StockDocumentId]
                    WHERE l.[IsDeleted] = 0 AND d.[IsDeleted] = 0
                      AND d.[Type] = 1 AND d.[ReceiptSource] = 2
                      AND l.[PurchaseOrderLineId] IS NOT NULL
                      AND l.[ProductUnitConversionId] IS NOT NULL
                    GROUP BY l.[StockDocumentId], l.[PurchaseOrderLineId], l.[ProductUnitConversionId]
                    HAVING COUNT_BIG(*) > 1)
                    THROW 51004, 'RW migration preflight: duplicate active PO receipt components exist.', 1;
                """);

            migrationBuilder.AddColumn<DateTime>(
                name: "OutsidePoDecisionAtUtc",
                table: "StockDocumentLine",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OutsidePoDecisionByUserId",
                table: "StockDocumentLine",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OutsidePoDecisionNote",
                table: "StockDocumentLine",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OutsidePoDecisionStatus",
                table: "StockDocumentLine",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ReceiptAllocationKind",
                table: "StockDocumentLine",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReceivingLastSavedAtUtc",
                table: "StockDocument",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReceivingLeaseExpiresAtUtc",
                table: "StockDocument",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReceivingLeaseToken",
                table: "StockDocument",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReceivingOwnerUserId",
                table: "StockDocument",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReceivingRevision",
                table: "StockDocument",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ReceivingSessionState",
                table: "StockDocument",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql(
                """
                UPDATE l
                SET [ReceiptAllocationKind] = 1,
                    [OutsidePoDecisionStatus] = 0
                FROM [StockDocumentLine] l
                INNER JOIN [StockDocument] d ON d.[Id] = l.[StockDocumentId]
                WHERE l.[IsDeleted] = 0 AND d.[IsDeleted] = 0
                  AND d.[Type] = 1 AND d.[ReceiptSource] = 2
                  AND l.[PurchaseOrderLineId] IS NOT NULL;

                UPDATE [StockDocument]
                SET [ReceivingSessionState] = CASE
                        WHEN [Status] IN (1, 4) THEN 1
                        WHEN [Status] = 2 THEN 2
                        WHEN [Status] IN (3, 5) THEN 3
                        ELSE 0
                    END,
                    [ReceivingRevision] = CASE WHEN [Status] = 4 THEN 1 ELSE 0 END
                WHERE [IsDeleted] = 0 AND [Type] = 1 AND [ReceiptSource] = 2
                  AND [PurchaseOrderId] IS NOT NULL;
                """);

            migrationBuilder.CreateTable(
                name: "PurchaseReceivingActions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StoreId = table.Column<int>(type: "int", nullable: false),
                    StockDocumentId = table.Column<int>(type: "int", nullable: false),
                    ReceivingRevision = table.Column<int>(type: "int", nullable: false),
                    CommandId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActionType = table.Column<int>(type: "int", nullable: false),
                    StockDocumentLineId = table.Column<int>(type: "int", nullable: false),
                    ProductVariantId = table.Column<int>(type: "int", nullable: false),
                    ProductUnitConversionId = table.Column<int>(type: "int", nullable: false),
                    PurchaseOrderLineId = table.Column<int>(type: "int", nullable: true),
                    ReceiptAllocationKind = table.Column<int>(type: "int", nullable: false),
                    BeforeQuantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    AfterQuantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    BeforeIsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    AfterIsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    ActorUserId = table.Column<int>(type: "int", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UndoOfActionId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseReceivingActions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseReceivingActions_PurchaseReceivingActions_UndoOfActionId",
                        column: x => x.UndoOfActionId,
                        principalTable: "PurchaseReceivingActions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseReceivingActions_StockDocumentLine_StockDocumentLineId",
                        column: x => x.StockDocumentLineId,
                        principalTable: "StockDocumentLine",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseReceivingActions_StockDocument_StockDocumentId",
                        column: x => x.StockDocumentId,
                        principalTable: "StockDocument",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "UX_StockDocumentLine_OutsideReceivingComponent",
                table: "StockDocumentLine",
                columns: new[] { "StockDocumentId", "ProductVariantId", "ProductUnitConversionId" },
                unique: true,
                filter: "[IsDeleted] = 0 AND [ReceiptAllocationKind] = 2 AND [ProductUnitConversionId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_StockDocumentLine_PoReceivingComponent",
                table: "StockDocumentLine",
                columns: new[] { "StockDocumentId", "PurchaseOrderLineId", "ProductUnitConversionId" },
                unique: true,
                filter: "[IsDeleted] = 0 AND [ReceiptAllocationKind] = 1 AND [PurchaseOrderLineId] IS NOT NULL AND [ProductUnitConversionId] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_StockDocumentLine_ReceivingAllocation",
                table: "StockDocumentLine",
                sql: "[ReceiptAllocationKind] = 0 AND [OutsidePoDecisionStatus] = 0 OR [ReceiptAllocationKind] = 1 AND [PurchaseOrderLineId] IS NOT NULL AND [OutsidePoDecisionStatus] = 0 OR [ReceiptAllocationKind] = 2 AND [PurchaseOrderLineId] IS NULL AND ([OutsidePoDecisionStatus] = 1 OR [OutsidePoDecisionStatus] = 2 OR [OutsidePoDecisionStatus] = 3)");

            migrationBuilder.CreateIndex(
                name: "IX_StockDocument_ReceivingLease",
                table: "StockDocument",
                columns: new[] { "StoreId", "ReceivingOwnerUserId", "ReceivingLeaseExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_StockDocument_ActiveReceivingDraft",
                table: "StockDocument",
                columns: new[] { "StoreId", "PurchaseOrderId" },
                unique: true,
                filter: "[IsDeleted] = 0 AND [Type] = 1 AND [ReceiptSource] = 2 AND [ReceivingSessionState] = 1 AND [PurchaseOrderId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_PurchaseOrderLines_Store_Order_ProductVariant",
                table: "PurchaseOrderLines",
                columns: new[] { "StoreId", "PurchaseOrderId", "ProductVariantId" },
                unique: true,
                filter: "[IsDeleted] = 0 AND [ProductVariantId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReceivingActions_Latest",
                table: "PurchaseReceivingActions",
                columns: new[] { "StoreId", "StockDocumentId", "ReceivingRevision", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReceivingActions_StockDocumentId",
                table: "PurchaseReceivingActions",
                column: "StockDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReceivingActions_StockDocumentLineId",
                table: "PurchaseReceivingActions",
                column: "StockDocumentLineId");

            migrationBuilder.CreateIndex(
                name: "UX_PurchaseReceivingActions_Document_Command",
                table: "PurchaseReceivingActions",
                columns: new[] { "StoreId", "StockDocumentId", "CommandId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_PurchaseReceivingActions_UndoOf",
                table: "PurchaseReceivingActions",
                column: "UndoOfActionId",
                unique: true,
                filter: "[UndoOfActionId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PurchaseReceivingActions");

            migrationBuilder.DropIndex(
                name: "UX_StockDocumentLine_OutsideReceivingComponent",
                table: "StockDocumentLine");

            migrationBuilder.DropIndex(
                name: "UX_StockDocumentLine_PoReceivingComponent",
                table: "StockDocumentLine");

            migrationBuilder.DropCheckConstraint(
                name: "CK_StockDocumentLine_ReceivingAllocation",
                table: "StockDocumentLine");

            migrationBuilder.DropIndex(
                name: "IX_StockDocument_ReceivingLease",
                table: "StockDocument");

            migrationBuilder.DropIndex(
                name: "UX_StockDocument_ActiveReceivingDraft",
                table: "StockDocument");

            migrationBuilder.DropIndex(
                name: "UX_PurchaseOrderLines_Store_Order_ProductVariant",
                table: "PurchaseOrderLines");

            migrationBuilder.DropColumn(
                name: "OutsidePoDecisionAtUtc",
                table: "StockDocumentLine");

            migrationBuilder.DropColumn(
                name: "OutsidePoDecisionByUserId",
                table: "StockDocumentLine");

            migrationBuilder.DropColumn(
                name: "OutsidePoDecisionNote",
                table: "StockDocumentLine");

            migrationBuilder.DropColumn(
                name: "OutsidePoDecisionStatus",
                table: "StockDocumentLine");

            migrationBuilder.DropColumn(
                name: "ReceiptAllocationKind",
                table: "StockDocumentLine");

            migrationBuilder.DropColumn(
                name: "ReceivingLastSavedAtUtc",
                table: "StockDocument");

            migrationBuilder.DropColumn(
                name: "ReceivingLeaseExpiresAtUtc",
                table: "StockDocument");

            migrationBuilder.DropColumn(
                name: "ReceivingLeaseToken",
                table: "StockDocument");

            migrationBuilder.DropColumn(
                name: "ReceivingOwnerUserId",
                table: "StockDocument");

            migrationBuilder.DropColumn(
                name: "ReceivingRevision",
                table: "StockDocument");

            migrationBuilder.DropColumn(
                name: "ReceivingSessionState",
                table: "StockDocument");

        }
    }
}
