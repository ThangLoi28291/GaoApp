using System;
using Microsoft.EntityFrameworkCore.Migrations;
using GaoApp.Infrastructure.Data.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStorefrontQrBankEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(StorefrontQrBankSchemaSqlCatalog.PreflightSql);
            migrationBuilder.DropIndex(
                name: "IX_AcbPaymentTransactions_StoreId",
                table: "AcbPaymentTransactions");

            migrationBuilder.CreateSequence(
                name: "StorefrontBankCorrelationSequence",
                minValue: 1L,
                maxValue: 999999999999L);

            migrationBuilder.AddColumn<int>(
                name: "GlobalBankReceiptId",
                table: "StorefrontBankReceipts",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BankVerifiedReceiptId",
                table: "OrderPayments",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BankBindingSnapshotJson",
                table: "AcbQrSessions",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BankVerifiedReceiptId",
                table: "AcbPaymentTransactions",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BankPaymentReferences",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AccountIdentity = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    NotificationIdentityMappingVerified = table.Column<bool>(type: "bit", nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Channel = table.Column<int>(type: "int", nullable: false),
                    OriginStoreId = table.Column<int>(type: "int", nullable: false),
                    PosQrRequestId = table.Column<int>(type: "int", nullable: true),
                    StorefrontPaymentIntentId = table.Column<int>(type: "int", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    CreatedBy = table.Column<int>(type: "int", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<int>(type: "int", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedBy = table.Column<int>(type: "int", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankPaymentReferences", x => x.Id);
                    table.CheckConstraint("CK_BankPaymentReferences_Channel", "([Channel]=1 AND [PosQrRequestId] IS NOT NULL AND [StorefrontPaymentIntentId] IS NULL) OR ([Channel]=2 AND [PosQrRequestId] IS NULL AND [StorefrontPaymentIntentId] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_BankPaymentReferences_PosPaymentQrRequests_PosQrRequestId",
                        column: x => x.PosQrRequestId,
                        principalTable: "PosPaymentQrRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BankPaymentReferences_StorefrontPaymentIntents_StorefrontPaymentIntentId",
                        column: x => x.StorefrontPaymentIntentId,
                        principalTable: "StorefrontPaymentIntents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BankPaymentReferences_Stores_OriginStoreId",
                        column: x => x.OriginStoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "BankVerifiedReceipts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AssetConfidence = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    IdentityKind = table.Column<int>(type: "int", nullable: false),
                    CrossProtocolMappingVerified = table.Column<bool>(type: "bit", nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    AccountIdentity = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ExternalTransactionId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false, collation: "Latin1_General_100_BIN2"),
                    OriginStoreId = table.Column<int>(type: "int", nullable: false),
                    BankAccountId = table.Column<int>(type: "int", nullable: false),
                    ProviderReference = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Channel = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Fingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ProtectedEvidence = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RawBankTime = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TimePrecision = table.Column<int>(type: "int", nullable: false),
                    BankBusinessDate = table.Column<DateTime>(type: "date", nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    VerifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    NeedsReview = table.Column<bool>(type: "bit", nullable: false),
                    ReviewCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    CreatedBy = table.Column<int>(type: "int", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<int>(type: "int", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedBy = table.Column<int>(type: "int", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankVerifiedReceipts", x => x.Id);
                    table.UniqueConstraint("AK_BankVerifiedReceipts_OriginStoreId_Id", x => new { x.OriginStoreId, x.Id });
                    table.CheckConstraint("CK_BankVerifiedReceipts_Amount", "[Amount]>=0 AND [Amount]=ROUND([Amount],0)");
                    table.CheckConstraint("CK_BankVerifiedReceipts_Time", "([TimePrecision]=0 AND [OccurredAtUtc] IS NULL AND [BankBusinessDate] IS NULL) OR ([TimePrecision] IN (1,2) AND [OccurredAtUtc] IS NOT NULL AND [BankBusinessDate] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_BankVerifiedReceipts_StoreBankAccounts_BankAccountId",
                        column: x => x.BankAccountId,
                        principalTable: "StoreBankAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BankVerifiedReceipts_Stores_OriginStoreId",
                        column: x => x.OriginStoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "StorefrontBankObservations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PaymentIntentId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Fingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ProtectedEvidence = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ObservedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                    table.PrimaryKey("PK_StorefrontBankObservations", x => x.Id);
                    table.UniqueConstraint("AK_StorefrontBankObservations_StoreId_Id", x => new { x.StoreId, x.Id });
                    table.ForeignKey(
                        name: "FK_StorefrontBankObservations_StorefrontPaymentIntents_StoreId_PaymentIntentId",
                        columns: x => new { x.StoreId, x.PaymentIntentId },
                        principalTable: "StorefrontPaymentIntents",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontBankObservations_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "StorefrontQrProcessing",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PaymentIntentId = table.Column<int>(type: "int", nullable: false),
                    State = table.Column<int>(type: "int", nullable: false),
                    TraceNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ProviderTraceNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    VirtualAccount = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    QrPng = table.Column<byte[]>(type: "varbinary(max)", maxLength: 262144, nullable: true),
                    NextAttemptAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastAttemptAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    NextScanBankDate = table.Column<DateTime>(type: "date", nullable: true),
                    RequestedBankDate = table.Column<DateTime>(type: "date", nullable: true),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    ReviewCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
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
                    table.PrimaryKey("PK_StorefrontQrProcessing", x => x.Id);
                    table.UniqueConstraint("AK_StorefrontQrProcessing_StoreId_Id", x => new { x.StoreId, x.Id });
                    table.ForeignKey(
                        name: "FK_StorefrontQrProcessing_StorefrontPaymentIntents_StoreId_PaymentIntentId",
                        columns: x => new { x.StoreId, x.PaymentIntentId },
                        principalTable: "StorefrontPaymentIntents",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontQrProcessing_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "StorefrontUnattributedBankReceipts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PaymentIntentId = table.Column<int>(type: "int", nullable: false),
                    Fingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ExternalTransactionId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    RequestedAccountIdentity = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ReportedAccountIdentity = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    RawBankTime = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TimePrecision = table.Column<int>(type: "int", nullable: false),
                    BankBusinessDate = table.Column<DateTime>(type: "date", nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ObservedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
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
                    table.PrimaryKey("PK_StorefrontUnattributedBankReceipts", x => x.Id);
                    table.UniqueConstraint("AK_StorefrontUnattributedBankReceipts_StoreId_Id", x => new { x.StoreId, x.Id });
                    table.ForeignKey(
                        name: "FK_StorefrontUnattributedBankReceipts_StorefrontPaymentIntents_StoreId_PaymentIntentId",
                        columns: x => new { x.StoreId, x.PaymentIntentId },
                        principalTable: "StorefrontPaymentIntents",
                        principalColumns: new[] { "StoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StorefrontUnattributedBankReceipts_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "BankReceiptConflicts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ObservedAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ObservedIdentityKind = table.Column<int>(type: "int", nullable: false),
                    BankVerifiedReceiptId = table.Column<int>(type: "int", nullable: false),
                    Fingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ProtectedEvidence = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AttemptedReference = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    AttemptedStoreId = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    CreatedBy = table.Column<int>(type: "int", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<int>(type: "int", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedBy = table.Column<int>(type: "int", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankReceiptConflicts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BankReceiptConflicts_BankVerifiedReceipts_BankVerifiedReceiptId",
                        column: x => x.BankVerifiedReceiptId,
                        principalTable: "BankVerifiedReceipts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BankReceiptConsumptions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BankVerifiedReceiptId = table.Column<int>(type: "int", nullable: false),
                    Channel = table.Column<int>(type: "int", nullable: false),
                    OriginStoreId = table.Column<int>(type: "int", nullable: false),
                    PosPaymentId = table.Column<int>(type: "int", nullable: true),
                    StorefrontAllocationId = table.Column<int>(type: "int", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    CreatedBy = table.Column<int>(type: "int", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<int>(type: "int", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedBy = table.Column<int>(type: "int", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankReceiptConsumptions", x => x.Id);
                    table.CheckConstraint("CK_BankReceiptConsumptions_Channel", "([Channel]=1 AND [PosPaymentId] IS NOT NULL AND [StorefrontAllocationId] IS NULL) OR ([Channel]=2 AND [PosPaymentId] IS NULL AND [StorefrontAllocationId] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_BankReceiptConsumptions_BankVerifiedReceipts_OriginStoreId_BankVerifiedReceiptId",
                        columns: x => new { x.OriginStoreId, x.BankVerifiedReceiptId },
                        principalTable: "BankVerifiedReceipts",
                        principalColumns: new[] { "OriginStoreId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BankReceiptConsumptions_OrderPayments_PosPaymentId",
                        column: x => x.PosPaymentId,
                        principalTable: "OrderPayments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BankReceiptConsumptions_StorefrontPaymentAllocations_StorefrontAllocationId",
                        column: x => x.StorefrontAllocationId,
                        principalTable: "StorefrontPaymentAllocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontBankReceipts_GlobalBankReceiptId",
                table: "StorefrontBankReceipts",
                column: "GlobalBankReceiptId",
                unique: true,
                filter: "[GlobalBankReceiptId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontBankReceipts_StoreId_GlobalBankReceiptId",
                table: "StorefrontBankReceipts",
                columns: new[] { "StoreId", "GlobalBankReceiptId" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderPayments_StoreId_BankVerifiedReceiptId",
                table: "OrderPayments",
                columns: new[] { "StoreId", "BankVerifiedReceiptId" });

            migrationBuilder.CreateIndex(
                name: "IX_AcbPaymentTransactions_StoreId_BankVerifiedReceiptId",
                table: "AcbPaymentTransactions",
                columns: new[] { "StoreId", "BankVerifiedReceiptId" });

            migrationBuilder.CreateIndex(
                name: "IX_BankPaymentReferences_AccountIdentity_Channel_NotificationIdentityMappingVerified",
                table: "BankPaymentReferences",
                columns: new[] { "AccountIdentity", "Channel", "NotificationIdentityMappingVerified" });

            migrationBuilder.CreateIndex(
                name: "IX_BankPaymentReferences_OriginStoreId",
                table: "BankPaymentReferences",
                column: "OriginStoreId");

            migrationBuilder.CreateIndex(
                name: "IX_BankPaymentReferences_PosQrRequestId",
                table: "BankPaymentReferences",
                column: "PosQrRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_BankPaymentReferences_Provider_Reference",
                table: "BankPaymentReferences",
                columns: new[] { "Provider", "Reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BankPaymentReferences_StorefrontPaymentIntentId",
                table: "BankPaymentReferences",
                column: "StorefrontPaymentIntentId");

            migrationBuilder.CreateIndex(
                name: "IX_BankReceiptConflicts_BankVerifiedReceiptId_Fingerprint_AttemptedStoreId",
                table: "BankReceiptConflicts",
                columns: new[] { "BankVerifiedReceiptId", "Fingerprint", "AttemptedStoreId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BankReceiptConsumptions_BankVerifiedReceiptId",
                table: "BankReceiptConsumptions",
                column: "BankVerifiedReceiptId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BankReceiptConsumptions_OriginStoreId_BankVerifiedReceiptId",
                table: "BankReceiptConsumptions",
                columns: new[] { "OriginStoreId", "BankVerifiedReceiptId" });

            migrationBuilder.CreateIndex(
                name: "IX_BankReceiptConsumptions_PosPaymentId",
                table: "BankReceiptConsumptions",
                column: "PosPaymentId",
                unique: true,
                filter: "[PosPaymentId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_BankReceiptConsumptions_StorefrontAllocationId",
                table: "BankReceiptConsumptions",
                column: "StorefrontAllocationId",
                unique: true,
                filter: "[StorefrontAllocationId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_BankVerifiedReceipts_BankAccountId",
                table: "BankVerifiedReceipts",
                column: "BankAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_BankVerifiedReceipts_OriginStoreId_ProviderReference",
                table: "BankVerifiedReceipts",
                columns: new[] { "OriginStoreId", "ProviderReference" });

            migrationBuilder.CreateIndex(
                name: "IX_BankVerifiedReceipts_Provider_AccountIdentity_ExternalTransactionId",
                table: "BankVerifiedReceipts",
                columns: new[] { "Provider", "AccountIdentity", "ExternalTransactionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontBankObservations_StoreId_PaymentIntentId_Fingerprint",
                table: "StorefrontBankObservations",
                columns: new[] { "StoreId", "PaymentIntentId", "Fingerprint" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontQrProcessing_NextAttemptAtUtc_State",
                table: "StorefrontQrProcessing",
                columns: new[] { "NextAttemptAtUtc", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontQrProcessing_StoreId_PaymentIntentId",
                table: "StorefrontQrProcessing",
                columns: new[] { "StoreId", "PaymentIntentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontUnattributedBankReceipts_StoreId_PaymentIntentId_Fingerprint",
                table: "StorefrontUnattributedBankReceipts",
                columns: new[] { "StoreId", "PaymentIntentId", "Fingerprint" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_AcbPaymentTransactions_BankVerifiedReceipts_StoreId_BankVerifiedReceiptId",
                table: "AcbPaymentTransactions",
                columns: new[] { "StoreId", "BankVerifiedReceiptId" },
                principalTable: "BankVerifiedReceipts",
                principalColumns: new[] { "OriginStoreId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_OrderPayments_BankVerifiedReceipts_StoreId_BankVerifiedReceiptId",
                table: "OrderPayments",
                columns: new[] { "StoreId", "BankVerifiedReceiptId" },
                principalTable: "BankVerifiedReceipts",
                principalColumns: new[] { "OriginStoreId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_StorefrontBankReceipts_BankVerifiedReceipts_StoreId_GlobalBankReceiptId",
                table: "StorefrontBankReceipts",
                columns: new[] { "StoreId", "GlobalBankReceiptId" },
                principalTable: "BankVerifiedReceipts",
                principalColumns: new[] { "OriginStoreId", "Id" },
                onDelete: ReferentialAction.Restrict);
            migrationBuilder.Sql(StorefrontQrBankSchemaSqlCatalog.ReplaceEventDropSql);
            foreach(var trigger in StorefrontQrBankSchemaSqlCatalog.Triggers) migrationBuilder.Sql(trigger.Sql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(StorefrontQrBankSchemaSqlCatalog.DownGuardSql);
            foreach(var trigger in StorefrontQrBankSchemaSqlCatalog.Triggers) migrationBuilder.Sql($"DROP TRIGGER [dbo].[{trigger.Name}];");
            migrationBuilder.Sql(StorefrontCheckoutSchemaSqlCatalog.EventSql);
            migrationBuilder.DropForeignKey(
                name: "FK_AcbPaymentTransactions_BankVerifiedReceipts_StoreId_BankVerifiedReceiptId",
                table: "AcbPaymentTransactions");

            migrationBuilder.DropForeignKey(
                name: "FK_OrderPayments_BankVerifiedReceipts_StoreId_BankVerifiedReceiptId",
                table: "OrderPayments");

            migrationBuilder.DropForeignKey(
                name: "FK_StorefrontBankReceipts_BankVerifiedReceipts_StoreId_GlobalBankReceiptId",
                table: "StorefrontBankReceipts");

            migrationBuilder.DropTable(
                name: "BankPaymentReferences");

            migrationBuilder.DropTable(
                name: "BankReceiptConflicts");

            migrationBuilder.DropTable(
                name: "BankReceiptConsumptions");

            migrationBuilder.DropTable(
                name: "StorefrontBankObservations");

            migrationBuilder.DropTable(
                name: "StorefrontQrProcessing");

            migrationBuilder.DropTable(
                name: "StorefrontUnattributedBankReceipts");

            migrationBuilder.DropTable(
                name: "BankVerifiedReceipts");

            migrationBuilder.DropIndex(
                name: "IX_StorefrontBankReceipts_GlobalBankReceiptId",
                table: "StorefrontBankReceipts");

            migrationBuilder.DropIndex(
                name: "IX_StorefrontBankReceipts_StoreId_GlobalBankReceiptId",
                table: "StorefrontBankReceipts");

            migrationBuilder.DropIndex(
                name: "IX_OrderPayments_StoreId_BankVerifiedReceiptId",
                table: "OrderPayments");

            migrationBuilder.DropIndex(
                name: "IX_AcbPaymentTransactions_StoreId_BankVerifiedReceiptId",
                table: "AcbPaymentTransactions");

            migrationBuilder.DropColumn(
                name: "GlobalBankReceiptId",
                table: "StorefrontBankReceipts");

            migrationBuilder.DropColumn(
                name: "BankVerifiedReceiptId",
                table: "OrderPayments");

            migrationBuilder.DropColumn(
                name: "BankBindingSnapshotJson",
                table: "AcbQrSessions");

            migrationBuilder.DropColumn(
                name: "BankVerifiedReceiptId",
                table: "AcbPaymentTransactions");

            migrationBuilder.DropSequence(
                name: "StorefrontBankCorrelationSequence");

            migrationBuilder.CreateIndex(
                name: "IX_AcbPaymentTransactions_StoreId",
                table: "AcbPaymentTransactions",
                column: "StoreId");
        }
    }
}
