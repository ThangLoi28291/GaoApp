using GaoApp.Application.Common;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.DTOs.Audit;
using GaoApp.Application.DTOs.Inventory;
using GaoApp.Application.DTOs.Returns;
using GaoApp.Application.DTOs.Rewards;
using GaoApp.Application.Interfaces.Repositories.Orders;
using GaoApp.Application.Interfaces.Repositories.Rewards;
using GaoApp.Application.Interfaces.Services.Audit;
using GaoApp.Application.Interfaces.Services.Inventory;
using GaoApp.Application.Interfaces.Services.Invoices;
using GaoApp.Application.Interfaces.Services.Orders;
using GaoApp.Application.Interfaces.Services.Rewards;
using GaoApp.Application.Services.Inventory;
using GaoApp.Application.Services.Orders;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Data.Repositories.Orders;
using GaoApp.Infrastructure.Repositories.Inventory;
using GaoApp.Infrastructure.Repositories.Orders;
using GaoApp.Infrastructure.Tenant;
using GaoApp.Tests.Configuration;
using GaoApp.Tests.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Inventory;

[Collection("R1FinalDatabasePreflight")]
public sealed class InventoryPosPostingContractTests
{
    private const string PosServicePath =
        "GaoApp.Application/Services/Orders/POSService.cs";
    private const string SalesReturnServicePath =
        "GaoApp.Application/Services/Orders/SalesReturnService.cs";

    [Fact]
    public void LegacyFinalize_ShouldMaterializeOneBatchBeforeFirstMovement()
    {
        var method = Method(
            ReadRepositoryFile(PosServicePath),
            "private async Task<FinalizeInventorySummary> ApplyInventoryForFinalizeAsync",
            "private static FinalizeInventorySummary MapLegalEntityInventorySummary");

        Position(method, "var plans = new List<FinalizeInventoryMovementPlan>()")
            .Should().BeLessThan(Position(method, "var balanceKeys = plans"));
        Position(method, "PreLockBalancesAsync")
            .Should().BeLessThan(
                Position(method, "_inventoryMovementService.CreateAsync"));
        Count(method, "PreLockBalancesAsync").Should().Be(1);
    }

    [Fact]
    public void LegacyFinalize_ShouldUseEveryActiveLineExactBalanceIdentity()
    {
        var method = Method(
            ReadRepositoryFile(PosServicePath),
            "private async Task<FinalizeInventorySummary> ApplyInventoryForFinalizeAsync",
            "private static FinalizeInventorySummary MapLegalEntityInventorySummary");

        method.Should().Contain("order.Lines.Where(x => !x.IsDeleted)");
        method.Should().Contain(
            "new InventoryPostingLockKey(\n                order.StoreId,\n                warehouse.Id,\n                x.Line.VariantId)");
        method.Should().Contain(".Distinct()");
    }

    [Fact]
    public void LegacyFinalize_ShouldPreserveSaleIssueOrderAndIdentity()
    {
        var method = Method(
            ReadRepositoryFile(PosServicePath),
            "private async Task<FinalizeInventorySummary> ApplyInventoryForFinalizeAsync",
            "private static FinalizeInventorySummary MapLegalEntityInventorySummary");

        method.Should().Contain("foreach (var plan in plans)");
        method.Should().NotContain("plans.OrderBy");
        method.Should().Contain(
            "_inventoryMovementFactory.CreateSaleFinalize(\n                warehouse.Id,\n                line.VariantId,\n                order.Id,\n                line.Id");
    }

    [Fact]
    public void LegacyVoid_ShouldDiscoverAllFragmentsBeforePrelock()
    {
        var method = Method(
            ReadRepositoryFile(PosServicePath),
            "private async Task ApplyInventoryForVoidAsync",
            "private async Task ApplyInventoryForRefundAsync");

        Position(method, "GetByReferenceAsync")
            .Should().BeLessThan(Position(method, "var balanceKeys = plans"));
        Position(method, "plans.Add(new VoidInventoryMovementPlan")
            .Should().BeLessThan(Position(method, "PreLockBalancesAsync"));
        Position(method, "PreLockBalancesAsync")
            .Should().BeLessThan(
                Position(method, "_inventoryMovementService.CreateAsync"));
        Count(method, "PreLockBalancesAsync").Should().Be(1);
    }

    [Fact]
    public void LegacyVoid_ShouldPreserveFragmentOrderKeysAndSubkeys()
    {
        var method = Method(
            ReadRepositoryFile(PosServicePath),
            "private async Task ApplyInventoryForVoidAsync",
            "private async Task ApplyInventoryForRefundAsync");

        method.Should().Contain(".OrderByDescending(x => x.Id)");
        method.Should().Contain(
            "order.StoreId,\n                x.SourceEntry.WarehouseId,\n                x.Line.VariantId");
        method.Should().Contain("foreach (var plan in plans)");
        method.Should().Contain(
            "movementRequest.ReferenceSubKey = $\"void-from-val-{entry.Id}\"");
        method.Should().Contain(
            "movementRequest.SourceValuationEntryId = entry.Id");
    }

    [Fact]
    public void SalesReturnLegacy_ShouldPlanAllRestockLinesBeforeOneUnionPrelock()
    {
        var method = Method(
            ReadRepositoryFile(SalesReturnServicePath),
            "public async Task<SalesReturnDto> CreateAsync",
            "public async Task<SalesReturnDto?> GetByIdAsync");

        Position(method, "var persistedLines = entity.Lines.ToList()")
            .Should().BeLessThan(Position(method, "PrepareSalesReturnBatchAsync"));
        Position(method, "PrepareSalesReturnBatchAsync")
            .Should().BeLessThan(Position(method, "var legacyPlansByLineId"));
        var balanceKeysPosition = Position(method, "var balanceKeys = legalEntityBatch.LockKeys");
        var legacyPrelockPosition = PositionAfter(
            method,
            "PreLockBalancesAsync",
            balanceKeysPosition);
        balanceKeysPosition.Should().BeLessThan(legacyPrelockPosition);
        legacyPrelockPosition
            .Should().BeLessThan(
                PositionAfter(
                    method,
                    "_inventoryMovementService.CreateAsync",
                    legacyPrelockPosition));
        method.Should().Contain(
            "order.StoreId,\n                        warehouse.Id,\n                        x.Line.VariantId");
        Count(method, "PreLockBalancesAsync").Should().Be(1);
    }

    [Fact]
    public void SalesReturn_ShouldKeepNoRestockOutOfLegacyLockAndMovementPlans()
    {
        var method = Method(
            ReadRepositoryFile(SalesReturnServicePath),
            "public async Task<SalesReturnDto> CreateAsync",
            "public async Task<SalesReturnDto?> GetByIdAsync");

        Position(
                method,
                "if (line.Action != SalesReturnLineAction.Restock ||\n                    legalEntityBatch.HandledLineIds.Contains(line.Id))")
            .Should().BeLessThan(
                Position(method, "legacyPlansByLineId.Add("));
    }

    [Fact]
    public void SalesReturnMultiLegalEntity_ShouldPrelockPreparedAllLineUnionBeforeApply()
    {
        var method = Method(
            ReadRepositoryFile(SalesReturnServicePath),
            "public async Task<SalesReturnDto> CreateAsync",
            "public async Task<SalesReturnDto?> GetByIdAsync");

        Position(method, "PrepareSalesReturnBatchAsync")
            .Should().BeLessThan(
                Position(method, "var balanceKeys = legalEntityBatch.LockKeys"));
        method.Should().Contain(
            "legalEntityBatch.LockKeys\n                .Concat(legacyPlansByLineId.Values.Select");
        var prelockPosition = Position(method, "PreLockBalancesAsync");
        var applyLoopPosition = PositionAfter(
            method,
            "foreach (var line in persistedLines)",
            prelockPosition);
        prelockPosition.Should().BeLessThan(applyLoopPosition);
        applyLoopPosition
            .Should().BeLessThan(
                Position(method, "ApplyPreparedSalesReturnLineAsync"));
        method.Should().NotContain("ReverseSalesReturnLineIfAllocatedAsync");
        Count(method, "PreLockBalancesAsync").Should().Be(1);
    }

    [Fact]
    public void SalesReturnLegacy_ShouldPreserveAllocationOrderAndIdentity()
    {
        var method = Method(
            ReadRepositoryFile(SalesReturnServicePath),
            "public async Task<SalesReturnDto> CreateAsync",
            "public async Task<SalesReturnDto?> GetByIdAsync");

        method.Should().Contain("foreach (var line in persistedLines)");
        method.Should().Contain(
            "for (var i = 0; i < allocations.Count; i++)");
        method.Should().Contain(
            "$\"RET:{line.Id}:ALLOC:{i + 1}:SRC:{allocation.SourceValuationEntryId}\"");
    }

    [Fact]
    public async Task Mixed_sales_return_should_prelock_one_union_and_preserve_line_order()
    {
        await using var fixture = await BehaviorFixture.CreateAsync(
            BehaviorScenario.Mixed,
            callerOwnedTransaction: true);

        var result = await fixture.Service.CreateAsync(
            fixture.CreateRequest(20, 10, 30));

        var legacyReturnLineId = result.Lines
            .Single(x => x.OrderLineId == 20)
            .Id;
        var legalEntityReturnLineId = result.Lines
            .Single(x => x.OrderLineId == 10)
            .Id;
        var noRestockReturnLineId = result.Lines
            .Single(x => x.OrderLineId == 30)
            .Id;
        var observation = fixture.Observe();

        AssertMixedOperationContract(
            observation,
            legacyReturnLineId,
            legalEntityReturnLineId);
        observation.CreatedRequests.Should().NotContain(
            x => x.ReferenceLineId == noRestockReturnLineId);
        fixture.Fragments.RequestedOrderLineIds
            .Should().Contain([20, 10, 30]);
        fixture.UnitOfWork.IsActive.Should().BeTrue(
            "the joined caller transaction remains caller-owned");
        fixture.UnitOfWork.OwnedByCaller.Should().BeTrue();
    }

    [Fact]
    public async Task Pure_legacy_return_should_keep_one_parent_batch()
    {
        await using var fixture = await BehaviorFixture.CreateAsync(
            BehaviorScenario.PureLegacy);

        var result = await fixture.Service.CreateAsync(
            fixture.CreateRequest(20));
        var lineId = result.Lines.Single().Id;

        fixture.Movements.PreLockBatches.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(
                [new InventoryPostingLockKey(1, 22, 202)]);
        fixture.Movements.CreatedRequests.Should().ContainSingle()
            .Which.ReferenceSubKey.Should()
            .Be($"RET:{lineId}:ALLOC:1:SRC:2001");
        fixture.Movements.Events.IndexOf("prelock")
            .Should().BeLessThan(
                fixture.Movements.Events.FindIndex(x =>
                    x.StartsWith("movement:", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Pure_legal_entity_return_should_keep_one_parent_batch()
    {
        await using var fixture = await BehaviorFixture.CreateAsync(
            BehaviorScenario.PureLegalEntity);

        var result = await fixture.Service.CreateAsync(
            fixture.CreateRequest(10));
        var lineId = result.Lines.Single().Id;

        fixture.Movements.PreLockBatches.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(
                [new InventoryPostingLockKey(1, 11, 101)]);
        fixture.Movements.CreatedRequests.Should().ContainSingle()
            .Which.ReferenceSubKey.Should()
            .Be($"LE-RET:R{lineId}:A901:S1001");
        fixture.Movements.Events.IndexOf("prelock")
            .Should().BeLessThan(
                fixture.Movements.Events.FindIndex(x =>
                    x.StartsWith("movement:", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Partial_legal_entity_evidence_should_fail_before_prelock_or_movement()
    {
        await using var fixture = await BehaviorFixture.CreateAsync(
            BehaviorScenario.PartialLegalEntity);

        var act = () => fixture.Service.CreateAsync(
            fixture.CreateRequest(10));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*evidence*");
        fixture.Movements.PreLockBatches.Should().BeEmpty();
        fixture.Movements.CreatedRequests.Should().BeEmpty();
        fixture.Reversals.Added.Should().BeEmpty();
        fixture.UnitOfWork.RollbackCount.Should().Be(1);
    }

    [Fact]
    public async Task Missing_batch_capability_should_fail_before_prelock_or_movement()
    {
        await using var fixture = await BehaviorFixture.CreateAsync(
            BehaviorScenario.MissingBatchCapability);

        var act = () => fixture.Service.CreateAsync(
            fixture.CreateRequest(20));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*operation-wide batch planning*");
        fixture.Movements.PreLockBatches.Should().BeEmpty();
        fixture.Movements.CreatedRequests.Should().BeEmpty();
        fixture.UnitOfWork.RollbackCount.Should().Be(1);
    }

    [Fact]
    public async Task NoRestock_only_should_have_zero_prelock_and_zero_movement()
    {
        await using var fixture = await BehaviorFixture.CreateAsync(
            BehaviorScenario.NoRestockOnly);

        var result = await fixture.Service.CreateAsync(
            fixture.CreateRequest(30));

        result.Lines.Should().ContainSingle()
            .Which.Action.Should().Be(SalesReturnLineAction.NoRestock);
        fixture.Movements.PreLockBatches.Should().BeEmpty();
        fixture.Movements.CreatedRequests.Should().BeEmpty();
        fixture.Fragments.RequestedOrderLineIds.Should().Contain(30);
    }

    [Theory]
    [InlineData("A")]
    [InlineData("C")]
    [InlineData("E")]
    [InlineData("G")]
    public void Mixed_sales_return_assertions_should_reject_mutations(
        string mutation)
    {
        var observation = MixedObservation.Valid();

        switch (mutation)
        {
            case "A":
                observation.CreatedRequests[0].ReferenceSubKey =
                    "LE-RET:R401:A999:S2001";
                break;
            case "C":
                observation.PreLockBatches.Add(
                    [new InventoryPostingLockKey(1, 11, 101)]);
                break;
            case "E":
                observation.PreLockBatches[0].RemoveAll(x =>
                    x.WarehouseId == 22);
                break;
            case "G":
                observation.Events.Clear();
                observation.Events.AddRange(
                    ["movement:401", "prelock", "movement:402"]);
                break;
            default:
                throw new InvalidOperationException(
                    $"Unknown mutation {mutation}.");
        }

        var act = () => AssertMixedOperationContract(
            observation,
            legacyReturnLineId: 401,
            legalEntityReturnLineId: 402);

        act.Should().Throw<Exception>(
            $"mutation {mutation} must invalidate the mixed-return contract");
    }

    [Fact]
    public async Task Mixed_sales_return_should_persist_legacy_and_legal_entity_restock_after_one_union_prelock()
    {
        await using var database = new InventoryPostingLocalDb();
        await database.MigrateAsync();
        var seed = await database.SeedInventoryCatalogAsync();

        RelationalSalesReturnSeed relational;
        await using (var posting =
                     database.CreateTenantContext(seed.StoreId))
        {
            relational = await SeedRelationalSalesReturnAsync(
                posting,
                seed);
            var realMovements = CreateRealMovementService(posting);
            var factory = new InventoryMovementFactory();

            await realMovements.CreateAsync(
                factory.CreatePurchaseReceipt(
                    relational.LegalEntityWarehouseId,
                    relational.LegalEntityVariantId,
                    2m,
                    10m,
                    "R1-LE-RECEIPT",
                    1,
                    "R1-LE-RECEIPT",
                    1));
            await realMovements.CreateAsync(
                factory.CreatePurchaseReceipt(
                    relational.LegacyWarehouseId,
                    relational.LegacyVariantId,
                    2m,
                    12m,
                    "R1-LEGACY-RECEIPT",
                    2,
                    "R1-LEGACY-RECEIPT",
                    1));

            var legalEntitySale = factory.CreateSaleFinalize(
                relational.LegalEntityWarehouseId,
                relational.LegalEntityVariantId,
                relational.OrderId,
                relational.LegalEntityOrderLineId,
                "LE item",
                1m,
                null);
            legalEntitySale.ReferenceSubKey =
                $"LE:{relational.LegalEntityId}:WH:{relational.LegalEntityWarehouseId}";
            var legalEntityIssue =
                await realMovements.CreateAsync(legalEntitySale);
            var legacyIssue = await realMovements.CreateAsync(
                factory.CreateSaleFinalize(
                    relational.LegacyWarehouseId,
                    relational.LegacyVariantId,
                    relational.OrderId,
                    relational.LegacyOrderLineId,
                    "Legacy item",
                    1m,
                    null));

            legalEntityIssue.IsCreated.Should().BeTrue();
            legacyIssue.IsCreated.Should().BeTrue();
            var legalEntitySourceId = legalEntityIssue
                .InventoryValuationEntryIds.Single();
            var legacySourceId = legacyIssue
                .InventoryValuationEntryIds.Single();

            var allocation = new OrderLegalEntityAllocation
            {
                StoreId = seed.StoreId,
                OrderId = relational.OrderId,
                OrderLineId = relational.LegalEntityOrderLineId,
                ProductVariantId = relational.LegalEntityVariantId,
                LegalEntityId = relational.LegalEntityId,
                WarehouseId = relational.LegalEntityWarehouseId,
                InventoryTransactionId =
                    legalEntityIssue.InventoryTransactionId,
                SalePriority = 1,
                Quantity = 1m,
                BaseQuantity = 1m,
                UnitPrice = 20m,
                LineTotal = 20m,
                NetAmount = 20m,
                AllocationSource =
                    OrderLegalEntityAllocationSource.AutoBySalePriority
            };
            posting.OrderLegalEntityAllocations.Add(allocation);
            await posting.SaveChangesAsync();

            var recordingMovements =
                new RecordingDelegatingMovementService(realMovements);
            var reversalRepository =
                new OrderLegalEntityAllocationReversalRepository(posting);
            var fragmentService =
                new ReturnableValuationFragmentService(
                    new InventoryValuationEntryRepository(posting),
                    reversalRepository);
            var reversal = new OrderLegalEntityReversalService(
                new OrderLegalEntityAllocationRepository(posting),
                reversalRepository,
                fragmentService,
                new ReturnCostAllocator(),
                recordingMovements,
                factory);
            var unitOfWork = new UnitOfWork(posting);
            var service = new SalesReturnService(
                unitOfWork,
                new OrderRepository(posting),
                new SalesReturnRepository(posting),
                new POSShiftRepository(posting),
                new POSAuditLogRepository(posting),
                new WarehouseRepository(posting),
                recordingMovements,
                factory,
                new NoOpAuditLogService(),
                new FixedCurrentStore(seed.StoreId),
                new FixedCurrentUser(
                    relational.UserId,
                    relational.TerminalId),
                fragmentService,
                new ReturnCostAllocator(),
                new NoOpRewardLedgerRepository(),
                new NoOpOrderRewardCalculator(),
                reversal,
                new NoOpDraftInvoiceReturnSyncService());

            var result = await service.CreateAsync(new CreateSalesReturnRequest
            {
                OrderId = relational.OrderId,
                Type = SalesReturnType.ReturnOnly,
                Reason = "R1 relational mixed return",
                Lines =
                [
                    RelationalReturnLine(
                        relational.LegacyOrderLineId,
                        SalesReturnLineAction.Restock),
                    RelationalReturnLine(
                        relational.LegalEntityOrderLineId,
                        SalesReturnLineAction.Restock),
                    RelationalReturnLine(
                        relational.NoRestockOrderLineId,
                        SalesReturnLineAction.NoRestock)
                ]
            });

            var legacyReturnLineId = result.Lines
                .Single(x =>
                    x.OrderLineId == relational.LegacyOrderLineId)
                .Id;
            var legalEntityReturnLineId = result.Lines
                .Single(x =>
                    x.OrderLineId == relational.LegalEntityOrderLineId)
                .Id;
            var noRestockReturnLineId = result.Lines
                .Single(x =>
                    x.OrderLineId == relational.NoRestockOrderLineId)
                .Id;

            recordingMovements.PreLockBatches.Should().ContainSingle()
                .Which.Should().BeEquivalentTo(
                    [
                        new InventoryPostingLockKey(
                            seed.StoreId,
                            relational.LegalEntityWarehouseId,
                            relational.LegalEntityVariantId),
                        new InventoryPostingLockKey(
                            seed.StoreId,
                            relational.LegacyWarehouseId,
                            relational.LegacyVariantId)
                    ]);
            recordingMovements.CreatedRequests.Should().HaveCount(2);
            recordingMovements.CreatedRequests.Select(x =>
                    x.ReferenceLineId)
                .Should().Equal(
                    legacyReturnLineId,
                    legalEntityReturnLineId);
            recordingMovements.CreatedRequests[0].ReferenceSubKey
                .Should().Be(
                    $"RET:{legacyReturnLineId}:ALLOC:1:SRC:{legacySourceId}");
            recordingMovements.CreatedRequests[1].ReferenceSubKey
                .Should().Be(
                    $"LE-RET:R{legalEntityReturnLineId}:A{allocation.Id}:S{legalEntitySourceId}");
            recordingMovements.CreatedRequests.Should().NotContain(x =>
                x.ReferenceLineId == noRestockReturnLineId);
            recordingMovements.Events.IndexOf("prelock")
                .Should().BeLessThan(
                    recordingMovements.Events.FindIndex(x =>
                        x.StartsWith(
                            "movement:",
                            StringComparison.Ordinal)));
            posting.Database.CurrentTransaction.Should().BeNull(
                "SalesReturnService remains the standalone transaction owner");

            relational = relational with
            {
                SalesReturnId = result.Id,
                LegacyReturnLineId = legacyReturnLineId,
                LegalEntityReturnLineId = legalEntityReturnLineId,
                NoRestockReturnLineId = noRestockReturnLineId,
                LegacySourceValuationEntryId = legacySourceId,
                LegalEntitySourceValuationEntryId =
                    legalEntitySourceId,
                AllocationId = allocation.Id
            };
        }

        await using var verification =
            database.CreateTenantContext(seed.StoreId);
        var returnTransactions = await verification.InventoryTransactions
            .AsNoTracking()
            .Where(x =>
                x.ReferenceType == InventoryReferenceType.Refund
                && x.ReferenceId ==
                    relational.SalesReturnId.ToString())
            .OrderBy(x => x.Id)
            .ToListAsync();
        returnTransactions.Should().HaveCount(2);
        returnTransactions.Should().OnlyContain(x =>
            x.TransactionType ==
                InventoryTransactionType.CustomerReturnIn);
        returnTransactions.Select(x => x.ReferenceLineId)
            .Should().BeEquivalentTo(
                [
                    relational.LegacyReturnLineId,
                    relational.LegalEntityReturnLineId
                ]);
        returnTransactions.Should().ContainSingle(x =>
            x.ReferenceSubKey ==
            $"RET:{relational.LegacyReturnLineId}:ALLOC:1:SRC:{relational.LegacySourceValuationEntryId}");
        returnTransactions.Should().ContainSingle(x =>
            x.ReferenceSubKey ==
            $"LE-RET:R{relational.LegalEntityReturnLineId}:A{relational.AllocationId}:S{relational.LegalEntitySourceValuationEntryId}");
        returnTransactions.Select(x => x.ReferenceSubKey)
            .Should().OnlyHaveUniqueItems();
        returnTransactions.Should().NotContain(x =>
            x.ReferenceLineId == relational.NoRestockReturnLineId);

        var balances = await verification.InventoryBalances
            .AsNoTracking()
            .Where(x =>
                (x.WarehouseId ==
                    relational.LegalEntityWarehouseId
                 && x.ProductVariantId ==
                    relational.LegalEntityVariantId)
                || (x.WarehouseId ==
                    relational.LegacyWarehouseId
                    && x.ProductVariantId ==
                    relational.LegacyVariantId))
            .ToListAsync();
        balances.Should().HaveCount(2);
        balances.Should().OnlyContain(x =>
            x.OnHandQty == 2m,
            "each source balance was 1 after sale and returns to 2");
    }

    [Fact]
    public void DeadLegacyRefundHelper_ShouldRemainWithoutRuntimeCallSite()
    {
        var source = ReadRepositoryFile(PosServicePath);

        Count(source, "ApplyInventoryForRefundAsync(").Should().Be(1);
    }

    private static void AssertMixedOperationContract(
        MixedObservation observation,
        int legacyReturnLineId,
        int legalEntityReturnLineId)
    {
        observation.PreLockBatches.Should().ContainSingle(
            "the parent return operation must acquire exactly one batch");
        observation.PreLockBatches[0].Should().BeEquivalentTo(
            [
                new InventoryPostingLockKey(1, 11, 101),
                new InventoryPostingLockKey(1, 22, 202)
            ]);
        observation.CreatedRequests.Should().HaveCount(2);
        observation.CreatedRequests.Select(x => x.ReferenceLineId)
            .Should().Equal(
                legacyReturnLineId,
                legalEntityReturnLineId);
        observation.CreatedRequests[0].ReferenceSubKey.Should()
            .Be($"RET:{legacyReturnLineId}:ALLOC:1:SRC:2001");
        observation.CreatedRequests[1].ReferenceSubKey.Should()
            .Be($"LE-RET:R{legalEntityReturnLineId}:A901:S1001");

        var prelockIndex = observation.Events.IndexOf("prelock");
        var firstMovementIndex = observation.Events.FindIndex(x =>
            x.StartsWith("movement:", StringComparison.Ordinal));
        prelockIndex.Should().BeGreaterThanOrEqualTo(0);
        observation.Events.Should().Contain(
            ["plan:20", "plan:10", "plan:30"]);
        observation.Events
            .Select((value, index) => (value, index))
            .Where(x => x.value.StartsWith(
                "plan:",
                StringComparison.Ordinal))
            .Should().OnlyContain(x => x.index < prelockIndex);
        firstMovementIndex.Should().BeGreaterThan(prelockIndex);
    }

    private enum BehaviorScenario
    {
        Mixed,
        PureLegacy,
        PureLegalEntity,
        PartialLegalEntity,
        MissingBatchCapability,
        NoRestockOnly
    }

    private static CreateSalesReturnLineRequest RelationalReturnLine(
        int orderLineId,
        SalesReturnLineAction action)
        => new()
        {
            OrderLineId = orderLineId,
            ReturnQuantity = 1m,
            ReturnBaseQuantity = 1m,
            RefundUnitAmount = 0m,
            Action = action
        };

    private static async Task<RelationalSalesReturnSeed>
        SeedRelationalSalesReturnAsync(
            AppDbContext context,
            InventoryPostingSeed seed)
    {
        var legalEntityWarehouse = await context.Warehouses
            .SingleAsync(x => x.Id == seed.WarehouseId);
        var legalEntityVariant = await context.ProductVariants
            .SingleAsync(x => x.Id == seed.ProductVariantId);
        var legacyWarehouse = new Warehouse
        {
            StoreId = seed.StoreId,
            LegalEntityId = legalEntityWarehouse.LegalEntityId,
            Code = $"R1-LEGACY-WH-{Guid.NewGuid():N}",
            Name = "R1 Legacy Return Warehouse",
            IsActive = true
        };
        var legacyVariant = new ProductVariant
        {
            StoreId = seed.StoreId,
            ProductId = legalEntityVariant.ProductId,
            Sku = $"R1-LEGACY-{Guid.NewGuid():N}",
            ProductVariantName = "R1 Legacy Variant",
            CostPrice = 12m,
            Price = 20m,
            IsActive = true
        };
        var user = new User
        {
            UserName = $"r1-return-{Guid.NewGuid():N}",
            FullName = "R1 Return User",
            PasswordHash = "test-only",
            IsActive = true
        };
        var terminal = new POSTerminal
        {
            StoreId = seed.StoreId,
            Code = $"R1-POS-{Guid.NewGuid():N}"[..30],
            Name = "R1 Return POS",
            IsActive = true
        };
        context.AddRange(
            legacyWarehouse,
            legacyVariant,
            user,
            terminal);
        await context.SaveChangesAsync();

        var shift = new POSShift
        {
            StoreId = seed.StoreId,
            TerminalId = terminal.Id,
            WarehouseId = legacyWarehouse.Id,
            OpenedByUserId = user.Id,
            OpenedAtUtc = DateTime.UtcNow,
            ShiftCode = $"R1-{Guid.NewGuid():N}"[..30],
            Status = POSShiftStatus.Open
        };
        context.POSShifts.Add(shift);
        await context.SaveChangesAsync();

        var order = new Order
        {
            StoreId = seed.StoreId,
            OrderNumber = $"R1-{Guid.NewGuid():N}"[..30],
            Status = OrderStatus.Completed,
            PaymentStatus = PaymentStatus.Paid,
            POSShiftId = shift.Id,
            Subtotal = 60m,
            GrandTotal = 60m,
            PaidTotal = 60m,
            CompletedAtUtc = DateTime.UtcNow,
            UseMultiLegalEntity = true,
            LegalEntityCount = 1,
            LegalEntityAllocatedAtUtc = DateTime.UtcNow
        };
        var legalEntityLine = RelationalOrderLine(
            seed.StoreId,
            legalEntityVariant.ProductId,
            legalEntityVariant.Id,
            "LE item");
        var legacyLine = RelationalOrderLine(
            seed.StoreId,
            legacyVariant.ProductId,
            legacyVariant.Id,
            "Legacy item");
        var noRestockLine = RelationalOrderLine(
            seed.StoreId,
            legacyVariant.ProductId,
            legacyVariant.Id,
            "NoRestock item");
        order.Lines.Add(legalEntityLine);
        order.Lines.Add(legacyLine);
        order.Lines.Add(noRestockLine);
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        return new RelationalSalesReturnSeed(
            seed.StoreId,
            legalEntityWarehouse.LegalEntityId,
            legalEntityWarehouse.Id,
            legalEntityVariant.Id,
            legacyWarehouse.Id,
            legacyVariant.Id,
            user.Id,
            terminal.Id,
            order.Id,
            legalEntityLine.Id,
            legacyLine.Id,
            noRestockLine.Id);
    }

    private static OrderLine RelationalOrderLine(
        int storeId,
        int productId,
        int variantId,
        string itemName)
        => new()
        {
            StoreId = storeId,
            ProductId = productId,
            VariantId = variantId,
            ItemName = itemName,
            UnitName = "unit",
            SellingUnitName = "unit",
            Quantity = 1m,
            BaseQuantity = 1m,
            Multiplier = 1m,
            UnitPrice = 20m,
            OriginalUnitPrice = 20m,
            LineTotal = 20m
        };

    private static InventoryMovementService CreateRealMovementService(
        AppDbContext context)
        => new(
            new InventoryBalanceRepository(context),
            new InventoryTransactionRepository(context),
            new InventoryValuationEntryRepository(context),
            new InventoryCostLayerRepository(context),
            new InventoryCostLayerAllocationRepository(context),
            new WarehouseRepository(context),
            new InventoryPostingTransactionCoordinator(context));

    private sealed class RecordingDelegatingMovementService
        : IInventoryMovementService
    {
        private readonly IInventoryMovementService _inner;

        public RecordingDelegatingMovementService(
            IInventoryMovementService inner)
        {
            _inner = inner;
        }

        public List<List<InventoryPostingLockKey>> PreLockBatches { get; } = [];
        public List<CreateInventoryMovementRequest> CreatedRequests { get; } = [];
        public List<string> Events { get; } = [];

        public async Task PreLockBalancesAsync(
            IEnumerable<InventoryPostingLockKey> keys,
            CancellationToken ct = default)
        {
            var materialized = keys.ToList();
            Events.Add("prelock");
            PreLockBatches.Add(materialized);
            await _inner.PreLockBalancesAsync(materialized, ct);
        }

        public async Task<InventoryMovementResultDto> CreateAsync(
            CreateInventoryMovementRequest request,
            CancellationToken ct = default)
        {
            Events.Add($"movement:{request.ReferenceLineId}");
            CreatedRequests.Add(request);
            return await _inner.CreateAsync(request, ct);
        }

        public Task<decimal> PeekOutboundUnitCostAsync(
            int warehouseId,
            int productVariantId,
            decimal quantity,
            CancellationToken ct = default)
            => _inner.PeekOutboundUnitCostAsync(
                warehouseId,
                productVariantId,
                quantity,
                ct);
    }

    private sealed class FixedCurrentStore : ICurrentStore
    {
        public FixedCurrentStore(int storeId)
        {
            StoreId = storeId;
        }

        public int StoreId { get; }
    }

    private sealed class FixedCurrentUser : ICurrentUser
    {
        public FixedCurrentUser(int userId, int terminalId)
        {
            UserId = userId;
            TerminalId = terminalId;
        }

        public int? UserId { get; }
        public string? UserName => "r1-relational-return";
        public int? TerminalId { get; }
        public string? TerminalCode => "R1-POS";
        public bool IsAuthenticated => true;
    }

    private sealed record RelationalSalesReturnSeed(
        int StoreId,
        int LegalEntityId,
        int LegalEntityWarehouseId,
        int LegalEntityVariantId,
        int LegacyWarehouseId,
        int LegacyVariantId,
        int UserId,
        int TerminalId,
        int OrderId,
        int LegalEntityOrderLineId,
        int LegacyOrderLineId,
        int NoRestockOrderLineId,
        int SalesReturnId = 0,
        int LegacyReturnLineId = 0,
        int LegalEntityReturnLineId = 0,
        int NoRestockReturnLineId = 0,
        int LegacySourceValuationEntryId = 0,
        int LegalEntitySourceValuationEntryId = 0,
        int AllocationId = 0);

    private sealed class BehaviorFixture : IAsyncDisposable
    {
        private BehaviorFixture(
            InMemoryAppDbContext context,
            SalesReturnService service,
            RecordingUnitOfWork unitOfWork,
            RecordingMovementService movements,
            RecordingFragmentService fragments,
            RecordingReversalRepository reversals)
        {
            Context = context;
            Service = service;
            UnitOfWork = unitOfWork;
            Movements = movements;
            Fragments = fragments;
            Reversals = reversals;
        }

        public InMemoryAppDbContext Context { get; }
        public SalesReturnService Service { get; }
        public RecordingUnitOfWork UnitOfWork { get; }
        public RecordingMovementService Movements { get; }
        public RecordingFragmentService Fragments { get; }
        public RecordingReversalRepository Reversals { get; }

        public static async Task<BehaviorFixture> CreateAsync(
            BehaviorScenario scenario,
            bool callerOwnedTransaction = false)
        {
            var currentUser = new TestCurrentUser();
            var tenant = new TenantContext();
            tenant.SetStore(1, "r1-sales-return");
            var options =
                new DbContextOptionsBuilder<InMemoryAppDbContext>()
                    .UseInMemoryDatabase(Guid.NewGuid().ToString())
                    .Options;
            var context = new InMemoryAppDbContext(
                options,
                tenant,
                currentUser);
            context.VerifyRowVersionConfiguration();
            await SeedBehaviorOrderAsync(context);

            var unitOfWork = new RecordingUnitOfWork(
                context,
                callerOwnedTransaction);
            var movements = new RecordingMovementService(unitOfWork);
            var fragments = new RecordingFragmentService(movements.Events);
            var reversals = new RecordingReversalRepository();
            var allocations = new RecordingAllocationRepository();

            if (scenario is BehaviorScenario.Mixed
                or BehaviorScenario.PureLegalEntity
                or BehaviorScenario.PartialLegalEntity)
            {
                allocations.Items.Add(new OrderLegalEntityAllocation
                {
                    Id = 901,
                    StoreId = 1,
                    OrderId = 100,
                    OrderLineId = 10,
                    ProductVariantId = 101,
                    LegalEntityId = 1,
                    WarehouseId = 11,
                    InventoryTransactionId = 501,
                    SalePriority = 1,
                    Quantity = 1m,
                    BaseQuantity = 1m,
                    UnitPrice = 20m,
                    LineTotal = 20m,
                    NetAmount = 20m,
                    AllocationSource =
                        OrderLegalEntityAllocationSource.AutoBySalePriority
                });
            }

            if (scenario is BehaviorScenario.Mixed
                or BehaviorScenario.PureLegalEntity)
            {
                fragments.Items[10] =
                [
                    Fragment(
                        transactionId: 501,
                        warehouseId: 11,
                        variantId: 101,
                        sourceId: 1001,
                        referenceSubKey: "LE:1:WH:11",
                        occurredAtUtc: DateTime.UtcNow.AddMinutes(-2))
                ];
            }
            else if (scenario == BehaviorScenario.PartialLegalEntity)
            {
                fragments.Items[10] =
                [
                    Fragment(
                        transactionId: 501,
                        warehouseId: 11,
                        variantId: 101,
                        sourceId: 1001,
                        referenceSubKey: "LE:1:WH:11",
                        occurredAtUtc: DateTime.UtcNow.AddMinutes(-2)),
                    Fragment(
                        transactionId: 999,
                        warehouseId: 11,
                        variantId: 101,
                        sourceId: 1002,
                        referenceSubKey: "LE:1:WH:11",
                        occurredAtUtc: DateTime.UtcNow.AddMinutes(-1))
                ];
            }

            if (scenario is BehaviorScenario.Mixed
                or BehaviorScenario.PureLegacy
                or BehaviorScenario.MissingBatchCapability)
            {
                fragments.Items[20] =
                [
                    Fragment(
                        transactionId: 777,
                        warehouseId: 22,
                        variantId: 202,
                        sourceId: 2001,
                        referenceSubKey: "legacy-sale",
                        occurredAtUtc: DateTime.UtcNow.AddMinutes(-1))
                ];
            }

            fragments.Items[30] = [];

            var reversal = new OrderLegalEntityReversalService(
                allocations,
                reversals,
                fragments,
                new ReturnCostAllocator(),
                movements,
                new InventoryMovementFactory());
            IOrderLegalEntityReversalService injectedReversal =
                scenario == BehaviorScenario.MissingBatchCapability
                    ? new DirectOnlyReversalService()
                    : reversal;

            var service = new SalesReturnService(
                unitOfWork,
                new OrderRepository(context),
                new SalesReturnRepository(context),
                new POSShiftRepository(context),
                new POSAuditLogRepository(context),
                new WarehouseRepository(context),
                movements,
                new InventoryMovementFactory(),
                new NoOpAuditLogService(),
                new TestCurrentStore(),
                currentUser,
                fragments,
                new ReturnCostAllocator(),
                new NoOpRewardLedgerRepository(),
                new NoOpOrderRewardCalculator(),
                injectedReversal,
                new NoOpDraftInvoiceReturnSyncService());

            return new BehaviorFixture(
                context,
                service,
                unitOfWork,
                movements,
                fragments,
                reversals);
        }

        public CreateSalesReturnRequest CreateRequest(
            params int[] orderLineIds)
            => new()
            {
                OrderId = 100,
                Type = SalesReturnType.ReturnOnly,
                Reason = "R1 mixed return",
                Lines = orderLineIds.Select(orderLineId =>
                    new CreateSalesReturnLineRequest
                    {
                        OrderLineId = orderLineId,
                        ReturnQuantity = 1m,
                        ReturnBaseQuantity = 1m,
                        RefundUnitAmount = 0m,
                        Action = orderLineId == 30
                            ? SalesReturnLineAction.NoRestock
                            : SalesReturnLineAction.Restock
                    }).ToList()
            };

        public MixedObservation Observe()
            => new(
                Movements.PreLockBatches,
                Movements.CreatedRequests,
                Movements.Events);

        public ValueTask DisposeAsync()
            => Context.DisposeAsync();
    }

    private static async Task SeedBehaviorOrderAsync(
        InMemoryAppDbContext context)
    {
        var store = new Store
        {
            Id = 1,
            Name = "R1 Store",
            SubDomain = "r1-sales-return",
            SubDomainNormalized = "R1-SALES-RETURN",
            IsActive = true
        };
        var legalEntity = new LegalEntity
        {
            Id = 1,
            StoreId = 1,
            Code = "R1-LE",
            Name = "R1 Legal Entity",
            LegalName = "R1 Legal Entity",
            IsActive = true
        };
        var warehouse = new Warehouse
        {
            Id = 22,
            StoreId = 1,
            LegalEntityId = 1,
            Code = "R1-LEGACY-WH",
            Name = "R1 Legacy Warehouse",
            IsActive = true
        };
        var product = new Product
        {
            Id = 1000,
            StoreId = 1,
            Name = "R1 Product",
            Alias = "r1-product",
            CategoryId = 1,
            SupplierId = 1,
            BaseUnitId = 1,
            BasePrice = 20m,
            IsActive = true,
            IsSellable = true
        };
        var variants = new[]
        {
            new ProductVariant
            {
                Id = 101,
                StoreId = 1,
                ProductId = product.Id,
                Product = product,
                Sku = "R1-LE-VARIANT",
                CostPrice = 10m,
                Price = 20m,
                IsActive = true
            },
            new ProductVariant
            {
                Id = 202,
                StoreId = 1,
                ProductId = product.Id,
                Product = product,
                Sku = "R1-LEGACY-VARIANT",
                CostPrice = 12m,
                Price = 20m,
                IsActive = true
            },
            new ProductVariant
            {
                Id = 303,
                StoreId = 1,
                ProductId = product.Id,
                Product = product,
                Sku = "R1-NORESTOCK-VARIANT",
                CostPrice = 8m,
                Price = 20m,
                IsActive = true
            }
        };
        var terminal = new POSTerminal
        {
            Id = 7,
            StoreId = 1,
            Code = "R1-POS",
            Name = "R1 POS",
            IsActive = true
        };
        var shift = new POSShift
        {
            Id = 5,
            StoreId = 1,
            TerminalId = terminal.Id,
            WarehouseId = warehouse.Id,
            OpenedByUserId = 9,
            ShiftCode = "R1-SHIFT",
            Status = POSShiftStatus.Open
        };
        var order = new Order
        {
            Id = 100,
            StoreId = 1,
            OrderNumber = "R1-ORDER",
            Status = OrderStatus.Completed,
            PaymentStatus = PaymentStatus.Paid,
            POSShiftId = shift.Id,
            GrandTotal = 60m,
            PaidTotal = 60m,
            CompletedAtUtc = DateTime.UtcNow
        };
        order.Lines.Add(OrderLine(10, 101, "LE item"));
        order.Lines.Add(OrderLine(20, 202, "Legacy item"));
        order.Lines.Add(OrderLine(30, 303, "NoRestock item"));

        context.AddRange(
            store,
            legalEntity,
            warehouse,
            product,
            variants[0],
            variants[1],
            variants[2],
            terminal,
            shift,
            order);
        await context.SaveChangesAsync();
    }

    private static OrderLine OrderLine(
        int id,
        int variantId,
        string itemName)
        => new()
        {
            Id = id,
            StoreId = 1,
            ProductId = 1000,
            VariantId = variantId,
            ItemName = itemName,
            Quantity = 1m,
            BaseQuantity = 1m,
            Multiplier = 1m,
            UnitPrice = 20m,
            OriginalUnitPrice = 20m,
            LineTotal = 20m,
            UnitName = "unit",
            SellingUnitName = "unit"
        };

    private static ReturnableValuationFragmentDto Fragment(
        int transactionId,
        int warehouseId,
        int variantId,
        int sourceId,
        string referenceSubKey,
        DateTime occurredAtUtc)
        => new()
        {
            InventoryTransactionId = transactionId,
            WarehouseId = warehouseId,
            ProductVariantId = variantId,
            SourceValuationEntryId = sourceId,
            ReferenceSubKey = referenceSubKey,
            SourceQuantityAbs = 1m,
            RemainingQuantityAbs = 1m,
            UnitCost = 10m,
            OccurredAtUtc = occurredAtUtc
        };

    private sealed class RecordingUnitOfWork : IUnitOfWork
    {
        private readonly AppDbContext _context;

        public RecordingUnitOfWork(
            AppDbContext context,
            bool callerOwnedTransaction)
        {
            _context = context;
            IsActive = callerOwnedTransaction;
            OwnedByCaller = callerOwnedTransaction;
        }

        public bool IsActive { get; private set; }
        public bool OwnedByCaller { get; }
        public int RollbackCount { get; private set; }

        public Task BeginTransactionAsync(CancellationToken ct = default)
        {
            IsActive = true;
            return Task.CompletedTask;
        }

        public Task CommitTransactionAsync(CancellationToken ct = default)
        {
            if (!OwnedByCaller)
            {
                IsActive = false;
            }

            return Task.CompletedTask;
        }

        public Task RollbackTransactionAsync(CancellationToken ct = default)
        {
            RollbackCount++;
            if (!OwnedByCaller)
            {
                IsActive = false;
            }

            return Task.CompletedTask;
        }

        public Task<int> SaveChangesAsync(CancellationToken ct = default)
            => _context.SaveChangesAsync(ct);
    }

    private sealed class RecordingMovementService
        : IInventoryMovementService
    {
        private readonly RecordingUnitOfWork _unitOfWork;
        private int _nextTransactionId = 3000;

        public RecordingMovementService(RecordingUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }

        public List<List<InventoryPostingLockKey>> PreLockBatches { get; } = [];
        public List<CreateInventoryMovementRequest> CreatedRequests { get; } = [];
        public List<string> Events { get; } = [];

        public Task PreLockBalancesAsync(
            IEnumerable<InventoryPostingLockKey> keys,
            CancellationToken ct = default)
        {
            _unitOfWork.IsActive.Should().BeTrue(
                "a non-empty parent prelock requires the active outer transaction");
            Events.Add("prelock");
            PreLockBatches.Add(keys.ToList());
            return Task.CompletedTask;
        }

        public Task<InventoryMovementResultDto> CreateAsync(
            CreateInventoryMovementRequest request,
            CancellationToken ct = default)
        {
            _unitOfWork.IsActive.Should().BeTrue(
                "movement application must remain inside the outer transaction");
            Events.Add($"movement:{request.ReferenceLineId}");
            CreatedRequests.Add(request);
            return Task.FromResult(new InventoryMovementResultDto
            {
                IsCreated = true,
                InventoryTransactionId = ++_nextTransactionId,
                WarehouseId = request.WarehouseId,
                ProductVariantId = request.ProductVariantId,
                QuantityChange = request.QuantityChange
            });
        }

        public Task<decimal> PeekOutboundUnitCostAsync(
            int warehouseId,
            int productVariantId,
            decimal quantity,
            CancellationToken ct = default)
            => Task.FromResult(10m);
    }

    private sealed class RecordingFragmentService
        : IReturnableValuationFragmentService
    {
        private readonly List<string> _events;

        public RecordingFragmentService(List<string> events)
        {
            _events = events;
        }

        public Dictionary<int, List<ReturnableValuationFragmentDto>> Items
            { get; } = [];
        public List<int> RequestedOrderLineIds { get; } = [];

        public Task<List<ReturnableValuationFragmentDto>>
            GetForOrderLineAsync(
                int orderId,
                int orderLineId,
                CancellationToken ct = default)
        {
            _events.Add($"plan:{orderLineId}");
            RequestedOrderLineIds.Add(orderLineId);
            return Task.FromResult(
                Items.GetValueOrDefault(orderLineId)?.ToList() ?? []);
        }
    }

    private sealed class RecordingAllocationRepository
        : IOrderLegalEntityAllocationRepository
    {
        public List<OrderLegalEntityAllocation> Items { get; } = [];

        public Task<List<OrderLegalEntityAllocation>> GetForOrderAsync(
            int orderId,
            CancellationToken ct = default)
            => Task.FromResult(
                Items.Where(x => x.OrderId == orderId).ToList());

        public Task<bool> AnyForOrderAsync(
            int orderId,
            CancellationToken ct = default)
            => Task.FromResult(Items.Any(x => x.OrderId == orderId));

        public Task<Store?> GetStoreFeatureStateAsync(
            int storeId,
            CancellationToken ct = default)
            => Task.FromResult<Store?>(null);

        public Task<List<LegalEntity>> GetActiveSalesLegalEntitiesAsync(
            int storeId,
            CancellationToken ct = default)
            => Task.FromResult(new List<LegalEntity>());

        public Task<List<InventoryBalance>> LockInventoryForAllocationAsync(
            int storeId,
            IReadOnlyCollection<int> warehouseIds,
            IReadOnlyCollection<int> productVariantIds,
            CancellationToken ct = default)
            => Task.FromResult(new List<InventoryBalance>());

        public Task AddRangeAsync(
            IReadOnlyCollection<OrderLegalEntityAllocation> allocations,
            CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class RecordingReversalRepository
        : IOrderLegalEntityAllocationReversalRepository
    {
        public List<OrderLegalEntityAllocationReversal> Added { get; } = [];

        public Task<Dictionary<int, decimal>>
            GetReversedBaseQuantityBySourceEntryIdsAsync(
                IReadOnlyCollection<int> sourceValuationEntryIds,
                CancellationToken ct = default)
            => Task.FromResult(new Dictionary<int, decimal>());

        public Task<List<OrderLegalEntityAllocationReversal>>
            GetForOrderAsync(
                int orderId,
                CancellationToken ct = default)
            => Task.FromResult(
                Added.Where(x => x.OrderId == orderId).ToList());

        public Task AddRangeAsync(
            IReadOnlyCollection<OrderLegalEntityAllocationReversal> reversals,
            CancellationToken ct = default)
        {
            Added.AddRange(reversals);
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class DirectOnlyReversalService
        : IOrderLegalEntityReversalService
    {
        public Task<bool> ReverseVoidIfAllocatedAsync(
            Order order,
            string reason,
            CancellationToken ct = default)
            => Task.FromResult(false);

        public Task<bool> ReverseSalesReturnLineIfAllocatedAsync(
            Order order,
            SalesReturn salesReturn,
            SalesReturnLine salesReturnLine,
            CancellationToken ct = default)
            => Task.FromResult(false);
    }

    private sealed class TestCurrentStore : ICurrentStore
    {
        public int StoreId => 1;
    }

    private sealed class TestCurrentUser : ICurrentUser
    {
        public int? UserId => 9;
        public string? UserName => "r1-sales-return";
        public int? TerminalId => 7;
        public string? TerminalCode => "R1-POS";
        public bool IsAuthenticated => true;
    }

    private sealed class NoOpAuditLogService : IAuditLogService
    {
        public Task WriteAsync(
            WriteAuditLogRequest request,
            CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<PagedResult<AuditLogListItemDto>> SearchAsync(
            AuditLogQueryDto query,
            CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<AuditLogDetailDto?> GetDetailAsync(
            long id,
            CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed class NoOpDraftInvoiceReturnSyncService
        : IDraftInvoiceReturnSyncService
    {
        public Task EnsurePosReturnAllowedAsync(
            int storeId,
            int orderId,
            IReadOnlyCollection<int> orderLineIds,
            CancellationToken ct = default)
            => Task.CompletedTask;

        public Task SyncAfterReturnAsync(
            int orderId,
            int salesReturnId,
            CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class NoOpOrderRewardCalculator
        : IOrderRewardCalculator
    {
        public Task<OrderRewardCalculationDto> CalculateAsync(
            int orderId,
            CancellationToken ct = default)
            => Task.FromResult(new OrderRewardCalculationDto
            {
                OrderId = orderId
            });
    }

    private sealed class NoOpRewardLedgerRepository
        : ICustomerRewardLedgerRepository
    {
        public Task<decimal> GetBalanceAmountAsync(
            int customerId,
            CancellationToken ct = default)
            => Task.FromResult(0m);

        public Task<bool> HasLedgerForOrderAsync(
            int orderId,
            CancellationToken ct = default)
            => Task.FromResult(false);

        public Task<bool> HasLedgerForSalesReturnAsync(
            int salesReturnId,
            CancellationToken ct = default)
            => Task.FromResult(false);

        public Task AddAsync(
            CustomerRewardLedger ledger,
            CancellationToken ct = default)
            => Task.CompletedTask;

        public Task SaveChangesAsync(CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<bool> HasLedgerForOrderAsync(
            int orderId,
            CustomerRewardLedgerType type,
            CancellationToken ct = default)
            => Task.FromResult(false);

        public Task<decimal> GetOrderLedgerAmountAsync(
            int orderId,
            CustomerRewardLedgerType type,
            CancellationToken ct = default)
            => Task.FromResult(0m);

        public Task<bool> HasLedgerForSalesReturnAsync(
            int salesReturnId,
            CustomerRewardLedgerType type,
            CancellationToken ct = default)
            => Task.FromResult(false);
    }

    private sealed record MixedObservation(
        List<List<InventoryPostingLockKey>> PreLockBatches,
        List<CreateInventoryMovementRequest> CreatedRequests,
        List<string> Events)
    {
        public static MixedObservation Valid()
            => new(
                [
                    [
                        new InventoryPostingLockKey(1, 11, 101),
                        new InventoryPostingLockKey(1, 22, 202)
                    ]
                ],
                [
                    new CreateInventoryMovementRequest
                    {
                        WarehouseId = 22,
                        ProductVariantId = 202,
                        ReferenceLineId = 401,
                        ReferenceSubKey =
                            "RET:401:ALLOC:1:SRC:2001"
                    },
                    new CreateInventoryMovementRequest
                    {
                        WarehouseId = 11,
                        ProductVariantId = 101,
                        ReferenceLineId = 402,
                        ReferenceSubKey =
                            "LE-RET:R402:A901:S1001"
                    }
                ],
                ["plan:20", "plan:10", "plan:30", "prelock", "movement:401", "movement:402"]);
    }

    private static int Position(string source, string value)
    {
        var position = source.IndexOf(value, StringComparison.Ordinal);
        position.Should().BeGreaterThanOrEqualTo(
            0,
            $"source should contain '{value}'");
        return position;
    }

    private static int Count(string source, string value)
    {
        var count = 0;
        var offset = 0;
        while ((offset = source.IndexOf(
                   value,
                   offset,
                   StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += value.Length;
        }

        return count;
    }

    private static int PositionAfter(
        string source,
        string value,
        int startIndex)
    {
        var position = source.IndexOf(
            value,
            startIndex,
            StringComparison.Ordinal);
        position.Should().BeGreaterThanOrEqualTo(
            0,
            $"source after offset {startIndex} should contain '{value}'");
        return position;
    }

    private static string Method(
        string source,
        string startMarker,
        string endMarker)
    {
        var start = Position(source, startMarker);
        var end = source.IndexOf(
            endMarker,
            start + startMarker.Length,
            StringComparison.Ordinal);
        end.Should().BeGreaterThan(
            start,
            $"source should contain method boundary '{endMarker}'");
        return source[start..end];
    }

    private static string ReadRepositoryFile(string relativePath)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null &&
               !File.Exists(Path.Combine(current.FullName, "GaoApp.sln")))
        {
            current = current.Parent;
        }

        current.Should().NotBeNull(
            "the test must run under a GaoApp repository checkout");
        return File.ReadAllText(
            Path.Combine(
                current!.FullName,
                relativePath.Replace('/', Path.DirectorySeparatorChar)))
            .ReplaceLineEndings("\n");
    }
}
