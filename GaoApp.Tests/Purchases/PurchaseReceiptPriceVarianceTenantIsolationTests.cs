using FluentAssertions;
using GaoApp.Application.Common;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Repositories.Inventory;
using GaoApp.Tests.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GaoApp.Tests.Purchases;

public sealed class PurchaseReceiptPriceVarianceTenantIsolationTests
{
    [Fact]
    public async Task Last_confirmed_price_excludes_other_stores_and_nonconfirmed_receipts()
    {
        var databaseName = $"price-variance-{Guid.NewGuid():N}";
        var root = new InMemoryDatabaseRoot();
        await using (var storeOne = CreateContext(databaseName, root, 1))
        {
            storeOne.StockDocuments.Add(NewReceipt(1, 31, 10m, StockDocumentStatus.Confirmed, 1));
            storeOne.StockDocuments.Add(NewReceipt(1, 31, 50m, StockDocumentStatus.PendingApproval, 2));
            await storeOne.SaveChangesAsync();
        }
        await using (var storeTwo = CreateContext(databaseName, root, 2))
        {
            storeTwo.StockDocuments.Add(NewReceipt(2, 31, 99m, StockDocumentStatus.Confirmed, 3));
            await storeTwo.SaveChangesAsync();
        }

        await using var verify = CreateContext(databaseName, root, 1);
        var prices = await new StockDocumentRepository(verify)
            .GetLastPurchaseBaseUnitPricesBeforeVatAsync([31]);

        prices.Should().ContainSingle();
        prices[31].Should().Be(10m);
    }

    private static StockDocument NewReceipt(
        int storeId,
        int variantId,
        decimal unitPrice,
        StockDocumentStatus status,
        int suffix)
    {
        var document = new StockDocument
        {
            StoreId = storeId,
            DocumentNo = $"NK-PV-{storeId}-{suffix}",
            Type = StockDocumentType.Receipt,
            Status = status,
            WarehouseId = storeId,
            DocumentDate = new DateTime(2026, 8, suffix, 0, 0, 0, DateTimeKind.Unspecified),
            ApprovedAtUtc = status == StockDocumentStatus.Confirmed
                ? new DateTime(2026, 8, suffix, 0, 0, 0, DateTimeKind.Utc)
                : null,
            ReceiptSource = PurchaseReceiptSource.Direct,
            DirectReceiptReason = "Tenant test"
        };
        document.Lines.Add(new StockDocumentLine
        {
            LineNo = 1,
            ProductVariantId = variantId,
            ProductNameSnapshot = "Gạo",
            UnitNameSnapshot = "kg",
            Factor = 1m,
            Quantity = 1m,
            BaseQuantity = 1m,
            UnitPriceBeforeVat = unitPrice,
            UnitPriceAfterVat = unitPrice,
            UnitCost = unitPrice,
            LineTotal = unitPrice
        });
        return document;
    }

    private static InMemoryAppDbContext CreateContext(
        string databaseName,
        InMemoryDatabaseRoot root,
        int storeId)
    {
        var options = new DbContextOptionsBuilder<InMemoryAppDbContext>()
            .UseInMemoryDatabase(databaseName, root)
            .Options;
        return new InMemoryAppDbContext(
            options,
            new TenantContextStub(storeId),
            new CurrentUserStub());
    }

    private sealed class TenantContextStub(int storeId) : ITenantContext
    {
        public int? StoreId => storeId;
        public bool IsHostAdmin => false;
        public string? Subdomain => $"store-{storeId}";
    }

    private sealed class CurrentUserStub : ICurrentUser
    {
        public int? UserId => 7;
        public string? UserName => "price-variance-test";
        public int? TerminalId => null;
        public string? TerminalCode => null;
        public bool IsAuthenticated => true;
    }
}
