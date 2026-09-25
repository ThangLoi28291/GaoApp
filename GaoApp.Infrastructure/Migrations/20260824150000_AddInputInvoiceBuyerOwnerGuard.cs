using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GaoApp.Infrastructure.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260824150000_AddInputInvoiceBuyerOwnerGuard")]
public sealed partial class AddInputInvoiceBuyerOwnerGuard : Migration
{
    private const string NormalizeSql =
        "CONVERT(nvarchar(50), NULLIF(UPPER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(LTRIM(RTRIM({0})), N' ', N''), N'.', N''), N'-', N''), NCHAR(9), N''), NCHAR(13), N''), NCHAR(10), N'')), N''))";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_LegalEntities_StoreId_TaxCode",
            table: "LegalEntities");

        migrationBuilder.AddColumn<string>(
            name: "NormalizedTaxCode", table: "LegalEntities", type: "nvarchar(50)",
            maxLength: 50, nullable: true,
            computedColumnSql: string.Format(NormalizeSql, "[TaxCode]"), stored: true);
        migrationBuilder.AddColumn<string>(
            name: "NormalizedBuyerTaxCode", table: "InputInvoiceHead", type: "nvarchar(50)",
            maxLength: 50, nullable: true,
            computedColumnSql: string.Format(NormalizeSql, "[BuyerTaxCode]"), stored: true);
        migrationBuilder.AddColumn<int>(
            name: "BuyerOwnerResolutionStatus", table: "InputInvoiceHead",
            type: "int", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<int>(
            name: "ResolvedBuyerLegalEntityId", table: "InputInvoiceHead",
            type: "int", nullable: true);
        migrationBuilder.AddColumn<DateTime>(
            name: "BuyerOwnerResolutionUpdatedAtUtc", table: "InputInvoiceHead",
            type: "datetime2", nullable: true);
        migrationBuilder.AddColumn<int>(
            name: "ConfirmedLegalEntityId", table: "StockDocument",
            type: "int", nullable: true);

        // Deterministic evidence only: BuyerTaxCode -> normalized exact same-Store
        // active LegalEntity. Supplier/Seller/name/filename/free text are never used.
        migrationBuilder.Sql("""
            UPDATE i
               SET [BuyerOwnerResolutionStatus] =
                     CASE WHEN i.[NormalizedBuyerTaxCode] IS NULL THEN 1
                          WHEN c.[CandidateCount] = 0 THEN 2
                          WHEN c.[CandidateCount] > 1 THEN 3 ELSE 4 END,
                   [ResolvedBuyerLegalEntityId] =
                     CASE WHEN c.[CandidateCount] = 1 THEN c.[LegalEntityId] ELSE NULL END,
                   [BuyerOwnerResolutionUpdatedAtUtc] = SYSUTCDATETIME()
              FROM [InputInvoiceHead] i
              OUTER APPLY (
                    SELECT COUNT_BIG(*) [CandidateCount], MIN(le.[Id]) [LegalEntityId]
                      FROM [LegalEntities] le
                     WHERE le.[StoreId] = i.[StoreId]
                       AND le.[NormalizedTaxCode] = i.[NormalizedBuyerTaxCode]
                       AND le.[IsActive] = 1 AND le.[IsDeleted] = 0
              ) c;

            UPDATE d
               SET [ConfirmedLegalEntityId] = w.[LegalEntityId]
              FROM [StockDocument] d
              JOIN [Warehouses] w ON w.[Id] = d.[WarehouseId]
                                  AND w.[StoreId] = d.[StoreId]
             WHERE d.[Type] = 1 AND d.[Status] = 3;

            IF EXISTS (
                SELECT 1 FROM [StockDocument]
                 WHERE [Type] = 1 AND [Status] = 3
                   AND [ConfirmedLegalEntityId] IS NULL)
                THROW 51001, 'C2 backfill blocked: confirmed receipt owner is not deterministic.', 1;

            IF EXISTS (
                SELECT 1
                  FROM [StockDocumentInputInvoiceMap] m
                  JOIN [StockDocument] d ON d.[Id] = m.[StockDocumentId]
                  JOIN [InputInvoiceHead] i ON i.[Id] = m.[InputInvoiceHeadId]
                  JOIN [Warehouses] w ON w.[Id] = d.[WarehouseId]
                 WHERE m.[IsDeleted] = 0 AND d.[IsDeleted] = 0 AND i.[IsDeleted] = 0
                   AND d.[Type] = 1
                   AND (i.[BuyerOwnerResolutionStatus] <> 4
                     OR i.[ResolvedBuyerLegalEntityId] <>
                        CASE WHEN d.[Status] = 3 THEN d.[ConfirmedLegalEntityId]
                             ELSE w.[LegalEntityId] END))
                THROW 51002, 'C2 backfill blocked: active receipt-invoice owner link is unsafe.', 1;
            """);

        migrationBuilder.CreateIndex(
            name: "IX_LegalEntities_StoreId_TaxCode", table: "LegalEntities",
            columns: new[] { "StoreId", "TaxCode" },
            filter: "[TaxCode] IS NOT NULL AND [TaxCode] <> '' AND [IsDeleted] = 0");
        migrationBuilder.CreateIndex(
            name: "IX_LegalEntities_StoreId_NormalizedTaxCode_State", table: "LegalEntities",
            columns: new[] { "StoreId", "NormalizedTaxCode", "IsDeleted", "IsActive" });
        migrationBuilder.CreateIndex(
            name: "IX_InputInvoiceHead_StoreId_NormalizedBuyerTaxCode", table: "InputInvoiceHead",
            columns: new[] { "StoreId", "NormalizedBuyerTaxCode" });
        migrationBuilder.CreateIndex(
            name: "IX_InputInvoiceHead_StoreId_ResolvedBuyerLegalEntityId", table: "InputInvoiceHead",
            columns: new[] { "StoreId", "ResolvedBuyerLegalEntityId" });
        migrationBuilder.CreateIndex(
            name: "IX_StockDocument_StoreId_ConfirmedLegalEntityId", table: "StockDocument",
            columns: new[] { "StoreId", "ConfirmedLegalEntityId" });

        migrationBuilder.AddForeignKey(
            name: "FK_InputInvoiceHead_LegalEntities_StoreId_ResolvedBuyerLegalEntityId",
            table: "InputInvoiceHead",
            columns: new[] { "StoreId", "ResolvedBuyerLegalEntityId" },
            principalTable: "LegalEntities", principalColumns: new[] { "StoreId", "Id" },
            onDelete: ReferentialAction.Restrict);
        migrationBuilder.AddForeignKey(
            name: "FK_StockDocument_LegalEntities_StoreId_ConfirmedLegalEntityId",
            table: "StockDocument",
            columns: new[] { "StoreId", "ConfirmedLegalEntityId" },
            principalTable: "LegalEntities", principalColumns: new[] { "StoreId", "Id" },
            onDelete: ReferentialAction.Restrict);
        migrationBuilder.AddCheckConstraint(
            name: "CK_StockDocument_ConfirmedReceiptOwner", table: "StockDocument",
            sql: "[Type] <> 1 OR [Status] <> 3 OR [ConfirmedLegalEntityId] IS NOT NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint("CK_StockDocument_ConfirmedReceiptOwner", "StockDocument");
        migrationBuilder.DropForeignKey("FK_InputInvoiceHead_LegalEntities_StoreId_ResolvedBuyerLegalEntityId", "InputInvoiceHead");
        migrationBuilder.DropForeignKey("FK_StockDocument_LegalEntities_StoreId_ConfirmedLegalEntityId", "StockDocument");
        migrationBuilder.DropIndex("IX_LegalEntities_StoreId_NormalizedTaxCode_State", "LegalEntities");
        migrationBuilder.DropIndex("IX_InputInvoiceHead_StoreId_NormalizedBuyerTaxCode", "InputInvoiceHead");
        migrationBuilder.DropIndex("IX_InputInvoiceHead_StoreId_ResolvedBuyerLegalEntityId", "InputInvoiceHead");
        migrationBuilder.DropIndex("IX_StockDocument_StoreId_ConfirmedLegalEntityId", "StockDocument");
        migrationBuilder.DropColumn("NormalizedTaxCode", "LegalEntities");
        migrationBuilder.DropColumn("NormalizedBuyerTaxCode", "InputInvoiceHead");
        migrationBuilder.DropColumn("BuyerOwnerResolutionStatus", "InputInvoiceHead");
        migrationBuilder.DropColumn("ResolvedBuyerLegalEntityId", "InputInvoiceHead");
        migrationBuilder.DropColumn("BuyerOwnerResolutionUpdatedAtUtc", "InputInvoiceHead");
        migrationBuilder.DropColumn("ConfirmedLegalEntityId", "StockDocument");
        migrationBuilder.DropIndex("IX_LegalEntities_StoreId_TaxCode", "LegalEntities");
        migrationBuilder.CreateIndex(
            name: "IX_LegalEntities_StoreId_TaxCode", table: "LegalEntities",
            columns: new[] { "StoreId", "TaxCode" }, unique: true,
            filter: "[TaxCode] IS NOT NULL AND [TaxCode] <> '' AND [IsDeleted] = 0");
    }
}
