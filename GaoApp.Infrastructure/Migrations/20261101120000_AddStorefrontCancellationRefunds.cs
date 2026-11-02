using System;
using GaoApp.Infrastructure.Data.Migrations;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStorefrontCancellationRefunds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(StorefrontCancellationSchemaSqlCatalog.PreflightSql);
            migrationBuilder.AlterColumn<int>(
                name: "BankReceiptId",
                table: "StorefrontRefundEvidence",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<string>(
                name: "EvidenceReference",
                table: "StorefrontRefundEvidence",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Fund",
                table: "StorefrontRefundEvidence",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FundingId",
                table: "StorefrontRefundEvidence",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "POSShiftCashTransactionId",
                table: "StorefrontRefundEvidence",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PayerBankAccountId",
                table: "StorefrontRefundEvidence",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PayerLegalEntityId",
                table: "StorefrontRefundEvidence",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PayoutSourceKind",
                table: "StorefrontRefundEvidence",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProtectedPayoutEvidence",
                table: "StorefrontRefundEvidence",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceSnapshotJson",
                table: "StorefrontRefundEvidence",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TreasuryEntryId",
                table: "StorefrontRefundEvidence",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VerificationMethod",
                table: "StorefrontRefundEvidence",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_StorefrontFulfillmentEvidence_StoreId_Id_StorefrontOrderId",
                table: "StorefrontFulfillmentEvidence",
                columns: new[] { "StoreId", "Id", "StorefrontOrderId" });

            migrationBuilder.CreateTable(
                name: "StorefrontBankReviewRestrictions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StorefrontOrderId = table.Column<int>(type: "int", nullable: false),
                    PaymentIntentId = table.Column<int>(type: "int", nullable: false),
                    ReasonCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    EvidenceKind = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    EvidenceFingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ProtectedEvidence = table.Column<string>(type: "nvarchar(max)", nullable: false),
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
                    table.PrimaryKey("PK_StorefrontBankReviewRestrictions", x => x.Id);
                    table.UniqueConstraint("AK_StorefrontBankReviewRestrictions_StoreId_Id", x => new { x.StoreId, x.Id });
                    table.CheckConstraint("CK_StorefrontBankReviewRestrictions_Evidence", "LEN([ReasonCode])>0 AND LEN([EvidenceKind])>0 AND DATALENGTH([EvidenceFingerprint])=128 AND DATALENGTH([ProtectedEvidence])>0");
                    table.CheckConstraint("CK_StorefrontBankReviewRestrictions_Live", "[IsDeleted]=0");
                    table.ForeignKey(
                        name: "FK_StorefrontBankReviewRestrictions_StorefrontOrders_StoreId_StorefrontOrderId",
                        columns: x => new { x.StoreId, x.StorefrontOrderId },
                        principalTable: "StorefrontOrders",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontBankReviewRestrictions_StorefrontPaymentIntents_StoreId_PaymentIntentId",
                        columns: x => new { x.StoreId, x.PaymentIntentId },
                        principalTable: "StorefrontPaymentIntents",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontBankReviewRestrictions_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "StorefrontBankScanAttempts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StorefrontOrderId = table.Column<int>(type: "int", nullable: false),
                    PaymentIntentId = table.Column<int>(type: "int", nullable: false),
                    AccountIdentity = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    BindingFingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RequestedWindowsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
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
                    table.PrimaryKey("PK_StorefrontBankScanAttempts", x => x.Id);
                    table.UniqueConstraint("AK_StorefrontBankScanAttempts_StoreId_Id", x => new { x.StoreId, x.Id });
                    table.UniqueConstraint("AK_StorefrontBankScanAttempts_StoreId_Id_StorefrontOrderId_PaymentIntentId", x => new { x.StoreId, x.Id, x.StorefrontOrderId, x.PaymentIntentId });
                    table.CheckConstraint("CK_StorefrontBankScanAttempts_Live", "[IsDeleted]=0");
                    table.CheckConstraint("CK_StorefrontBankScanAttempts_Scope", "DATALENGTH([AccountIdentity])=128 AND DATALENGTH([BindingFingerprint])=128 AND ISJSON([RequestedWindowsJson])=1 AND JSON_QUERY([RequestedWindowsJson],'$[0]') IS NOT NULL");
                    table.ForeignKey(
                        name: "FK_StorefrontBankScanAttempts_StorefrontOrders_StoreId_StorefrontOrderId",
                        columns: x => new { x.StoreId, x.StorefrontOrderId },
                        principalTable: "StorefrontOrders",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontBankScanAttempts_StorefrontPaymentIntents_StoreId_PaymentIntentId",
                        columns: x => new { x.StoreId, x.PaymentIntentId },
                        principalTable: "StorefrontPaymentIntents",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontBankScanAttempts_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "StorefrontCancellationRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StorefrontOrderId = table.Column<int>(type: "int", nullable: false),
                    ClientRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GuestTokenHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    RequestedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsImmediate = table.Column<bool>(type: "bit", nullable: false),
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
                    table.PrimaryKey("PK_StorefrontCancellationRequests", x => x.Id);
                    table.UniqueConstraint("AK_StorefrontCancellationRequests_StoreId_Id", x => new { x.StoreId, x.Id });
                    table.UniqueConstraint("AK_StorefrontCancellationRequests_StoreId_Id_StorefrontOrderId", x => new { x.StoreId, x.Id, x.StorefrontOrderId });
                    table.CheckConstraint("CK_StorefrontCancellationRequests_Live", "[IsDeleted]=0");
                    table.CheckConstraint("CK_StorefrontCancellationRequests_Request", "[ClientRequestId]<>'00000000-0000-0000-0000-000000000000' AND DATALENGTH([GuestTokenHash])=128 AND LEN([Reason])>0");
                    table.ForeignKey(
                        name: "FK_StorefrontCancellationRequests_StorefrontOrders_StoreId_StorefrontOrderId",
                        columns: x => new { x.StoreId, x.StorefrontOrderId },
                        principalTable: "StorefrontOrders",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontCancellationRequests_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "StorefrontRefundDecisions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StorefrontOrderId = table.Column<int>(type: "int", nullable: false),
                    RefundId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    ActorUserId = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    DecidedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                    table.PrimaryKey("PK_StorefrontRefundDecisions", x => x.Id);
                    table.UniqueConstraint("AK_StorefrontRefundDecisions_StoreId_Id", x => new { x.StoreId, x.Id });
                    table.CheckConstraint("CK_StorefrontRefundDecisions_Decision", "([Kind]=1 OR [Kind]=2) AND LEN([Reason])>0");
                    table.CheckConstraint("CK_StorefrontRefundDecisions_Live", "[IsDeleted]=0");
                    table.ForeignKey(
                        name: "FK_StorefrontRefundDecisions_StorefrontOrders_StoreId_StorefrontOrderId",
                        columns: x => new { x.StoreId, x.StorefrontOrderId },
                        principalTable: "StorefrontOrders",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontRefundDecisions_StorefrontRefunds_StoreId_RefundId_StorefrontOrderId",
                        columns: x => new { x.StoreId, x.RefundId, x.StorefrontOrderId },
                        principalTable: "StorefrontRefunds",
                        principalColumns: new[] { "StoreId", "Id", "StorefrontOrderId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontRefundDecisions_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_StorefrontRefundDecisions_UserInStores_StoreId_ActorUserId",
                        columns: x => new { x.StoreId, x.ActorUserId },
                        principalTable: "UserInStores",
                        principalColumns: new[] { "StoreId", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StorefrontRefundFundings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StorefrontOrderId = table.Column<int>(type: "int", nullable: false),
                    RefundId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    GlobalBankReceiptId = table.Column<int>(type: "int", nullable: true),
                    CodReceiptId = table.Column<int>(type: "int", nullable: true),
                    ReceivingLegalEntityId = table.Column<int>(type: "int", nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
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
                    table.PrimaryKey("PK_StorefrontRefundFundings", x => x.Id);
                    table.UniqueConstraint("AK_StorefrontRefundFundings_StoreId_Id", x => new { x.StoreId, x.Id });
                    table.UniqueConstraint("AK_StorefrontRefundFundings_StoreId_Id_RefundId_StorefrontOrderId", x => new { x.StoreId, x.Id, x.RefundId, x.StorefrontOrderId });
                    table.CheckConstraint("CK_StorefrontRefundFundings_Amount", "[Amount]>0 AND [Amount]<10000000000000000 AND [Amount]=ROUND([Amount],0)");
                    table.CheckConstraint("CK_StorefrontRefundFundings_Live", "[IsDeleted]=0");
                    table.CheckConstraint("CK_StorefrontRefundFundings_Source", "(([Kind]=1 AND [GlobalBankReceiptId] IS NOT NULL AND [CodReceiptId] IS NULL AND [ReceivingLegalEntityId] IS NOT NULL) OR ([Kind]=2 AND [CodReceiptId] IS NOT NULL)) AND ISJSON([SourceSnapshotJson])=1");
                    table.ForeignKey(
                        name: "FK_StorefrontRefundFundings_BankVerifiedReceipts_StoreId_GlobalBankReceiptId",
                        columns: x => new { x.StoreId, x.GlobalBankReceiptId },
                        principalTable: "BankVerifiedReceipts",
                        principalColumns: new[] { "OriginStoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontRefundFundings_LegalEntities_StoreId_ReceivingLegalEntityId",
                        columns: x => new { x.StoreId, x.ReceivingLegalEntityId },
                        principalTable: "LegalEntities",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontRefundFundings_StorefrontCodReceipts_StoreId_CodReceiptId",
                        columns: x => new { x.StoreId, x.CodReceiptId },
                        principalTable: "StorefrontCodReceipts",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontRefundFundings_StorefrontOrders_StoreId_StorefrontOrderId",
                        columns: x => new { x.StoreId, x.StorefrontOrderId },
                        principalTable: "StorefrontOrders",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontRefundFundings_StorefrontRefunds_StoreId_RefundId_StorefrontOrderId",
                        columns: x => new { x.StoreId, x.RefundId, x.StorefrontOrderId },
                        principalTable: "StorefrontRefunds",
                        principalColumns: new[] { "StoreId", "Id", "StorefrontOrderId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontRefundFundings_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "StorefrontBankScanCompletions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StorefrontOrderId = table.Column<int>(type: "int", nullable: false),
                    PaymentIntentId = table.Column<int>(type: "int", nullable: false),
                    ScanAttemptId = table.Column<int>(type: "int", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Fingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ProtectedEvidence = table.Column<string>(type: "nvarchar(max)", nullable: false),
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
                    table.PrimaryKey("PK_StorefrontBankScanCompletions", x => x.Id);
                    table.UniqueConstraint("AK_StorefrontBankScanCompletions_StoreId_Id", x => new { x.StoreId, x.Id });
                    table.CheckConstraint("CK_StorefrontBankScanCompletions_Evidence", "DATALENGTH([Fingerprint])=128 AND DATALENGTH([ProtectedEvidence])>0");
                    table.CheckConstraint("CK_StorefrontBankScanCompletions_Live", "[IsDeleted]=0");
                    table.ForeignKey(
                        name: "FK_StorefrontBankScanCompletions_StorefrontBankScanAttempts_StoreId_ScanAttemptId_StorefrontOrderId_PaymentIntentId",
                        columns: x => new { x.StoreId, x.ScanAttemptId, x.StorefrontOrderId, x.PaymentIntentId },
                        principalTable: "StorefrontBankScanAttempts",
                        principalColumns: new[] { "StoreId", "Id", "StorefrontOrderId", "PaymentIntentId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontBankScanCompletions_StorefrontOrders_StoreId_StorefrontOrderId",
                        columns: x => new { x.StoreId, x.StorefrontOrderId },
                        principalTable: "StorefrontOrders",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontBankScanCompletions_StorefrontPaymentIntents_StoreId_PaymentIntentId",
                        columns: x => new { x.StoreId, x.PaymentIntentId },
                        principalTable: "StorefrontPaymentIntents",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontBankScanCompletions_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "StorefrontCancellationDecisions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StorefrontOrderId = table.Column<int>(type: "int", nullable: false),
                    CancellationRequestId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    ActorUserId = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    DecidedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AuthorisesPhysicalReturn = table.Column<bool>(type: "bit", nullable: false),
                    ActualDeliveryEvidenceId = table.Column<int>(type: "int", nullable: true),
                    ActualReturnEvidenceId = table.Column<int>(type: "int", nullable: true),
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
                    table.PrimaryKey("PK_StorefrontCancellationDecisions", x => x.Id);
                    table.UniqueConstraint("AK_StorefrontCancellationDecisions_StoreId_Id", x => new { x.StoreId, x.Id });
                    table.UniqueConstraint("AK_StorefrontCancellationDecisions_StoreId_Id_StorefrontOrderId", x => new { x.StoreId, x.Id, x.StorefrontOrderId });
                    table.CheckConstraint("CK_StorefrontCancellationDecisions_Decision", "LEN([Reason])>0 AND (([Kind]=1 AND [ActualDeliveryEvidenceId] IS NULL AND [ActualReturnEvidenceId] IS NULL) OR ([Kind]=2 AND [AuthorisesPhysicalReturn]=0 AND [ActualDeliveryEvidenceId] IS NULL AND [ActualReturnEvidenceId] IS NULL) OR ([Kind]=3 AND [AuthorisesPhysicalReturn]=0 AND [ActualDeliveryEvidenceId] IS NOT NULL AND [ActualReturnEvidenceId] IS NULL) OR ([Kind]=4 AND [AuthorisesPhysicalReturn]=0 AND [ActualDeliveryEvidenceId] IS NULL AND [ActualReturnEvidenceId] IS NOT NULL))");
                    table.CheckConstraint("CK_StorefrontCancellationDecisions_Live", "[IsDeleted]=0");
                    table.ForeignKey(
                        name: "FK_StorefrontCancellationDecisions_StorefrontCancellationRequests_StoreId_CancellationRequestId_StorefrontOrderId",
                        columns: x => new { x.StoreId, x.CancellationRequestId, x.StorefrontOrderId },
                        principalTable: "StorefrontCancellationRequests",
                        principalColumns: new[] { "StoreId", "Id", "StorefrontOrderId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontCancellationDecisions_StorefrontFulfillmentEvidence_StoreId_ActualDeliveryEvidenceId_StorefrontOrderId",
                        columns: x => new { x.StoreId, x.ActualDeliveryEvidenceId, x.StorefrontOrderId },
                        principalTable: "StorefrontFulfillmentEvidence",
                        principalColumns: new[] { "StoreId", "Id", "StorefrontOrderId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontCancellationDecisions_StorefrontFulfillmentEvidence_StoreId_ActualReturnEvidenceId_StorefrontOrderId",
                        columns: x => new { x.StoreId, x.ActualReturnEvidenceId, x.StorefrontOrderId },
                        principalTable: "StorefrontFulfillmentEvidence",
                        principalColumns: new[] { "StoreId", "Id", "StorefrontOrderId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontCancellationDecisions_StorefrontOrders_StoreId_StorefrontOrderId",
                        columns: x => new { x.StoreId, x.StorefrontOrderId },
                        principalTable: "StorefrontOrders",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontCancellationDecisions_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_StorefrontCancellationDecisions_UserInStores_StoreId_ActorUserId",
                        columns: x => new { x.StoreId, x.ActorUserId },
                        principalTable: "UserInStores",
                        principalColumns: new[] { "StoreId", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StorefrontQrClosureProofs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StorefrontOrderId = table.Column<int>(type: "int", nullable: false),
                    PaymentIntentId = table.Column<int>(type: "int", nullable: false),
                    ScanCompletionId = table.Column<int>(type: "int", nullable: false),
                    CancellationObservationId = table.Column<int>(type: "int", nullable: false),
                    ProviderReference = table.Column<string>(type: "nvarchar(13)", maxLength: 13, nullable: false),
                    AccountIdentity = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Fingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ProtectedEvidence = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    VerifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                    table.PrimaryKey("PK_StorefrontQrClosureProofs", x => x.Id);
                    table.UniqueConstraint("AK_StorefrontQrClosureProofs_StoreId_Id", x => new { x.StoreId, x.Id });
                    table.CheckConstraint("CK_StorefrontQrClosureProofs_Live", "[IsDeleted]=0");
                    table.CheckConstraint("CK_StorefrontQrClosureProofs_Proof", "DATALENGTH([ProviderReference])=26 AND DATALENGTH([AccountIdentity])=128 AND DATALENGTH([Fingerprint])=128 AND DATALENGTH([ProtectedEvidence])>0");
                    table.ForeignKey(
                        name: "FK_StorefrontQrClosureProofs_StorefrontBankObservations_StoreId_CancellationObservationId",
                        columns: x => new { x.StoreId, x.CancellationObservationId },
                        principalTable: "StorefrontBankObservations",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontQrClosureProofs_StorefrontBankScanCompletions_StoreId_ScanCompletionId",
                        columns: x => new { x.StoreId, x.ScanCompletionId },
                        principalTable: "StorefrontBankScanCompletions",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontQrClosureProofs_StorefrontOrders_StoreId_StorefrontOrderId",
                        columns: x => new { x.StoreId, x.StorefrontOrderId },
                        principalTable: "StorefrontOrders",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontQrClosureProofs_StorefrontPaymentIntents_StoreId_PaymentIntentId",
                        columns: x => new { x.StoreId, x.PaymentIntentId },
                        principalTable: "StorefrontPaymentIntents",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontQrClosureProofs_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "StorefrontCancellationReviewAcknowledgements",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StorefrontOrderId = table.Column<int>(type: "int", nullable: false),
                    CancellationDecisionId = table.Column<int>(type: "int", nullable: false),
                    ActorUserId = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    AcknowledgedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                    table.PrimaryKey("PK_StorefrontCancellationReviewAcknowledgements", x => x.Id);
                    table.UniqueConstraint("AK_StorefrontCancellationReviewAcknowledgements_StoreId_Id", x => new { x.StoreId, x.Id });
                    table.CheckConstraint("CK_StorefrontCancellationReviewAcknowledgements_Live", "[IsDeleted]=0");
                    table.CheckConstraint("CK_StorefrontCancellationReviewAcknowledgements_Reason", "LEN([Reason])>0");
                    table.ForeignKey(
                        name: "FK_StorefrontCancellationReviewAcknowledgements_StorefrontCancellationDecisions_StoreId_CancellationDecisionId_StorefrontOrderId",
                        columns: x => new { x.StoreId, x.CancellationDecisionId, x.StorefrontOrderId },
                        principalTable: "StorefrontCancellationDecisions",
                        principalColumns: new[] { "StoreId", "Id", "StorefrontOrderId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontCancellationReviewAcknowledgements_StorefrontOrders_StoreId_StorefrontOrderId",
                        columns: x => new { x.StoreId, x.StorefrontOrderId },
                        principalTable: "StorefrontOrders",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontCancellationReviewAcknowledgements_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_StorefrontCancellationReviewAcknowledgements_UserInStores_StoreId_ActorUserId",
                        columns: x => new { x.StoreId, x.ActorUserId },
                        principalTable: "UserInStores",
                        principalColumns: new[] { "StoreId", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontRefundEvidence_POSShiftCashTransactionId",
                table: "StorefrontRefundEvidence",
                column: "POSShiftCashTransactionId",
                unique: true,
                filter: "[POSShiftCashTransactionId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontRefundEvidence_StoreId_FundingId_RefundId_StorefrontOrderId",
                table: "StorefrontRefundEvidence",
                columns: new[] { "StoreId", "FundingId", "RefundId", "StorefrontOrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontRefundEvidence_StoreId_PayerBankAccountId",
                table: "StorefrontRefundEvidence",
                columns: new[] { "StoreId", "PayerBankAccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontRefundEvidence_StoreId_PayerLegalEntityId",
                table: "StorefrontRefundEvidence",
                columns: new[] { "StoreId", "PayerLegalEntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontRefundEvidence_StoreId_POSShiftCashTransactionId",
                table: "StorefrontRefundEvidence",
                columns: new[] { "StoreId", "POSShiftCashTransactionId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontRefundEvidence_StoreId_TreasuryEntryId",
                table: "StorefrontRefundEvidence",
                columns: new[] { "StoreId", "TreasuryEntryId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontRefundEvidence_TreasuryEntryId",
                table: "StorefrontRefundEvidence",
                column: "TreasuryEntryId",
                unique: true,
                filter: "[TreasuryEntryId] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_StorefrontRefundEvidence_NativePayout",
                table: "StorefrontRefundEvidence",
                sql: "([FundingId] IS NULL AND [PayoutSourceKind] IS NULL AND [TreasuryEntryId] IS NULL AND [POSShiftCashTransactionId] IS NULL AND [PayerBankAccountId] IS NULL AND [PayerLegalEntityId] IS NULL AND [Fund] IS NULL AND [VerificationMethod] IS NULL AND [EvidenceReference] IS NULL AND [ProtectedPayoutEvidence] IS NULL AND [SourceSnapshotJson] IS NULL) OR ([FundingId] IS NOT NULL AND [PayerLegalEntityId] IS NOT NULL AND [Fund] IS NOT NULL AND ([Fund]=N'cash' OR [Fund]=N'bank') AND [VerificationMethod] IS NOT NULL AND [EvidenceReference] IS NOT NULL AND LEN([EvidenceReference])>0 AND [ProtectedPayoutEvidence] IS NOT NULL AND DATALENGTH([ProtectedPayoutEvidence])>0 AND [SourceSnapshotJson] IS NOT NULL AND ISJSON([SourceSnapshotJson])=1 AND LEFT(LTRIM(REPLACE(REPLACE(REPLACE([SourceSnapshotJson],NCHAR(9),N''),NCHAR(10),N''),NCHAR(13),N'')),1)=N'{' AND (([PayoutSourceKind]=1 AND [POSShiftCashTransactionId] IS NOT NULL) OR ([PayoutSourceKind]=2 AND [TreasuryEntryId] IS NOT NULL)))");

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontBankReviewRestrictions_StoreId_PaymentIntentId_ReasonCode_EvidenceFingerprint",
                table: "StorefrontBankReviewRestrictions",
                columns: new[] { "StoreId", "PaymentIntentId", "ReasonCode", "EvidenceFingerprint" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontBankReviewRestrictions_StoreId_StorefrontOrderId",
                table: "StorefrontBankReviewRestrictions",
                columns: new[] { "StoreId", "StorefrontOrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontBankScanAttempts_StoreId_PaymentIntentId_Id",
                table: "StorefrontBankScanAttempts",
                columns: new[] { "StoreId", "PaymentIntentId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontBankScanAttempts_StoreId_StorefrontOrderId",
                table: "StorefrontBankScanAttempts",
                columns: new[] { "StoreId", "StorefrontOrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontBankScanCompletions_StoreId_PaymentIntentId_Id",
                table: "StorefrontBankScanCompletions",
                columns: new[] { "StoreId", "PaymentIntentId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontBankScanCompletions_StoreId_ScanAttemptId",
                table: "StorefrontBankScanCompletions",
                columns: new[] { "StoreId", "ScanAttemptId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontBankScanCompletions_StoreId_ScanAttemptId_StorefrontOrderId_PaymentIntentId",
                table: "StorefrontBankScanCompletions",
                columns: new[] { "StoreId", "ScanAttemptId", "StorefrontOrderId", "PaymentIntentId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontBankScanCompletions_StoreId_StorefrontOrderId",
                table: "StorefrontBankScanCompletions",
                columns: new[] { "StoreId", "StorefrontOrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontCancellationDecisions_StoreId_ActorUserId",
                table: "StorefrontCancellationDecisions",
                columns: new[] { "StoreId", "ActorUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontCancellationDecisions_StoreId_ActualDeliveryEvidenceId_StorefrontOrderId",
                table: "StorefrontCancellationDecisions",
                columns: new[] { "StoreId", "ActualDeliveryEvidenceId", "StorefrontOrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontCancellationDecisions_StoreId_ActualReturnEvidenceId_StorefrontOrderId",
                table: "StorefrontCancellationDecisions",
                columns: new[] { "StoreId", "ActualReturnEvidenceId", "StorefrontOrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontCancellationDecisions_StoreId_CancellationRequestId",
                table: "StorefrontCancellationDecisions",
                columns: new[] { "StoreId", "CancellationRequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontCancellationDecisions_StoreId_CancellationRequestId_StorefrontOrderId",
                table: "StorefrontCancellationDecisions",
                columns: new[] { "StoreId", "CancellationRequestId", "StorefrontOrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontCancellationDecisions_StoreId_StorefrontOrderId",
                table: "StorefrontCancellationDecisions",
                columns: new[] { "StoreId", "StorefrontOrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontCancellationRequests_StoreId_ClientRequestId",
                table: "StorefrontCancellationRequests",
                columns: new[] { "StoreId", "ClientRequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontCancellationRequests_StoreId_StorefrontOrderId_IsImmediate",
                table: "StorefrontCancellationRequests",
                columns: new[] { "StoreId", "StorefrontOrderId", "IsImmediate" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontCancellationReviewAcknowledgements_StoreId_ActorUserId",
                table: "StorefrontCancellationReviewAcknowledgements",
                columns: new[] { "StoreId", "ActorUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontCancellationReviewAcknowledgements_StoreId_CancellationDecisionId",
                table: "StorefrontCancellationReviewAcknowledgements",
                columns: new[] { "StoreId", "CancellationDecisionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontCancellationReviewAcknowledgements_StoreId_CancellationDecisionId_StorefrontOrderId",
                table: "StorefrontCancellationReviewAcknowledgements",
                columns: new[] { "StoreId", "CancellationDecisionId", "StorefrontOrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontCancellationReviewAcknowledgements_StoreId_StorefrontOrderId",
                table: "StorefrontCancellationReviewAcknowledgements",
                columns: new[] { "StoreId", "StorefrontOrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontQrClosureProofs_StoreId_CancellationObservationId",
                table: "StorefrontQrClosureProofs",
                columns: new[] { "StoreId", "CancellationObservationId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontQrClosureProofs_StoreId_PaymentIntentId_Fingerprint",
                table: "StorefrontQrClosureProofs",
                columns: new[] { "StoreId", "PaymentIntentId", "Fingerprint" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontQrClosureProofs_StoreId_ScanCompletionId",
                table: "StorefrontQrClosureProofs",
                columns: new[] { "StoreId", "ScanCompletionId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontQrClosureProofs_StoreId_StorefrontOrderId",
                table: "StorefrontQrClosureProofs",
                columns: new[] { "StoreId", "StorefrontOrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontRefundDecisions_StoreId_ActorUserId",
                table: "StorefrontRefundDecisions",
                columns: new[] { "StoreId", "ActorUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontRefundDecisions_StoreId_RefundId_Id",
                table: "StorefrontRefundDecisions",
                columns: new[] { "StoreId", "RefundId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontRefundDecisions_StoreId_RefundId_StorefrontOrderId",
                table: "StorefrontRefundDecisions",
                columns: new[] { "StoreId", "RefundId", "StorefrontOrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontRefundDecisions_StoreId_StorefrontOrderId",
                table: "StorefrontRefundDecisions",
                columns: new[] { "StoreId", "StorefrontOrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontRefundFundings_CodReceiptId",
                table: "StorefrontRefundFundings",
                column: "CodReceiptId",
                unique: true,
                filter: "[CodReceiptId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontRefundFundings_GlobalBankReceiptId",
                table: "StorefrontRefundFundings",
                column: "GlobalBankReceiptId",
                unique: true,
                filter: "[GlobalBankReceiptId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontRefundFundings_StoreId_CodReceiptId",
                table: "StorefrontRefundFundings",
                columns: new[] { "StoreId", "CodReceiptId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontRefundFundings_StoreId_GlobalBankReceiptId",
                table: "StorefrontRefundFundings",
                columns: new[] { "StoreId", "GlobalBankReceiptId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontRefundFundings_StoreId_ReceivingLegalEntityId",
                table: "StorefrontRefundFundings",
                columns: new[] { "StoreId", "ReceivingLegalEntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontRefundFundings_StoreId_RefundId",
                table: "StorefrontRefundFundings",
                columns: new[] { "StoreId", "RefundId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontRefundFundings_StoreId_RefundId_StorefrontOrderId",
                table: "StorefrontRefundFundings",
                columns: new[] { "StoreId", "RefundId", "StorefrontOrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontRefundFundings_StoreId_StorefrontOrderId",
                table: "StorefrontRefundFundings",
                columns: new[] { "StoreId", "StorefrontOrderId" });

            migrationBuilder.AddForeignKey(
                name: "FK_StorefrontRefundEvidence_LegalEntities_StoreId_PayerLegalEntityId",
                table: "StorefrontRefundEvidence",
                columns: new[] { "StoreId", "PayerLegalEntityId" },
                principalTable: "LegalEntities",
                principalColumns: new[] { "StoreId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_StorefrontRefundEvidence_POSShiftCashTransactions_StoreId_POSShiftCashTransactionId",
                table: "StorefrontRefundEvidence",
                columns: new[] { "StoreId", "POSShiftCashTransactionId" },
                principalTable: "POSShiftCashTransactions",
                principalColumns: new[] { "StoreId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_StorefrontRefundEvidence_StoreBankAccounts_StoreId_PayerBankAccountId",
                table: "StorefrontRefundEvidence",
                columns: new[] { "StoreId", "PayerBankAccountId" },
                principalTable: "StoreBankAccounts",
                principalColumns: new[] { "StoreId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_StorefrontRefundEvidence_StorefrontRefundFundings_StoreId_FundingId_RefundId_StorefrontOrderId",
                table: "StorefrontRefundEvidence",
                columns: new[] { "StoreId", "FundingId", "RefundId", "StorefrontOrderId" },
                principalTable: "StorefrontRefundFundings",
                principalColumns: new[] { "StoreId", "Id", "RefundId", "StorefrontOrderId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_StorefrontRefundEvidence_TreasuryEntries_StoreId_TreasuryEntryId",
                table: "StorefrontRefundEvidence",
                columns: new[] { "StoreId", "TreasuryEntryId" },
                principalTable: "TreasuryEntries",
                principalColumns: new[] { "StoreId", "Id" },
                onDelete: ReferentialAction.Restrict);
            foreach (var guard in StorefrontCancellationSchemaSqlCatalog.Triggers) migrationBuilder.Sql(guard.Sql);
            migrationBuilder.Sql(StorefrontCancellationSchemaSqlCatalog.PermissionSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(StorefrontCancellationSchemaSqlCatalog.DownGuardSql);
            foreach (var guard in StorefrontCancellationSchemaSqlCatalog.Triggers) migrationBuilder.Sql($"DROP TRIGGER [dbo].[{guard.Name}];");
            migrationBuilder.DropForeignKey(
                name: "FK_StorefrontRefundEvidence_LegalEntities_StoreId_PayerLegalEntityId",
                table: "StorefrontRefundEvidence");

            migrationBuilder.DropForeignKey(
                name: "FK_StorefrontRefundEvidence_POSShiftCashTransactions_StoreId_POSShiftCashTransactionId",
                table: "StorefrontRefundEvidence");

            migrationBuilder.DropForeignKey(
                name: "FK_StorefrontRefundEvidence_StoreBankAccounts_StoreId_PayerBankAccountId",
                table: "StorefrontRefundEvidence");

            migrationBuilder.DropForeignKey(
                name: "FK_StorefrontRefundEvidence_StorefrontRefundFundings_StoreId_FundingId_RefundId_StorefrontOrderId",
                table: "StorefrontRefundEvidence");

            migrationBuilder.DropForeignKey(
                name: "FK_StorefrontRefundEvidence_TreasuryEntries_StoreId_TreasuryEntryId",
                table: "StorefrontRefundEvidence");

            migrationBuilder.DropTable(
                name: "StorefrontBankReviewRestrictions");

            migrationBuilder.DropTable(
                name: "StorefrontCancellationReviewAcknowledgements");

            migrationBuilder.DropTable(
                name: "StorefrontQrClosureProofs");

            migrationBuilder.DropTable(
                name: "StorefrontRefundDecisions");

            migrationBuilder.DropTable(
                name: "StorefrontRefundFundings");

            migrationBuilder.DropTable(
                name: "StorefrontCancellationDecisions");

            migrationBuilder.DropTable(
                name: "StorefrontBankScanCompletions");

            migrationBuilder.DropTable(
                name: "StorefrontCancellationRequests");

            migrationBuilder.DropTable(
                name: "StorefrontBankScanAttempts");

            migrationBuilder.DropIndex(
                name: "IX_StorefrontRefundEvidence_POSShiftCashTransactionId",
                table: "StorefrontRefundEvidence");

            migrationBuilder.DropIndex(
                name: "IX_StorefrontRefundEvidence_StoreId_FundingId_RefundId_StorefrontOrderId",
                table: "StorefrontRefundEvidence");

            migrationBuilder.DropIndex(
                name: "IX_StorefrontRefundEvidence_StoreId_PayerBankAccountId",
                table: "StorefrontRefundEvidence");

            migrationBuilder.DropIndex(
                name: "IX_StorefrontRefundEvidence_StoreId_PayerLegalEntityId",
                table: "StorefrontRefundEvidence");

            migrationBuilder.DropIndex(
                name: "IX_StorefrontRefundEvidence_StoreId_POSShiftCashTransactionId",
                table: "StorefrontRefundEvidence");

            migrationBuilder.DropIndex(
                name: "IX_StorefrontRefundEvidence_StoreId_TreasuryEntryId",
                table: "StorefrontRefundEvidence");

            migrationBuilder.DropIndex(
                name: "IX_StorefrontRefundEvidence_TreasuryEntryId",
                table: "StorefrontRefundEvidence");

            migrationBuilder.DropCheckConstraint(
                name: "CK_StorefrontRefundEvidence_NativePayout",
                table: "StorefrontRefundEvidence");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_StorefrontFulfillmentEvidence_StoreId_Id_StorefrontOrderId",
                table: "StorefrontFulfillmentEvidence");

            migrationBuilder.DropColumn(
                name: "EvidenceReference",
                table: "StorefrontRefundEvidence");

            migrationBuilder.DropColumn(
                name: "Fund",
                table: "StorefrontRefundEvidence");

            migrationBuilder.DropColumn(
                name: "FundingId",
                table: "StorefrontRefundEvidence");

            migrationBuilder.DropColumn(
                name: "POSShiftCashTransactionId",
                table: "StorefrontRefundEvidence");

            migrationBuilder.DropColumn(
                name: "PayerBankAccountId",
                table: "StorefrontRefundEvidence");

            migrationBuilder.DropColumn(
                name: "PayerLegalEntityId",
                table: "StorefrontRefundEvidence");

            migrationBuilder.DropColumn(
                name: "PayoutSourceKind",
                table: "StorefrontRefundEvidence");

            migrationBuilder.DropColumn(
                name: "ProtectedPayoutEvidence",
                table: "StorefrontRefundEvidence");

            migrationBuilder.DropColumn(
                name: "SourceSnapshotJson",
                table: "StorefrontRefundEvidence");

            migrationBuilder.DropColumn(
                name: "TreasuryEntryId",
                table: "StorefrontRefundEvidence");

            migrationBuilder.DropColumn(
                name: "VerificationMethod",
                table: "StorefrontRefundEvidence");

            // SQL Server requires removing the original child lookup index before
            // restoring NOT NULL. Restore its exact nonunique, unfiltered key shape.
            migrationBuilder.DropIndex(
                name: "IX_StorefrontRefundEvidence_StoreId_BankReceiptId",
                table: "StorefrontRefundEvidence");

            migrationBuilder.AlterColumn<int>(
                name: "BankReceiptId",
                table: "StorefrontRefundEvidence",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontRefundEvidence_StoreId_BankReceiptId",
                table: "StorefrontRefundEvidence",
                columns: new[] { "StoreId", "BankReceiptId" });
        }
    }
}

