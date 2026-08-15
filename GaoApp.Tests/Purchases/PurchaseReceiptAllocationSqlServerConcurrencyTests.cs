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
public sealed class PurchaseReceiptAllocationSqlServerConcurrencyTests
{
    [Fact]
    public async Task Aggregation_includes_active_workflow_states_and_excludes_terminal_or_deleted_rows()
    {
        await using var database = new InventoryPostingLocalDb();
        await database.MigrateAsync();
        var seed = await database.SeedInventoryCatalogAsync();
        var (orderId, lineIds) = await SeedOrderAsync(database, seed, 1);
        await using var db = CreateContext(database, seed.StoreId);
        var receipts = new List<(StockDocument Receipt, bool DeleteDocument, bool DeleteLine)>();
        foreach (var item in new[]
        {
            (StockDocumentStatus.Draft, false, false),
            (StockDocumentStatus.PendingApproval, false, false),
            (StockDocumentStatus.Rejected, false, false),
            (StockDocumentStatus.Confirmed, false, false),
            (StockDocumentStatus.Cancelled, false, false),
            (StockDocumentStatus.Draft, true, false),
            (StockDocumentStatus.Draft, false, true)
        })
        {
            var receipt = new StockDocument
            {
                StoreId = seed.StoreId, DocumentNo = $"NK-{Guid.NewGuid():N}"[..20],
                Type = StockDocumentType.Receipt, Status = item.Item1,
                ReceiptSource = PurchaseReceiptSource.PurchaseOrder, PurchaseOrderId = orderId,
                WarehouseId = seed.WarehouseId, DocumentDate = DateTime.UtcNow,
                IsDeleted = item.Item2
            };
            receipt.Lines.Add(new StockDocumentLine
            {
                LineNo = 1, ProductVariantId = seed.ProductVariantId,
                PurchaseOrderLineId = lineIds[0], Quantity = 1m, BaseQuantity = 1m,
                Factor = 1m, ProductNameSnapshot = "R2 Product", IsDeleted = item.Item3
            });
            db.StockDocuments.Add(receipt);
            receipts.Add((receipt, item.Item2, item.Item3));
        }
        await db.SaveChangesAsync();
        foreach (var item in receipts)
        {
            if (item.DeleteDocument) item.Receipt.IsDeleted = true;
            if (item.DeleteLine) item.Receipt.Lines.Single().IsDeleted = true;
        }
        await db.SaveChangesAsync();
        var quantities = await new StockDocumentRepository(db)
            .GetInFlightPurchaseReceiptQuantitiesAsync(seed.StoreId, orderId, lineIds);
        quantities[lineIds[0]].Should().Be(3m);
    }

    [Fact]
    public async Task Concurrent_seven_of_ten_allocations_allow_exactly_one_receipt()
    {
        await using var database = new InventoryPostingLocalDb();
        await database.MigrateAsync();
        var seed = await database.SeedInventoryCatalogAsync();
        var (orderId, lineIds) = await SeedOrderAsync(database, seed, 1);
        var firstLocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<bool> AllocateAsync(bool first)
        {
            await using var db = CreateContext(database, seed.StoreId);
            var repository = new StockDocumentRepository(db);
            if (!first) await firstLocked.Task;
            await repository.BeginTransactionAsync();
            try
            {
                var locked = await repository.LockPurchaseOrderLinesAsync(seed.StoreId, orderId, lineIds);
                if (first)
                {
                    firstLocked.SetResult();
                    await Task.Delay(400);
                }
                var held = await repository.GetInFlightPurchaseReceiptQuantitiesAsync(
                    seed.StoreId, orderId, lineIds);
                var state = locked[lineIds[0]];
                var projection = PurchaseReceiptQuantityProjection.Create(
                    state.OrderedQuantity, state.ReceivedQuantity, state.ShortClosedQuantity,
                    held.GetValueOrDefault(lineIds[0]));
                if (projection.AvailableToAllocateQuantity < 7m)
                {
                    await repository.RollbackTransactionAsync();
                    return false;
                }
                var receipt = new StockDocument
                {
                    StoreId = seed.StoreId, DocumentNo = $"NK-{Guid.NewGuid():N}"[..20],
                    Type = StockDocumentType.Receipt, Status = StockDocumentStatus.Draft,
                    ReceiptSource = PurchaseReceiptSource.PurchaseOrder,
                    PurchaseOrderId = orderId, WarehouseId = seed.WarehouseId,
                    DocumentDate = DateTime.UtcNow
                };
                receipt.Lines.Add(new StockDocumentLine
                {
                    LineNo = 1, ProductVariantId = seed.ProductVariantId,
                    PurchaseOrderLineId = lineIds[0], Quantity = 7m, BaseQuantity = 7m,
                    Factor = 1m, ProductNameSnapshot = "R2 Product"
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
        (await verify.StockDocuments.CountAsync(x => x.PurchaseOrderId == orderId)).Should().Be(1);
        (await verify.StockDocumentLines.Where(x => x.PurchaseOrderLineId == lineIds[0]).SumAsync(x => x.Quantity))
            .Should().Be(7m);
        (await verify.PurchaseReceiptAuditEvents.CountAsync(x =>
            x.EventType == PurchaseReceiptAuditEventType.ReceiptCreated)).Should().Be(1);
    }

    [Fact]
    public async Task Inverse_multi_line_requests_complete_without_deadlock()
    {
        await using var database = new InventoryPostingLocalDb();
        await database.MigrateAsync();
        var seed = await database.SeedInventoryCatalogAsync();
        var (orderId, ids) = await SeedOrderAsync(database, seed, 2);
        async Task LockAsync(int[] requested)
        {
            await using var db = CreateContext(database, seed.StoreId);
            var repository = new StockDocumentRepository(db);
            await repository.BeginTransactionAsync();
            await repository.LockPurchaseOrderLinesAsync(seed.StoreId, orderId, requested);
            await Task.Delay(100);
            await repository.CommitTransactionAsync();
        }
        await Task.WhenAll(LockAsync(ids), LockAsync(ids.Reverse().ToArray()))
            .WaitAsync(TimeSpan.FromSeconds(15));
    }

    private static async Task<(int OrderId, int[] LineIds)> SeedOrderAsync(
        InventoryPostingLocalDb database, InventoryPostingSeed seed, int lineCount)
    {
        await using var db = CreateContext(database, seed.StoreId);
        var warehouse = await db.Warehouses.SingleAsync(x => x.Id == seed.WarehouseId);
        var supplierId = await db.Suppliers.Select(x => x.Id).SingleAsync();
        var order = new PurchaseOrder
        {
            StoreId = seed.StoreId, OrderNumber = $"PO-{Guid.NewGuid():N}"[..20],
            SupplierId = supplierId, ExpectedWarehouseId = seed.WarehouseId,
            LegalEntityId = warehouse.LegalEntityId, Status = PurchaseOrderStatus.Approved
        };
        for (var i = 1; i <= lineCount; i++) order.Lines.Add(new PurchaseOrderLine
        {
            StoreId = seed.StoreId, LineNo = i, ProductVariantId = seed.ProductVariantId,
            ProductNameSnapshot = $"Product {i}", UnitNameSnapshot = "Unit",
            ConversionFactor = 1m, OrderedQuantity = 10m
        });
        db.PurchaseOrders.Add(order);
        await db.SaveChangesAsync();
        return (order.Id, order.Lines.Select(x => x.Id).ToArray());
    }

    private static AppDbContext CreateContext(InventoryPostingLocalDb database, int storeId)
        => new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(database.ConnectionString).Options,
            new TenantStub(storeId), new UserStub());
    private sealed class TenantStub(int id) : ITenantContext
    { public int? StoreId => id; public bool IsHostAdmin => false; public string? Subdomain => "allocation"; }
    private sealed class UserStub : ICurrentUser
    {
        public int? UserId => 501; public string? UserName => "allocation";
        public int? TerminalId => null; public string? TerminalCode => null; public bool IsAuthenticated => true;
    }
}
