using FluentAssertions;
using GaoApp.Application.DTOs.Returns;
using GaoApp.Application.Services.Inventory;
using GaoApp.Application.Services.Orders;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Data.Repositories.Orders;
using GaoApp.Infrastructure.Repositories.Inventory;
using GaoApp.Infrastructure.Repositories.Orders;
using GaoApp.Infrastructure.Repositories.Rewards;
using GaoApp.Tests.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using static GaoApp.Tests.Inventory.InventoryPosPostingContractTests;

namespace GaoApp.Tests.Inventory;

[Collection("R1FinalDatabasePreflight")]
public sealed class SaleCostReversalIntegrationTests
{
    [Theory]
    [InlineData("actual", 10)]
    [InlineData("auto", 12)]
    [InlineData("auto", 8)]
    [InlineData("dedicated", 12)]
    [InlineData("dedicated", 8)]
    [InlineData("equal", 10)]
    [InlineData("mixed", 12)]
    [InlineData("multiple", 12.6)]
    public async Task Legacy_completed_void_closes_persisted_cost_and_preserves_original_audit(string writer, decimal unit)
    {
        await using var fixture = await CostFixture.CreateAsync(writer, unit);
        await using var db = fixture.Database.CreateTenantContext(fixture.Seed.StoreId);
        var original = await db.InventoryValuationEntries.AsNoTracking()
            .Where(x => x.ReferenceType == InventoryReferenceType.Order &&
                x.ReferenceId == fixture.Seed.OrderId.ToString()).ToListAsync();
        var receipt = await fixture.Pos(db).VoidCompletedOrderAsync(fixture.Seed.OrderId, "RPT2 cost closure");
        receipt.Status.Should().Be(nameof(OrderStatus.Voided));
        await fixture.AssertCostAsync(db, 0);
        var expectedMirror = writer == "mixed" ? 112 : writer == "multiple" ? 126 : 10 * unit;
        (await db.InventoryValuationEntries.Where(x => x.EntryType == InventoryValuationEntryType.Inbound &&
                x.InventoryTransaction.TransactionType == InventoryTransactionType.SaleVoidIn)
            .SumAsync(x => x.Amount)).Should().Be(expectedMirror);
        (await db.POSAuditLogs.CountAsync(x => x.OrderId == fixture.Seed.OrderId && x.Action == "ORDER_VOIDED"))
            .Should().Be(1);
        foreach (var row in original)
        {
            var retained = await db.InventoryValuationEntries.AsNoTracking().SingleAsync(x => x.Id == row.Id);
            (retained.Amount, retained.Quantity, retained.RevaluationOfEntryId)
                .Should().Be((row.Amount, row.Quantity, row.RevaluationOfEntryId));
        }
        // Later receipt cannot reopen the already-resolved, fully voided sale.
        await fixture.ReceiveAsync(db, 2, 17, "AFTER-VOID");
        await fixture.AssertCostAsync(db, 0);
    }

    [Theory]
    [InlineData("actual", 10)]
    [InlineData("auto", 12)]
    [InlineData("auto", 8)]
    [InlineData("dedicated", 12)]
    [InlineData("dedicated", 8)]
    public async Task Partial_and_multiple_restock_use_real_normalized_cost_and_prevent_over_return(string writer, decimal unit)
    {
        await using var fixture = await CostFixture.CreateAsync(writer, unit);
        await using var db = fixture.Database.CreateTenantContext(fixture.Seed.StoreId);
        foreach (var quantity in new[] { 2m, 3m, 5m })
        {
            // Existing return numbers have second precision. Separate operator
            // commands so this cost regression does not test that unrelated generator.
            await Task.Delay(1100);
            var result = await fixture.Returns(db).CreateAsync(fixture.ReturnRequest(quantity));
            result.Lines.Should().ContainSingle();
            var persisted = await db.SalesReturnLines.AsNoTracking().SingleAsync(x => x.Id == result.Lines[0].Id);
            persisted.LineCostTotal.Should().Be(quantity * unit);
        }
        await fixture.AssertCostAsync(db, 0);
        var before = await db.InventoryTransactions.CountAsync();
        Func<Task> overReturn = () => fixture.Returns(db).CreateAsync(fixture.ReturnRequest(1));
        await overReturn.Should().ThrowAsync<Exception>();
        (await db.InventoryTransactions.CountAsync()).Should().Be(before);
        await fixture.AssertCostAsync(db, 0);
    }

    [Fact]
    public async Task NoRestock_keeps_cost_and_completed_return_prevents_void()
    {
        await using var fixture = await CostFixture.CreateAsync("auto", 12);
        await using var db = fixture.Database.CreateTenantContext(fixture.Seed.StoreId);
        var before = await db.InventoryTransactions.CountAsync();
        await fixture.Returns(db).CreateAsync(fixture.ReturnRequest(2, SalesReturnLineAction.NoRestock));
        (await db.InventoryTransactions.CountAsync()).Should().Be(before);
        await fixture.AssertCostAsync(db, 120);
        Func<Task> voidAfterReturn = () => fixture.Pos(db).VoidCompletedOrderAsync(fixture.Seed.OrderId, "must reject");
        await voidAfterReturn.Should().ThrowAsync<Exception>();
        await fixture.AssertCostAsync(db, 120);
        await Task.Delay(1100);
        await fixture.Returns(db).CreateAsync(fixture.ReturnRequest(2));
        await fixture.AssertCostAsync(db, 96);
    }

    [Theory]
    [InlineData("open")]
    [InlineData("mixed-open")]
    public async Task Provisional_NoRestock_still_works_while_inventory_reversal_fails_closed(string writer)
    {
        await using var fixture = await CostFixture.CreateAsync(writer, 10);
        await using var db = fixture.Database.CreateTenantContext(fixture.Seed.StoreId);
        var before = await db.InventoryTransactions.CountAsync();
        Func<Task> restock = () => fixture.Returns(db).CreateAsync(fixture.ReturnRequest(2));
        await restock.Should().ThrowAsync<InvalidOperationException>();
        (await db.InventoryTransactions.CountAsync()).Should().Be(before);
        // Discard tracked failed-request objects by using a fresh caller context.
        await using var noRestockDb = fixture.Database.CreateTenantContext(fixture.Seed.StoreId);
        await fixture.Returns(noRestockDb).CreateAsync(fixture.ReturnRequest(2, SalesReturnLineAction.NoRestock));
        (await noRestockDb.InventoryTransactions.CountAsync()).Should().Be(before);
    }

    [Theory]
    [InlineData("open")]
    [InlineData("partial")]
    [InlineData("mixed-open")]
    public async Task Unsupported_provisional_void_does_not_post_or_mark_order_voided(string writer)
    {
        await using var fixture = await CostFixture.CreateAsync(writer, 10);
        await using var db = fixture.Database.CreateTenantContext(fixture.Seed.StoreId);
        var before = await db.InventoryTransactions.CountAsync();
        Func<Task> attempt = () => fixture.Pos(db).VoidCompletedOrderAsync(fixture.Seed.OrderId, "unresolved guard");
        await attempt.Should().ThrowAsync<InvalidOperationException>();
        await using var verify = fixture.Database.CreateTenantContext(fixture.Seed.StoreId);
        (await verify.InventoryTransactions.CountAsync()).Should().Be(before);
        (await verify.Orders.SingleAsync(x => x.Id == fixture.Seed.OrderId)).Status.Should().Be(OrderStatus.Completed);
    }

    [Fact]
    public async Task Historical_return_before_resolution_does_not_enable_an_estimated_second_return()
    {
        await using var fixture = await CostFixture.CreateAsync("open", 10);
        await using var db = fixture.Database.CreateTenantContext(fixture.Seed.StoreId);
        var repo = new InventoryValuationEntryRepository(db);
        var source = (await repo.GetSaleIssueEntriesByOrderLineAsync(fixture.Seed.OrderId, fixture.Seed.LegacyOrderLineId)).Single();
        // Reproduce an old accepted return via the actual movement engine; current
        // guarded SalesReturnService must not create this unresolved history anew.
        await CreateRealMovementService(db).CreateAsync(new InventoryMovementFactory().CreateSaleRefund(
            source.WarehouseId, source.ProductVariantId, 800, 801, 2, 10, "historical first return",
            DateTime.UtcNow, "HISTORICAL-RETURN", source.Id, source.ReferenceSubKey));
        await fixture.ReceiveAsync(db, 8, 12, "FINAL-AFTER-HISTORICAL-RETURN");
        var before = await db.InventoryTransactions.CountAsync();
        Func<Task> attempt = () => fixture.Returns(db).CreateAsync(fixture.ReturnRequest(2));
        await attempt.Should().ThrowAsync<InvalidOperationException>();
        (await db.InventoryTransactions.CountAsync()).Should().Be(before);
        source = (await repo.GetSaleIssueEntriesByOrderLineAsync(fixture.Seed.OrderId, fixture.Seed.LegacyOrderLineId)).Single();
        SaleValuationCostPolicy.Evaluate(source, await repo.GetRevaluationEntriesBySourceIdAsync(source.Id),
            await repo.GetReverseEntriesBySourceEntryIdAsync(source.Id)).State
            .Should().Be(SaleValuationCostPolicy.Quality.Unavailable);
    }

    [Fact]
    public async Task LegalEntity_void_uses_same_real_normalization_and_preserves_owner()
    {
        await using var fixture = await CostFixture.CreateAsync("dedicated", 12);
        await using var db = fixture.Database.CreateTenantContext(fixture.Seed.StoreId);
        await fixture.AddLegalEntityAllocationAsync(db);
        await fixture.Pos(db).VoidCompletedOrderAsync(fixture.Seed.OrderId, "LE final cost");
        await fixture.AssertCostAsync(db, 0);
        var reversal = await db.OrderLegalEntityAllocationReversals.SingleAsync();
        reversal.LegalEntityId.Should().Be(fixture.Seed.LegalEntityId);
        reversal.WarehouseId.Should().Be(fixture.Seed.LegacyWarehouseId);
        reversal.BaseQuantity.Should().Be(10);
    }

    [Fact]
    public async Task Mixed_actual_and_finalized_fragments_return_newest_first_without_averaging()
    {
        await using var fixture = await CostFixture.CreateAsync("mixed", 12);
        await using var db = fixture.Database.CreateTenantContext(fixture.Seed.StoreId);
        var result = await fixture.Returns(db).CreateAsync(fixture.ReturnRequest(7));
        var returnLineId = result.Lines.Single().Id;
        var inbound = await db.InventoryValuationEntries.Where(x =>
            x.ReferenceType == InventoryReferenceType.Refund && x.ReferenceLineId == returnLineId &&
            x.EntryType == InventoryValuationEntryType.Inbound).OrderBy(x => x.Id).ToListAsync();
        inbound.Select(x => (x.Quantity, x.UnitCost)).Should().Equal((6m, 12m), (1m, 10m));
        await fixture.AssertCostAsync(db, 30);
    }

    [Fact]
    public async Task Full_void_closes_each_of_two_real_LegalEntities_and_warehouses()
    {
        await using var fixture = await CostFixture.CreateAsync("auto", 12);
        await using var db = fixture.Database.CreateTenantContext(fixture.Seed.StoreId);
        await fixture.AddLegalEntityAllocationAsync(db);
        var owner = new LegalEntity
        {
            StoreId = fixture.Seed.StoreId, Code = "SECOND-OWNER", Name = "Second owner",
            LegalName = "Second owner", IsActive = true, SalePriority = 2
        };
        db.LegalEntities.Add(owner);
        await db.SaveChangesAsync();
        var warehouse = await db.Warehouses.SingleAsync(x => x.Id == fixture.Seed.LegalEntityWarehouseId);
        warehouse.LegalEntityId = owner.Id;
        var line = await db.OrderLines.IgnoreQueryFilters().SingleAsync(x => x.Id == fixture.Seed.LegalEntityOrderLineId);
        line.IsDeleted = false;
        await db.SaveChangesAsync();
        var movements = CreateRealMovementService(db);
        var factory = new InventoryMovementFactory();
        await movements.CreateAsync(factory.CreatePurchaseReceipt(warehouse.Id, line.VariantId, 1, 7,
            "SECOND-OWNER-RECEIPT", 1, "SECOND-OWNER-RECEIPT", 1));
        var sale = await movements.CreateAsync(factory.CreateSaleFinalize(warehouse.Id, line.VariantId,
            fixture.Seed.OrderId, line.Id, "Second owner line", 1, null));
        db.OrderLegalEntityAllocations.Add(new OrderLegalEntityAllocation
        {
            StoreId = fixture.Seed.StoreId, OrderId = fixture.Seed.OrderId, OrderLineId = line.Id,
            ProductVariantId = line.VariantId, WarehouseId = warehouse.Id, LegalEntityId = owner.Id,
            InventoryTransactionId = sale.InventoryTransactionId, SalePriority = 2,
            Quantity = 1, BaseQuantity = 1, UnitPrice = 20, LineTotal = 20, NetAmount = 20,
            AllocationSource = OrderLegalEntityAllocationSource.AutoBySalePriority
        });
        await db.SaveChangesAsync();
        await fixture.Pos(db).VoidCompletedOrderAsync(fixture.Seed.OrderId, "two legal owners");
        var reversals = await db.OrderLegalEntityAllocationReversals.ToListAsync();
        reversals.Select(x => x.LegalEntityId).Distinct().Should().HaveCount(2);
        (await db.InventoryValuationEntries.Where(x => x.EntryType == InventoryValuationEntryType.Inbound &&
            x.InventoryTransaction.TransactionType == InventoryTransactionType.SaleVoidIn).SumAsync(x => x.Amount))
            .Should().Be(127);
        await fixture.AssertCostAsync(db, 0);
        await new ReturnableValuationFragmentService(new InventoryValuationEntryRepository(db),
            new OrderLegalEntityAllocationReversalRepository(db))
            .ValidateVoidClosureAsync(fixture.Seed.OrderId, line.Id, default);
    }

    internal sealed class CostFixture : IAsyncDisposable
    {
        public InventoryPostingLocalDb Database { get; } = new();
        public RelationalSalesReturnSeed Seed { get; private set; } = null!;

        public static async Task<CostFixture> CreateAsync(string writer, decimal unit)
        {
            var fixture = new CostFixture();
            try
            {
                await fixture.Database.MigrateAsync();
                var catalog = await fixture.Database.SeedInventoryCatalogAsync(allowNegativeInventory: true);
                await using var db = fixture.Database.CreateTenantContext(catalog.StoreId);
                fixture.Seed = await SeedRelationalSalesReturnAsync(db, catalog);
                var order = await db.Orders.Include(x => x.Lines).SingleAsync(x => x.Id == fixture.Seed.OrderId);
                foreach (var other in order.Lines.Where(x => x.Id != fixture.Seed.LegacyOrderLineId)) other.IsDeleted = true;
                var line = order.Lines.Single(x => x.Id == fixture.Seed.LegacyOrderLineId);
                line.Quantity = 10; line.BaseQuantity = 10; line.LineTotal = 200;
                order.Subtotal = 200; order.GrandTotal = 200; order.PaidTotal = 200;
                order.UseMultiLegalEntity = false;
                (await db.Warehouses.SingleAsync(x => x.Id == fixture.Seed.LegacyWarehouseId)).AllowNegativeInventory = true;
                await db.SaveChangesAsync();
                if (writer == "actual") await fixture.ReceiveAsync(db, 10, unit, "ACTUAL");
                if (writer is "mixed" or "mixed-open") await fixture.ReceiveAsync(db, 4, 10, "ACTUAL-PART");
                var factory = new InventoryMovementFactory();
                await CreateRealMovementService(db).CreateAsync(factory.CreateSaleFinalize(
                    fixture.Seed.LegacyWarehouseId, fixture.Seed.LegacyVariantId, fixture.Seed.OrderId,
                    fixture.Seed.LegacyOrderLineId, "RPT2 sale", 10, 10));
                if (writer is "auto" or "equal") await fixture.ReceiveAsync(db, 10, unit, "FINAL");
                if (writer == "mixed") await fixture.ReceiveAsync(db, 6, 12, "FINAL-PART");
                if (writer == "partial") await fixture.ReceiveAsync(db, 4, 12, "PARTIALLY-FINAL");
                if (writer == "multiple")
                {
                    await fixture.ReceiveAsync(db, 4, 12, "FINAL-FIRST");
                    await fixture.ReceiveAsync(db, 6, 13, "FINAL-SECOND");
                }
                if (writer == "dedicated") await fixture.ResolveDedicatedAsync(db, unit);
                return fixture;
            }
            catch { await fixture.DisposeAsync(); throw; }
        }

        public Task ReceiveAsync(AppDbContext db, decimal qty, decimal unit, string identity)
            => CreateRealMovementService(db).CreateAsync(new InventoryMovementFactory().CreatePurchaseReceipt(
                Seed.LegacyWarehouseId, Seed.LegacyVariantId, qty, unit, identity, 1, identity, 1));

        private async Task ResolveDedicatedAsync(AppDbContext db, decimal unit)
        {
            // Arrange an inbound layer at the dedicated resolver's entry point.
            // A normal movement would auto-resolve it first and make this branch a no-op.
            await using var tx = await db.Database.BeginTransactionAsync();
            var movement = CreateRealMovementService(db);
            await movement.PreLockBalancesAsync([new(Seed.StoreId, Seed.LegacyWarehouseId, Seed.LegacyVariantId)]);
            var balance = await db.InventoryBalances.SingleAsync(x => x.WarehouseId == Seed.LegacyWarehouseId);
            balance.OnHandQty += 10;
            balance.InventoryValue += unit * 10;
            var inboundTx = new InventoryTransaction
            {
                StoreId = Seed.StoreId, WarehouseId = Seed.LegacyWarehouseId, ProductVariantId = Seed.LegacyVariantId,
                TransactionType = InventoryTransactionType.PurchaseReceipt, ReferenceType = InventoryReferenceType.PurchaseReceipt,
                ReferenceId = "DEDICATED-RECEIPT", QuantityChange = 10, OccurredAtUtc = DateTime.UtcNow
            };
            db.InventoryTransactions.Add(inboundTx);
            await db.SaveChangesAsync();
            var entry = new InventoryValuationEntry
            {
                StoreId = Seed.StoreId, WarehouseId = Seed.LegacyWarehouseId, ProductVariantId = Seed.LegacyVariantId,
                InventoryTransactionId = inboundTx.Id, EntryType = InventoryValuationEntryType.Inbound,
                ReferenceType = inboundTx.ReferenceType, ReferenceId = inboundTx.ReferenceId,
                Quantity = 10, UnitCost = unit, Amount = 10 * unit, OccurredAtUtc = inboundTx.OccurredAtUtc
            };
            db.InventoryValuationEntries.Add(entry);
            await db.SaveChangesAsync();
            var layer = new InventoryCostLayer
            {
                StoreId = Seed.StoreId, WarehouseId = Seed.LegacyWarehouseId, ProductVariantId = Seed.LegacyVariantId,
                InventoryTransactionId = inboundTx.Id, InventoryValuationEntryId = entry.Id,
                ReferenceType = entry.ReferenceType, ReferenceId = entry.ReferenceId,
                OriginalQuantity = 10, RemainingQuantity = 10, UnitCost = unit, OccurredAtUtc = entry.OccurredAtUtc
            };
            db.InventoryCostLayers.Add(layer);
            await db.SaveChangesAsync();
            entry.InventoryCostLayerId = layer.Id;
            await db.SaveChangesAsync();
            var resolver = new InventoryRevaluationService(new InventoryCostLayerRepository(db),
                new InventoryCostLayerAllocationRepository(db), new InventoryValuationEntryRepository(db),
                new InventoryTransactionRepository(db), new InventoryBalanceRepository(db), movement);
            var plans = await resolver.ResolveByInboundLayerAsync(layer.Id, DateTime.UtcNow, "RPT2 dedicated");
            plans.Should().ContainSingle();
            plans[0].QuantityAbs.Should().Be(10);
            await tx.CommitAsync();
        }

        public async Task AddLegalEntityAllocationAsync(AppDbContext db)
        {
            var source = await db.InventoryValuationEntries.SingleAsync(x => x.EntryType == InventoryValuationEntryType.Outbound);
            db.OrderLegalEntityAllocations.Add(new OrderLegalEntityAllocation
            {
                StoreId = Seed.StoreId, OrderId = Seed.OrderId, OrderLineId = Seed.LegacyOrderLineId,
                ProductVariantId = Seed.LegacyVariantId, WarehouseId = Seed.LegacyWarehouseId,
                LegalEntityId = Seed.LegalEntityId, InventoryTransactionId = source.InventoryTransactionId,
                SalePriority = 1,
                Quantity = 10, BaseQuantity = 10, UnitPrice = 20, LineTotal = 200, NetAmount = 200,
                AllocationSource = OrderLegalEntityAllocationSource.AutoBySalePriority
            });
            await db.SaveChangesAsync();
        }

        private ReturnableValuationFragmentService Reader(AppDbContext db)
            => new(new InventoryValuationEntryRepository(db), new OrderLegalEntityAllocationReversalRepository(db));

        private OrderLegalEntityReversalService Reversals(AppDbContext db)
            => new(new OrderLegalEntityAllocationRepository(db), new OrderLegalEntityAllocationReversalRepository(db),
                Reader(db), new ReturnCostAllocator(), CreateRealMovementService(db), new InventoryMovementFactory());

        public SalesReturnService Returns(AppDbContext db)
            => new(new UnitOfWork(db), new OrderRepository(db), new SalesReturnRepository(db),
                new POSShiftRepository(db), new POSAuditLogRepository(db), new WarehouseRepository(db),
                CreateRealMovementService(db), new InventoryMovementFactory(), new NoOpAuditLogService(),
                new FixedCurrentStore(Seed.StoreId), new FixedCurrentUser(Seed.UserId, Seed.TerminalId), Reader(db),
                new ReturnCostAllocator(), new NoOpRewardLedgerRepository(), new NoOpOrderRewardCalculator(),
                Reversals(db), new NoOpDraftInvoiceReturnSyncService());

        public POSService Pos(AppDbContext db)
        {
            // Exercise the public completed-order workflow; only unrelated UI,
            // promotion and customer collaborators (unused for these fixtures) are omitted.
            var values = new Dictionary<string, object>
            {
                ["uow"] = new AppUnitOfWork(db), ["orders"] = new OrderRepository(db),
                ["shifts"] = new POSShiftRepository(db), ["auditLogs"] = new POSAuditLogRepository(db),
                ["inventoryMovementService"] = CreateRealMovementService(db),
                ["inventoryMovementFactory"] = new InventoryMovementFactory(),
                ["auditLogService"] = new NoOpAuditLogService(), ["salesReturns"] = new SalesReturnRepository(db),
                ["inventoryValuationEntryRepository"] = new InventoryValuationEntryRepository(db),
                ["rewardVoucherRepository"] = new CustomerRewardVoucherRepository(db),
                ["logger"] = NullLogger<POSService>.Instance, ["legalEntityReversalService"] = Reversals(db)
            };
            var constructor = typeof(POSService).GetConstructors().Single();
            return (POSService)constructor.Invoke(constructor.GetParameters()
                .Select(x => values.GetValueOrDefault(x.Name!)).ToArray());
        }

        public CreateSalesReturnRequest ReturnRequest(decimal quantity, SalesReturnLineAction action = SalesReturnLineAction.Restock)
            => new()
            {
                OrderId = Seed.OrderId, Type = SalesReturnType.ReturnOnly, Reason = "RPT2 regression",
                Lines = [new() { OrderLineId = Seed.LegacyOrderLineId, ReturnQuantity = quantity,
                    ReturnBaseQuantity = quantity, RefundUnitAmount = 0, Action = action }]
            };

        public async Task AssertCostAsync(AppDbContext db, decimal expected)
        {
            var repo = new InventoryValuationEntryRepository(db);
            var roots = await repo.GetSaleIssueEntriesByOrderLineAsync(Seed.OrderId, Seed.LegacyOrderLineId);
            roots.Should().NotBeEmpty();
            var total = 0m;
            foreach (var root in roots)
            {
                var cost = SaleValuationCostPolicy.Evaluate(root,
                    await repo.GetRevaluationEntriesBySourceIdAsync(root.Id),
                    await repo.GetReverseEntriesBySourceEntryIdAsync(root.Id));
                cost.State.Should().Be(SaleValuationCostPolicy.Quality.Finalized, cost.Reason);
                cost.ProvisionalExposure.Should().Be(0);
                total += cost.Cost!.Value;
            }
            total.Should().Be(expected);
        }

        public ValueTask DisposeAsync() => Database.DisposeAsync();
    }
}
