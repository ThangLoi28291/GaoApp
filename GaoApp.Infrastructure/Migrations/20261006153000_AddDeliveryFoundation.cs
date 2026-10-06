using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDeliveryFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_Warehouses_StoreId_Id_LegalEntityId",
                table: "Warehouses",
                columns: new[] { "StoreId", "Id", "LegalEntityId" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_UserInStores_StoreId_UserId",
                table: "UserInStores",
                columns: new[] { "StoreId", "UserId" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_ProductVariant_StoreId_Id",
                table: "ProductVariant",
                columns: new[] { "StoreId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_POSShifts_StoreId_Id_TerminalId",
                table: "POSShifts",
                columns: new[] { "StoreId", "Id", "TerminalId" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Orders_StoreId_Id",
                table: "Orders",
                columns: new[] { "StoreId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Orders_StoreId_Id_POSShiftId",
                table: "Orders",
                columns: new[] { "StoreId", "Id", "POSShiftId" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_OrderLines_StoreId_OrderId_Id",
                table: "OrderLines",
                columns: new[] { "StoreId", "OrderId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_InventoryCostLayerAllocations_StoreId_Id",
                table: "InventoryCostLayerAllocations",
                columns: new[] { "StoreId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Customers_StoreId_Id",
                table: "Customers",
                columns: new[] { "StoreId", "Id" });

            migrationBuilder.CreateTable(
                name: "DeliveryOrders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    LookupToken = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    State = table.Column<int>(type: "int", nullable: false),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    SourceWarehouseId = table.Column<int>(type: "int", nullable: false),
                    SourceLegalEntityId = table.Column<int>(type: "int", nullable: false),
                    SourceCartId = table.Column<int>(type: "int", nullable: false),
                    CreatedTerminalId = table.Column<int>(type: "int", nullable: false),
                    CreatedShiftId = table.Column<int>(type: "int", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: false),
                    CustomerId = table.Column<int>(type: "int", nullable: true),
                    RecipientName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    RecipientPhone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    RecipientAddress = table.Column<string>(type: "nvarchar(1200)", maxLength: 1200, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    QuotedTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
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
                    table.PrimaryKey("PK_DeliveryOrders", x => x.Id);
                    table.UniqueConstraint("AK_DeliveryOrders_StoreId_Id", x => new { x.StoreId, x.Id });
                    table.UniqueConstraint("AK_DeliveryOrders_StoreId_Id_SourceCartId", x => new { x.StoreId, x.Id, x.SourceCartId });
                    table.UniqueConstraint("AK_DeliveryOrders_StoreId_Id_SourceWarehouseId_SourceLegalEntityId", x => new { x.StoreId, x.Id, x.SourceWarehouseId, x.SourceLegalEntityId });
                    table.CheckConstraint("CK_DeliveryOrders_Live", "[IsDeleted] = 0");
                    table.CheckConstraint("CK_DeliveryOrders_State", "[State] BETWEEN 0 AND 10 AND [Revision] > 0");
                    table.CheckConstraint("CK_DeliveryOrders_Total", "[QuotedTotal] > 0 AND [QuotedTotal] = ROUND([QuotedTotal], 0)");
                    table.ForeignKey(
                        name: "FK_DeliveryOrders_Customers_StoreId_CustomerId",
                        columns: x => new { x.StoreId, x.CustomerId },
                        principalTable: "Customers",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeliveryOrders_Orders_StoreId_SourceCartId_CreatedShiftId",
                        columns: x => new { x.StoreId, x.SourceCartId, x.CreatedShiftId },
                        principalTable: "Orders",
                        principalColumns: new[] { "StoreId", "Id", "POSShiftId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeliveryOrders_POSShifts_StoreId_CreatedShiftId_CreatedTerminalId",
                        columns: x => new { x.StoreId, x.CreatedShiftId, x.CreatedTerminalId },
                        principalTable: "POSShifts",
                        principalColumns: new[] { "StoreId", "Id", "TerminalId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeliveryOrders_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_DeliveryOrders_UserInStores_StoreId_CreatedByUserId",
                        columns: x => new { x.StoreId, x.CreatedByUserId },
                        principalTable: "UserInStores",
                        principalColumns: new[] { "StoreId", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeliveryOrders_Warehouses_StoreId_SourceWarehouseId_SourceLegalEntityId",
                        columns: x => new { x.StoreId, x.SourceWarehouseId, x.SourceLegalEntityId },
                        principalTable: "Warehouses",
                        principalColumns: new[] { "StoreId", "Id", "LegalEntityId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DeliveryCommandReceipts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClientRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DeliveryOrderId = table.Column<int>(type: "int", nullable: false),
                    ActorUserId = table.Column<int>(type: "int", nullable: false),
                    Operation = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    RequestHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    OutcomeJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
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
                    table.PrimaryKey("PK_DeliveryCommandReceipts", x => x.Id);
                    table.CheckConstraint("CK_DeliveryCommandReceipts_Live", "[IsDeleted] = 0");
                    table.CheckConstraint("CK_DeliveryCommandReceipts_OutcomeJson", "ISJSON([OutcomeJson]) = 1");
                    table.ForeignKey(
                        name: "FK_DeliveryCommandReceipts_DeliveryOrders_StoreId_DeliveryOrderId",
                        columns: x => new { x.StoreId, x.DeliveryOrderId },
                        principalTable: "DeliveryOrders",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeliveryCommandReceipts_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_DeliveryCommandReceipts_UserInStores_StoreId_ActorUserId",
                        columns: x => new { x.StoreId, x.ActorUserId },
                        principalTable: "UserInStores",
                        principalColumns: new[] { "StoreId", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DeliveryOrderLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DeliveryOrderId = table.Column<int>(type: "int", nullable: false),
                    SourceCartId = table.Column<int>(type: "int", nullable: false),
                    SourceOrderLineId = table.Column<int>(type: "int", nullable: false),
                    VariantId = table.Column<int>(type: "int", nullable: false),
                    ItemName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    UnitName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    BaseUnitName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    OrderedQuantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    BaseMultiplier = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Gross = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    LineDiscount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    AllocatedOrderDiscount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Net = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
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
                    table.PrimaryKey("PK_DeliveryOrderLines", x => x.Id);
                    table.UniqueConstraint("AK_DeliveryOrderLines_StoreId_DeliveryOrderId_Id", x => new { x.StoreId, x.DeliveryOrderId, x.Id });
                    table.CheckConstraint("CK_DeliveryOrderLines_Live", "[IsDeleted] = 0");
                    table.CheckConstraint("CK_DeliveryOrderLines_Quote", "[OrderedQuantity] > 0 AND [BaseMultiplier] > 0 AND [UnitPrice] >= 0 AND [Gross] >= 0 AND [LineDiscount] >= 0 AND [AllocatedOrderDiscount] >= 0 AND [Net] = [Gross]-[LineDiscount]-[AllocatedOrderDiscount] AND [Net] >= 0 AND [Net] = ROUND([Net],0)");
                    table.ForeignKey(
                        name: "FK_DeliveryOrderLines_DeliveryOrders_StoreId_DeliveryOrderId_SourceCartId",
                        columns: x => new { x.StoreId, x.DeliveryOrderId, x.SourceCartId },
                        principalTable: "DeliveryOrders",
                        principalColumns: new[] { "StoreId", "Id", "SourceCartId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeliveryOrderLines_OrderLines_StoreId_SourceCartId_SourceOrderLineId",
                        columns: x => new { x.StoreId, x.SourceCartId, x.SourceOrderLineId },
                        principalTable: "OrderLines",
                        principalColumns: new[] { "StoreId", "OrderId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeliveryOrderLines_ProductVariant_StoreId_VariantId",
                        columns: x => new { x.StoreId, x.VariantId },
                        principalTable: "ProductVariant",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeliveryOrderLines_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "DeliveryRevisions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DeliveryOrderId = table.Column<int>(type: "int", nullable: false),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    ActorUserId = table.Column<int>(type: "int", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    AggregateVersion = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    SnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SnapshotHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
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
                    table.PrimaryKey("PK_DeliveryRevisions", x => x.Id);
                    table.UniqueConstraint("AK_DeliveryRevisions_StoreId_DeliveryOrderId_Revision", x => new { x.StoreId, x.DeliveryOrderId, x.Revision });
                    table.CheckConstraint("CK_DeliveryRevisions_Live", "[IsDeleted] = 0");
                    table.CheckConstraint("CK_DeliveryRevisions_SnapshotJson", "ISJSON([SnapshotJson]) = 1");
                    table.ForeignKey(
                        name: "FK_DeliveryRevisions_DeliveryOrders_StoreId_DeliveryOrderId",
                        columns: x => new { x.StoreId, x.DeliveryOrderId },
                        principalTable: "DeliveryOrders",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeliveryRevisions_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_DeliveryRevisions_UserInStores_StoreId_ActorUserId",
                        columns: x => new { x.StoreId, x.ActorUserId },
                        principalTable: "UserInStores",
                        principalColumns: new[] { "StoreId", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DeliveryDispatchCostFragments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DeliveryOrderId = table.Column<int>(type: "int", nullable: false),
                    DeliveryOrderLineId = table.Column<int>(type: "int", nullable: false),
                    SourceWarehouseId = table.Column<int>(type: "int", nullable: false),
                    SourceLegalEntityId = table.Column<int>(type: "int", nullable: false),
                    InventoryCostLayerAllocationId = table.Column<int>(type: "int", nullable: false),
                    BaseQuantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    UnitCost = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    CostAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
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
                    table.PrimaryKey("PK_DeliveryDispatchCostFragments", x => x.Id);
                    table.CheckConstraint("CK_DeliveryDispatchCostFragments_Cost", "[BaseQuantity] >= 0 AND [UnitCost] >= 0 AND [CostAmount] >= 0");
                    table.CheckConstraint("CK_DeliveryDispatchCostFragments_Live", "[IsDeleted] = 0");
                    table.ForeignKey(
                        name: "FK_DeliveryDispatchCostFragments_DeliveryOrderLines_StoreId_DeliveryOrderId_DeliveryOrderLineId",
                        columns: x => new { x.StoreId, x.DeliveryOrderId, x.DeliveryOrderLineId },
                        principalTable: "DeliveryOrderLines",
                        principalColumns: new[] { "StoreId", "DeliveryOrderId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeliveryDispatchCostFragments_DeliveryOrders_StoreId_DeliveryOrderId_SourceWarehouseId_SourceLegalEntityId",
                        columns: x => new { x.StoreId, x.DeliveryOrderId, x.SourceWarehouseId, x.SourceLegalEntityId },
                        principalTable: "DeliveryOrders",
                        principalColumns: new[] { "StoreId", "Id", "SourceWarehouseId", "SourceLegalEntityId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeliveryDispatchCostFragments_InventoryCostLayerAllocations_StoreId_InventoryCostLayerAllocationId",
                        columns: x => new { x.StoreId, x.InventoryCostLayerAllocationId },
                        principalTable: "InventoryCostLayerAllocations",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeliveryDispatchCostFragments_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "DeliveryJournalEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DeliveryOrderId = table.Column<int>(type: "int", nullable: false),
                    DeliveryOrderLineId = table.Column<int>(type: "int", nullable: true),
                    SourceWarehouseId = table.Column<int>(type: "int", nullable: false),
                    SourceLegalEntityId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    PostingKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    BaseQuantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    UnitCost = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    CostAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    MoneyAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    SaleOrderId = table.Column<int>(type: "int", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
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
                    table.PrimaryKey("PK_DeliveryJournalEntries", x => x.Id);
                    table.CheckConstraint("CK_DeliveryJournalEntries_Cost", "[BaseQuantity] >= 0 AND [UnitCost] >= 0 AND [CostAmount] >= 0");
                    table.CheckConstraint("CK_DeliveryJournalEntries_Live", "[IsDeleted] = 0");
                    table.CheckConstraint("CK_DeliveryJournalEntries_Money", "[Kind] BETWEEN 1 AND 7 AND [MoneyAmount] >= 0 AND [MoneyAmount] = ROUND([MoneyAmount],0)");
                    table.ForeignKey(
                        name: "FK_DeliveryJournalEntries_DeliveryOrderLines_StoreId_DeliveryOrderId_DeliveryOrderLineId",
                        columns: x => new { x.StoreId, x.DeliveryOrderId, x.DeliveryOrderLineId },
                        principalTable: "DeliveryOrderLines",
                        principalColumns: new[] { "StoreId", "DeliveryOrderId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeliveryJournalEntries_DeliveryOrders_StoreId_DeliveryOrderId_SourceWarehouseId_SourceLegalEntityId",
                        columns: x => new { x.StoreId, x.DeliveryOrderId, x.SourceWarehouseId, x.SourceLegalEntityId },
                        principalTable: "DeliveryOrders",
                        principalColumns: new[] { "StoreId", "Id", "SourceWarehouseId", "SourceLegalEntityId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeliveryJournalEntries_Orders_StoreId_SaleOrderId",
                        columns: x => new { x.StoreId, x.SaleOrderId },
                        principalTable: "Orders",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeliveryJournalEntries_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "DeliveryOutboxMessages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DeliveryOrderId = table.Column<int>(type: "int", nullable: false),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
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
                    table.PrimaryKey("PK_DeliveryOutboxMessages", x => x.Id);
                    table.UniqueConstraint("AK_DeliveryOutboxMessages_StoreId_EventId", x => new { x.StoreId, x.EventId });
                    table.CheckConstraint("CK_DeliveryOutboxMessages_Live", "[IsDeleted] = 0");
                    table.CheckConstraint("CK_DeliveryOutboxMessages_PayloadJson", "ISJSON([PayloadJson]) = 1");
                    table.ForeignKey(
                        name: "FK_DeliveryOutboxMessages_DeliveryRevisions_StoreId_DeliveryOrderId_Revision",
                        columns: x => new { x.StoreId, x.DeliveryOrderId, x.Revision },
                        principalTable: "DeliveryRevisions",
                        principalColumns: new[] { "StoreId", "DeliveryOrderId", "Revision" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeliveryOutboxMessages_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "DeliveryOutboxReceipts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Consumer = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
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
                    table.PrimaryKey("PK_DeliveryOutboxReceipts", x => x.Id);
                    table.CheckConstraint("CK_DeliveryOutboxReceipts_Live", "[IsDeleted] = 0");
                    table.ForeignKey(
                        name: "FK_DeliveryOutboxReceipts_DeliveryOutboxMessages_StoreId_EventId",
                        columns: x => new { x.StoreId, x.EventId },
                        principalTable: "DeliveryOutboxMessages",
                        principalColumns: new[] { "StoreId", "EventId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeliveryOutboxReceipts_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryCommandReceipts_StoreId_ActorUserId",
                table: "DeliveryCommandReceipts",
                columns: new[] { "StoreId", "ActorUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryCommandReceipts_StoreId_ClientRequestId",
                table: "DeliveryCommandReceipts",
                columns: new[] { "StoreId", "ClientRequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryCommandReceipts_StoreId_DeliveryOrderId",
                table: "DeliveryCommandReceipts",
                columns: new[] { "StoreId", "DeliveryOrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryDispatchCostFragments_StoreId_DeliveryOrderId_DeliveryOrderLineId_InventoryCostLayerAllocationId",
                table: "DeliveryDispatchCostFragments",
                columns: new[] { "StoreId", "DeliveryOrderId", "DeliveryOrderLineId", "InventoryCostLayerAllocationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryDispatchCostFragments_StoreId_DeliveryOrderId_SourceWarehouseId_SourceLegalEntityId",
                table: "DeliveryDispatchCostFragments",
                columns: new[] { "StoreId", "DeliveryOrderId", "SourceWarehouseId", "SourceLegalEntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryDispatchCostFragments_StoreId_InventoryCostLayerAllocationId",
                table: "DeliveryDispatchCostFragments",
                columns: new[] { "StoreId", "InventoryCostLayerAllocationId" });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryJournalEntries_StoreId_DeliveryOrderId",
                table: "DeliveryJournalEntries",
                columns: new[] { "StoreId", "DeliveryOrderId" },
                unique: true,
                filter: "[Kind] = 6");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryJournalEntries_StoreId_DeliveryOrderId_DeliveryOrderLineId",
                table: "DeliveryJournalEntries",
                columns: new[] { "StoreId", "DeliveryOrderId", "DeliveryOrderLineId" });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryJournalEntries_StoreId_DeliveryOrderId_SourceWarehouseId_SourceLegalEntityId",
                table: "DeliveryJournalEntries",
                columns: new[] { "StoreId", "DeliveryOrderId", "SourceWarehouseId", "SourceLegalEntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryJournalEntries_StoreId_PostingKey",
                table: "DeliveryJournalEntries",
                columns: new[] { "StoreId", "PostingKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryJournalEntries_StoreId_SaleOrderId",
                table: "DeliveryJournalEntries",
                columns: new[] { "StoreId", "SaleOrderId" },
                unique: true,
                filter: "[Kind] = 6 AND [SaleOrderId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryOrderLines_StoreId_DeliveryOrderId_SourceCartId",
                table: "DeliveryOrderLines",
                columns: new[] { "StoreId", "DeliveryOrderId", "SourceCartId" });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryOrderLines_StoreId_DeliveryOrderId_SourceOrderLineId",
                table: "DeliveryOrderLines",
                columns: new[] { "StoreId", "DeliveryOrderId", "SourceOrderLineId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryOrderLines_StoreId_SourceCartId_SourceOrderLineId",
                table: "DeliveryOrderLines",
                columns: new[] { "StoreId", "SourceCartId", "SourceOrderLineId" });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryOrderLines_StoreId_VariantId",
                table: "DeliveryOrderLines",
                columns: new[] { "StoreId", "VariantId" });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryOrders_StoreId_Code",
                table: "DeliveryOrders",
                columns: new[] { "StoreId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryOrders_StoreId_CreatedByUserId",
                table: "DeliveryOrders",
                columns: new[] { "StoreId", "CreatedByUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryOrders_StoreId_CreatedShiftId_CreatedTerminalId",
                table: "DeliveryOrders",
                columns: new[] { "StoreId", "CreatedShiftId", "CreatedTerminalId" });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryOrders_StoreId_CustomerId",
                table: "DeliveryOrders",
                columns: new[] { "StoreId", "CustomerId" });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryOrders_StoreId_LookupToken",
                table: "DeliveryOrders",
                columns: new[] { "StoreId", "LookupToken" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryOrders_StoreId_SourceCartId",
                table: "DeliveryOrders",
                columns: new[] { "StoreId", "SourceCartId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryOrders_StoreId_SourceCartId_CreatedShiftId",
                table: "DeliveryOrders",
                columns: new[] { "StoreId", "SourceCartId", "CreatedShiftId" });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryOrders_StoreId_SourceWarehouseId_SourceLegalEntityId",
                table: "DeliveryOrders",
                columns: new[] { "StoreId", "SourceWarehouseId", "SourceLegalEntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryOrders_StoreId_State_CreatedAtUtc",
                table: "DeliveryOrders",
                columns: new[] { "StoreId", "State", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryOutboxMessages_StoreId_DeliveryOrderId_Revision",
                table: "DeliveryOutboxMessages",
                columns: new[] { "StoreId", "DeliveryOrderId", "Revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryOutboxReceipts_StoreId_EventId_Consumer",
                table: "DeliveryOutboxReceipts",
                columns: new[] { "StoreId", "EventId", "Consumer" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryRevisions_StoreId_ActorUserId",
                table: "DeliveryRevisions",
                columns: new[] { "StoreId", "ActorUserId" });

            migrationBuilder.Sql("CREATE TRIGGER [TR_DeliveryRevisions_Immutable] ON [DeliveryRevisions] INSTEAD OF UPDATE, DELETE AS BEGIN THROW 51002, 'Delivery history is append only.', 1; END;");

            migrationBuilder.Sql("CREATE TRIGGER [TR_DeliveryJournalEntries_Immutable] ON [DeliveryJournalEntries] INSTEAD OF UPDATE, DELETE AS BEGIN THROW 51002, 'Delivery history is append only.', 1; END;");

            migrationBuilder.Sql("CREATE TRIGGER [TR_DeliveryDispatchCostFragments_Immutable] ON [DeliveryDispatchCostFragments] INSTEAD OF UPDATE, DELETE AS BEGIN THROW 51002, 'Delivery history is append only.', 1; END;");

            migrationBuilder.Sql("CREATE TRIGGER [TR_DeliveryCommandReceipts_Immutable] ON [DeliveryCommandReceipts] INSTEAD OF UPDATE, DELETE AS BEGIN THROW 51002, 'Delivery history is append only.', 1; END;");

            migrationBuilder.Sql("CREATE TRIGGER [TR_DeliveryOutboxMessages_Immutable] ON [DeliveryOutboxMessages] INSTEAD OF UPDATE, DELETE AS BEGIN THROW 51002, 'Delivery history is append only.', 1; END;");

            migrationBuilder.Sql("CREATE TRIGGER [TR_DeliveryOutboxReceipts_Immutable] ON [DeliveryOutboxReceipts] INSTEAD OF UPDATE, DELETE AS BEGIN THROW 51002, 'Delivery history is append only.', 1; END;");

            migrationBuilder.Sql("CREATE TRIGGER [TR_DeliveryOrders_Origin] ON [DeliveryOrders] AFTER UPDATE AS BEGIN IF EXISTS (SELECT 1 FROM inserted i JOIN deleted d ON i.Id=d.Id WHERE i.[StoreId]<>d.[StoreId] OR i.[Code]<>d.[Code] OR i.[LookupToken]<>d.[LookupToken] OR i.[SourceWarehouseId]<>d.[SourceWarehouseId] OR i.[SourceLegalEntityId]<>d.[SourceLegalEntityId] OR i.[SourceCartId]<>d.[SourceCartId] OR i.[CreatedTerminalId]<>d.[CreatedTerminalId] OR i.[CreatedShiftId]<>d.[CreatedShiftId] OR i.[CreatedByUserId]<>d.[CreatedByUserId] OR ISNULL(i.CustomerId,0)<>ISNULL(d.CustomerId,0)) THROW 51003, 'Delivery origin is immutable.', 1; END;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeliveryCommandReceipts");

            migrationBuilder.DropTable(
                name: "DeliveryDispatchCostFragments");

            migrationBuilder.DropTable(
                name: "DeliveryJournalEntries");

            migrationBuilder.DropTable(
                name: "DeliveryOutboxReceipts");

            migrationBuilder.DropTable(
                name: "DeliveryOrderLines");

            migrationBuilder.DropTable(
                name: "DeliveryOutboxMessages");

            migrationBuilder.DropTable(
                name: "DeliveryRevisions");

            migrationBuilder.DropTable(
                name: "DeliveryOrders");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Warehouses_StoreId_Id_LegalEntityId",
                table: "Warehouses");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_UserInStores_StoreId_UserId",
                table: "UserInStores");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_ProductVariant_StoreId_Id",
                table: "ProductVariant");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_POSShifts_StoreId_Id_TerminalId",
                table: "POSShifts");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Orders_StoreId_Id",
                table: "Orders");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Orders_StoreId_Id_POSShiftId",
                table: "Orders");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_OrderLines_StoreId_OrderId_Id",
                table: "OrderLines");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_InventoryCostLayerAllocations_StoreId_Id",
                table: "InventoryCostLayerAllocations");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Customers_StoreId_Id",
                table: "Customers");
        }
    }
}
