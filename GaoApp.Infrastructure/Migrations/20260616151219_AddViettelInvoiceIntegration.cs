using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddViettelInvoiceIntegration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_InvoiceDetails_InvoiceHeads_InvoiceHeadId",
                table: "InvoiceDetails");

            migrationBuilder.DropForeignKey(
                name: "FK_InvoiceDetails_OrderLines_OrderLineId",
                table: "InvoiceDetails");

            migrationBuilder.DropForeignKey(
                name: "FK_InvoiceDetails_ProductVariant_ProductVariantId",
                table: "InvoiceDetails");

            migrationBuilder.DropIndex(
                name: "IX_InvoiceHeads_StoreId_InvoiceDate",
                table: "InvoiceHeads");

            migrationBuilder.DropIndex(
                name: "IX_InvoiceHeads_StoreId_InvoiceNumber",
                table: "InvoiceHeads");

            migrationBuilder.DropIndex(
                name: "IX_InvoiceHeads_StoreId_IsLocked",
                table: "InvoiceHeads");

            migrationBuilder.DropIndex(
                name: "IX_InvoiceHeads_StoreId_OrderId",
                table: "InvoiceHeads");

            migrationBuilder.DropIndex(
                name: "IX_InvoiceDetails_StoreId_InvoiceHeadId",
                table: "InvoiceDetails");

            migrationBuilder.DropIndex(
                name: "IX_InvoiceDetails_StoreId_OrderLineId",
                table: "InvoiceDetails");

            migrationBuilder.DropIndex(
                name: "IX_InvoiceDetails_StoreId_ProductVariantId",
                table: "InvoiceDetails");

            migrationBuilder.DropIndex(
                name: "IX_InvoiceDetails_StoreId_SourceType",
                table: "InvoiceDetails");

            migrationBuilder.AddColumn<string>(
                name: "CodeOfTax",
                table: "InvoiceHeads",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InvoiceSeries",
                table: "InvoiceHeads",
                type: "nvarchar(25)",
                maxLength: 25,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InvoiceType",
                table: "InvoiceHeads",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "IssuedAtUtc",
                table: "InvoiceHeads",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastErrorCode",
                table: "InvoiceHeads",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastErrorMessage",
                table: "InvoiceHeads",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastSyncedAtUtc",
                table: "InvoiceHeads",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PdfFilePath",
                table: "InvoiceHeads",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProviderCode",
                table: "InvoiceHeads",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProviderInvoiceNo",
                table: "InvoiceHeads",
                type: "nvarchar(35)",
                maxLength: 35,
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "ProviderStatus",
                table: "InvoiceHeads",
                type: "tinyint",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.AddColumn<string>(
                name: "ProviderTransactionId",
                table: "InvoiceHeads",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReservationCode",
                table: "InvoiceHeads",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SupplierTaxCode",
                table: "InvoiceHeads",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TemplateCode",
                table: "InvoiceHeads",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TransactionUuid",
                table: "InvoiceHeads",
                type: "nvarchar(36)",
                maxLength: 36,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ZipFilePath",
                table: "InvoiceHeads",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "VatRate",
                table: "InvoiceDetails",
                type: "decimal(9,2)",
                precision: 9,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(5,2)",
                oldPrecision: 5,
                oldScale: 2);

            migrationBuilder.AlterColumn<byte>(
                name: "SourceType",
                table: "InvoiceDetails",
                type: "tinyint",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.CreateTable(
                name: "InvoiceIntegrationLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InvoiceHeadId = table.Column<int>(type: "int", nullable: false),
                    ActionType = table.Column<byte>(type: "tinyint", nullable: false),
                    RequestUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RequestBody = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ResponseBody = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsSuccess = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    ErrorCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FinishedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DurationMs = table.Column<long>(type: "bigint", nullable: true),
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
                    table.PrimaryKey("PK_InvoiceIntegrationLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvoiceIntegrationLogs_InvoiceHeads_InvoiceHeadId",
                        column: x => x.InvoiceHeadId,
                        principalTable: "InvoiceHeads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvoiceIntegrationLogs_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "InvoiceProviderSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProviderCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    IsProduction = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    BaseUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Username = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Password = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    SupplierTaxCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    InvoiceType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    TemplateCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    InvoiceSeries = table.Column<string>(type: "nvarchar(25)", maxLength: 25, nullable: false),
                    CurrencyCode = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    ExchangeRate = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false, defaultValue: 1m),
                    PaymentMethodName = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CusGetInvoiceRight = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    DefaultPaymentStatus = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
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
                    table.PrimaryKey("PK_InvoiceProviderSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvoiceProviderSettings_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceHeads_StoreId_InvoiceDate_IsDeleted",
                table: "InvoiceHeads",
                columns: new[] { "StoreId", "InvoiceDate", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceHeads_StoreId_OrderId",
                table: "InvoiceHeads",
                columns: new[] { "StoreId", "OrderId" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceHeads_StoreId_ProviderInvoiceNo_IsDeleted",
                table: "InvoiceHeads",
                columns: new[] { "StoreId", "ProviderInvoiceNo", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceHeads_StoreId_ProviderStatus_IsDeleted",
                table: "InvoiceHeads",
                columns: new[] { "StoreId", "ProviderStatus", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceHeads_StoreId_TransactionUuid",
                table: "InvoiceHeads",
                columns: new[] { "StoreId", "TransactionUuid" },
                unique: true,
                filter: "[TransactionUuid] IS NOT NULL AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceDetails_StoreId_InvoiceHeadId_IsDeleted",
                table: "InvoiceDetails",
                columns: new[] { "StoreId", "InvoiceHeadId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceDetails_StoreId_InvoiceHeadId_OrderLineId",
                table: "InvoiceDetails",
                columns: new[] { "StoreId", "InvoiceHeadId", "OrderLineId" },
                unique: true,
                filter: "[OrderLineId] IS NOT NULL AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceDetails_StoreId_ProductVariantId_IsDeleted",
                table: "InvoiceDetails",
                columns: new[] { "StoreId", "ProductVariantId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceDetails_StoreId_SourceType_IsDeleted",
                table: "InvoiceDetails",
                columns: new[] { "StoreId", "SourceType", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceIntegrationLogs_InvoiceHeadId",
                table: "InvoiceIntegrationLogs",
                column: "InvoiceHeadId");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceIntegrationLogs_StoreId_InvoiceHeadId_ActionType",
                table: "InvoiceIntegrationLogs",
                columns: new[] { "StoreId", "InvoiceHeadId", "ActionType" });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceIntegrationLogs_StoreId_IsSuccess_ActionType",
                table: "InvoiceIntegrationLogs",
                columns: new[] { "StoreId", "IsSuccess", "ActionType" });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceIntegrationLogs_StoreId_StartedAtUtc",
                table: "InvoiceIntegrationLogs",
                columns: new[] { "StoreId", "StartedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceProviderSettings_StoreId_IsActive_IsDeleted",
                table: "InvoiceProviderSettings",
                columns: new[] { "StoreId", "IsActive", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceProviderSettings_StoreId_ProviderCode_SupplierTaxCode_TemplateCode_InvoiceSeries",
                table: "InvoiceProviderSettings",
                columns: new[] { "StoreId", "ProviderCode", "SupplierTaxCode", "TemplateCode", "InvoiceSeries" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.AddForeignKey(
                name: "FK_InvoiceDetails_InvoiceHeads_InvoiceHeadId",
                table: "InvoiceDetails",
                column: "InvoiceHeadId",
                principalTable: "InvoiceHeads",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_InvoiceDetails_OrderLines_OrderLineId",
                table: "InvoiceDetails",
                column: "OrderLineId",
                principalTable: "OrderLines",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_InvoiceDetails_ProductVariant_ProductVariantId",
                table: "InvoiceDetails",
                column: "ProductVariantId",
                principalTable: "ProductVariant",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_InvoiceDetails_InvoiceHeads_InvoiceHeadId",
                table: "InvoiceDetails");

            migrationBuilder.DropForeignKey(
                name: "FK_InvoiceDetails_OrderLines_OrderLineId",
                table: "InvoiceDetails");

            migrationBuilder.DropForeignKey(
                name: "FK_InvoiceDetails_ProductVariant_ProductVariantId",
                table: "InvoiceDetails");

            migrationBuilder.DropTable(
                name: "InvoiceIntegrationLogs");

            migrationBuilder.DropTable(
                name: "InvoiceProviderSettings");

            migrationBuilder.DropIndex(
                name: "IX_InvoiceHeads_StoreId_InvoiceDate_IsDeleted",
                table: "InvoiceHeads");

            migrationBuilder.DropIndex(
                name: "IX_InvoiceHeads_StoreId_OrderId",
                table: "InvoiceHeads");

            migrationBuilder.DropIndex(
                name: "IX_InvoiceHeads_StoreId_ProviderInvoiceNo_IsDeleted",
                table: "InvoiceHeads");

            migrationBuilder.DropIndex(
                name: "IX_InvoiceHeads_StoreId_ProviderStatus_IsDeleted",
                table: "InvoiceHeads");

            migrationBuilder.DropIndex(
                name: "IX_InvoiceHeads_StoreId_TransactionUuid",
                table: "InvoiceHeads");

            migrationBuilder.DropIndex(
                name: "IX_InvoiceDetails_StoreId_InvoiceHeadId_IsDeleted",
                table: "InvoiceDetails");

            migrationBuilder.DropIndex(
                name: "IX_InvoiceDetails_StoreId_InvoiceHeadId_OrderLineId",
                table: "InvoiceDetails");

            migrationBuilder.DropIndex(
                name: "IX_InvoiceDetails_StoreId_ProductVariantId_IsDeleted",
                table: "InvoiceDetails");

            migrationBuilder.DropIndex(
                name: "IX_InvoiceDetails_StoreId_SourceType_IsDeleted",
                table: "InvoiceDetails");

            migrationBuilder.DropColumn(
                name: "CodeOfTax",
                table: "InvoiceHeads");

            migrationBuilder.DropColumn(
                name: "InvoiceSeries",
                table: "InvoiceHeads");

            migrationBuilder.DropColumn(
                name: "InvoiceType",
                table: "InvoiceHeads");

            migrationBuilder.DropColumn(
                name: "IssuedAtUtc",
                table: "InvoiceHeads");

            migrationBuilder.DropColumn(
                name: "LastErrorCode",
                table: "InvoiceHeads");

            migrationBuilder.DropColumn(
                name: "LastErrorMessage",
                table: "InvoiceHeads");

            migrationBuilder.DropColumn(
                name: "LastSyncedAtUtc",
                table: "InvoiceHeads");

            migrationBuilder.DropColumn(
                name: "PdfFilePath",
                table: "InvoiceHeads");

            migrationBuilder.DropColumn(
                name: "ProviderCode",
                table: "InvoiceHeads");

            migrationBuilder.DropColumn(
                name: "ProviderInvoiceNo",
                table: "InvoiceHeads");

            migrationBuilder.DropColumn(
                name: "ProviderStatus",
                table: "InvoiceHeads");

            migrationBuilder.DropColumn(
                name: "ProviderTransactionId",
                table: "InvoiceHeads");

            migrationBuilder.DropColumn(
                name: "ReservationCode",
                table: "InvoiceHeads");

            migrationBuilder.DropColumn(
                name: "SupplierTaxCode",
                table: "InvoiceHeads");

            migrationBuilder.DropColumn(
                name: "TemplateCode",
                table: "InvoiceHeads");

            migrationBuilder.DropColumn(
                name: "TransactionUuid",
                table: "InvoiceHeads");

            migrationBuilder.DropColumn(
                name: "ZipFilePath",
                table: "InvoiceHeads");

            migrationBuilder.AlterColumn<decimal>(
                name: "VatRate",
                table: "InvoiceDetails",
                type: "decimal(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(9,2)",
                oldPrecision: 9,
                oldScale: 2);

            migrationBuilder.AlterColumn<int>(
                name: "SourceType",
                table: "InvoiceDetails",
                type: "int",
                nullable: false,
                oldClrType: typeof(byte),
                oldType: "tinyint");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceHeads_StoreId_InvoiceDate",
                table: "InvoiceHeads",
                columns: new[] { "StoreId", "InvoiceDate" });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceHeads_StoreId_InvoiceNumber",
                table: "InvoiceHeads",
                columns: new[] { "StoreId", "InvoiceNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceHeads_StoreId_IsLocked",
                table: "InvoiceHeads",
                columns: new[] { "StoreId", "IsLocked" });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceHeads_StoreId_OrderId",
                table: "InvoiceHeads",
                columns: new[] { "StoreId", "OrderId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceDetails_StoreId_InvoiceHeadId",
                table: "InvoiceDetails",
                columns: new[] { "StoreId", "InvoiceHeadId" });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceDetails_StoreId_OrderLineId",
                table: "InvoiceDetails",
                columns: new[] { "StoreId", "OrderLineId" });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceDetails_StoreId_ProductVariantId",
                table: "InvoiceDetails",
                columns: new[] { "StoreId", "ProductVariantId" });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceDetails_StoreId_SourceType",
                table: "InvoiceDetails",
                columns: new[] { "StoreId", "SourceType" });

            migrationBuilder.AddForeignKey(
                name: "FK_InvoiceDetails_InvoiceHeads_InvoiceHeadId",
                table: "InvoiceDetails",
                column: "InvoiceHeadId",
                principalTable: "InvoiceHeads",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_InvoiceDetails_OrderLines_OrderLineId",
                table: "InvoiceDetails",
                column: "OrderLineId",
                principalTable: "OrderLines",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_InvoiceDetails_ProductVariant_ProductVariantId",
                table: "InvoiceDetails",
                column: "ProductVariantId",
                principalTable: "ProductVariant",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
