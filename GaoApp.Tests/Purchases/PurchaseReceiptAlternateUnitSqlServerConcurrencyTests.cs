using FluentAssertions;
using GaoApp.Application.Common;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Services.Purchases;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Repositories.Inventory;
using GaoApp.Tests.Configuration;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Purchases;

[Collection("R1FinalDatabasePreflight")]
public sealed class PurchaseReceiptAlternateUnitSqlServerConcurrencyTests
{
    [Fact]
    public async Task Parent_po_lock_serializes_status_change_and_returns_current_status()
    {
        await using var database = new InventoryPostingLocalDb();
        await database.MigrateAsync();
        var seed = await database.SeedInventoryCatalogAsync();
        var (orderId, _) = await SeedOrderAsync(database, seed);

        await using var lockingDb = CreateContext(database, seed.StoreId);
        var lockingRepository = new StockDocumentRepository(lockingDb);
        await lockingRepository.BeginTransactionAsync();
        var initial = await lockingRepository.LockPurchaseOrderForReceiptAsync(seed.StoreId, orderId);
        initial.Should().NotBeNull();
        initial!.Status.Should().Be(PurchaseOrderStatus.Approved);

        var statusUpdate = Task.Run(async () =>
        {
            await using var competingDb = CreateContext(database, seed.StoreId);
            return await competingDb.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE [PurchaseOrders] SET [Status] = {(int)PurchaseOrderStatus.Cancelled} WHERE [Id] = {orderId}");
        });
        await Task.Delay(200);
        statusUpdate.IsCompleted.Should().BeFalse();

        await lockingRepository.CommitTransactionAsync();
        (await statusUpdate).Should().Be(1);

        await using var verifyDb = CreateContext(database, seed.StoreId);
        var verifyRepository = new StockDocumentRepository(verifyDb);
        await verifyRepository.BeginTransactionAsync();
        var current = await verifyRepository.LockPurchaseOrderForReceiptAsync(seed.StoreId, orderId);
        current.Should().NotBeNull();
        current!.Status.Should().Be(PurchaseOrderStatus.Cancelled);
        await verifyRepository.RollbackTransactionAsync();
    }

    [Fact]
    public async Task Concurrent_different_units_compete_for_one_canonical_pool()
    {
        await using var database = new InventoryPostingLocalDb();
        await database.MigrateAsync();
        var seed = await database.SeedInventoryCatalogAsync();
        var (orderId, lineId) = await SeedOrderAsync(database, seed);
        var firstLocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<bool> AllocateAsync(bool boxes)
        {
            await using var db = CreateContext(database, seed.StoreId);
            var repository = new StockDocumentRepository(db);
            if (!boxes) await firstLocked.Task;
            await repository.BeginTransactionAsync();
            try
            {
                var locked = await repository.LockPurchaseOrderLinesAsync(
                    seed.StoreId, orderId, new[] { lineId });
                if (boxes)
                {
                    firstLocked.SetResult();
                    await Task.Delay(400);
                }

                var held = await repository.GetInFlightPurchaseReceiptQuantitiesAsync(
                    seed.StoreId, orderId, new[] { lineId });
                var state = locked[lineId];
                var orderedCanonical = PurchaseReceiptQuantityConversionPolicy.ToCanonical(
                    state.OrderedQuantity, state.ConversionFactor);
                var projection = PurchaseReceiptQuantityProjection.Create(
                    orderedCanonical, 0m, 0m, held.GetValueOrDefault(lineId));

                var receiptQuantity = boxes ? 7m : 84m;
                var receiptFactor = boxes ? 12m : 1m;
                var canonical = PurchaseReceiptQuantityConversionPolicy.ToCanonical(
                    receiptQuantity, receiptFactor);
                if (canonical > projection.AvailableToAllocateQuantity)
                {
                    await repository.RollbackTransactionAsync();
                    return false;
                }

                var receipt = new StockDocument
                {
                    StoreId = seed.StoreId,
                    DocumentNo = $"NK-{Guid.NewGuid():N}"[..20],
                    Type = StockDocumentType.Receipt,
                    Status = StockDocumentStatus.Draft,
                    ReceiptSource = PurchaseReceiptSource.PurchaseOrder,
                    PurchaseOrderId = orderId,
                    WarehouseId = seed.WarehouseId,
                    DocumentDate = DateTime.UtcNow
                };
                receipt.Lines.Add(new StockDocumentLine
                {
                    LineNo = 1,
                    ProductVariantId = seed.ProductVariantId,
                    PurchaseOrderLineId = lineId,
                    Quantity = receiptQuantity,
                    Factor = receiptFactor,
                    BaseQuantity = canonical,
                    ProductNameSnapshot = "Alternate unit product"
                });
                PurchaseReceiptAuditEvidence.MarkWorkflowEvent(
                    receipt, PurchaseReceiptAuditEventType.ReceiptCreated);
                await repository.AddAsync(receipt);
                await repository.SaveChangesAsync();
                await repository.CommitTransactionAsync();
                return true;
            }
            catch
            {
                await repository.RollbackTransactionAsync();
                throw;
            }
        }

        var results = await Task.WhenAll(AllocateAsync(true), AllocateAsync(false));
        results.Count(x => x).Should().Be(1);

        await using var verify = CreateContext(database, seed.StoreId);
        var lines = await verify.StockDocumentLines
            .Where(x => x.PurchaseOrderLineId == lineId)
            .ToListAsync();
        lines.Should().ContainSingle();
        lines.Sum(x => x.BaseQuantity).Should().Be(84m);
        (await verify.PurchaseReceiptAuditEvents.CountAsync(x =>
            x.EventType == PurchaseReceiptAuditEventType.ReceiptCreated)).Should().Be(1);
    }

    private static async Task<(int OrderId, int LineId)> SeedOrderAsync(
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
            Status = PurchaseOrderStatus.Approved
        };
        order.Lines.Add(new PurchaseOrderLine
        {
            StoreId = seed.StoreId,
            LineNo = 1,
            ProductVariantId = seed.ProductVariantId,
            ProductNameSnapshot = "Alternate unit product",
            UnitNameSnapshot = "Box",
            ConversionFactor = 12m,
            OrderedQuantity = 10m
        });
        db.PurchaseOrders.Add(order);
        await db.SaveChangesAsync();
        return (order.Id, order.Lines.Single().Id);
    }

    private static AppDbContext CreateContext(InventoryPostingLocalDb database, int storeId)
        => new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(database.ConnectionString).Options,
            new TenantStub(storeId), new UserStub());

    private sealed class TenantStub(int id) : ITenantContext
    {
        public int? StoreId => id;
        public bool IsHostAdmin => false;
        public string? Subdomain => "alternate-unit";
    }

    private sealed class UserStub : ICurrentUser
    {
        public int? UserId => 502;
        public string? UserName => "alternate-unit";
        public int? TerminalId => null;
        public string? TerminalCode => null;
        public bool IsAuthenticated => true;
    }
}
