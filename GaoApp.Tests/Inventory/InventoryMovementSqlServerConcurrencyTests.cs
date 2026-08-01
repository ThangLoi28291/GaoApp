using GaoApp.Application.DTOs.Inventory;
using GaoApp.Application.Services.Inventory;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Repositories.Inventory;
using GaoApp.Tests.Configuration;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GaoApp.Tests.Inventory;

[Collection("R1FinalDatabasePreflight")]
public sealed class InventoryMovementSqlServerConcurrencyTests
{
    private const int ConcurrentWaitTimeoutSeconds = 30;

    private static readonly TimeSpan ConcurrentWaitTimeout =
        TimeSpan.FromSeconds(ConcurrentWaitTimeoutSeconds);

    [Fact]
    public async Task Concurrent_same_idempotent_movement_creates_exactly_one_posting()
    {
        await using var database = new InventoryPostingLocalDb();
        await database.MigrateAsync();
        var seed = await database.SeedInventoryCatalogAsync();
        var barrier = new AsyncStartBarrier(2);
        using var timeoutCts =
            new CancellationTokenSource(ConcurrentWaitTimeout);

        var first = RunMovementAsync(
            database,
            seed,
            CreateInboundRequest(seed, "R2-SAME", 2m, 10m),
            barrier,
            ct: timeoutCts.Token);
        var second = RunMovementAsync(
            database,
            seed,
            CreateInboundRequest(seed, "R2-SAME", 2m, 10m),
            barrier,
            ct: timeoutCts.Token);

        var results = await WaitForConcurrentPostingsAsync(
            first,
            second,
            timeoutCts.Token);

        barrier.ArrivedCount.Should().Be(2);
        results.Count(x => x.IsCreated).Should().Be(1);
        results.Count(x => x.IsSkipped).Should().Be(1);
        await AssertPostingCountsAsync(
            database,
            seed,
            transactions: 1,
            valuations: 1,
            layers: 1);
        await using var verification =
            database.CreateHostContext();
        var balance = await verification.InventoryBalances
            .IgnoreQueryFilters()
            .SingleAsync(x =>
                x.StoreId == seed.StoreId
                && x.WarehouseId == seed.WarehouseId
                && x.ProductVariantId == seed.ProductVariantId);
        balance.OnHandQty.Should().Be(2m);
        balance.InventoryValue.Should().Be(20m);
    }

    [Fact]
    public async Task Concurrent_distinct_movements_share_initially_missing_balance_without_lost_update()
    {
        await using var database = new InventoryPostingLocalDb();
        await database.MigrateAsync();
        var seed = await database.SeedInventoryCatalogAsync();
        var barrier = new AsyncStartBarrier(2);
        using var timeoutCts =
            new CancellationTokenSource(ConcurrentWaitTimeout);

        var first = RunMovementAsync(
            database,
            seed,
            CreateInboundRequest(seed, "R2-DISTINCT-A", 2m, 10m),
            barrier,
            ct: timeoutCts.Token);
        var second = RunMovementAsync(
            database,
            seed,
            CreateInboundRequest(seed, "R2-DISTINCT-B", 3m, 20m),
            barrier,
            ct: timeoutCts.Token);

        var results = await WaitForConcurrentPostingsAsync(
            first,
            second,
            timeoutCts.Token);

        barrier.ArrivedCount.Should().Be(2);
        results.Should().OnlyContain(x => x.IsCreated);
        await AssertPostingCountsAsync(
            database,
            seed,
            transactions: 2,
            valuations: 2,
            layers: 2);
        await using var verification =
            database.CreateHostContext();
        var balances = await verification.InventoryBalances
            .IgnoreQueryFilters()
            .Where(x =>
                x.StoreId == seed.StoreId
                && x.WarehouseId == seed.WarehouseId
                && x.ProductVariantId == seed.ProductVariantId)
            .ToListAsync();
        balances.Should().ContainSingle();
        balances[0].OnHandQty.Should().Be(5m);
        balances[0].InventoryValue.Should().Be(80m);

        var transactions = await verification.InventoryTransactions
            .IgnoreQueryFilters()
            .Where(x => x.StoreId == seed.StoreId)
            .OrderBy(x => x.Id)
            .ToListAsync();
        transactions[0].BeforeQty.Should().Be(0m);
        transactions[1].BeforeQty
            .Should().Be(transactions[0].AfterQty);
        transactions[1].AfterQty.Should().Be(5m);
    }

    [Fact]
    public async Task Committed_movement_retry_is_skipped_without_new_dependents()
    {
        await using var database = new InventoryPostingLocalDb();
        await database.MigrateAsync();
        var seed = await database.SeedInventoryCatalogAsync();
        var request =
            CreateInboundRequest(seed, "R2-RETRY", 4m, 7.5m);

        var first = await RunMovementAsync(
            database,
            seed,
            request);
        var second = await RunMovementAsync(
            database,
            seed,
            CreateInboundRequest(
                seed,
                "R2-RETRY",
                4m,
                7.5m));

        first.IsCreated.Should().BeTrue();
        second.IsSkipped.Should().BeTrue();
        await AssertPostingCountsAsync(
            database,
            seed,
            transactions: 1,
            valuations: 1,
            layers: 1);
        (await database.ExecuteScalarAsync<decimal>(
            """
            SELECT [OnHandQty]
            FROM [dbo].[InventoryBalances];
            """)).Should().Be(4m);
    }

    [Fact]
    public async Task Service_owned_transaction_rolls_back_when_save_fails_after_transaction_staging()
    {
        await using var database = new InventoryPostingLocalDb();
        await database.MigrateAsync();
        var seed = await database.SeedInventoryCatalogAsync();

        await using (var setup =
                     database.CreateTenantContext(seed.StoreId))
        {
            setup.InventoryBalances.Add(new InventoryBalance
            {
                StoreId = seed.StoreId,
                WarehouseId = seed.WarehouseId,
                ProductVariantId = seed.ProductVariantId,
                OnHandQty = 0m,
                ReservedQty = 0m,
                InventoryValue = 0m,
                AverageUnitCost = 0m
            });
            await setup.SaveChangesAsync();
        }

        var action = () => RunMovementAsync(
            database,
            seed,
            CreateInboundRequest(
                seed,
                "R2-ROLLBACK",
                2m,
                10m),
            interceptor:
                new FailOnSaveChangesInterceptor(2));

        await action.Should()
            .ThrowAsync<InjectedPostingFailureException>();

        await AssertPostingCountsAsync(
            database,
            seed,
            transactions: 0,
            valuations: 0,
            layers: 0);
        await using var verification =
            database.CreateHostContext();
        var balance = await verification.InventoryBalances
            .IgnoreQueryFilters()
            .SingleAsync(x =>
                x.StoreId == seed.StoreId
                && x.WarehouseId == seed.WarehouseId
                && x.ProductVariantId == seed.ProductVariantId);
        balance.OnHandQty.Should().Be(0m);
        balance.InventoryValue.Should().Be(0m);
        (await verification.InventoryCostLayerAllocations
                .IgnoreQueryFilters()
                .CountAsync(x => x.StoreId == seed.StoreId))
            .Should().Be(0);
    }

    private static async Task<InventoryMovementResultDto>
        RunMovementAsync(
            InventoryPostingLocalDb database,
            InventoryPostingSeed seed,
            CreateInventoryMovementRequest request,
            AsyncStartBarrier? barrier = null,
            IInterceptor? interceptor = null,
            CancellationToken ct = default)
    {
        await using var db = database.CreateTenantContext(
            seed.StoreId,
            interceptor);
        if (barrier is not null)
        {
            await barrier.SignalAndWaitAsync(ct);
        }

        return await CreateService(db).CreateAsync(request, ct);
    }

    private static async Task<InventoryMovementResultDto[]>
        WaitForConcurrentPostingsAsync(
            Task<InventoryMovementResultDto> first,
            Task<InventoryMovementResultDto> second,
            CancellationToken timeoutToken)
    {
        var concurrentPostings = Task.WhenAll(first, second);

        try
        {
            return await concurrentPostings.WaitAsync(
                ConcurrentWaitTimeout);
        }
        catch (OperationCanceledException exception)
            when (timeoutToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Concurrent inventory postings did not complete within {ConcurrentWaitTimeoutSeconds} seconds.",
                exception);
        }
        catch (TimeoutException exception)
            when (!concurrentPostings.IsCompleted)
        {
            throw new TimeoutException(
                $"Concurrent inventory postings did not complete within {ConcurrentWaitTimeoutSeconds} seconds.",
                exception);
        }
    }

    private static InventoryMovementService CreateService(
        AppDbContext db)
        => new(
            new InventoryBalanceRepository(db),
            new InventoryTransactionRepository(db),
            new InventoryValuationEntryRepository(db),
            new InventoryCostLayerRepository(db),
            new InventoryCostLayerAllocationRepository(db),
            new WarehouseRepository(db),
            new InventoryPostingTransactionCoordinator(db));

    private static CreateInventoryMovementRequest
        CreateInboundRequest(
            InventoryPostingSeed seed,
            string referenceId,
            decimal quantity,
            decimal unitCost)
        => new()
        {
            WarehouseId = seed.WarehouseId,
            ProductVariantId = seed.ProductVariantId,
            QuantityChange = quantity,
            UnitCost = unitCost,
            TransactionType =
                InventoryTransactionType.PurchaseReceipt,
            ReferenceType =
                InventoryReferenceType.PurchaseReceipt,
            ReferenceId = referenceId,
            ReferenceLineId = 1,
            SkipIfExists = true
        };

    private static async Task AssertPostingCountsAsync(
        InventoryPostingLocalDb database,
        InventoryPostingSeed seed,
        int transactions,
        int valuations,
        int layers)
    {
        await using var verification =
            database.CreateHostContext();
        (await verification.InventoryTransactions
                .IgnoreQueryFilters()
                .CountAsync(x => x.StoreId == seed.StoreId))
            .Should().Be(transactions);
        (await verification.InventoryValuationEntries
                .IgnoreQueryFilters()
                .CountAsync(x => x.StoreId == seed.StoreId))
            .Should().Be(valuations);
        (await verification.InventoryCostLayers
                .IgnoreQueryFilters()
                .CountAsync(x => x.StoreId == seed.StoreId))
            .Should().Be(layers);
    }

    private sealed class AsyncStartBarrier
    {
        private readonly int _participants;
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrived;

        public int ArrivedCount => Volatile.Read(ref _arrived);

        public AsyncStartBarrier(int participants)
        {
            if (participants <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(participants));
            }

            _participants = participants;
        }

        public async Task SignalAndWaitAsync(
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var arrived = Interlocked.Increment(ref _arrived);
            if (arrived > _participants)
            {
                var exception = new InvalidOperationException(
                    "AsyncStartBarrier received more participants than configured.");
                _release.TrySetException(exception);
                throw exception;
            }

            if (arrived == _participants)
            {
                _release.TrySetResult();
            }

            try
            {
                await _release.Task.WaitAsync(ct);
            }
            catch (OperationCanceledException exception)
                when (ct.IsCancellationRequested)
            {
                throw new TimeoutException(
                    $"AsyncStartBarrier did not receive all {_participants} participants within {ConcurrentWaitTimeoutSeconds} seconds. Arrived={ArrivedCount}.",
                    exception);
            }
        }
    }

    private sealed class FailOnSaveChangesInterceptor
        : SaveChangesInterceptor
    {
        private readonly int _failingCall;
        private int _calls;

        public FailOnSaveChangesInterceptor(int failingCall)
        {
            _failingCall = failingCall;
        }

        public override ValueTask<InterceptionResult<int>>
            SavingChangesAsync(
                DbContextEventData eventData,
                InterceptionResult<int> result,
                CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _calls)
                == _failingCall)
            {
                throw new InjectedPostingFailureException();
            }

            return ValueTask.FromResult(result);
        }
    }

    private sealed class InjectedPostingFailureException
        : Exception;
}
