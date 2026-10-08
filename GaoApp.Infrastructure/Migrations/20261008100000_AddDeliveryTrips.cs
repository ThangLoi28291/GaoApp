using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDeliveryTrips : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DeliveryTrips",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    State = table.Column<int>(type: "int", nullable: false),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    CourierUserId = table.Column<int>(type: "int", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    HandedOverByUserId = table.Column<int>(type: "int", nullable: true),
                    HandedOverAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DepartedByUserId = table.Column<int>(type: "int", nullable: true),
                    DepartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AbandonedByUserId = table.Column<int>(type: "int", nullable: true),
                    AbandonedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
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
                    table.PrimaryKey("PK_DeliveryTrips", x => x.Id);
                    table.UniqueConstraint("AK_DeliveryTrips_StoreId_Id", x => new { x.StoreId, x.Id });
                    table.CheckConstraint("CK_DeliveryTrips_Live", "[IsDeleted]=0");
                    table.CheckConstraint("CK_DeliveryTrips_State", "[Revision]>0 AND (([State]=0 AND [HandedOverByUserId] IS NULL AND [HandedOverAtUtc] IS NULL AND [DepartedByUserId] IS NULL AND [DepartedAtUtc] IS NULL AND [AbandonedByUserId] IS NULL AND [AbandonedAtUtc] IS NULL) OR ([State]=1 AND [HandedOverByUserId] IS NOT NULL AND [HandedOverAtUtc] IS NOT NULL AND [DepartedByUserId] IS NULL AND [DepartedAtUtc] IS NULL AND [AbandonedByUserId] IS NULL AND [AbandonedAtUtc] IS NULL) OR ([State]=2 AND [HandedOverByUserId] IS NOT NULL AND [HandedOverAtUtc] IS NOT NULL AND [DepartedByUserId] IS NOT NULL AND [DepartedAtUtc] IS NOT NULL AND [DepartedAtUtc]>=[HandedOverAtUtc] AND [AbandonedByUserId] IS NULL AND [AbandonedAtUtc] IS NULL) OR ([State]=3 AND [HandedOverByUserId] IS NULL AND [HandedOverAtUtc] IS NULL AND [DepartedByUserId] IS NULL AND [DepartedAtUtc] IS NULL AND [AbandonedByUserId] IS NOT NULL AND [AbandonedAtUtc] IS NOT NULL))");
                    table.ForeignKey(
                        name: "FK_DeliveryTrips_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_DeliveryTrips_UserInStores_StoreId_AbandonedByUserId",
                        columns: x => new { x.StoreId, x.AbandonedByUserId },
                        principalTable: "UserInStores",
                        principalColumns: new[] { "StoreId", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeliveryTrips_UserInStores_StoreId_CourierUserId",
                        columns: x => new { x.StoreId, x.CourierUserId },
                        principalTable: "UserInStores",
                        principalColumns: new[] { "StoreId", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeliveryTrips_UserInStores_StoreId_CreatedByUserId",
                        columns: x => new { x.StoreId, x.CreatedByUserId },
                        principalTable: "UserInStores",
                        principalColumns: new[] { "StoreId", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeliveryTrips_UserInStores_StoreId_DepartedByUserId",
                        columns: x => new { x.StoreId, x.DepartedByUserId },
                        principalTable: "UserInStores",
                        principalColumns: new[] { "StoreId", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeliveryTrips_UserInStores_StoreId_HandedOverByUserId",
                        columns: x => new { x.StoreId, x.HandedOverByUserId },
                        principalTable: "UserInStores",
                        principalColumns: new[] { "StoreId", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DeliveryTripCommandReceipts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TripId = table.Column<int>(type: "int", nullable: false),
                    ClientRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorUserId = table.Column<int>(type: "int", nullable: false),
                    Operation = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    RequestHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    OutcomeJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RequiredCapabilities = table.Column<int>(type: "int", nullable: false),
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
                    table.PrimaryKey("PK_DeliveryTripCommandReceipts", x => x.Id);
                    table.CheckConstraint("CK_DeliveryTripCommandReceipts_Live", "[IsDeleted]=0");
                    table.CheckConstraint("CK_DeliveryTripCommandReceipts_OutcomeJson", "ISJSON([OutcomeJson])=1");
                    table.CheckConstraint("CK_DeliveryTripCommandReceipts_Request", "[ClientRequestId]<>'00000000-0000-0000-0000-000000000000' AND [RequiredCapabilities]>0 AND [RequiredCapabilities]<=131071 AND LEN([RequestHash])=64");
                    table.ForeignKey(
                        name: "FK_DeliveryTripCommandReceipts_DeliveryTrips_StoreId_TripId",
                        columns: x => new { x.StoreId, x.TripId },
                        principalTable: "DeliveryTrips",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeliveryTripCommandReceipts_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_DeliveryTripCommandReceipts_UserInStores_StoreId_ActorUserId",
                        columns: x => new { x.StoreId, x.ActorUserId },
                        principalTable: "UserInStores",
                        principalColumns: new[] { "StoreId", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DeliveryTripRevisions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TripId = table.Column<int>(type: "int", nullable: false),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    ActorUserId = table.Column<int>(type: "int", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    AggregateVersion = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
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
                    table.PrimaryKey("PK_DeliveryTripRevisions", x => x.Id);
                    table.UniqueConstraint("AK_DeliveryTripRevisions_StoreId_TripId_Revision", x => new { x.StoreId, x.TripId, x.Revision });
                    table.CheckConstraint("CK_DeliveryTripRevisions_Live", "[IsDeleted]=0");
                    table.CheckConstraint("CK_DeliveryTripRevisions_Revision", "[Revision]>0 AND LEN([AggregateVersion])=12 AND LEN([SnapshotHash])=64");
                    table.CheckConstraint("CK_DeliveryTripRevisions_SnapshotJson", "ISJSON([SnapshotJson])=1");
                    table.ForeignKey(
                        name: "FK_DeliveryTripRevisions_DeliveryTrips_StoreId_TripId",
                        columns: x => new { x.StoreId, x.TripId },
                        principalTable: "DeliveryTrips",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeliveryTripRevisions_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_DeliveryTripRevisions_UserInStores_StoreId_ActorUserId",
                        columns: x => new { x.StoreId, x.ActorUserId },
                        principalTable: "UserInStores",
                        principalColumns: new[] { "StoreId", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DeliveryTripOutboxMessages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TripId = table.Column<int>(type: "int", nullable: false),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    EventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
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
                    table.PrimaryKey("PK_DeliveryTripOutboxMessages", x => x.Id);
                    table.CheckConstraint("CK_DeliveryTripOutboxMessages_Live", "[IsDeleted]=0");
                    table.CheckConstraint("CK_DeliveryTripOutboxMessages_PayloadJson", "ISJSON([PayloadJson])=1");
                    table.ForeignKey(
                        name: "FK_DeliveryTripOutboxMessages_DeliveryTripRevisions_StoreId_TripId_Revision",
                        columns: x => new { x.StoreId, x.TripId, x.Revision },
                        principalTable: "DeliveryTripRevisions",
                        principalColumns: new[] { "StoreId", "TripId", "Revision" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeliveryTripOutboxMessages_DeliveryTrips_StoreId_TripId",
                        columns: x => new { x.StoreId, x.TripId },
                        principalTable: "DeliveryTrips",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeliveryTripOutboxMessages_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "DeliveryCustodyEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TripId = table.Column<int>(type: "int", nullable: false),
                    DeliveryOrderId = table.Column<int>(type: "int", nullable: false),
                    TripOrderId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    PreviousCustodyEventId = table.Column<int>(type: "int", nullable: true),
                    CourierUserId = table.Column<int>(type: "int", nullable: false),
                    PreviousCourierUserId = table.Column<int>(type: "int", nullable: true),
                    ActorUserId = table.Column<int>(type: "int", nullable: false),
                    ConfirmedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ConfirmationNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    SourceWarehouseId = table.Column<int>(type: "int", nullable: false),
                    SourceLegalEntityId = table.Column<int>(type: "int", nullable: false),
                    RootClientRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
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
                    table.PrimaryKey("PK_DeliveryCustodyEvents", x => x.Id);
                    table.UniqueConstraint("AK_DeliveryCustodyEvents_StoreId_Id", x => new { x.StoreId, x.Id });
                    table.UniqueConstraint("AK_DeliveryCustodyEvents_StoreId_Id_SourceWarehouseId_SourceLegalEntityId", x => new { x.StoreId, x.Id, x.SourceWarehouseId, x.SourceLegalEntityId });
                    table.CheckConstraint("CK_DeliveryCustodyEvents_Kind", "[RootClientRequestId]<>'00000000-0000-0000-0000-000000000000' AND LEN(LTRIM(RTRIM([ConfirmationNote])))>0 AND (([Kind]=1 AND [PreviousCustodyEventId] IS NULL AND [PreviousCourierUserId] IS NULL) OR ([Kind]=2 AND [PreviousCustodyEventId] IS NOT NULL AND [PreviousCourierUserId] IS NOT NULL AND [PreviousCourierUserId]<>[CourierUserId] AND [Reason] IS NOT NULL AND LEN(LTRIM(RTRIM([Reason])))>0))");
                    table.CheckConstraint("CK_DeliveryCustodyEvents_Live", "[IsDeleted]=0");
                    table.ForeignKey(
                        name: "FK_DeliveryCustodyEvents_DeliveryCustodyEvents_StoreId_PreviousCustodyEventId",
                        columns: x => new { x.StoreId, x.PreviousCustodyEventId },
                        principalTable: "DeliveryCustodyEvents",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeliveryCustodyEvents_DeliveryTrips_StoreId_TripId",
                        columns: x => new { x.StoreId, x.TripId },
                        principalTable: "DeliveryTrips",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeliveryCustodyEvents_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_DeliveryCustodyEvents_UserInStores_StoreId_ActorUserId",
                        columns: x => new { x.StoreId, x.ActorUserId },
                        principalTable: "UserInStores",
                        principalColumns: new[] { "StoreId", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeliveryCustodyEvents_UserInStores_StoreId_CourierUserId",
                        columns: x => new { x.StoreId, x.CourierUserId },
                        principalTable: "UserInStores",
                        principalColumns: new[] { "StoreId", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeliveryCustodyEvents_UserInStores_StoreId_PreviousCourierUserId",
                        columns: x => new { x.StoreId, x.PreviousCourierUserId },
                        principalTable: "UserInStores",
                        principalColumns: new[] { "StoreId", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DeliveryCustodyLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CustodyEventId = table.Column<int>(type: "int", nullable: false),
                    DeliveryOrderLineId = table.Column<int>(type: "int", nullable: false),
                    DispatchJournalEntryId = table.Column<int>(type: "int", nullable: false),
                    VariantId = table.Column<int>(type: "int", nullable: false),
                    SourceWarehouseId = table.Column<int>(type: "int", nullable: false),
                    SourceLegalEntityId = table.Column<int>(type: "int", nullable: false),
                    ConfirmedBaseQuantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    CumulativeAcceptedReturnAtConfirmation = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
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
                    table.PrimaryKey("PK_DeliveryCustodyLines", x => x.Id);
                    table.CheckConstraint("CK_DeliveryCustodyLines_Live", "[IsDeleted]=0");
                    table.CheckConstraint("CK_DeliveryCustodyLines_Quantity", "[ConfirmedBaseQuantity]>=0 AND [CumulativeAcceptedReturnAtConfirmation]>=0");
                    table.ForeignKey(
                        name: "FK_DeliveryCustodyLines_DeliveryCustodyEvents_StoreId_CustodyEventId_SourceWarehouseId_SourceLegalEntityId",
                        columns: x => new { x.StoreId, x.CustodyEventId, x.SourceWarehouseId, x.SourceLegalEntityId },
                        principalTable: "DeliveryCustodyEvents",
                        principalColumns: new[] { "StoreId", "Id", "SourceWarehouseId", "SourceLegalEntityId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeliveryCustodyLines_DeliveryJournalEntries_DispatchJournalEntryId",
                        column: x => x.DispatchJournalEntryId,
                        principalTable: "DeliveryJournalEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeliveryCustodyLines_DeliveryOrderLines_DeliveryOrderLineId",
                        column: x => x.DeliveryOrderLineId,
                        principalTable: "DeliveryOrderLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeliveryCustodyLines_ProductVariant_StoreId_VariantId",
                        columns: x => new { x.StoreId, x.VariantId },
                        principalTable: "ProductVariant",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeliveryCustodyLines_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "DeliveryTripOrders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TripId = table.Column<int>(type: "int", nullable: false),
                    DeliveryOrderId = table.Column<int>(type: "int", nullable: false),
                    SourceWarehouseId = table.Column<int>(type: "int", nullable: false),
                    SourceLegalEntityId = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Position = table.Column<int>(type: "int", nullable: false),
                    AssignedOrderVersion = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    AssignedApprovedRevision = table.Column<int>(type: "int", nullable: false),
                    CurrentCustodyEventId = table.Column<int>(type: "int", nullable: true),
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
                    table.PrimaryKey("PK_DeliveryTripOrders", x => x.Id);
                    table.UniqueConstraint("AK_DeliveryTripOrders_StoreId_TripId_Id_DeliveryOrderId_SourceWarehouseId_SourceLegalEntityId", x => new { x.StoreId, x.TripId, x.Id, x.DeliveryOrderId, x.SourceWarehouseId, x.SourceLegalEntityId });
                    table.CheckConstraint("CK_DeliveryTripOrders_Assignment", "[Position]>0 AND [AssignedApprovedRevision]>0 AND LEN([AssignedOrderVersion])=12");
                    table.CheckConstraint("CK_DeliveryTripOrders_Live", "[IsDeleted]=0");
                    table.ForeignKey(
                        name: "FK_DeliveryTripOrders_DeliveryCustodyEvents_StoreId_CurrentCustodyEventId",
                        columns: x => new { x.StoreId, x.CurrentCustodyEventId },
                        principalTable: "DeliveryCustodyEvents",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeliveryTripOrders_DeliveryOrders_StoreId_DeliveryOrderId_SourceWarehouseId_SourceLegalEntityId",
                        columns: x => new { x.StoreId, x.DeliveryOrderId, x.SourceWarehouseId, x.SourceLegalEntityId },
                        principalTable: "DeliveryOrders",
                        principalColumns: new[] { "StoreId", "Id", "SourceWarehouseId", "SourceLegalEntityId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeliveryTripOrders_DeliveryTrips_StoreId_TripId",
                        columns: x => new { x.StoreId, x.TripId },
                        principalTable: "DeliveryTrips",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeliveryTripOrders_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryCustodyEvents_StoreId_ActorUserId",
                table: "DeliveryCustodyEvents",
                columns: new[] { "StoreId", "ActorUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryCustodyEvents_StoreId_CourierUserId",
                table: "DeliveryCustodyEvents",
                columns: new[] { "StoreId", "CourierUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryCustodyEvents_StoreId_DeliveryOrderId",
                table: "DeliveryCustodyEvents",
                columns: new[] { "StoreId", "DeliveryOrderId" },
                unique: true,
                filter: "[Kind]=1");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryCustodyEvents_StoreId_EventId",
                table: "DeliveryCustodyEvents",
                columns: new[] { "StoreId", "EventId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryCustodyEvents_StoreId_PreviousCourierUserId",
                table: "DeliveryCustodyEvents",
                columns: new[] { "StoreId", "PreviousCourierUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryCustodyEvents_StoreId_PreviousCustodyEventId",
                table: "DeliveryCustodyEvents",
                columns: new[] { "StoreId", "PreviousCustodyEventId" },
                unique: true,
                filter: "[PreviousCustodyEventId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryCustodyEvents_StoreId_TripId_TripOrderId_DeliveryOrderId_SourceWarehouseId_SourceLegalEntityId",
                table: "DeliveryCustodyEvents",
                columns: new[] { "StoreId", "TripId", "TripOrderId", "DeliveryOrderId", "SourceWarehouseId", "SourceLegalEntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryCustodyLines_DeliveryOrderLineId",
                table: "DeliveryCustodyLines",
                column: "DeliveryOrderLineId");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryCustodyLines_DispatchJournalEntryId",
                table: "DeliveryCustodyLines",
                column: "DispatchJournalEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryCustodyLines_StoreId_CustodyEventId_DeliveryOrderLineId",
                table: "DeliveryCustodyLines",
                columns: new[] { "StoreId", "CustodyEventId", "DeliveryOrderLineId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryCustodyLines_StoreId_CustodyEventId_SourceWarehouseId_SourceLegalEntityId",
                table: "DeliveryCustodyLines",
                columns: new[] { "StoreId", "CustodyEventId", "SourceWarehouseId", "SourceLegalEntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryCustodyLines_StoreId_VariantId",
                table: "DeliveryCustodyLines",
                columns: new[] { "StoreId", "VariantId" });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryTripCommandReceipts_StoreId_ActorUserId",
                table: "DeliveryTripCommandReceipts",
                columns: new[] { "StoreId", "ActorUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryTripCommandReceipts_StoreId_ClientRequestId",
                table: "DeliveryTripCommandReceipts",
                columns: new[] { "StoreId", "ClientRequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryTripCommandReceipts_StoreId_TripId",
                table: "DeliveryTripCommandReceipts",
                columns: new[] { "StoreId", "TripId" });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryTripOrders_StoreId_CurrentCustodyEventId",
                table: "DeliveryTripOrders",
                columns: new[] { "StoreId", "CurrentCustodyEventId" });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryTripOrders_StoreId_DeliveryOrderId",
                table: "DeliveryTripOrders",
                columns: new[] { "StoreId", "DeliveryOrderId" },
                unique: true,
                filter: "[IsActive]=1 AND [IsDeleted]=0");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryTripOrders_StoreId_DeliveryOrderId_SourceWarehouseId_SourceLegalEntityId",
                table: "DeliveryTripOrders",
                columns: new[] { "StoreId", "DeliveryOrderId", "SourceWarehouseId", "SourceLegalEntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryTripOrders_StoreId_TripId_Position",
                table: "DeliveryTripOrders",
                columns: new[] { "StoreId", "TripId", "Position" },
                unique: true,
                filter: "[IsActive]=1 AND [IsDeleted]=0");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryTripOutboxMessages_StoreId_EventId",
                table: "DeliveryTripOutboxMessages",
                columns: new[] { "StoreId", "EventId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryTripOutboxMessages_StoreId_TripId_Revision",
                table: "DeliveryTripOutboxMessages",
                columns: new[] { "StoreId", "TripId", "Revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryTripRevisions_StoreId_ActorUserId",
                table: "DeliveryTripRevisions",
                columns: new[] { "StoreId", "ActorUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryTrips_StoreId_AbandonedByUserId",
                table: "DeliveryTrips",
                columns: new[] { "StoreId", "AbandonedByUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryTrips_StoreId_Code",
                table: "DeliveryTrips",
                columns: new[] { "StoreId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryTrips_StoreId_CourierUserId",
                table: "DeliveryTrips",
                columns: new[] { "StoreId", "CourierUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryTrips_StoreId_CreatedByUserId",
                table: "DeliveryTrips",
                columns: new[] { "StoreId", "CreatedByUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryTrips_StoreId_DepartedByUserId",
                table: "DeliveryTrips",
                columns: new[] { "StoreId", "DepartedByUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryTrips_StoreId_HandedOverByUserId",
                table: "DeliveryTrips",
                columns: new[] { "StoreId", "HandedOverByUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryTrips_StoreId_State_Id",
                table: "DeliveryTrips",
                columns: new[] { "StoreId", "State", "Id" });

            migrationBuilder.AddForeignKey(
                name: "FK_DeliveryCustodyEvents_DeliveryTripOrders_StoreId_TripId_TripOrderId_DeliveryOrderId_SourceWarehouseId_SourceLegalEntityId",
                table: "DeliveryCustodyEvents",
                columns: new[] { "StoreId", "TripId", "TripOrderId", "DeliveryOrderId", "SourceWarehouseId", "SourceLegalEntityId" },
                principalTable: "DeliveryTripOrders",
                principalColumns: new[] { "StoreId", "TripId", "Id", "DeliveryOrderId", "SourceWarehouseId", "SourceLegalEntityId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql("CREATE TRIGGER [TR_DeliveryTrips_Transition] ON [DeliveryTrips] AFTER INSERT, UPDATE, DELETE AS BEGIN\n SET NOCOUNT ON;\n IF EXISTS (SELECT 1 FROM deleted d LEFT JOIN inserted i ON i.Id=d.Id WHERE i.Id IS NULL OR i.StoreId<>d.StoreId OR CONVERT(varbinary(max),i.Code)<>CONVERT(varbinary(max),d.Code) OR i.CreatedByUserId<>d.CreatedByUserId OR i.CreatedAtUtc<>d.CreatedAtUtc OR i.IsDeleted<>0 OR i.Revision<>d.Revision+1 OR d.State=0 AND i.State NOT IN (0,1,3) OR d.State=1 AND i.State NOT IN (1,2) OR d.State IN (2,3) AND i.State<>d.State OR d.State<>0 AND (CONVERT(varbinary(max),ISNULL(i.Note,N''))<>CONVERT(varbinary(max),ISNULL(d.Note,N'')) OR (i.Note IS NULL AND d.Note IS NOT NULL) OR (i.Note IS NOT NULL AND d.Note IS NULL)) OR d.State IN (2,3) AND i.CourierUserId<>d.CourierUserId OR d.HandedOverAtUtc IS NOT NULL AND (i.HandedOverAtUtc IS NULL OR i.HandedOverAtUtc<>d.HandedOverAtUtc OR i.HandedOverByUserId IS NULL OR i.HandedOverByUserId<>d.HandedOverByUserId) OR d.DepartedAtUtc IS NOT NULL AND (i.DepartedAtUtc IS NULL OR i.DepartedAtUtc<>d.DepartedAtUtc OR i.DepartedByUserId IS NULL OR i.DepartedByUserId<>d.DepartedByUserId) OR d.AbandonedAtUtc IS NOT NULL AND (i.AbandonedAtUtc IS NULL OR i.AbandonedAtUtc<>d.AbandonedAtUtc OR i.AbandonedByUserId IS NULL OR i.AbandonedByUserId<>d.AbandonedByUserId))\n THROW 51030, 'Trip identity, transitions and recorded server times cannot be rewritten.', 1;\n IF EXISTS (SELECT 1 FROM inserted i LEFT JOIN deleted d ON d.Id=i.Id WHERE d.Id IS NULL AND (i.State<>0 OR i.Revision<>1))\n THROW 51030, 'New trips begin as draft without custody or departure.', 1;\n IF EXISTS (SELECT 1 FROM inserted i LEFT JOIN deleted d ON d.Id=i.Id LEFT JOIN UserInStores m ON m.StoreId=i.StoreId AND m.UserId=i.CourierUserId LEFT JOIN Users u ON u.Id=i.CourierUserId WHERE (d.Id IS NULL OR i.CourierUserId<>d.CourierUserId OR i.State<>d.State AND i.State IN (1,2)) AND (m.Id IS NULL OR m.IsActive=0 OR m.IsDeleted=1 OR u.Id IS NULL OR u.IsActive=0 OR u.IsDeleted=1))\n THROW 51030, 'New custody or departure requires an active same-store courier.', 1;\n IF EXISTS (SELECT 1 FROM inserted i JOIN deleted d ON d.Id=i.Id WHERE (d.State=0 AND i.State=1 OR d.State=1 AND i.CourierUserId<>d.CourierUserId OR d.State=1 AND i.State=2) AND (NOT EXISTS (SELECT 1 FROM DeliveryTripOrders b WHERE b.StoreId=i.StoreId AND b.TripId=i.Id AND b.IsActive=1) OR EXISTS (SELECT 1 FROM DeliveryTripOrders b LEFT JOIN DeliveryCustodyEvents e ON e.StoreId=b.StoreId AND e.Id=b.CurrentCustodyEventId AND e.TripId=b.TripId AND e.TripOrderId=b.Id AND e.DeliveryOrderId=b.DeliveryOrderId LEFT JOIN DeliveryOrders o ON o.StoreId=b.StoreId AND o.Id=b.DeliveryOrderId WHERE b.StoreId=i.StoreId AND b.TripId=i.Id AND b.IsActive=1 AND (e.Id IS NULL OR e.CourierUserId<>i.CourierUserId OR i.State=1 AND (o.State<>4 OR e.ConfirmedAtUtc>i.HandedOverAtUtc AND d.State=0) OR i.State=2 AND (o.State<>5 OR e.ConfirmedAtUtc>i.DepartedAtUtc) OR NOT EXISTS (SELECT 1 FROM DeliveryCustodyLines l WHERE l.StoreId=e.StoreId AND l.CustodyEventId=e.Id) OR EXISTS (SELECT 1 FROM DeliveryJournalEntries j WHERE j.StoreId=e.StoreId AND j.DeliveryOrderId=e.DeliveryOrderId AND j.Kind=1 AND NOT EXISTS (SELECT 1 FROM DeliveryCustodyLines l WHERE l.StoreId=e.StoreId AND l.CustodyEventId=e.Id AND l.DispatchJournalEntryId=j.Id))))))\n THROW 51030, 'Every active order requires complete current physical custody before handover, transfer or departure.', 1;\n IF EXISTS (SELECT 1 FROM inserted i WHERE i.State=3 AND EXISTS (SELECT 1 FROM DeliveryTripOrders b WHERE b.StoreId=i.StoreId AND b.TripId=i.Id AND (b.IsActive=1 OR b.CurrentCustodyEventId IS NOT NULL)))\n THROW 51030, 'Abandonment releases planned bindings only and cannot discard physical custody.', 1;\nEND;");

            migrationBuilder.Sql("CREATE TRIGGER [TR_DeliveryTripOrders_Source] ON [DeliveryTripOrders] AFTER INSERT, UPDATE, DELETE AS BEGIN\n SET NOCOUNT ON;\n IF EXISTS (SELECT 1 FROM deleted d LEFT JOIN inserted i ON i.Id=d.Id JOIN DeliveryTrips t ON t.StoreId=d.StoreId AND t.Id=d.TripId WHERE i.Id IS NULL OR i.StoreId<>d.StoreId OR i.TripId<>d.TripId OR i.DeliveryOrderId<>d.DeliveryOrderId OR i.SourceWarehouseId<>d.SourceWarehouseId OR i.SourceLegalEntityId<>d.SourceLegalEntityId OR i.IsDeleted<>0 OR d.CurrentCustodyEventId IS NOT NULL AND (i.IsActive<>d.IsActive OR i.Position<>d.Position OR i.AssignedApprovedRevision<>d.AssignedApprovedRevision OR CONVERT(varbinary(max),i.AssignedOrderVersion)<>CONVERT(varbinary(max),d.AssignedOrderVersion) OR i.CurrentCustodyEventId IS NULL) OR (i.IsActive<>d.IsActive OR i.Position<>d.Position OR i.AssignedApprovedRevision<>d.AssignedApprovedRevision OR CONVERT(varbinary(max),i.AssignedOrderVersion)<>CONVERT(varbinary(max),d.AssignedOrderVersion)) AND (t.State<>0 OR d.CurrentCustodyEventId IS NOT NULL))\n THROW 51031, 'Binding origin is immutable; only draft unconfirmed assignments may be refreshed, reordered or removed.', 1;\n IF EXISTS (SELECT 1 FROM inserted i LEFT JOIN deleted d ON d.Id=i.Id JOIN DeliveryTrips t ON t.StoreId=i.StoreId AND t.Id=i.TripId WHERE d.Id IS NULL AND (t.State<>0 OR i.CurrentCustodyEventId IS NOT NULL OR i.IsActive<>1))\n THROW 51031, 'New bindings are planned draft assignments.', 1;\n IF EXISTS (SELECT 1 FROM inserted i LEFT JOIN deleted d ON d.Id=i.Id JOIN DeliveryTrips t ON t.StoreId=i.StoreId AND t.Id=i.TripId LEFT JOIN DeliveryCustodyEvents e ON e.StoreId=i.StoreId AND e.Id=i.CurrentCustodyEventId WHERE i.CurrentCustodyEventId IS NOT NULL AND ISNULL(d.CurrentCustodyEventId,0)<>i.CurrentCustodyEventId AND (i.IsActive<>1 OR e.Id IS NULL OR e.TripId<>i.TripId OR e.TripOrderId<>i.Id OR e.DeliveryOrderId<>i.DeliveryOrderId OR e.SourceWarehouseId<>i.SourceWarehouseId OR e.SourceLegalEntityId<>i.SourceLegalEntityId OR d.CurrentCustodyEventId IS NULL AND (t.State<>0 OR e.Kind<>1 OR e.CourierUserId<>t.CourierUserId) OR d.CurrentCustodyEventId IS NOT NULL AND (t.State<>1 OR e.Kind<>2 OR e.PreviousCustodyEventId IS NULL OR e.PreviousCustodyEventId<>d.CurrentCustodyEventId) OR NOT EXISTS (SELECT 1 FROM DeliveryCustodyLines l WHERE l.StoreId=e.StoreId AND l.CustodyEventId=e.Id) OR EXISTS (SELECT 1 FROM DeliveryJournalEntries j WHERE j.StoreId=e.StoreId AND j.DeliveryOrderId=e.DeliveryOrderId AND j.Kind=1 AND NOT EXISTS (SELECT 1 FROM DeliveryCustodyLines l WHERE l.StoreId=e.StoreId AND l.CustodyEventId=e.Id AND l.DispatchJournalEntryId=j.Id))))\n THROW 51031, 'Current custody must advance through a complete same-source physical receipt.', 1;\nEND;");

            migrationBuilder.Sql("CREATE TRIGGER [TR_DeliveryCustodyEvents_Source] ON [DeliveryCustodyEvents] AFTER INSERT AS BEGIN\n SET NOCOUNT ON;\n IF EXISTS (SELECT 1 FROM inserted i JOIN DeliveryTripOrders b ON b.StoreId=i.StoreId AND b.Id=i.TripOrderId JOIN DeliveryTrips t ON t.StoreId=i.StoreId AND t.Id=i.TripId LEFT JOIN DeliveryCustodyEvents p ON p.StoreId=i.StoreId AND p.Id=i.PreviousCustodyEventId LEFT JOIN UserInStores m ON m.StoreId=i.StoreId AND m.UserId=i.CourierUserId LEFT JOIN Users u ON u.Id=i.CourierUserId WHERE b.IsActive<>1 OR i.ConfirmedAtUtc>SYSUTCDATETIME() OR m.Id IS NULL OR m.IsActive=0 OR m.IsDeleted=1 OR u.Id IS NULL OR u.IsActive=0 OR u.IsDeleted=1 OR i.Kind=1 AND (t.State<>0 OR b.CurrentCustodyEventId IS NOT NULL OR i.CourierUserId<>t.CourierUserId) OR i.Kind=2 AND (t.State<>1 OR p.Id IS NULL OR b.CurrentCustodyEventId<>i.PreviousCustodyEventId OR p.TripId<>i.TripId OR p.TripOrderId<>i.TripOrderId OR p.DeliveryOrderId<>i.DeliveryOrderId OR p.SourceWarehouseId<>i.SourceWarehouseId OR p.SourceLegalEntityId<>i.SourceLegalEntityId OR i.PreviousCourierUserId<>p.CourierUserId OR i.PreviousCourierUserId<>t.CourierUserId OR i.ConfirmedAtUtc<p.ConfirmedAtUtc))\n THROW 51032, 'Physical receipts require the current draft or predeparture custody source and active named courier.', 1;\n IF EXISTS (SELECT 1 FROM inserted i WHERE NOT EXISTS (SELECT 1 FROM DeliveryJournalEntries j WHERE j.StoreId=i.StoreId AND j.DeliveryOrderId=i.DeliveryOrderId AND j.Kind=1) OR EXISTS (SELECT 1 FROM DeliveryJournalEntries j WHERE j.StoreId=i.StoreId AND j.DeliveryOrderId=i.DeliveryOrderId AND j.Kind=1 AND (j.SourceWarehouseId<>i.SourceWarehouseId OR j.SourceLegalEntityId<>i.SourceLegalEntityId OR j.CreatedAtUtc>i.ConfirmedAtUtc)))\n THROW 51032, 'A physical receipt follows genuine dispatch evidence from the order source.', 1;\nEND;");

            migrationBuilder.Sql("CREATE TRIGGER [TR_DeliveryCustodyLines_Source] ON [DeliveryCustodyLines] AFTER INSERT AS BEGIN\n SET NOCOUNT ON;\n IF EXISTS (SELECT 1 FROM inserted i JOIN DeliveryCustodyEvents e ON e.StoreId=i.StoreId AND e.Id=i.CustodyEventId JOIN DeliveryTripOrders b ON b.StoreId=e.StoreId AND b.Id=e.TripOrderId LEFT JOIN DeliveryJournalEntries j ON j.Id=i.DispatchJournalEntryId LEFT JOIN DeliveryOrderLines q ON q.Id=i.DeliveryOrderLineId OUTER APPLY (SELECT SUM(r.BaseQuantity) Quantity FROM DeliveryJournalEntries r WHERE r.StoreId=e.StoreId AND r.DeliveryOrderId=e.DeliveryOrderId AND r.DeliveryOrderLineId=i.DeliveryOrderLineId AND r.Kind=2) received WHERE b.CurrentCustodyEventId=e.Id OR j.Id IS NULL OR j.StoreId<>i.StoreId OR j.Kind<>1 OR j.DeliveryOrderId<>e.DeliveryOrderId OR j.DeliveryOrderLineId IS NULL OR j.DeliveryOrderLineId<>i.DeliveryOrderLineId OR j.VariantId IS NULL OR j.VariantId<>i.VariantId OR j.SourceWarehouseId<>i.SourceWarehouseId OR j.SourceLegalEntityId<>i.SourceLegalEntityId OR q.Id IS NULL OR q.StoreId<>i.StoreId OR q.DeliveryOrderId<>e.DeliveryOrderId OR q.VariantId<>i.VariantId OR i.CumulativeAcceptedReturnAtConfirmation<>ISNULL(received.Quantity,0) OR i.ConfirmedBaseQuantity<>j.BaseQuantity-i.CumulativeAcceptedReturnAtConfirmation OR j.CreatedAtUtc>e.ConfirmedAtUtc OR e.Kind=2 AND NOT EXISTS (SELECT 1 FROM DeliveryCustodyLines p WHERE p.StoreId=i.StoreId AND p.CustodyEventId=e.PreviousCustodyEventId AND p.DeliveryOrderLineId=i.DeliveryOrderLineId AND p.DispatchJournalEntryId=i.DispatchJournalEntryId AND p.VariantId=i.VariantId AND p.SourceWarehouseId=i.SourceWarehouseId AND p.SourceLegalEntityId=i.SourceLegalEntityId AND p.CumulativeAcceptedReturnAtConfirmation<=i.CumulativeAcceptedReturnAtConfirmation AND p.ConfirmedBaseQuantity+p.CumulativeAcceptedReturnAtConfirmation=i.ConfirmedBaseQuantity+i.CumulativeAcceptedReturnAtConfirmation))\n THROW 51033, 'Custody line must preserve its exact dispatch and accepted-return responsibility source.', 1;\n IF EXISTS (SELECT 1 FROM inserted i JOIN DeliveryCustodyEvents e ON e.StoreId=i.StoreId AND e.Id=i.CustodyEventId JOIN DeliveryJournalEntries j ON j.Id=i.DispatchJournalEntryId CROSS APPLY (SELECT CONVERT(uniqueidentifier,SUBSTRING(HASHBYTES('SHA2_256',CONVERT(varchar(200),'GaoApp:DeliveryTrip:v1:'+LOWER(CONVERT(varchar(36),e.RootClientRequestId))+':'+CONVERT(varchar(20),e.DeliveryOrderId)+':inventory-dispatch')),1,16)) ChildId) child WHERE e.Kind=1 AND CONVERT(varbinary(max),j.PostingKey)<>CONVERT(varbinary(max),N'dispatch:'+REPLACE(LOWER(CONVERT(nvarchar(36),child.ChildId)),N'-',N'')+N':'+CONVERT(nvarchar(20),i.DeliveryOrderLineId)))\n THROW 51033, 'Initial custody cannot adopt an unrelated historical dispatch.', 1;\nEND;");

            migrationBuilder.Sql("CREATE TRIGGER [TR_DeliveryTripCommandReceipts_Namespace] ON [DeliveryTripCommandReceipts] AFTER INSERT AS BEGIN SET NOCOUNT ON; IF EXISTS (SELECT 1 FROM inserted i JOIN DeliveryCommandReceipts r WITH (UPDLOCK,HOLDLOCK) ON r.StoreId=i.StoreId AND r.ClientRequestId=i.ClientRequestId) THROW 51034, 'Command GUID is already owned by an order receipt.', 1; END;");

            migrationBuilder.Sql("CREATE TRIGGER [TR_DeliveryCommandReceipts_TripNamespace] ON [DeliveryCommandReceipts] AFTER INSERT AS BEGIN SET NOCOUNT ON; IF EXISTS (SELECT 1 FROM inserted i JOIN DeliveryTripCommandReceipts r WITH (UPDLOCK,HOLDLOCK) ON r.StoreId=i.StoreId AND r.ClientRequestId=i.ClientRequestId) THROW 51034, 'Command GUID is already owned by a trip receipt.', 1; END;");

            migrationBuilder.Sql("CREATE TRIGGER [TR_DeliveryCustodyEvents_Immutable] ON [DeliveryCustodyEvents] INSTEAD OF UPDATE, DELETE AS BEGIN THROW 51035, 'Trip and physical custody history is append only.', 1; END;");

            migrationBuilder.Sql("CREATE TRIGGER [TR_DeliveryCustodyLines_Immutable] ON [DeliveryCustodyLines] INSTEAD OF UPDATE, DELETE AS BEGIN THROW 51035, 'Trip and physical custody history is append only.', 1; END;");

            migrationBuilder.Sql("CREATE TRIGGER [TR_DeliveryTripRevisions_Immutable] ON [DeliveryTripRevisions] INSTEAD OF UPDATE, DELETE AS BEGIN THROW 51035, 'Trip and physical custody history is append only.', 1; END;");

            migrationBuilder.Sql("CREATE TRIGGER [TR_DeliveryTripCommandReceipts_Immutable] ON [DeliveryTripCommandReceipts] INSTEAD OF UPDATE, DELETE AS BEGIN THROW 51035, 'Trip and physical custody history is append only.', 1; END;");

            migrationBuilder.Sql("CREATE TRIGGER [TR_DeliveryTripOutboxMessages_Immutable] ON [DeliveryTripOutboxMessages] INSTEAD OF UPDATE, DELETE AS BEGIN THROW 51035, 'Trip and physical custody history is append only.', 1; END;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM DeliveryTrips) OR EXISTS (SELECT 1 FROM DeliveryTripOrders) OR EXISTS (SELECT 1 FROM DeliveryCustodyEvents) OR EXISTS (SELECT 1 FROM DeliveryCustodyLines) OR EXISTS (SELECT 1 FROM DeliveryTripRevisions) OR EXISTS (SELECT 1 FROM DeliveryTripCommandReceipts) OR EXISTS (SELECT 1 FROM DeliveryTripOutboxMessages) THROW 51039, 'Trip and custody history is occupied; use a forward fix.', 1;");

            migrationBuilder.Sql("DROP TRIGGER [TR_DeliveryTripOutboxMessages_Immutable];");

            migrationBuilder.Sql("DROP TRIGGER [TR_DeliveryTripCommandReceipts_Immutable];");

            migrationBuilder.Sql("DROP TRIGGER [TR_DeliveryTripRevisions_Immutable];");

            migrationBuilder.Sql("DROP TRIGGER [TR_DeliveryCustodyLines_Immutable];");

            migrationBuilder.Sql("DROP TRIGGER [TR_DeliveryCustodyEvents_Immutable];");

            migrationBuilder.Sql("DROP TRIGGER [TR_DeliveryCommandReceipts_TripNamespace];");

            migrationBuilder.Sql("DROP TRIGGER [TR_DeliveryTripCommandReceipts_Namespace];");

            migrationBuilder.Sql("DROP TRIGGER [TR_DeliveryCustodyLines_Source];");

            migrationBuilder.Sql("DROP TRIGGER [TR_DeliveryCustodyEvents_Source];");

            migrationBuilder.Sql("DROP TRIGGER [TR_DeliveryTripOrders_Source];");

            migrationBuilder.Sql("DROP TRIGGER [TR_DeliveryTrips_Transition];");

            migrationBuilder.DropForeignKey(
                name: "FK_DeliveryCustodyEvents_DeliveryTripOrders_StoreId_TripId_TripOrderId_DeliveryOrderId_SourceWarehouseId_SourceLegalEntityId",
                table: "DeliveryCustodyEvents");

            migrationBuilder.DropTable(
                name: "DeliveryCustodyLines");

            migrationBuilder.DropTable(
                name: "DeliveryTripCommandReceipts");

            migrationBuilder.DropTable(
                name: "DeliveryTripOutboxMessages");

            migrationBuilder.DropTable(
                name: "DeliveryTripRevisions");

            migrationBuilder.DropTable(
                name: "DeliveryTripOrders");

            migrationBuilder.DropTable(
                name: "DeliveryCustodyEvents");

            migrationBuilder.DropTable(
                name: "DeliveryTrips");
        }
    }
}
