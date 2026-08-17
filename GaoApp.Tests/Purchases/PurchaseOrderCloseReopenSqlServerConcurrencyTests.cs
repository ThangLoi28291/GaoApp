using FluentAssertions;
using GaoApp.Application.Common;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Repositories.Inventory;
using GaoApp.Infrastructure.Repositories.Purchases;
using GaoApp.Tests.Configuration;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Purchases;

[Collection("R1FinalDatabasePreflight")]
public sealed class PurchaseOrderCloseReopenSqlServerConcurrencyTests
{
    [Fact]
    public async Task Close_parent_lock_serializes_receipt_creation_and_exposes_closed_status()
    {
        await using var database = new InventoryPostingLocalDb();
        await database.MigrateAsync();
        var seed = await database.SeedInventoryCatalogAsync();
        var orderId = await SeedOrderAsync(database, seed);

        await using var closeDb = CreateContext(database, seed.StoreId);
        await using var closeTransaction = await closeDb.Database.BeginTransactionAsync();
        var closeRepository = new PurchaseOrderRepository(closeDb);
        (await closeRepository.LockForOutstandingManagementAsync(seed.StoreId, orderId))
            .Should().BeTrue();

        var receiptAttempt = Task.Run(async () =>
        {
            await using var receiptDb = CreateContext(database, seed.StoreId);
            var receiptRepository = new StockDocumentRepository(receiptDb);
            await receiptRepository.BeginTransactionAsync();
            try
            {
                return await receiptRepository.LockPurchaseOrderForReceiptAsync(
                    seed.StoreId, orderId);
            }
            finally
            {
                await receiptRepository.RollbackTransactionAsync();
            }
        });
        await Task.Delay(200);
        receiptAttempt.IsCompleted.Should().BeFalse();

        await closeDb.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE [PurchaseOrders] SET [Status] = {(int)PurchaseOrderStatus.ShortClosed} WHERE [Id] = {orderId}");
        await closeTransaction.CommitAsync();

        var current = await receiptAttempt;
        current.Should().NotBeNull();
        current!.Status.Should().Be(PurchaseOrderStatus.ShortClosed);
    }

    private static async Task<int> SeedOrderAsync(
        InventoryPostingLocalDb database,
        InventoryPostingSeed seed)
    {
        await using var db = CreateContext(database, seed.StoreId);
        var warehouse = await db.Warehouses.SingleAsync(x => x.Id == seed.WarehouseId);
        var supplierId = await db.Suppliers.Select(x => x.Id).SingleAsync();
        var order = new PurchaseOrder
        {
            StoreId = seed.StoreId,
            OrderNumber = $"PO-{Guid.NewGuid():N}"[..20],
            SupplierId = supplierId,
            ExpectedWarehouseId = seed.WarehouseId,
            LegalEntityId = warehouse.LegalEntityId,
            Status = PurchaseOrderStatus.PartiallyReceived
        };
        order.Lines.Add(new PurchaseOrderLine
        {
            StoreId = seed.StoreId,
            LineNo = 1,
            ProductVariantId = seed.ProductVariantId,
            ProductNameSnapshot = "Close concurrency product",
            UnitNameSnapshot = "Unit",
            ConversionFactor = 1m,
            OrderedQuantity = 10m,
            ReceivedQuantity = 4m,
            ReceiptStatus = PurchaseOrderLineReceiptStatus.PartiallyReceived
        });
        db.PurchaseOrders.Add(order);
        await db.SaveChangesAsync();
        return order.Id;
    }

    private static AppDbContext CreateContext(InventoryPostingLocalDb database, int storeId)
        => new(new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlServer(database.ConnectionString).Options,
            new TenantStub(storeId), new UserStub());

    private sealed class TenantStub(int storeId) : ITenantContext
    {
        public int? StoreId => storeId;
        public bool IsHostAdmin => false;
        public string? Subdomain => "close-reopen";
    }

    private sealed class UserStub : ICurrentUser
    {
        public int? UserId => 503;
        public string? UserName => "close-reopen";
        public int? TerminalId => null;
        public string? TerminalCode => null;
        public bool IsAuthenticated => true;
    }
}
