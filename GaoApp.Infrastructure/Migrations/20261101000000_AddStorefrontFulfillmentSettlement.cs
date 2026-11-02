using System;
using GaoApp.Infrastructure.Data.Migrations;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStorefrontFulfillmentSettlement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(StorefrontFulfillmentSchemaSqlCatalog.PreflightSql);
            foreach (var guard in StorefrontFulfillmentSchemaSqlCatalog.Replacements) migrationBuilder.Sql($"DROP TRIGGER [dbo].[{guard.Name}];");
            migrationBuilder.DropForeignKey(
                name: "FK_DeliveryOrderLines_DeliveryOrders_StoreId_DeliveryOrderId_SourceCartId",
                table: "DeliveryOrderLines");

            migrationBuilder.DropIndex(
                name: "IX_StorefrontSettlementLinks_StoreId_SaleOrderId",
                table: "StorefrontSettlementLinks");

            migrationBuilder.DropCheckConstraint(
                name: "CK_StorefrontSettlementLinks_GoodsAmount",
                table: "StorefrontSettlementLinks");

            migrationBuilder.DropCheckConstraint(
                name: "CK_InvoiceHeads_LegacyOrder",
                table: "InvoiceHeads");

            migrationBuilder.DropCheckConstraint(
                name: "CK_DeliveryPickingWorks_Approval",
                table: "DeliveryPickingWorks");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_DeliveryOrders_StoreId_Id_SourceCartId",
                table: "DeliveryOrders");

            migrationBuilder.DropIndex(
                name: "IX_DeliveryOrders_StoreId_SourceCartId",
                table: "DeliveryOrders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_DeliveryOrders_Total",
                table: "DeliveryOrders");

            migrationBuilder.DropIndex(
                name: "IX_DeliveryOrderLines_StoreId_DeliveryOrderId_SourceCartId",
                table: "DeliveryOrderLines");

            migrationBuilder.DropCheckConstraint(
                name: "CK_DeliveryOrderLines_Identity",
                table: "DeliveryOrderLines");

            migrationBuilder.AddColumn<DateTime>(
                name: "LegalEntityModeCapturedAtUtc",
                table: "StorefrontOrders",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "UseMultiLegalEntitySnapshot",
                table: "StorefrontOrders",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "HasInputInvoiceSnapshot",
                table: "StorefrontOrderLines",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AccountIdentitySnapshot",
                table: "PosPaymentQrRequests",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AccountVersionSnapshot",
                table: "PosPaymentQrRequests",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StorefrontCarrierWithholdingId",
                table: "OrderPayments",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StorefrontCodReceiptId",
                table: "OrderPayments",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "HasInputInvoiceSnapshot",
                table: "OrderLines",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StorefrontOrderId",
                table: "OrderLines",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StorefrontOrderLineId",
                table: "OrderLines",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "StorefrontRoundingAdjustment",
                table: "OrderLines",
                type: "decimal(18,0)",
                precision: 18,
                scale: 0,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "InvoiceUnitId",
                table: "OrderLegalEntityAllocations",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "InvoiceUnitKind",
                table: "OrderLegalEntityAllocations",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "InvoiceUnitName",
                table: "OrderLegalEntityAllocations",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "StorefrontHeaderDiscountAllocated",
                table: "OrderLegalEntityAllocations",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AlterColumn<decimal>(
                name: "TotalQuantity",
                table: "InvoiceHeads",
                type: "decimal(19,4)",
                precision: 19,
                scale: 4,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,3)",
                oldPrecision: 18,
                oldScale: 3);

            migrationBuilder.AddColumn<int>(
                name: "NativeServiceActorUserId",
                table: "InvoiceHeads",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "NativeServiceCashOnly",
                table: "InvoiceHeads",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<byte>(
                name: "NativeServiceInvoiceRoute",
                table: "InvoiceHeads",
                type: "tinyint",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "NativeServiceRecognizedAtUtc",
                table: "InvoiceHeads",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StorefrontServiceRevenueId",
                table: "InvoiceHeads",
                type: "int",
                nullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "Quantity",
                table: "InvoiceDetails",
                type: "decimal(19,4)",
                precision: 19,
                scale: 4,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,3)",
                oldPrecision: 18,
                oldScale: 3);

            migrationBuilder.AddColumn<int>(
                name: "StorefrontServiceRevenueId",
                table: "InvoiceDetails",
                type: "int",
                nullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "SourceCartId",
                table: "DeliveryOrders",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AlterColumn<int>(
                name: "CreatedTerminalId",
                table: "DeliveryOrders",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AlterColumn<int>(
                name: "CreatedShiftId",
                table: "DeliveryOrders",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<int>(
                name: "SourceKind",
                table: "DeliveryOrders",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "StorefrontAllowNegativeInventory",
                table: "DeliveryOrders",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StorefrontFulfillmentSourceId",
                table: "DeliveryOrders",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StorefrontOrderId",
                table: "DeliveryOrders",
                type: "int",
                nullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "SourceCartId",
                table: "DeliveryOrderLines",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<decimal>(
                name: "StorefrontBaseQuantity",
                table: "DeliveryOrderLines",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StorefrontOrderId",
                table: "DeliveryOrderLines",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StorefrontOrderLineId",
                table: "DeliveryOrderLines",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AccountVersion",
                table: "BankVerifiedReceipts",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "NativeCollectionAuthorityProofJson",
                table: "BankVerifiedReceipts",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RoutingProvenance",
                table: "BankVerifiedReceipts",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_TreasuryEntries_StoreId_Id",
                table: "TreasuryEntries",
                columns: new[] { "StoreId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Taxes_StoreId_Id",
                table: "Taxes",
                columns: new[] { "StoreId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_POSShiftCashTransactions_StoreId_Id",
                table: "POSShiftCashTransactions",
                columns: new[] { "StoreId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_OrderInventoryIssues_StoreId_Id",
                table: "OrderInventoryIssues",
                columns: new[] { "StoreId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_OrderInventoryIssueLines_StoreId_Id",
                table: "OrderInventoryIssueLines",
                columns: new[] { "StoreId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_OperatingExpenses_StoreId_Id",
                table: "OperatingExpenses",
                columns: new[] { "StoreId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_BankNotificationReceipts_StoreId_Id",
                table: "BankNotificationReceipts",
                columns: new[] { "StoreId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_BankNotificationDevices_StoreId_Id",
                table: "BankNotificationDevices",
                columns: new[] { "StoreId", "Id" });

            migrationBuilder.CreateTable(
                name: "StorefrontCarrierWithholdings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StorefrontOrderId = table.Column<int>(type: "int", nullable: false),
                    OperatingExpenseId = table.Column<int>(type: "int", nullable: false),
                    LegalEntityId = table.Column<int>(type: "int", nullable: false),
                    ActorUserId = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CarrierEvidenceReference = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
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
                    table.PrimaryKey("PK_StorefrontCarrierWithholdings", x => x.Id);
                    table.UniqueConstraint("AK_StorefrontCarrierWithholdings_StoreId_Id", x => new { x.StoreId, x.Id });
                    table.CheckConstraint("CK_StorefrontCarrierWithholdings_Amount", "[Amount]>0 AND [Amount]<10000000000000000 AND [Amount]=ROUND([Amount],0)");
                    table.CheckConstraint("CK_StorefrontCarrierWithholdings_Live", "[IsDeleted]=0");
                    table.ForeignKey(
                        name: "FK_StorefrontCarrierWithholdings_LegalEntities_StoreId_LegalEntityId",
                        columns: x => new { x.StoreId, x.LegalEntityId },
                        principalTable: "LegalEntities",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontCarrierWithholdings_OperatingExpenses_StoreId_OperatingExpenseId",
                        columns: x => new { x.StoreId, x.OperatingExpenseId },
                        principalTable: "OperatingExpenses",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontCarrierWithholdings_StorefrontOrders_StoreId_StorefrontOrderId",
                        columns: x => new { x.StoreId, x.StorefrontOrderId },
                        principalTable: "StorefrontOrders",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontCarrierWithholdings_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_StorefrontCarrierWithholdings_UserInStores_StoreId_ActorUserId",
                        columns: x => new { x.StoreId, x.ActorUserId },
                        principalTable: "UserInStores",
                        principalColumns: new[] { "StoreId", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StorefrontCodBankRoutings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StorefrontOrderId = table.Column<int>(type: "int", nullable: false),
                    GlobalBankReceiptId = table.Column<int>(type: "int", nullable: false),
                    ReceivingLegalEntityId = table.Column<int>(type: "int", nullable: false),
                    ActorUserId = table.Column<int>(type: "int", nullable: false),
                    AuthoritySnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EvidenceReference = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
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
                    table.PrimaryKey("PK_StorefrontCodBankRoutings", x => x.Id);
                    table.UniqueConstraint("AK_StorefrontCodBankRoutings_StoreId_Id", x => new { x.StoreId, x.Id });
                    table.CheckConstraint("CK_StorefrontCodBankRoutings_AuthoritySnapshotJson", "ISJSON([AuthoritySnapshotJson])=1");
                    table.CheckConstraint("CK_StorefrontCodBankRoutings_Live", "[IsDeleted]=0");
                    table.ForeignKey(
                        name: "FK_StorefrontCodBankRoutings_BankVerifiedReceipts_StoreId_GlobalBankReceiptId",
                        columns: x => new { x.StoreId, x.GlobalBankReceiptId },
                        principalTable: "BankVerifiedReceipts",
                        principalColumns: new[] { "OriginStoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontCodBankRoutings_LegalEntities_StoreId_ReceivingLegalEntityId",
                        columns: x => new { x.StoreId, x.ReceivingLegalEntityId },
                        principalTable: "LegalEntities",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontCodBankRoutings_StorefrontOrders_StoreId_StorefrontOrderId",
                        columns: x => new { x.StoreId, x.StorefrontOrderId },
                        principalTable: "StorefrontOrders",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontCodBankRoutings_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_StorefrontCodBankRoutings_UserInStores_StoreId_ActorUserId",
                        columns: x => new { x.StoreId, x.ActorUserId },
                        principalTable: "UserInStores",
                        principalColumns: new[] { "StoreId", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StorefrontCodCollections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StorefrontOrderId = table.Column<int>(type: "int", nullable: false),
                    FulfillmentSourceId = table.Column<int>(type: "int", nullable: false),
                    RecordedByUserId = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CustomerPaidAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EvidenceReference = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
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
                    table.PrimaryKey("PK_StorefrontCodCollections", x => x.Id);
                    table.UniqueConstraint("AK_StorefrontCodCollections_StoreId_Id", x => new { x.StoreId, x.Id });
                    table.CheckConstraint("CK_StorefrontCodCollections_Amount", "[Amount]>0 AND [Amount]<10000000000000000 AND [Amount]=ROUND([Amount],0)");
                    table.CheckConstraint("CK_StorefrontCodCollections_Live", "[IsDeleted]=0");
                    table.ForeignKey(
                        name: "FK_StorefrontCodCollections_StorefrontFulfillmentSources_StoreId_StorefrontOrderId_FulfillmentSourceId",
                        columns: x => new { x.StoreId, x.StorefrontOrderId, x.FulfillmentSourceId },
                        principalTable: "StorefrontFulfillmentSources",
                        principalColumns: new[] { "StoreId", "StorefrontOrderId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontCodCollections_StorefrontOrders_StoreId_StorefrontOrderId",
                        columns: x => new { x.StoreId, x.StorefrontOrderId },
                        principalTable: "StorefrontOrders",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontCodCollections_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_StorefrontCodCollections_UserInStores_StoreId_RecordedByUserId",
                        columns: x => new { x.StoreId, x.RecordedByUserId },
                        principalTable: "UserInStores",
                        principalColumns: new[] { "StoreId", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StorefrontFulfillmentEvidence",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StorefrontOrderId = table.Column<int>(type: "int", nullable: false),
                    FulfillmentSourceId = table.Column<int>(type: "int", nullable: true),
                    ActorUserId = table.Column<int>(type: "int", nullable: false),
                    ClientRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    SnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
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
                    table.PrimaryKey("PK_StorefrontFulfillmentEvidence", x => x.Id);
                    table.UniqueConstraint("AK_StorefrontFulfillmentEvidence_StoreId_Id", x => new { x.StoreId, x.Id });
                    table.UniqueConstraint("AK_StorefrontFulfillmentEvidence_StoreId_StorefrontOrderId_Id", x => new { x.StoreId, x.StorefrontOrderId, x.Id });
                    table.CheckConstraint("CK_StorefrontFulfillmentEvidence_Json", "ISJSON([SnapshotJson])=1");
                    table.CheckConstraint("CK_StorefrontFulfillmentEvidence_Live", "[IsDeleted]=0");
                    table.ForeignKey(
                        name: "FK_StorefrontFulfillmentEvidence_StorefrontFulfillmentSources_StoreId_StorefrontOrderId_FulfillmentSourceId",
                        columns: x => new { x.StoreId, x.StorefrontOrderId, x.FulfillmentSourceId },
                        principalTable: "StorefrontFulfillmentSources",
                        principalColumns: new[] { "StoreId", "StorefrontOrderId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontFulfillmentEvidence_StorefrontOrders_StoreId_StorefrontOrderId",
                        columns: x => new { x.StoreId, x.StorefrontOrderId },
                        principalTable: "StorefrontOrders",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontFulfillmentEvidence_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_StorefrontFulfillmentEvidence_UserInStores_StoreId_ActorUserId",
                        columns: x => new { x.StoreId, x.ActorUserId },
                        principalTable: "UserInStores",
                        principalColumns: new[] { "StoreId", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StorefrontInventoryIssueSources",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StorefrontOrderId = table.Column<int>(type: "int", nullable: false),
                    OrderInventoryIssueId = table.Column<int>(type: "int", nullable: false),
                    OrderInventoryIssueLineId = table.Column<int>(type: "int", nullable: false),
                    InventoryValuationEntryId = table.Column<int>(type: "int", nullable: false),
                    InventoryCostLayerAllocationId = table.Column<int>(type: "int", nullable: false),
                    BaselineResolvedQuantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    UnresolvedQuantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
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
                    table.PrimaryKey("PK_StorefrontInventoryIssueSources", x => x.Id);
                    table.UniqueConstraint("AK_StorefrontInventoryIssueSources_StoreId_Id", x => new { x.StoreId, x.Id });
                    table.CheckConstraint("CK_StorefrontInventoryIssueSources_Live", "[IsDeleted]=0");
                    table.CheckConstraint("CK_StorefrontInventoryIssueSources_Quantity", "[BaselineResolvedQuantity]>=0 AND [UnresolvedQuantity]>0");
                    table.ForeignKey(
                        name: "FK_StorefrontInventoryIssueSources_InventoryCostLayerAllocations_StoreId_InventoryCostLayerAllocationId",
                        columns: x => new { x.StoreId, x.InventoryCostLayerAllocationId },
                        principalTable: "InventoryCostLayerAllocations",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontInventoryIssueSources_InventoryValuationEntries_StoreId_InventoryValuationEntryId",
                        columns: x => new { x.StoreId, x.InventoryValuationEntryId },
                        principalTable: "InventoryValuationEntries",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontInventoryIssueSources_OrderInventoryIssueLines_StoreId_OrderInventoryIssueLineId",
                        columns: x => new { x.StoreId, x.OrderInventoryIssueLineId },
                        principalTable: "OrderInventoryIssueLines",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontInventoryIssueSources_OrderInventoryIssues_StoreId_OrderInventoryIssueId",
                        columns: x => new { x.StoreId, x.OrderInventoryIssueId },
                        principalTable: "OrderInventoryIssues",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontInventoryIssueSources_StorefrontOrders_StoreId_StorefrontOrderId",
                        columns: x => new { x.StoreId, x.StorefrontOrderId },
                        principalTable: "StorefrontOrders",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontInventoryIssueSources_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "StorefrontNotificationAttestations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    HadManualAmbiguity = table.Column<bool>(type: "bit", nullable: false),
                    BankNotificationReceiptId = table.Column<int>(type: "int", nullable: false),
                    DeviceId = table.Column<int>(type: "int", nullable: false),
                    BankAccountId = table.Column<int>(type: "int", nullable: false),
                    AccountIdentity = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    AccountVersion = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    SourcePackage = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    PayloadHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    EvidenceKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    NativeTransactionId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    BankOccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AuthenticatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AccountSnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CollectionAuthoritySnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
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
                    table.PrimaryKey("PK_StorefrontNotificationAttestations", x => x.Id);
                    table.UniqueConstraint("AK_StorefrontNotificationAttestations_StoreId_Id", x => new { x.StoreId, x.Id });
                    table.CheckConstraint("CK_StorefrontNotificationAttestations_AccountSnapshotJson", "ISJSON([AccountSnapshotJson])=1");
                    table.CheckConstraint("CK_StorefrontNotificationAttestations_CollectionAuthoritySnapshotJson", "ISJSON([CollectionAuthoritySnapshotJson])=1");
                    table.CheckConstraint("CK_StorefrontNotificationAttestations_Live", "[IsDeleted]=0");
                    table.ForeignKey(
                        name: "FK_StorefrontNotificationAttestations_BankNotificationDevices_StoreId_DeviceId",
                        columns: x => new { x.StoreId, x.DeviceId },
                        principalTable: "BankNotificationDevices",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontNotificationAttestations_BankNotificationReceipts_StoreId_BankNotificationReceiptId",
                        columns: x => new { x.StoreId, x.BankNotificationReceiptId },
                        principalTable: "BankNotificationReceipts",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontNotificationAttestations_StoreBankAccounts_StoreId_BankAccountId",
                        columns: x => new { x.StoreId, x.BankAccountId },
                        principalTable: "StoreBankAccounts",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontNotificationAttestations_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "StorefrontReservationTransfers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StorefrontOrderId = table.Column<int>(type: "int", nullable: false),
                    FulfillmentSourceId = table.Column<int>(type: "int", nullable: false),
                    OrderLineId = table.Column<int>(type: "int", nullable: false),
                    WebReservationId = table.Column<int>(type: "int", nullable: false),
                    DeliveryReservationId = table.Column<int>(type: "int", nullable: false),
                    BaseQuantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
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
                    table.PrimaryKey("PK_StorefrontReservationTransfers", x => x.Id);
                    table.UniqueConstraint("AK_StorefrontReservationTransfers_StoreId_Id", x => new { x.StoreId, x.Id });
                    table.CheckConstraint("CK_StorefrontReservationTransfers_Live", "[IsDeleted]=0");
                    table.CheckConstraint("CK_StorefrontReservationTransfers_Qty", "[BaseQuantity]>0");
                    table.ForeignKey(
                        name: "FK_StorefrontReservationTransfers_InventoryReservations_StoreId_DeliveryReservationId",
                        columns: x => new { x.StoreId, x.DeliveryReservationId },
                        principalTable: "InventoryReservations",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontReservationTransfers_InventoryReservations_StoreId_WebReservationId",
                        columns: x => new { x.StoreId, x.WebReservationId },
                        principalTable: "InventoryReservations",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontReservationTransfers_StorefrontFulfillmentSources_StoreId_StorefrontOrderId_FulfillmentSourceId",
                        columns: x => new { x.StoreId, x.StorefrontOrderId, x.FulfillmentSourceId },
                        principalTable: "StorefrontFulfillmentSources",
                        principalColumns: new[] { "StoreId", "StorefrontOrderId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontReservationTransfers_StorefrontOrderLines_StoreId_StorefrontOrderId_OrderLineId",
                        columns: x => new { x.StoreId, x.StorefrontOrderId, x.OrderLineId },
                        principalTable: "StorefrontOrderLines",
                        principalColumns: new[] { "StoreId", "StorefrontOrderId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontReservationTransfers_StorefrontOrders_StoreId_StorefrontOrderId",
                        columns: x => new { x.StoreId, x.StorefrontOrderId },
                        principalTable: "StorefrontOrders",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontReservationTransfers_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "StorefrontSaleBindings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StorefrontOrderId = table.Column<int>(type: "int", nullable: false),
                    SaleOrderId = table.Column<int>(type: "int", nullable: false),
                    SettlingShiftId = table.Column<int>(type: "int", nullable: false),
                    ActorUserId = table.Column<int>(type: "int", nullable: false),
                    ClientRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceSnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
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
                    table.PrimaryKey("PK_StorefrontSaleBindings", x => x.Id);
                    table.UniqueConstraint("AK_StorefrontSaleBindings_StoreId_Id", x => new { x.StoreId, x.Id });
                    table.UniqueConstraint("AK_StorefrontSaleBindings_StoreId_StorefrontOrderId_SaleOrderId_SettlingShiftId_ActorUserId", x => new { x.StoreId, x.StorefrontOrderId, x.SaleOrderId, x.SettlingShiftId, x.ActorUserId });
                    table.CheckConstraint("CK_StorefrontSaleBindings_Live", "[IsDeleted]=0");
                    table.CheckConstraint("CK_StorefrontSaleBindings_SourceSnapshotJson", "ISJSON([SourceSnapshotJson])=1");
                    table.ForeignKey(
                        name: "FK_StorefrontSaleBindings_Orders_StoreId_SaleOrderId",
                        columns: x => new { x.StoreId, x.SaleOrderId },
                        principalTable: "Orders",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontSaleBindings_POSShifts_StoreId_SettlingShiftId_ActorUserId",
                        columns: x => new { x.StoreId, x.SettlingShiftId, x.ActorUserId },
                        principalTable: "POSShifts",
                        principalColumns: new[] { "StoreId", "Id", "OpenedByUserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontSaleBindings_StorefrontOrders_StoreId_StorefrontOrderId",
                        columns: x => new { x.StoreId, x.StorefrontOrderId },
                        principalTable: "StorefrontOrders",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontSaleBindings_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_StorefrontSaleBindings_UserInStores_StoreId_ActorUserId",
                        columns: x => new { x.StoreId, x.ActorUserId },
                        principalTable: "UserInStores",
                        principalColumns: new[] { "StoreId", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StorefrontServiceRevenues",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CashOnlyFunding = table.Column<bool>(type: "bit", nullable: false),
                    StorefrontOrderId = table.Column<int>(type: "int", nullable: false),
                    LegalEntityId = table.Column<int>(type: "int", nullable: false),
                    TaxId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    GrossAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    NetAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TaxAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TaxRate = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    InvoiceRoute = table.Column<byte>(type: "tinyint", nullable: false),
                    SettlingShiftId = table.Column<int>(type: "int", nullable: false),
                    ActorUserId = table.Column<int>(type: "int", nullable: false),
                    RecognizedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SourceSnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
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
                    table.PrimaryKey("PK_StorefrontServiceRevenues", x => x.Id);
                    table.UniqueConstraint("AK_StorefrontServiceRevenues_StoreId_Id", x => new { x.StoreId, x.Id });
                    table.CheckConstraint("CK_StorefrontServiceRevenues_Arithmetic", "[GrossAmount]=[NetAmount]+[TaxAmount] AND [NetAmount]>=0 AND [TaxAmount]>=0 AND [TaxRate]>=0 AND [TaxRate]<=100 AND [InvoiceRoute] IN (1,2)");
                    table.CheckConstraint("CK_StorefrontServiceRevenues_GrossAmount", "[GrossAmount]>0 AND [GrossAmount]<10000000000000000 AND [GrossAmount]=ROUND([GrossAmount],0)");
                    table.CheckConstraint("CK_StorefrontServiceRevenues_Live", "[IsDeleted]=0");
                    table.CheckConstraint("CK_StorefrontServiceRevenues_SourceSnapshotJson", "ISJSON([SourceSnapshotJson])=1");
                    table.ForeignKey(
                        name: "FK_StorefrontServiceRevenues_LegalEntities_StoreId_LegalEntityId",
                        columns: x => new { x.StoreId, x.LegalEntityId },
                        principalTable: "LegalEntities",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontServiceRevenues_POSShifts_StoreId_SettlingShiftId_ActorUserId",
                        columns: x => new { x.StoreId, x.SettlingShiftId, x.ActorUserId },
                        principalTable: "POSShifts",
                        principalColumns: new[] { "StoreId", "Id", "OpenedByUserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontServiceRevenues_StorefrontOrders_StoreId_StorefrontOrderId",
                        columns: x => new { x.StoreId, x.StorefrontOrderId },
                        principalTable: "StorefrontOrders",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontServiceRevenues_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_StorefrontServiceRevenues_Taxes_StoreId_TaxId",
                        columns: x => new { x.StoreId, x.TaxId },
                        principalTable: "Taxes",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontServiceRevenues_UserInStores_StoreId_ActorUserId",
                        columns: x => new { x.StoreId, x.ActorUserId },
                        principalTable: "UserInStores",
                        principalColumns: new[] { "StoreId", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StorefrontServiceRevenueSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LegalEntityId = table.Column<int>(type: "int", nullable: false),
                    TaxId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    InvoiceRoute = table.Column<byte>(type: "tinyint", nullable: false),
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
                    table.PrimaryKey("PK_StorefrontServiceRevenueSettings", x => x.Id);
                    table.UniqueConstraint("AK_StorefrontServiceRevenueSettings_StoreId_Id", x => new { x.StoreId, x.Id });
                    table.CheckConstraint("CK_StorefrontServiceRevenueSettings_Live", "[IsDeleted]=0");
                    table.CheckConstraint("CK_StorefrontServiceRevenueSettings_Route", "[InvoiceRoute] IN (1,2)");
                    table.ForeignKey(
                        name: "FK_StorefrontServiceRevenueSettings_LegalEntities_StoreId_LegalEntityId",
                        columns: x => new { x.StoreId, x.LegalEntityId },
                        principalTable: "LegalEntities",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontServiceRevenueSettings_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_StorefrontServiceRevenueSettings_Taxes_StoreId_TaxId",
                        columns: x => new { x.StoreId, x.TaxId },
                        principalTable: "Taxes",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StorefrontCodReceipts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StorefrontOrderId = table.Column<int>(type: "int", nullable: false),
                    GlobalBankReceiptId = table.Column<int>(type: "int", nullable: true),
                    CodBankRoutingId = table.Column<int>(type: "int", nullable: true),
                    TreasuryEntryId = table.Column<int>(type: "int", nullable: true),
                    POSShiftCashTransactionId = table.Column<int>(type: "int", nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ActorUserId = table.Column<int>(type: "int", nullable: false),
                    SourceSnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
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
                    table.PrimaryKey("PK_StorefrontCodReceipts", x => x.Id);
                    table.UniqueConstraint("AK_StorefrontCodReceipts_StoreId_Id", x => new { x.StoreId, x.Id });
                    table.CheckConstraint("CK_StorefrontCodReceipts_Amount", "[Amount]>0 AND [Amount]<10000000000000000 AND [Amount]=ROUND([Amount],0)");
                    table.CheckConstraint("CK_StorefrontCodReceipts_Live", "[IsDeleted]=0");
                    table.CheckConstraint("CK_StorefrontCodReceipts_Source", "([GlobalBankReceiptId] IS NOT NULL AND [CodBankRoutingId] IS NOT NULL AND [TreasuryEntryId] IS NULL AND [POSShiftCashTransactionId] IS NULL) OR ([GlobalBankReceiptId] IS NULL AND [CodBankRoutingId] IS NULL AND [POSShiftCashTransactionId] IS NOT NULL)");
                    table.CheckConstraint("CK_StorefrontCodReceipts_SourceSnapshotJson", "ISJSON([SourceSnapshotJson])=1");
                    table.ForeignKey(
                        name: "FK_StorefrontCodReceipts_BankVerifiedReceipts_StoreId_GlobalBankReceiptId",
                        columns: x => new { x.StoreId, x.GlobalBankReceiptId },
                        principalTable: "BankVerifiedReceipts",
                        principalColumns: new[] { "OriginStoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontCodReceipts_POSShiftCashTransactions_StoreId_POSShiftCashTransactionId",
                        columns: x => new { x.StoreId, x.POSShiftCashTransactionId },
                        principalTable: "POSShiftCashTransactions",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontCodReceipts_StorefrontCodBankRoutings_StoreId_CodBankRoutingId",
                        columns: x => new { x.StoreId, x.CodBankRoutingId },
                        principalTable: "StorefrontCodBankRoutings",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontCodReceipts_StorefrontOrders_StoreId_StorefrontOrderId",
                        columns: x => new { x.StoreId, x.StorefrontOrderId },
                        principalTable: "StorefrontOrders",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontCodReceipts_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_StorefrontCodReceipts_TreasuryEntries_StoreId_TreasuryEntryId",
                        columns: x => new { x.StoreId, x.TreasuryEntryId },
                        principalTable: "TreasuryEntries",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontCodReceipts_UserInStores_StoreId_ActorUserId",
                        columns: x => new { x.StoreId, x.ActorUserId },
                        principalTable: "UserInStores",
                        principalColumns: new[] { "StoreId", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StorefrontCustodyIncidents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StorefrontOrderId = table.Column<int>(type: "int", nullable: false),
                    EvidenceId = table.Column<int>(type: "int", nullable: false),
                    ActualCustodySourceIdsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ReconciledByEvidenceId = table.Column<int>(type: "int", nullable: true),
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
                    table.PrimaryKey("PK_StorefrontCustodyIncidents", x => x.Id);
                    table.UniqueConstraint("AK_StorefrontCustodyIncidents_StoreId_Id", x => new { x.StoreId, x.Id });
                    table.CheckConstraint("CK_StorefrontCustodyIncidents_Json", "ISJSON([ActualCustodySourceIdsJson])=1");
                    table.CheckConstraint("CK_StorefrontCustodyIncidents_Live", "[IsDeleted]=0");
                    table.ForeignKey(
                        name: "FK_StorefrontCustodyIncidents_StorefrontFulfillmentEvidence_StoreId_StorefrontOrderId_EvidenceId",
                        columns: x => new { x.StoreId, x.StorefrontOrderId, x.EvidenceId },
                        principalTable: "StorefrontFulfillmentEvidence",
                        principalColumns: new[] { "StoreId", "StorefrontOrderId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontCustodyIncidents_StorefrontFulfillmentEvidence_StoreId_StorefrontOrderId_ReconciledByEvidenceId",
                        columns: x => new { x.StoreId, x.StorefrontOrderId, x.ReconciledByEvidenceId },
                        principalTable: "StorefrontFulfillmentEvidence",
                        principalColumns: new[] { "StoreId", "StorefrontOrderId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontCustodyIncidents_StorefrontOrders_StoreId_StorefrontOrderId",
                        columns: x => new { x.StoreId, x.StorefrontOrderId },
                        principalTable: "StorefrontOrders",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontCustodyIncidents_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "StorefrontFulfillmentWorks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LatestEvidenceId = table.Column<int>(type: "int", nullable: true),
                    StorefrontOrderId = table.Column<int>(type: "int", nullable: false),
                    FulfillmentSourceId = table.Column<int>(type: "int", nullable: false),
                    DeliveryOrderId = table.Column<int>(type: "int", nullable: false),
                    Stage = table.Column<int>(type: "int", nullable: false),
                    CourierUserId = table.Column<int>(type: "int", nullable: true),
                    CarrierName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    TrackingCode = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
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
                    table.PrimaryKey("PK_StorefrontFulfillmentWorks", x => x.Id);
                    table.UniqueConstraint("AK_StorefrontFulfillmentWorks_StoreId_Id", x => new { x.StoreId, x.Id });
                    table.CheckConstraint("CK_StorefrontFulfillmentWorks_Live", "[IsDeleted]=0");
                    table.CheckConstraint("CK_StorefrontFulfillmentWorks_Stage", "[Stage] BETWEEN 0 AND 6");
                    table.ForeignKey(
                        name: "FK_StorefrontFulfillmentWorks_DeliveryOrders_StoreId_DeliveryOrderId",
                        columns: x => new { x.StoreId, x.DeliveryOrderId },
                        principalTable: "DeliveryOrders",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontFulfillmentWorks_StorefrontFulfillmentEvidence_StoreId_StorefrontOrderId_LatestEvidenceId",
                        columns: x => new { x.StoreId, x.StorefrontOrderId, x.LatestEvidenceId },
                        principalTable: "StorefrontFulfillmentEvidence",
                        principalColumns: new[] { "StoreId", "StorefrontOrderId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontFulfillmentWorks_StorefrontFulfillmentSources_StoreId_StorefrontOrderId_FulfillmentSourceId",
                        columns: x => new { x.StoreId, x.StorefrontOrderId, x.FulfillmentSourceId },
                        principalTable: "StorefrontFulfillmentSources",
                        principalColumns: new[] { "StoreId", "StorefrontOrderId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontFulfillmentWorks_StorefrontOrders_StoreId_StorefrontOrderId",
                        columns: x => new { x.StoreId, x.StorefrontOrderId },
                        principalTable: "StorefrontOrders",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontFulfillmentWorks_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_StorefrontFulfillmentWorks_UserInStores_StoreId_CourierUserId",
                        columns: x => new { x.StoreId, x.CourierUserId },
                        principalTable: "UserInStores",
                        principalColumns: new[] { "StoreId", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StorefrontNotificationCertifications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    NotificationAttestationId = table.Column<int>(type: "int", nullable: false),
                    GlobalBankReceiptId = table.Column<int>(type: "int", nullable: false),
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
                    table.PrimaryKey("PK_StorefrontNotificationCertifications", x => x.Id);
                    table.UniqueConstraint("AK_StorefrontNotificationCertifications_StoreId_Id", x => new { x.StoreId, x.Id });
                    table.CheckConstraint("CK_StorefrontNotificationCertifications_Live", "[IsDeleted]=0");
                    table.ForeignKey(
                        name: "FK_StorefrontNotificationCertifications_BankVerifiedReceipts_StoreId_GlobalBankReceiptId",
                        columns: x => new { x.StoreId, x.GlobalBankReceiptId },
                        principalTable: "BankVerifiedReceipts",
                        principalColumns: new[] { "OriginStoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontNotificationCertifications_StorefrontNotificationAttestations_StoreId_NotificationAttestationId",
                        columns: x => new { x.StoreId, x.NotificationAttestationId },
                        principalTable: "StorefrontNotificationAttestations",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontNotificationCertifications_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "StorefrontInvoiceWorks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    NextAttemptAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    StorefrontOrderId = table.Column<int>(type: "int", nullable: false),
                    SaleOrderId = table.Column<int>(type: "int", nullable: false),
                    ServiceRevenueId = table.Column<int>(type: "int", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    LastErrorCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
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
                    table.PrimaryKey("PK_StorefrontInvoiceWorks", x => x.Id);
                    table.UniqueConstraint("AK_StorefrontInvoiceWorks_StoreId_Id", x => new { x.StoreId, x.Id });
                    table.CheckConstraint("CK_StorefrontInvoiceWorks_Live", "[IsDeleted]=0");
                    table.ForeignKey(
                        name: "FK_StorefrontInvoiceWorks_Orders_StoreId_SaleOrderId",
                        columns: x => new { x.StoreId, x.SaleOrderId },
                        principalTable: "Orders",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontInvoiceWorks_StorefrontOrders_StoreId_StorefrontOrderId",
                        columns: x => new { x.StoreId, x.StorefrontOrderId },
                        principalTable: "StorefrontOrders",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontInvoiceWorks_StorefrontServiceRevenues_StoreId_ServiceRevenueId",
                        columns: x => new { x.StoreId, x.ServiceRevenueId },
                        principalTable: "StorefrontServiceRevenues",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontInvoiceWorks_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "StorefrontServiceFundings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StorefrontOrderId = table.Column<int>(type: "int", nullable: false),
                    ServiceRevenueId = table.Column<int>(type: "int", nullable: false),
                    GlobalBankReceiptId = table.Column<int>(type: "int", nullable: true),
                    CodReceiptId = table.Column<int>(type: "int", nullable: true),
                    CarrierWithholdingId = table.Column<int>(type: "int", nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
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
                    table.PrimaryKey("PK_StorefrontServiceFundings", x => x.Id);
                    table.UniqueConstraint("AK_StorefrontServiceFundings_StoreId_Id", x => new { x.StoreId, x.Id });
                    table.CheckConstraint("CK_StorefrontServiceFundings_Amount", "[Amount]>0 AND [Amount]<10000000000000000 AND [Amount]=ROUND([Amount],0)");
                    table.CheckConstraint("CK_StorefrontServiceFundings_Live", "[IsDeleted]=0");
                    table.CheckConstraint("CK_StorefrontServiceFundings_Source", "(CASE WHEN [GlobalBankReceiptId] IS NOT NULL THEN 1 ELSE 0 END + CASE WHEN [CodReceiptId] IS NOT NULL THEN 1 ELSE 0 END + CASE WHEN [CarrierWithholdingId] IS NOT NULL THEN 1 ELSE 0 END)=1");
                    table.ForeignKey(
                        name: "FK_StorefrontServiceFundings_BankVerifiedReceipts_StoreId_GlobalBankReceiptId",
                        columns: x => new { x.StoreId, x.GlobalBankReceiptId },
                        principalTable: "BankVerifiedReceipts",
                        principalColumns: new[] { "OriginStoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontServiceFundings_StorefrontCarrierWithholdings_StoreId_CarrierWithholdingId",
                        columns: x => new { x.StoreId, x.CarrierWithholdingId },
                        principalTable: "StorefrontCarrierWithholdings",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontServiceFundings_StorefrontCodReceipts_StoreId_CodReceiptId",
                        columns: x => new { x.StoreId, x.CodReceiptId },
                        principalTable: "StorefrontCodReceipts",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontServiceFundings_StorefrontOrders_StoreId_StorefrontOrderId",
                        columns: x => new { x.StoreId, x.StorefrontOrderId },
                        principalTable: "StorefrontOrders",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontServiceFundings_StorefrontServiceRevenues_StoreId_ServiceRevenueId",
                        columns: x => new { x.StoreId, x.ServiceRevenueId },
                        principalTable: "StorefrontServiceRevenues",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontServiceFundings_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontSettlementLinks_StoreId_SaleOrderId",
                table: "StorefrontSettlementLinks",
                columns: new[] { "StoreId", "SaleOrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontSettlementLinks_StoreId_StorefrontOrderId_SaleOrderId_SettlingShiftId_SettledByUserId",
                table: "StorefrontSettlementLinks",
                columns: new[] { "StoreId", "StorefrontOrderId", "SaleOrderId", "SettlingShiftId", "SettledByUserId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_StorefrontSettlementLinks_GoodsAmount",
                table: "StorefrontSettlementLinks",
                sql: "[GoodsAmount] >= 0 AND [GoodsAmount] = ROUND([GoodsAmount], 0)");

            migrationBuilder.CreateIndex(
                name: "IX_OrderPayments_StoreId_StorefrontCarrierWithholdingId",
                table: "OrderPayments",
                columns: new[] { "StoreId", "StorefrontCarrierWithholdingId" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderPayments_StoreId_StorefrontCodReceiptId",
                table: "OrderPayments",
                columns: new[] { "StoreId", "StorefrontCodReceiptId" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderLines_StoreId_StorefrontOrderId_StorefrontOrderLineId",
                table: "OrderLines",
                columns: new[] { "StoreId", "StorefrontOrderId", "StorefrontOrderLineId" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderLines_StoreId_StorefrontOrderLineId",
                table: "OrderLines",
                columns: new[] { "StoreId", "StorefrontOrderLineId" },
                unique: true,
                filter: "[StorefrontOrderLineId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_OrderLegalEntityAllocations_StoreId_InvoiceUnitId",
                table: "OrderLegalEntityAllocations",
                columns: new[] { "StoreId", "InvoiceUnitId" });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceHeads_StoreId_NativeServiceActorUserId",
                table: "InvoiceHeads",
                columns: new[] { "StoreId", "NativeServiceActorUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceHeads_StoreId_StorefrontServiceRevenueId",
                table: "InvoiceHeads",
                columns: new[] { "StoreId", "StorefrontServiceRevenueId" },
                unique: true,
                filter: "[StorefrontServiceRevenueId] IS NOT NULL AND [OriginalInvoiceHeadId] IS NULL AND [IsDeleted]=0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_InvoiceHeads_LegacyOrder",
                table: "InvoiceHeads",
                sql: "[OrderId] IS NOT NULL OR ([LegacySourceId] IS NOT NULL AND [LegacyReadOnly] = 1) OR [IsAutoInvoiceGroup] = 1 OR ([StorefrontServiceRevenueId] IS NOT NULL AND [NativeServiceInvoiceRoute] IN (1,2) AND [NativeServiceRecognizedAtUtc] IS NOT NULL AND [NativeServiceActorUserId] IS NOT NULL AND [LegacyReadOnly]=0 AND [LegacySourceId] IS NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceDetails_StoreId_StorefrontServiceRevenueId",
                table: "InvoiceDetails",
                columns: new[] { "StoreId", "StorefrontServiceRevenueId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_DeliveryPickingWorks_Approval",
                table: "DeliveryPickingWorks",
                sql: "([ApprovedRevision] IS NULL AND [ApprovedByUserId] IS NULL AND [ApprovedAtUtc] IS NULL AND [ApprovedTotal] IS NULL) OR ([ApprovedRevision] IS NOT NULL AND [ApprovedRevision]>0 AND [ApprovedByUserId] IS NOT NULL AND [ApprovedAtUtc] IS NOT NULL AND [ApprovedTotal] IS NOT NULL AND [ApprovedTotal]>=0 AND [ApprovedTotal]=ROUND([ApprovedTotal],0))");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryOrders_StoreId_SourceCartId",
                table: "DeliveryOrders",
                columns: new[] { "StoreId", "SourceCartId" },
                unique: true,
                filter: "[SourceCartId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryOrders_StoreId_StorefrontFulfillmentSourceId",
                table: "DeliveryOrders",
                columns: new[] { "StoreId", "StorefrontFulfillmentSourceId" },
                unique: true,
                filter: "[StorefrontFulfillmentSourceId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryOrders_StoreId_StorefrontOrderId_StorefrontFulfillmentSourceId",
                table: "DeliveryOrders",
                columns: new[] { "StoreId", "StorefrontOrderId", "StorefrontFulfillmentSourceId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_DeliveryOrders_NativeOrigin",
                table: "DeliveryOrders",
                sql: "([SourceKind]=0 AND [SourceCartId] IS NOT NULL AND [CreatedTerminalId] IS NOT NULL AND [CreatedShiftId] IS NOT NULL AND [StorefrontOrderId] IS NULL AND [StorefrontFulfillmentSourceId] IS NULL AND [StorefrontAllowNegativeInventory] IS NULL) OR ([SourceKind]=1 AND [SourceCartId] IS NULL AND [CreatedTerminalId] IS NULL AND [CreatedShiftId] IS NULL AND [StorefrontOrderId] IS NOT NULL AND [StorefrontFulfillmentSourceId] IS NOT NULL AND [StorefrontAllowNegativeInventory] IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_DeliveryOrders_Total",
                table: "DeliveryOrders",
                sql: "(([SourceKind]=0 AND [QuotedTotal]>0) OR ([SourceKind]=1 AND [QuotedTotal]>=0)) AND [QuotedTotal] = ROUND([QuotedTotal], 0)");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryOrderLines_StoreId_StorefrontOrderId_StorefrontOrderLineId",
                table: "DeliveryOrderLines",
                columns: new[] { "StoreId", "StorefrontOrderId", "StorefrontOrderLineId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_DeliveryOrderLines_Identity",
                table: "DeliveryOrderLines",
                sql: "([StorefrontOrderId] IS NULL AND [StorefrontOrderLineId] IS NULL AND [SourceCartId] IS NOT NULL AND (([SourceOrderLineId] IS NOT NULL AND [OriginalRootLineId] IS NULL AND [ProductUnitConversionId] IS NULL AND [SellingUnitId] IS NULL AND [BaseUnitId] IS NULL) OR ([SourceOrderLineId] IS NULL AND [OriginalRootLineId] IS NOT NULL AND [SellingUnitId] IS NOT NULL AND [BaseUnitId] IS NOT NULL AND [LineDiscount]=0 AND [AllocatedOrderDiscount]=0))) OR ([StorefrontOrderId] IS NOT NULL AND [StorefrontOrderLineId] IS NOT NULL AND [SourceCartId] IS NULL AND [SourceOrderLineId] IS NULL AND [OriginalRootLineId] IS NULL AND [SellingUnitId] IS NOT NULL AND [BaseUnitId] IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_BankVerifiedReceipts_CodProvenance",
                table: "BankVerifiedReceipts",
                sql: "[RoutingProvenance] IN (0,1,2) AND ([NativeCollectionAuthorityProofJson] IS NULL OR ISJSON([NativeCollectionAuthorityProofJson])=1)");

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontCarrierWithholdings_StoreId_ActorUserId",
                table: "StorefrontCarrierWithholdings",
                columns: new[] { "StoreId", "ActorUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontCarrierWithholdings_StoreId_LegalEntityId",
                table: "StorefrontCarrierWithholdings",
                columns: new[] { "StoreId", "LegalEntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontCarrierWithholdings_StoreId_OperatingExpenseId",
                table: "StorefrontCarrierWithholdings",
                columns: new[] { "StoreId", "OperatingExpenseId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontCarrierWithholdings_StoreId_StorefrontOrderId",
                table: "StorefrontCarrierWithholdings",
                columns: new[] { "StoreId", "StorefrontOrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontCodBankRoutings_GlobalBankReceiptId",
                table: "StorefrontCodBankRoutings",
                column: "GlobalBankReceiptId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontCodBankRoutings_StoreId_ActorUserId",
                table: "StorefrontCodBankRoutings",
                columns: new[] { "StoreId", "ActorUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontCodBankRoutings_StoreId_GlobalBankReceiptId",
                table: "StorefrontCodBankRoutings",
                columns: new[] { "StoreId", "GlobalBankReceiptId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontCodBankRoutings_StoreId_ReceivingLegalEntityId",
                table: "StorefrontCodBankRoutings",
                columns: new[] { "StoreId", "ReceivingLegalEntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontCodBankRoutings_StoreId_StorefrontOrderId",
                table: "StorefrontCodBankRoutings",
                columns: new[] { "StoreId", "StorefrontOrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontCodCollections_StoreId_RecordedByUserId",
                table: "StorefrontCodCollections",
                columns: new[] { "StoreId", "RecordedByUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontCodCollections_StoreId_StorefrontOrderId_FulfillmentSourceId",
                table: "StorefrontCodCollections",
                columns: new[] { "StoreId", "StorefrontOrderId", "FulfillmentSourceId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontCodReceipts_GlobalBankReceiptId",
                table: "StorefrontCodReceipts",
                column: "GlobalBankReceiptId",
                unique: true,
                filter: "[GlobalBankReceiptId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontCodReceipts_StoreId_ActorUserId",
                table: "StorefrontCodReceipts",
                columns: new[] { "StoreId", "ActorUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontCodReceipts_StoreId_CodBankRoutingId",
                table: "StorefrontCodReceipts",
                columns: new[] { "StoreId", "CodBankRoutingId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontCodReceipts_StoreId_GlobalBankReceiptId",
                table: "StorefrontCodReceipts",
                columns: new[] { "StoreId", "GlobalBankReceiptId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontCodReceipts_StoreId_POSShiftCashTransactionId",
                table: "StorefrontCodReceipts",
                columns: new[] { "StoreId", "POSShiftCashTransactionId" },
                unique: true,
                filter: "[POSShiftCashTransactionId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontCodReceipts_StoreId_StorefrontOrderId",
                table: "StorefrontCodReceipts",
                columns: new[] { "StoreId", "StorefrontOrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontCodReceipts_StoreId_TreasuryEntryId",
                table: "StorefrontCodReceipts",
                columns: new[] { "StoreId", "TreasuryEntryId" },
                unique: true,
                filter: "[TreasuryEntryId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontCustodyIncidents_StoreId_StorefrontOrderId_EvidenceId",
                table: "StorefrontCustodyIncidents",
                columns: new[] { "StoreId", "StorefrontOrderId", "EvidenceId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontCustodyIncidents_StoreId_StorefrontOrderId_ReconciledByEvidenceId",
                table: "StorefrontCustodyIncidents",
                columns: new[] { "StoreId", "StorefrontOrderId", "ReconciledByEvidenceId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontFulfillmentEvidence_StoreId_ActorUserId",
                table: "StorefrontFulfillmentEvidence",
                columns: new[] { "StoreId", "ActorUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontFulfillmentEvidence_StoreId_StorefrontOrderId_FulfillmentSourceId",
                table: "StorefrontFulfillmentEvidence",
                columns: new[] { "StoreId", "StorefrontOrderId", "FulfillmentSourceId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontFulfillmentWorks_StoreId_CourierUserId",
                table: "StorefrontFulfillmentWorks",
                columns: new[] { "StoreId", "CourierUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontFulfillmentWorks_StoreId_DeliveryOrderId",
                table: "StorefrontFulfillmentWorks",
                columns: new[] { "StoreId", "DeliveryOrderId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontFulfillmentWorks_StoreId_FulfillmentSourceId",
                table: "StorefrontFulfillmentWorks",
                columns: new[] { "StoreId", "FulfillmentSourceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontFulfillmentWorks_StoreId_StorefrontOrderId_FulfillmentSourceId",
                table: "StorefrontFulfillmentWorks",
                columns: new[] { "StoreId", "StorefrontOrderId", "FulfillmentSourceId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontFulfillmentWorks_StoreId_StorefrontOrderId_LatestEvidenceId",
                table: "StorefrontFulfillmentWorks",
                columns: new[] { "StoreId", "StorefrontOrderId", "LatestEvidenceId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontInventoryIssueSources_StoreId_InventoryCostLayerAllocationId",
                table: "StorefrontInventoryIssueSources",
                columns: new[] { "StoreId", "InventoryCostLayerAllocationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontInventoryIssueSources_StoreId_InventoryValuationEntryId",
                table: "StorefrontInventoryIssueSources",
                columns: new[] { "StoreId", "InventoryValuationEntryId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontInventoryIssueSources_StoreId_OrderInventoryIssueId",
                table: "StorefrontInventoryIssueSources",
                columns: new[] { "StoreId", "OrderInventoryIssueId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontInventoryIssueSources_StoreId_OrderInventoryIssueLineId",
                table: "StorefrontInventoryIssueSources",
                columns: new[] { "StoreId", "OrderInventoryIssueLineId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontInventoryIssueSources_StoreId_StorefrontOrderId",
                table: "StorefrontInventoryIssueSources",
                columns: new[] { "StoreId", "StorefrontOrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontInvoiceWorks_StoreId_SaleOrderId",
                table: "StorefrontInvoiceWorks",
                columns: new[] { "StoreId", "SaleOrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontInvoiceWorks_StoreId_ServiceRevenueId",
                table: "StorefrontInvoiceWorks",
                columns: new[] { "StoreId", "ServiceRevenueId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontInvoiceWorks_StoreId_StorefrontOrderId",
                table: "StorefrontInvoiceWorks",
                columns: new[] { "StoreId", "StorefrontOrderId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontNotificationAttestations_StoreId_BankAccountId",
                table: "StorefrontNotificationAttestations",
                columns: new[] { "StoreId", "BankAccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontNotificationAttestations_StoreId_BankNotificationReceiptId",
                table: "StorefrontNotificationAttestations",
                columns: new[] { "StoreId", "BankNotificationReceiptId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontNotificationAttestations_StoreId_DeviceId",
                table: "StorefrontNotificationAttestations",
                columns: new[] { "StoreId", "DeviceId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontNotificationCertifications_GlobalBankReceiptId",
                table: "StorefrontNotificationCertifications",
                column: "GlobalBankReceiptId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontNotificationCertifications_StoreId_GlobalBankReceiptId",
                table: "StorefrontNotificationCertifications",
                columns: new[] { "StoreId", "GlobalBankReceiptId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontNotificationCertifications_StoreId_NotificationAttestationId",
                table: "StorefrontNotificationCertifications",
                columns: new[] { "StoreId", "NotificationAttestationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontReservationTransfers_StoreId_DeliveryReservationId",
                table: "StorefrontReservationTransfers",
                columns: new[] { "StoreId", "DeliveryReservationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontReservationTransfers_StoreId_StorefrontOrderId_FulfillmentSourceId",
                table: "StorefrontReservationTransfers",
                columns: new[] { "StoreId", "StorefrontOrderId", "FulfillmentSourceId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontReservationTransfers_StoreId_StorefrontOrderId_OrderLineId",
                table: "StorefrontReservationTransfers",
                columns: new[] { "StoreId", "StorefrontOrderId", "OrderLineId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontReservationTransfers_StoreId_WebReservationId",
                table: "StorefrontReservationTransfers",
                columns: new[] { "StoreId", "WebReservationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontSaleBindings_StoreId_ActorUserId",
                table: "StorefrontSaleBindings",
                columns: new[] { "StoreId", "ActorUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontSaleBindings_StoreId_SaleOrderId",
                table: "StorefrontSaleBindings",
                columns: new[] { "StoreId", "SaleOrderId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontSaleBindings_StoreId_SettlingShiftId_ActorUserId",
                table: "StorefrontSaleBindings",
                columns: new[] { "StoreId", "SettlingShiftId", "ActorUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontSaleBindings_StoreId_StorefrontOrderId",
                table: "StorefrontSaleBindings",
                columns: new[] { "StoreId", "StorefrontOrderId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontServiceFundings_StoreId_CarrierWithholdingId",
                table: "StorefrontServiceFundings",
                columns: new[] { "StoreId", "CarrierWithholdingId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontServiceFundings_StoreId_CodReceiptId",
                table: "StorefrontServiceFundings",
                columns: new[] { "StoreId", "CodReceiptId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontServiceFundings_StoreId_GlobalBankReceiptId",
                table: "StorefrontServiceFundings",
                columns: new[] { "StoreId", "GlobalBankReceiptId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontServiceFundings_StoreId_ServiceRevenueId",
                table: "StorefrontServiceFundings",
                columns: new[] { "StoreId", "ServiceRevenueId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontServiceFundings_StoreId_StorefrontOrderId",
                table: "StorefrontServiceFundings",
                columns: new[] { "StoreId", "StorefrontOrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontServiceRevenues_StoreId_ActorUserId",
                table: "StorefrontServiceRevenues",
                columns: new[] { "StoreId", "ActorUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontServiceRevenues_StoreId_LegalEntityId",
                table: "StorefrontServiceRevenues",
                columns: new[] { "StoreId", "LegalEntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontServiceRevenues_StoreId_SettlingShiftId_ActorUserId",
                table: "StorefrontServiceRevenues",
                columns: new[] { "StoreId", "SettlingShiftId", "ActorUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontServiceRevenues_StoreId_StorefrontOrderId",
                table: "StorefrontServiceRevenues",
                columns: new[] { "StoreId", "StorefrontOrderId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontServiceRevenues_StoreId_TaxId",
                table: "StorefrontServiceRevenues",
                columns: new[] { "StoreId", "TaxId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontServiceRevenueSettings_StoreId",
                table: "StorefrontServiceRevenueSettings",
                column: "StoreId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontServiceRevenueSettings_StoreId_LegalEntityId",
                table: "StorefrontServiceRevenueSettings",
                columns: new[] { "StoreId", "LegalEntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontServiceRevenueSettings_StoreId_TaxId",
                table: "StorefrontServiceRevenueSettings",
                columns: new[] { "StoreId", "TaxId" });

            migrationBuilder.AddForeignKey(
                name: "FK_DeliveryOrderLines_DeliveryOrders_StoreId_DeliveryOrderId",
                table: "DeliveryOrderLines",
                columns: new[] { "StoreId", "DeliveryOrderId" },
                principalTable: "DeliveryOrders",
                principalColumns: new[] { "StoreId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_DeliveryOrderLines_StorefrontOrderLines_StoreId_StorefrontOrderId_StorefrontOrderLineId",
                table: "DeliveryOrderLines",
                columns: new[] { "StoreId", "StorefrontOrderId", "StorefrontOrderLineId" },
                principalTable: "StorefrontOrderLines",
                principalColumns: new[] { "StoreId", "StorefrontOrderId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_DeliveryOrders_StorefrontFulfillmentSources_StoreId_StorefrontOrderId_StorefrontFulfillmentSourceId",
                table: "DeliveryOrders",
                columns: new[] { "StoreId", "StorefrontOrderId", "StorefrontFulfillmentSourceId" },
                principalTable: "StorefrontFulfillmentSources",
                principalColumns: new[] { "StoreId", "StorefrontOrderId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_InvoiceDetails_StorefrontServiceRevenues_StoreId_StorefrontServiceRevenueId",
                table: "InvoiceDetails",
                columns: new[] { "StoreId", "StorefrontServiceRevenueId" },
                principalTable: "StorefrontServiceRevenues",
                principalColumns: new[] { "StoreId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_InvoiceHeads_StorefrontServiceRevenues_StoreId_StorefrontServiceRevenueId",
                table: "InvoiceHeads",
                columns: new[] { "StoreId", "StorefrontServiceRevenueId" },
                principalTable: "StorefrontServiceRevenues",
                principalColumns: new[] { "StoreId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_InvoiceHeads_UserInStores_StoreId_NativeServiceActorUserId",
                table: "InvoiceHeads",
                columns: new[] { "StoreId", "NativeServiceActorUserId" },
                principalTable: "UserInStores",
                principalColumns: new[] { "StoreId", "UserId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_OrderLegalEntityAllocations_Unit_StoreId_InvoiceUnitId",
                table: "OrderLegalEntityAllocations",
                columns: new[] { "StoreId", "InvoiceUnitId" },
                principalTable: "Unit",
                principalColumns: new[] { "StoreId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_OrderLines_StorefrontOrderLines_StoreId_StorefrontOrderId_StorefrontOrderLineId",
                table: "OrderLines",
                columns: new[] { "StoreId", "StorefrontOrderId", "StorefrontOrderLineId" },
                principalTable: "StorefrontOrderLines",
                principalColumns: new[] { "StoreId", "StorefrontOrderId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_OrderPayments_StorefrontCarrierWithholdings_StoreId_StorefrontCarrierWithholdingId",
                table: "OrderPayments",
                columns: new[] { "StoreId", "StorefrontCarrierWithholdingId" },
                principalTable: "StorefrontCarrierWithholdings",
                principalColumns: new[] { "StoreId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_OrderPayments_StorefrontCodReceipts_StoreId_StorefrontCodReceiptId",
                table: "OrderPayments",
                columns: new[] { "StoreId", "StorefrontCodReceiptId" },
                principalTable: "StorefrontCodReceipts",
                principalColumns: new[] { "StoreId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_StorefrontSettlementLinks_StorefrontSaleBindings_StoreId_StorefrontOrderId_SaleOrderId_SettlingShiftId_SettledByUserId",
                table: "StorefrontSettlementLinks",
                columns: new[] { "StoreId", "StorefrontOrderId", "SaleOrderId", "SettlingShiftId", "SettledByUserId" },
                principalTable: "StorefrontSaleBindings",
                principalColumns: new[] { "StoreId", "StorefrontOrderId", "SaleOrderId", "SettlingShiftId", "ActorUserId" },
                onDelete: ReferentialAction.Restrict);
            foreach (var guard in StorefrontFulfillmentSchemaSqlCatalog.Triggers) migrationBuilder.Sql(guard.Sql);
            migrationBuilder.Sql(StorefrontFulfillmentSchemaSqlCatalog.PermissionSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(StorefrontFulfillmentSchemaSqlCatalog.DownGuardSql);
            foreach (var guard in StorefrontFulfillmentSchemaSqlCatalog.Triggers) migrationBuilder.Sql($"DROP TRIGGER [dbo].[{guard.Name}];");
            migrationBuilder.DropForeignKey(
                name: "FK_DeliveryOrderLines_DeliveryOrders_StoreId_DeliveryOrderId",
                table: "DeliveryOrderLines");

            migrationBuilder.DropForeignKey(
                name: "FK_DeliveryOrderLines_StorefrontOrderLines_StoreId_StorefrontOrderId_StorefrontOrderLineId",
                table: "DeliveryOrderLines");

            migrationBuilder.DropForeignKey(
                name: "FK_DeliveryOrders_StorefrontFulfillmentSources_StoreId_StorefrontOrderId_StorefrontFulfillmentSourceId",
                table: "DeliveryOrders");

            migrationBuilder.DropForeignKey(
                name: "FK_InvoiceDetails_StorefrontServiceRevenues_StoreId_StorefrontServiceRevenueId",
                table: "InvoiceDetails");

            migrationBuilder.DropForeignKey(
                name: "FK_InvoiceHeads_StorefrontServiceRevenues_StoreId_StorefrontServiceRevenueId",
                table: "InvoiceHeads");

            migrationBuilder.DropForeignKey(
                name: "FK_InvoiceHeads_UserInStores_StoreId_NativeServiceActorUserId",
                table: "InvoiceHeads");

            migrationBuilder.DropForeignKey(
                name: "FK_OrderLegalEntityAllocations_Unit_StoreId_InvoiceUnitId",
                table: "OrderLegalEntityAllocations");

            migrationBuilder.DropForeignKey(
                name: "FK_OrderLines_StorefrontOrderLines_StoreId_StorefrontOrderId_StorefrontOrderLineId",
                table: "OrderLines");

            migrationBuilder.DropForeignKey(
                name: "FK_OrderPayments_StorefrontCarrierWithholdings_StoreId_StorefrontCarrierWithholdingId",
                table: "OrderPayments");

            migrationBuilder.DropForeignKey(
                name: "FK_OrderPayments_StorefrontCodReceipts_StoreId_StorefrontCodReceiptId",
                table: "OrderPayments");

            migrationBuilder.DropForeignKey(
                name: "FK_StorefrontSettlementLinks_StorefrontSaleBindings_StoreId_StorefrontOrderId_SaleOrderId_SettlingShiftId_SettledByUserId",
                table: "StorefrontSettlementLinks");

            migrationBuilder.DropTable(
                name: "StorefrontCodCollections");

            migrationBuilder.DropTable(
                name: "StorefrontCustodyIncidents");

            migrationBuilder.DropTable(
                name: "StorefrontFulfillmentWorks");

            migrationBuilder.DropTable(
                name: "StorefrontInventoryIssueSources");

            migrationBuilder.DropTable(
                name: "StorefrontInvoiceWorks");

            migrationBuilder.DropTable(
                name: "StorefrontNotificationCertifications");

            migrationBuilder.DropTable(
                name: "StorefrontReservationTransfers");

            migrationBuilder.DropTable(
                name: "StorefrontSaleBindings");

            migrationBuilder.DropTable(
                name: "StorefrontServiceFundings");

            migrationBuilder.DropTable(
                name: "StorefrontServiceRevenueSettings");

            migrationBuilder.DropTable(
                name: "StorefrontFulfillmentEvidence");

            migrationBuilder.DropTable(
                name: "StorefrontNotificationAttestations");

            migrationBuilder.DropTable(
                name: "StorefrontCarrierWithholdings");

            migrationBuilder.DropTable(
                name: "StorefrontCodReceipts");

            migrationBuilder.DropTable(
                name: "StorefrontServiceRevenues");

            migrationBuilder.DropTable(
                name: "StorefrontCodBankRoutings");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_TreasuryEntries_StoreId_Id",
                table: "TreasuryEntries");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Taxes_StoreId_Id",
                table: "Taxes");

            migrationBuilder.DropIndex(
                name: "IX_StorefrontSettlementLinks_StoreId_SaleOrderId",
                table: "StorefrontSettlementLinks");

            migrationBuilder.DropIndex(
                name: "IX_StorefrontSettlementLinks_StoreId_StorefrontOrderId_SaleOrderId_SettlingShiftId_SettledByUserId",
                table: "StorefrontSettlementLinks");

            migrationBuilder.DropCheckConstraint(
                name: "CK_StorefrontSettlementLinks_GoodsAmount",
                table: "StorefrontSettlementLinks");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_POSShiftCashTransactions_StoreId_Id",
                table: "POSShiftCashTransactions");

            migrationBuilder.DropIndex(
                name: "IX_OrderPayments_StoreId_StorefrontCarrierWithholdingId",
                table: "OrderPayments");

            migrationBuilder.DropIndex(
                name: "IX_OrderPayments_StoreId_StorefrontCodReceiptId",
                table: "OrderPayments");

            migrationBuilder.DropIndex(
                name: "IX_OrderLines_StoreId_StorefrontOrderId_StorefrontOrderLineId",
                table: "OrderLines");

            migrationBuilder.DropIndex(
                name: "IX_OrderLines_StoreId_StorefrontOrderLineId",
                table: "OrderLines");

            migrationBuilder.DropIndex(
                name: "IX_OrderLegalEntityAllocations_StoreId_InvoiceUnitId",
                table: "OrderLegalEntityAllocations");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_OrderInventoryIssues_StoreId_Id",
                table: "OrderInventoryIssues");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_OrderInventoryIssueLines_StoreId_Id",
                table: "OrderInventoryIssueLines");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_OperatingExpenses_StoreId_Id",
                table: "OperatingExpenses");

            migrationBuilder.DropIndex(
                name: "IX_InvoiceHeads_StoreId_NativeServiceActorUserId",
                table: "InvoiceHeads");

            migrationBuilder.DropIndex(
                name: "IX_InvoiceHeads_StoreId_StorefrontServiceRevenueId",
                table: "InvoiceHeads");

            migrationBuilder.DropCheckConstraint(
                name: "CK_InvoiceHeads_LegacyOrder",
                table: "InvoiceHeads");

            migrationBuilder.DropIndex(
                name: "IX_InvoiceDetails_StoreId_StorefrontServiceRevenueId",
                table: "InvoiceDetails");

            migrationBuilder.DropCheckConstraint(
                name: "CK_DeliveryPickingWorks_Approval",
                table: "DeliveryPickingWorks");

            migrationBuilder.DropIndex(
                name: "IX_DeliveryOrders_StoreId_SourceCartId",
                table: "DeliveryOrders");

            migrationBuilder.DropIndex(
                name: "IX_DeliveryOrders_StoreId_StorefrontFulfillmentSourceId",
                table: "DeliveryOrders");

            migrationBuilder.DropIndex(
                name: "IX_DeliveryOrders_StoreId_StorefrontOrderId_StorefrontFulfillmentSourceId",
                table: "DeliveryOrders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_DeliveryOrders_NativeOrigin",
                table: "DeliveryOrders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_DeliveryOrders_Total",
                table: "DeliveryOrders");

            migrationBuilder.DropIndex(
                name: "IX_DeliveryOrderLines_StoreId_StorefrontOrderId_StorefrontOrderLineId",
                table: "DeliveryOrderLines");

            migrationBuilder.DropCheckConstraint(
                name: "CK_DeliveryOrderLines_Identity",
                table: "DeliveryOrderLines");

            migrationBuilder.DropCheckConstraint(
                name: "CK_BankVerifiedReceipts_CodProvenance",
                table: "BankVerifiedReceipts");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_BankNotificationReceipts_StoreId_Id",
                table: "BankNotificationReceipts");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_BankNotificationDevices_StoreId_Id",
                table: "BankNotificationDevices");

            migrationBuilder.DropColumn(
                name: "LegalEntityModeCapturedAtUtc",
                table: "StorefrontOrders");

            migrationBuilder.DropColumn(
                name: "UseMultiLegalEntitySnapshot",
                table: "StorefrontOrders");

            migrationBuilder.DropColumn(
                name: "HasInputInvoiceSnapshot",
                table: "StorefrontOrderLines");

            migrationBuilder.DropColumn(
                name: "AccountIdentitySnapshot",
                table: "PosPaymentQrRequests");

            migrationBuilder.DropColumn(
                name: "AccountVersionSnapshot",
                table: "PosPaymentQrRequests");

            migrationBuilder.DropColumn(
                name: "StorefrontCarrierWithholdingId",
                table: "OrderPayments");

            migrationBuilder.DropColumn(
                name: "StorefrontCodReceiptId",
                table: "OrderPayments");

            migrationBuilder.DropColumn(
                name: "HasInputInvoiceSnapshot",
                table: "OrderLines");

            migrationBuilder.DropColumn(
                name: "StorefrontOrderId",
                table: "OrderLines");

            migrationBuilder.DropColumn(
                name: "StorefrontOrderLineId",
                table: "OrderLines");

            migrationBuilder.DropColumn(
                name: "StorefrontRoundingAdjustment",
                table: "OrderLines");

            migrationBuilder.DropColumn(
                name: "InvoiceUnitId",
                table: "OrderLegalEntityAllocations");

            migrationBuilder.DropColumn(
                name: "InvoiceUnitKind",
                table: "OrderLegalEntityAllocations");

            migrationBuilder.DropColumn(
                name: "InvoiceUnitName",
                table: "OrderLegalEntityAllocations");

            migrationBuilder.DropColumn(
                name: "StorefrontHeaderDiscountAllocated",
                table: "OrderLegalEntityAllocations");

            migrationBuilder.DropColumn(
                name: "NativeServiceActorUserId",
                table: "InvoiceHeads");

            migrationBuilder.DropColumn(
                name: "NativeServiceCashOnly",
                table: "InvoiceHeads");

            migrationBuilder.DropColumn(
                name: "NativeServiceInvoiceRoute",
                table: "InvoiceHeads");

            migrationBuilder.DropColumn(
                name: "NativeServiceRecognizedAtUtc",
                table: "InvoiceHeads");

            migrationBuilder.DropColumn(
                name: "StorefrontServiceRevenueId",
                table: "InvoiceHeads");

            migrationBuilder.DropColumn(
                name: "StorefrontServiceRevenueId",
                table: "InvoiceDetails");

            migrationBuilder.DropColumn(
                name: "SourceKind",
                table: "DeliveryOrders");

            migrationBuilder.DropColumn(
                name: "StorefrontAllowNegativeInventory",
                table: "DeliveryOrders");

            migrationBuilder.DropColumn(
                name: "StorefrontFulfillmentSourceId",
                table: "DeliveryOrders");

            migrationBuilder.DropColumn(
                name: "StorefrontOrderId",
                table: "DeliveryOrders");

            migrationBuilder.DropColumn(
                name: "StorefrontBaseQuantity",
                table: "DeliveryOrderLines");

            migrationBuilder.DropColumn(
                name: "StorefrontOrderId",
                table: "DeliveryOrderLines");

            migrationBuilder.DropColumn(
                name: "StorefrontOrderLineId",
                table: "DeliveryOrderLines");

            migrationBuilder.DropColumn(
                name: "AccountVersion",
                table: "BankVerifiedReceipts");

            migrationBuilder.DropColumn(
                name: "NativeCollectionAuthorityProofJson",
                table: "BankVerifiedReceipts");

            migrationBuilder.DropColumn(
                name: "RoutingProvenance",
                table: "BankVerifiedReceipts");

            migrationBuilder.AlterColumn<decimal>(
                name: "TotalQuantity",
                table: "InvoiceHeads",
                type: "decimal(18,3)",
                precision: 18,
                scale: 3,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(19,4)",
                oldPrecision: 19,
                oldScale: 4);

            migrationBuilder.AlterColumn<decimal>(
                name: "Quantity",
                table: "InvoiceDetails",
                type: "decimal(18,3)",
                precision: 18,
                scale: 3,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(19,4)",
                oldPrecision: 19,
                oldScale: 4);

            migrationBuilder.AlterColumn<int>(
                name: "SourceCartId",
                table: "DeliveryOrders",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "CreatedTerminalId",
                table: "DeliveryOrders",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "CreatedShiftId",
                table: "DeliveryOrders",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "SourceCartId",
                table: "DeliveryOrderLines",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_DeliveryOrders_StoreId_Id_SourceCartId",
                table: "DeliveryOrders",
                columns: new[] { "StoreId", "Id", "SourceCartId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontSettlementLinks_StoreId_SaleOrderId",
                table: "StorefrontSettlementLinks",
                columns: new[] { "StoreId", "SaleOrderId" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_StorefrontSettlementLinks_GoodsAmount",
                table: "StorefrontSettlementLinks",
                sql: "[GoodsAmount] > 0 AND [GoodsAmount] = ROUND([GoodsAmount], 0)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_InvoiceHeads_LegacyOrder",
                table: "InvoiceHeads",
                sql: "[OrderId] IS NOT NULL OR ([LegacySourceId] IS NOT NULL AND [LegacyReadOnly] = 1) OR [IsAutoInvoiceGroup] = 1");

            migrationBuilder.AddCheckConstraint(
                name: "CK_DeliveryPickingWorks_Approval",
                table: "DeliveryPickingWorks",
                sql: "([ApprovedRevision] IS NULL AND [ApprovedByUserId] IS NULL AND [ApprovedAtUtc] IS NULL AND [ApprovedTotal] IS NULL) OR ([ApprovedRevision] IS NOT NULL AND [ApprovedRevision]>0 AND [ApprovedByUserId] IS NOT NULL AND [ApprovedAtUtc] IS NOT NULL AND [ApprovedTotal] IS NOT NULL AND [ApprovedTotal]>0 AND [ApprovedTotal]=ROUND([ApprovedTotal],0))");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryOrders_StoreId_SourceCartId",
                table: "DeliveryOrders",
                columns: new[] { "StoreId", "SourceCartId" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_DeliveryOrders_Total",
                table: "DeliveryOrders",
                sql: "[QuotedTotal] > 0 AND [QuotedTotal] = ROUND([QuotedTotal], 0)");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryOrderLines_StoreId_DeliveryOrderId_SourceCartId",
                table: "DeliveryOrderLines",
                columns: new[] { "StoreId", "DeliveryOrderId", "SourceCartId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_DeliveryOrderLines_Identity",
                table: "DeliveryOrderLines",
                sql: "([SourceOrderLineId] IS NOT NULL AND [OriginalRootLineId] IS NULL AND [ProductUnitConversionId] IS NULL AND [SellingUnitId] IS NULL AND [BaseUnitId] IS NULL) OR ([SourceOrderLineId] IS NULL AND [OriginalRootLineId] IS NOT NULL AND [SellingUnitId] IS NOT NULL AND [BaseUnitId] IS NOT NULL AND [LineDiscount]=0 AND [AllocatedOrderDiscount]=0)");

            migrationBuilder.AddForeignKey(
                name: "FK_DeliveryOrderLines_DeliveryOrders_StoreId_DeliveryOrderId_SourceCartId",
                table: "DeliveryOrderLines",
                columns: new[] { "StoreId", "DeliveryOrderId", "SourceCartId" },
                principalTable: "DeliveryOrders",
                principalColumns: new[] { "StoreId", "Id", "SourceCartId" },
                onDelete: ReferentialAction.Restrict);
            foreach (var guard in StorefrontFulfillmentSchemaSqlCatalog.Replacements) migrationBuilder.Sql(guard.Previous);
        }
    }
}


