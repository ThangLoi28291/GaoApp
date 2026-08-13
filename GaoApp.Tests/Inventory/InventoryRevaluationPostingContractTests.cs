using FluentAssertions;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.DTOs.Inventory;
using GaoApp.Application.Interfaces.Repositories.Inventory;
using GaoApp.Application.Interfaces.Services.Inventory;
using GaoApp.Application.Services.Inventory;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Repositories.Inventory;
using GaoApp.Infrastructure.Tenant;
using GaoApp.Tests.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Inventory;

public sealed class InventoryRevaluationPostingContractTests
{
    [Fact]
    public async Task Resolve_ShouldPreLockExactBalanceBeforeDirectBalanceAccess()
    {
        await using var context = CreateContext();
        await SeedAsync(context);
        var events = new List<string>();
        var movements = new RecordingMovementService(events);
        var service = CreateService(context, movements, events);

        await service.ResolveByInboundLayerAsync(
            3001,
            new DateTime(2026, 8, 2, 3, 0, 0, DateTimeKind.Utc),
            "C2B2 revaluation");

        movements.PreLockBatches.Should().ContainSingle()
            .Which.Should().Equal(
                new InventoryPostingLockKey(1, 11, 101));
        events.IndexOf("prelock").Should().BeLessThan(
            events.IndexOf("balance:get"));
    }

    [Fact]
    public async Task Resolve_WithoutActiveTransaction_ShouldRejectBeforeBalanceMutation()
    {
        await using var context = CreateContext();
        await SeedAsync(context);
        var events = new List<string>();
        var movements = new RecordingMovementService(events)
        {
            HasActiveTransaction = false
        };
        var service = CreateService(context, movements, events);

        var action = () => service.ResolveByInboundLayerAsync(
            3001,
            DateTime.UtcNow,
            "no transaction");

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*active transaction*");
        events.Should().Equal("prelock-rejected");
        (await context.InventoryBalances.SingleAsync()).InventoryValue
            .Should().Be(-52m);
        (await context.InventoryTransactions.CountAsync()).Should().Be(3);
    }

    [Fact]
    public async Task Resolve_ShouldPreserveAllocationOrderMathAndIdentity()
    {
        await using var context = CreateContext();
        await SeedAsync(context);
        var events = new List<string>();
        var service = CreateService(
            context,
            new RecordingMovementService(events),
            events);
        var occurredAtUtc =
            new DateTime(2026, 8, 2, 4, 0, 0, DateTimeKind.Utc);

        var result = await service.ResolveByInboundLayerAsync(
            3001,
            occurredAtUtc,
            "C2B2 preserve math");

        result.Select(x => x.ProvisionalAllocationId)
            .Should().Equal(2001, 2002);
        result.Select(x => (x.QuantityAbs, x.RevaluationAmount))
            .Should().Equal((2m, 8m), (1m, 2m));

        var revaluations = await context.InventoryValuationEntries
            .Where(x => x.EntryType == InventoryValuationEntryType.Revaluation)
            .OrderBy(x => x.Id)
            .ToListAsync();
        revaluations.Select(x => x.ReferenceSubKey)
            .Should().Equal(
                "REVAL:L3001:A2001",
                "REVAL:L3001:A2002");
        revaluations.Select(x => x.RevaluationOfEntryId)
            .Should().Equal(1001, 1002);
        revaluations.Select(x => x.Amount)
            .Should().Equal(8m, 2m);

        var balance = await context.InventoryBalances.SingleAsync();
        balance.OnHandQty.Should().Be(-4m);
        balance.InventoryValue.Should().Be(-42m);
        balance.AverageUnitCost.Should().Be(10.5m);
        var layer = await context.InventoryCostLayers.SingleAsync();
        layer.RemainingQuantity.Should().Be(0m);
    }

    private static InventoryRevaluationService CreateService(
        InMemoryAppDbContext context,
        RecordingMovementService movements,
        List<string> events)
        => new(
            new InventoryCostLayerRepository(context),
            new InventoryCostLayerAllocationRepository(context),
            new InventoryValuationEntryRepository(context),
            new InventoryTransactionRepository(context),
            new RecordingBalanceRepository(
                new InventoryBalanceRepository(context),
                events),
            movements);

    private static async Task SeedAsync(InMemoryAppDbContext context)
    {
        var firstTransaction = SourceTransaction(501, "1");
        var secondTransaction = SourceTransaction(502, "2");
        var inboundTransaction = SourceTransaction(600, "receipt");
        var firstEntry = SourceEntry(
            1001,
            501,
            unitCost: 14m,
            occurredAtUtc: DateTime.UtcNow.AddMinutes(-2));
        var secondEntry = SourceEntry(
            1002,
            502,
            unitCost: 12m,
            occurredAtUtc: DateTime.UtcNow.AddMinutes(-1));

        context.InventoryTransactions.AddRange(
            firstTransaction,
            secondTransaction,
            inboundTransaction);
        context.InventoryValuationEntries.AddRange(firstEntry, secondEntry);
        context.InventoryCostLayerAllocations.AddRange(
            ProvisionalAllocation(2001, firstEntry, 2m, 14m),
            ProvisionalAllocation(2002, secondEntry, 2m, 12m));
        context.InventoryCostLayers.Add(new InventoryCostLayer
        {
            Id = 3001,
            StoreId = 1,
            WarehouseId = 11,
            ProductVariantId = 101,
            InventoryTransactionId = 600,
            InventoryValuationEntryId = 1999,
            ReferenceType = InventoryReferenceType.StockDocument,
            ReferenceId = "receipt",
            OriginalQuantity = 3m,
            RemainingQuantity = 3m,
            UnitCost = 10m,
            OccurredAtUtc = DateTime.UtcNow
        });
        context.InventoryBalances.Add(new InventoryBalance
        {
            StoreId = 1,
            WarehouseId = 11,
            ProductVariantId = 101,
            OnHandQty = -4m,
            InventoryValue = -52m,
            AverageUnitCost = 13m,
            RowVersion = new byte[8]
        });
        await context.SaveChangesAsync();
    }

    private static InventoryTransaction SourceTransaction(
        int id,
        string referenceId)
        => new()
        {
            Id = id,
            StoreId = 1,
            WarehouseId = 11,
            ProductVariantId = 101,
            TransactionType = InventoryTransactionType.SaleIssue,
            ReferenceType = InventoryReferenceType.Order,
            ReferenceId = referenceId,
            OccurredAtUtc = DateTime.UtcNow
        };

    private static InventoryValuationEntry SourceEntry(
        int id,
        int transactionId,
        decimal unitCost,
        DateTime occurredAtUtc)
        => new()
        {
            Id = id,
            StoreId = 1,
            InventoryTransactionId = transactionId,
            WarehouseId = 11,
            ProductVariantId = 101,
            EntryType = InventoryValuationEntryType.Outbound,
            ReferenceType = InventoryReferenceType.Order,
            ReferenceId = "700",
            ReferenceLineId = id,
            ReferenceSubKey = $"SRC-{id}",
            Quantity = -2m,
            UnitCost = unitCost,
            Amount = -2m * unitCost,
            IsProvisional = true,
            OccurredAtUtc = occurredAtUtc
        };

    private static InventoryCostLayerAllocation ProvisionalAllocation(
        int id,
        InventoryValuationEntry entry,
        decimal quantity,
        decimal unitCost)
        => new()
        {
            Id = id,
            StoreId = 1,
            InventoryValuationEntryId = entry.Id,
            InventoryValuationEntry = entry,
            Quantity = quantity,
            UnitCost = unitCost,
            Amount = quantity * unitCost,
            IsProvisional = true,
            IsResolved = false
        };

    private static InMemoryAppDbContext CreateContext()
    {
        var tenant = new TenantContext();
        tenant.SetStore(1, "store-one");
        var options = new DbContextOptionsBuilder<InMemoryAppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var context = new InMemoryAppDbContext(
            options,
            tenant,
            new TestCurrentUser());
        context.VerifyRowVersionConfiguration();
        return context;
    }

    private sealed class RecordingMovementService : IInventoryMovementService
    {
        private readonly List<string> _events;

        public RecordingMovementService(List<string> events)
            => _events = events;

        public bool HasActiveTransaction { get; set; } = true;
        public List<List<InventoryPostingLockKey>> PreLockBatches { get; } = [];

        public Task PreLockBalancesAsync(
            IEnumerable<InventoryPostingLockKey> keys,
            CancellationToken ct = default)
        {
            var batch = keys.ToList();
            if (batch.Count > 0 && !HasActiveTransaction)
            {
                _events.Add("prelock-rejected");
                throw new InvalidOperationException(
                    "Inventory balance batch pre-locking requires an active transaction.");
            }

            PreLockBatches.Add(batch);
            _events.Add("prelock");
            return Task.CompletedTask;
        }

        public Task<InventoryMovementResultDto> CreateAsync(
            CreateInventoryMovementRequest request,
            CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<decimal> PeekOutboundUnitCostAsync(
            int warehouseId,
            int productVariantId,
            decimal quantity,
            CancellationToken ct = default)
            => Task.FromResult(0m);
    }

    private sealed class RecordingBalanceRepository
        : IInventoryBalanceRepository
    {
        private readonly IInventoryBalanceRepository _inner;
        private readonly List<string> _events;

        public RecordingBalanceRepository(
            IInventoryBalanceRepository inner,
            List<string> events)
        {
            _inner = inner;
            _events = events;
        }

        public Task<InventoryBalance> GetOrCreateAsync(
            int warehouseId,
            int productVariantId,
            CancellationToken ct = default)
        {
            _events.Add("balance:get");
            return _inner.GetOrCreateAsync(
                warehouseId,
                productVariantId,
                ct);
        }

        public Task<InventoryBalance?> GetByWarehouseAndVariantAsync(
            int warehouseId,
            int productVariantId,
            CancellationToken ct = default)
            => _inner.GetByWarehouseAndVariantAsync(
                warehouseId,
                productVariantId,
                ct);

        public Task<InventoryBalance> LockAndGetOrCreateAsync(
            int storeId,
            int warehouseId,
            int productVariantId,
            CancellationToken ct = default)
            => _inner.LockAndGetOrCreateAsync(
                storeId,
                warehouseId,
                productVariantId,
                ct);

        public Task AddAsync(
            InventoryBalance balance,
            CancellationToken ct = default)
            => _inner.AddAsync(balance, ct);

        public Task<List<InventoryBalance>> GetByVariantAsync(
            int productVariantId,
            CancellationToken ct = default)
            => _inner.GetByVariantAsync(productVariantId, ct);

        public Task SaveChangesAsync(CancellationToken ct = default)
            => _inner.SaveChangesAsync(ct);

        public Task<List<InventoryBalance>> GetNegativeBalancesAsync(
            CancellationToken ct = default)
            => _inner.GetNegativeBalancesAsync(ct);

        public Task<InventoryBalance?> GetDetailByWarehouseAndVariantAsync(
            int warehouseId,
            int productVariantId,
            CancellationToken ct = default)
            => _inner.GetDetailByWarehouseAndVariantAsync(
                warehouseId,
                productVariantId,
                ct);

        public Task<(List<InventoryBalance> Items, int TotalItems)>
            QueryCurrentBalancesAsync(
                InventoryBalanceQueryRequest request,
                CancellationToken ct = default)
            => _inner.QueryCurrentBalancesAsync(request, ct);

        public Task<Dictionary<int, decimal>>
            GetAvailableQtyMapByVariantIdsAsync(
                int storeId,
                int warehouseId,
                IReadOnlyCollection<int> variantIds,
                CancellationToken ct = default)
            => _inner.GetAvailableQtyMapByVariantIdsAsync(
                storeId,
                warehouseId,
                variantIds,
                ct);
    }

    private sealed class TestCurrentUser : ICurrentUser
    {
        public int? UserId => 99;
        public string? UserName => "c2b2-revaluation-test";
        public int? TerminalId => null;
        public string? TerminalCode => null;
        public bool IsAuthenticated => true;
    }
}
