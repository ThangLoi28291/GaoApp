using FluentAssertions;
using GaoApp.Application.Common;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Repositories.Inventory;
using GaoApp.Tests.Configuration;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Purchases;

[Collection("R1FinalDatabasePreflight")]
public sealed class PurchaseReceiptPriceVarianceSqlServerConcurrencyTests
{
    [Fact]
    public async Task Different_receipt_confirmation_is_observed_after_canonical_variant_lock()
    {
        await using var database = new InventoryPostingLocalDb();
        await database.MigrateAsync();
        var seed = await database.SeedInventoryCatalogAsync();
        var competitorId = await SeedReceiptsAsync(database, seed);

        await using (var earlyDb = CreateContext(database, seed.StoreId))
        {
            var earlyPrices = await new StockDocumentRepository(earlyDb)
                .GetLastPurchaseBaseUnitPricesBeforeVatAsync([seed.ProductVariantId]);
            earlyPrices[seed.ProductVariantId].Should().Be(100m);
        }

        await using var competitorDb = CreateContext(database, seed.StoreId);
        var competitorRepository = new StockDocumentRepository(competitorDb);
        await competitorRepository.BeginTransactionAsync();
        Task<Dictionary<int, decimal>>? revalidation = null;
        try
        {
            (await competitorRepository.LockPurchasePriceHistoryVariantsAsync(
                seed.StoreId,
                [seed.ProductVariantId])).Should().BeTrue();
            await competitorDb.Database.ExecuteSqlInterpolatedAsync(
                $@"UPDATE [StockDocument]
                   SET [Status] = {(int)StockDocumentStatus.Confirmed},
                       [ApprovedAtUtc] = {new DateTime(2026, 8, 17, 12, 0, 0, DateTimeKind.Utc)}
                   WHERE [Id] = {competitorId}");

            revalidation = Task.Run(async () =>
            {
                await using var db = CreateContext(database, seed.StoreId);
                var repository = new StockDocumentRepository(db);
                await repository.BeginTransactionAsync();
                try
                {
                    (await repository.LockPurchasePriceHistoryVariantsAsync(
                        seed.StoreId,
                        [seed.ProductVariantId])).Should().BeTrue();
                    return await repository.GetLastPurchaseBaseUnitPricesBeforeVatAsync(
                        [seed.ProductVariantId]);
                }
                finally
                {
                    await repository.RollbackTransactionAsync();
                }
            });

            await Task.Delay(250);
            revalidation.IsCompleted.Should().BeFalse(
                "the competing confirmation owns the canonical Store/ProductVariant lock");

            await competitorRepository.CommitTransactionAsync();
            var lockedPrices = await revalidation.WaitAsync(TimeSpan.FromSeconds(15));
            lockedPrices[seed.ProductVariantId].Should().Be(110m,
                "locked revalidation must observe the receipt committed while the browser snapshot was stale");
        }
        finally
        {
            await competitorRepository.RollbackTransactionAsync();
            if (revalidation is { IsCompleted: false })
            {
                await revalidation.WaitAsync(TimeSpan.FromSeconds(15));
            }
        }
    }

    private static async Task<int> SeedReceiptsAsync(
        InventoryPostingLocalDb database,
        InventoryPostingSeed seed)
    {
        await using var db = CreateContext(database, seed.StoreId);
        var historical = NewReceipt(
            seed, "NK-PV-HISTORY", 100m, StockDocumentStatus.Confirmed);
        historical.ApprovedAtUtc = new DateTime(
            2026, 8, 16, 12, 0, 0, DateTimeKind.Utc);
        var competitor = NewReceipt(
            seed, "NK-PV-COMPETE", 110m, StockDocumentStatus.PendingApproval);
        var current = NewReceipt(
            seed, "NK-PV-CURRENT", 120m, StockDocumentStatus.PendingApproval);
        db.StockDocuments.AddRange(historical, competitor, current);
        await db.SaveChangesAsync();
        return competitor.Id;
    }

    private static StockDocument NewReceipt(
        InventoryPostingSeed seed,
        string documentNo,
        decimal unitPrice,
        StockDocumentStatus status)
    {
        var receipt = new StockDocument
        {
            StoreId = seed.StoreId,
            DocumentNo = documentNo,
            Type = StockDocumentType.Receipt,
            Status = status,
            ReceiptSource = PurchaseReceiptSource.Direct,
            DirectReceiptReason = "Price variance concurrency",
            WarehouseId = seed.WarehouseId,
            DocumentDate = new DateTime(2026, 8, 17)
        };
        receipt.Lines.Add(new StockDocumentLine
        {
            LineNo = 1,
            ProductVariantId = seed.ProductVariantId,
            ProductNameSnapshot = "Price history product",
            UnitNameSnapshot = "Unit",
            Quantity = 1m,
            Factor = 1m,
            BaseQuantity = 1m,
            UnitPriceBeforeVat = unitPrice,
            UnitPriceAfterVat = unitPrice,
            UnitCost = unitPrice,
            LineTotal = unitPrice
        });
        return receipt;
    }

    private static AppDbContext CreateContext(
        InventoryPostingLocalDb database,
        int storeId)
        => new(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlServer(database.ConnectionString)
                .Options,
            new TenantStub(storeId),
            new UserStub());

    private sealed class TenantStub(int storeId) : ITenantContext
    {
        public int? StoreId => storeId;
        public bool IsHostAdmin => false;
        public string? Subdomain => "price-variance";
    }

    private sealed class UserStub : ICurrentUser
    {
        public int? UserId => 504;
        public string? UserName => "price-variance";
        public int? TerminalId => null;
        public string? TerminalCode => null;
        public bool IsAuthenticated => true;
    }
}
