using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStoreAcbPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AcbQrSessions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    QrRequestId = table.Column<int>(type: "int", nullable: false),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    ShiftId = table.Column<int>(type: "int", nullable: false),
                    TerminalId = table.Column<int>(type: "int", nullable: false),
                    CashierId = table.Column<int>(type: "int", nullable: false),
                    ProviderOrderId = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    TraceNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    VirtualAccount = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CartFingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ReviewReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    PaymentId = table.Column<int>(type: "int", nullable: true),
                    PrintClaimedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastRetrievedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastRetrieveJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
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
                    table.PrimaryKey("PK_AcbQrSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AcbQrSessions_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AcbQrSessions_PosPaymentQrRequests_QrRequestId",
                        column: x => x.QrRequestId,
                        principalTable: "PosPaymentQrRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AcbQrSessions_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "StoreAcbSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
                    BankAccountId = table.Column<int>(type: "int", nullable: false),
                    TokenEndpoint = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ApiBaseUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    QrEndpoint = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ClientId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ClientSecretProtected = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CallbackApiKeyProtected = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TokenScope = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    XService = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    XProviderId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    XOwnerNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    XOwnerType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    VirtualAccountPrefix = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    MerchantId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    BeneficiaryName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
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
                    table.PrimaryKey("PK_StoreAcbSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StoreAcbSettings_StoreBankAccounts_BankAccountId",
                        column: x => x.BankAccountId,
                        principalTable: "StoreBankAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StoreAcbSettings_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "AcbPaymentTransactions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SessionId = table.Column<int>(type: "int", nullable: false),
                    TransactionNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Content = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PostedAt = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
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
                    table.PrimaryKey("PK_AcbPaymentTransactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AcbPaymentTransactions_AcbQrSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "AcbQrSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AcbPaymentTransactions_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_AcbPaymentTransactions_SessionId_TransactionNumber",
                table: "AcbPaymentTransactions",
                columns: new[] { "SessionId", "TransactionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AcbPaymentTransactions_StoreId",
                table: "AcbPaymentTransactions",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_AcbQrSessions_OrderId",
                table: "AcbQrSessions",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_AcbQrSessions_QrRequestId",
                table: "AcbQrSessions",
                column: "QrRequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AcbQrSessions_StoreId_OrderId_Status",
                table: "AcbQrSessions",
                columns: new[] { "StoreId", "OrderId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_AcbQrSessions_StoreId_ProviderOrderId",
                table: "AcbQrSessions",
                columns: new[] { "StoreId", "ProviderOrderId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StoreAcbSettings_BankAccountId",
                table: "StoreAcbSettings",
                column: "BankAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_StoreAcbSettings_StoreId",
                table: "StoreAcbSettings",
                column: "StoreId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AcbPaymentTransactions");

            migrationBuilder.DropTable(
                name: "StoreAcbSettings");

            migrationBuilder.DropTable(
                name: "AcbQrSessions");
        }
    }
}
