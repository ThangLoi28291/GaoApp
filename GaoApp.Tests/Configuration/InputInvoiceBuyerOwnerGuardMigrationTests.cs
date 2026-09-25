using FluentAssertions;
using GaoApp.Application.Services.Inventory;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Configuration;

[Collection("R1FinalDatabasePreflight")]
public sealed class InputInvoiceBuyerOwnerGuardMigrationTests
{
    private const string PreviousMigrationId =
        "20260822090000_AddInputInvoiceSupplierResolution";
    private const string BuyerOwnerMigrationId =
        "20260824150000_AddInputInvoiceBuyerOwnerGuard";
    private const string MigrationPath =
        "GaoApp.Infrastructure/Migrations/20260824150000_AddInputInvoiceBuyerOwnerGuard.cs";

    [Fact]
    public void Migration_must_keep_normalized_legal_entity_identity_non_unique()
    {
        var source = Read(MigrationPath);
        var up = source[..source.IndexOf("protected override void Down", StringComparison.Ordinal)];
        Assert.Contains("IX_LegalEntities_StoreId_NormalizedTaxCode_State", source, StringComparison.Ordinal);
        Assert.DoesNotContain("unique: true", up, StringComparison.Ordinal);
    }

    [Fact]
    public void Migration_must_use_only_deterministic_buyer_and_warehouse_evidence()
    {
        var source = Read(MigrationPath);

        Assert.Contains("BuyerTaxCode", source, StringComparison.Ordinal);
        Assert.Contains("WarehouseId", source, StringComparison.Ordinal);
        Assert.Contains("ConfirmedLegalEntityId", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SellerName", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("BuyerName", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("OriginalFileName", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Migration_must_fail_closed_and_scope_constraint_to_receipts()
    {
        var source = Read(MigrationPath);

        Assert.Contains("THROW", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("StockDocumentInputInvoiceMap", source, StringComparison.Ordinal);
        Assert.Contains("ConfirmedLegalEntityId", source, StringComparison.Ordinal);
        Assert.Contains("[Type]", source, StringComparison.Ordinal);
        Assert.Contains("[Status]", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unsafe_existing_link_fails_closed_and_rolls_back_schema_data_and_history()
    {
        await using var database = new InventoryPostingLocalDb();
        await database.MigrateAsync();
        var catalog = await database.SeedInventoryCatalogAsync();

        int receiptId;
        int invoiceId;
        await using (var db = database.CreateTenantContext(catalog.StoreId))
        {
            var supplier = await db.Suppliers.SingleAsync();
            var receipt = new StockDocument
            {
                StoreId = catalog.StoreId,
                DocumentNo = "PN-C2-UNSAFE",
                Type = StockDocumentType.Receipt,
                Status = StockDocumentStatus.PendingApproval,
                ReceiptSource = PurchaseReceiptSource.Direct,
                DirectReceiptReason = "Migration rollback evidence",
                WarehouseId = catalog.WarehouseId,
                SupplierId = supplier.Id,
                DocumentDate = new DateTime(2026, 8, 24)
            };
            var invoice = new InputInvoiceHead
            {
                StoreId = catalog.StoreId,
                SellerTaxCode = "0312770607",
                InvoiceSeries = "C26MVP",
                InvoiceNumber = "C2-UNSAFE-01",
                InvoiceDate = new DateTime(2026, 8, 24),
                BuyerTaxCode = null
            };
            InputInvoiceIdentityPolicy.ApplyRequiredIdentity(invoice);
            db.AddRange(receipt, invoice);
            await db.SaveChangesAsync();
            db.StockDocumentInputInvoiceMaps.Add(new StockDocumentInputInvoiceMap
            {
                StoreId = catalog.StoreId,
                StockDocumentId = receipt.Id,
                InputInvoiceHeadId = invoice.Id
            });
            await db.SaveChangesAsync();
            receiptId = receipt.Id;
            invoiceId = invoice.Id;
        }

        await database.MigrateAsync(PreviousMigrationId);
        var historyBefore = await database.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM [__EFMigrationsHistory];");
        var relationBefore = await database.ExecuteScalarAsync<int>($$"""
            SELECT COUNT(*) FROM [StockDocumentInputInvoiceMap]
             WHERE [StockDocumentId] = {{receiptId}}
               AND [InputInvoiceHeadId] = {{invoiceId}}
               AND [IsDeleted] = 0;
            """);

        var migrate = () => database.MigrateAsync();
        var failure = await migrate.Should().ThrowAsync<SqlException>();
        failure.Which.Number.Should().Be(51002);

        (await database.ExecuteScalarAsync<int>($$"""
            SELECT COUNT(*) FROM [StockDocumentInputInvoiceMap]
             WHERE [StockDocumentId] = {{receiptId}}
               AND [InputInvoiceHeadId] = {{invoiceId}}
               AND [IsDeleted] = 0;
            """)).Should().Be(relationBefore);
        (await database.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM [__EFMigrationsHistory];"))
            .Should().Be(historyBefore);
        (await database.ExecuteScalarAsync<int>($$"""
            SELECT COUNT(*) FROM [__EFMigrationsHistory]
             WHERE [MigrationId] = N'{{BuyerOwnerMigrationId}}';
            """)).Should().Be(0);
        (await database.ExecuteScalarAsync<int>("""
            SELECT COUNT(*) FROM sys.columns
             WHERE object_id = OBJECT_ID(N'[dbo].[InputInvoiceHead]')
               AND name IN
                   (N'NormalizedBuyerTaxCode', N'BuyerOwnerResolutionStatus',
                    N'ResolvedBuyerLegalEntityId', N'BuyerOwnerResolutionUpdatedAtUtc');
            """)).Should().Be(0);
        (await database.ExecuteScalarAsync<int>("""
            SELECT COUNT(*) FROM sys.columns
             WHERE object_id = OBJECT_ID(N'[dbo].[StockDocument]')
               AND name = N'ConfirmedLegalEntityId';
            """)).Should().Be(0);
        (await database.ExecuteScalarAsync<int>("""
            SELECT COUNT(*) FROM sys.indexes
             WHERE name IN
                 (N'IX_LegalEntities_StoreId_NormalizedTaxCode_State',
                  N'IX_InputInvoiceHead_StoreId_NormalizedBuyerTaxCode',
                  N'IX_InputInvoiceHead_StoreId_ResolvedBuyerLegalEntityId',
                  N'IX_StockDocument_StoreId_ConfirmedLegalEntityId');
            """)).Should().Be(0);
    }

    private static string Read(string relativePath)
        => File.ReadAllText(Path.Combine(FindRepositoryRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GaoApp.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }
}
