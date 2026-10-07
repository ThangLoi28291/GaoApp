using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.DTOs.Delivery;
using GaoApp.Application.DTOs.Inventory;
using GaoApp.Application.Services.Inventory;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Tests.Delivery;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GaoApp.Tests.Inventory;

[Collection("DeliveryD05"), Trait("Category", "DeliveryD05")]
public sealed class InventorySourceMirrorSqlServerTests(DeliveryD02Fixture fixture)
{
    [Theory]
    [InlineData(12)]
    [InlineData(10)]
    public async Task V01_Source_preserving_transfer_into_negative_keeps_inbound_cost_separate_from_neutral_resolution(decimal incomingCost)
    {
        using var c = await DeliveryD05Case.CreateAsync(fixture, ordered: 1, stock: [(4m, incomingCost)]);
        await PromoteAndRelease(c);
        var transfer = await TransferDocument(c, 4m); var variant = await c.VariantAsync();
        await using (var db = c.Picking.Source.Context())
        {
            var negative = new InventoryMovementFactory().CreateSaleFinalize(transfer.WarehouseId, variant,
                93501, 1, "Destination provisional", 2m, 10m); negative.AllowNegativeBalance = true;
            await InventoryPosPostingContractTests.CreateRealMovementService(db).CreateAsync(negative);
        }
        var finance = await c.FinanceAsync();
        await PostTransfer(c, transfer.DocumentId, transfer.LineId);
        await using var verify = c.Picking.Source.Context();
        var outgoing = await verify.InventoryTransactions.SingleAsync(x => x.ReferenceType == InventoryReferenceType.StockTransfer &&
            x.ReferenceId == transfer.DocumentId.ToString() && x.TransactionType == InventoryTransactionType.TransferOut);
        var incoming = await verify.InventoryTransactions.SingleAsync(x => x.ReferenceType == InventoryReferenceType.StockTransfer &&
            x.ReferenceId == transfer.DocumentId.ToString() && x.TransactionType == InventoryTransactionType.TransferIn);
        Assert.Equal(-4m * incomingCost, outgoing.TotalCost); Assert.Equal(4m * incomingCost, incoming.TotalCost);
        Assert.Equal(-outgoing.TotalCost, incoming.TotalCost); Assert.Equal(4m, incoming.QuantityChange);
        var neutral = Assert.Single(await verify.InventoryTransactions.Where(x => x.ProductVariantId == variant &&
            x.WarehouseId == transfer.WarehouseId && x.TransactionType == InventoryTransactionType.Revaluation).ToArrayAsync());
        Assert.Equal(0m, neutral.QuantityChange); Assert.Equal(20m - 2m * incomingCost, neutral.TotalCost);
        var balance = await verify.InventoryBalances.SingleAsync(x => x.ProductVariantId == variant && x.WarehouseId == transfer.WarehouseId);
        Assert.Equal(2m, balance.OnHandQty); Assert.Equal(2m * incomingCost, balance.InventoryValue);
        var sourceParts = await verify.InventoryCostBudgetDebits.Where(x => x.InventoryTransactionId == outgoing.Id).ToArrayAsync();
        var inParts = await verify.InventoryValuationEntries.Where(x => x.InventoryTransactionId == incoming.Id).ToArrayAsync();
        Assert.Equal(4m, sourceParts.Sum(x => x.EndQuantity - x.StartQuantity)); Assert.Equal(4m * incomingCost, sourceParts.Sum(x => x.Amount));
        Assert.Equal(4m * incomingCost, inParts.Sum(x => x.Amount));
        Assert.All(inParts, part => Assert.Contains(sourceParts, source => source.InventoryValuationEntryId == part.SourceValuationEntryId && source.Amount == part.Amount));
        var resolution = Assert.Single(await verify.InventoryCostBudgetDebits.Where(x => x.ProductVariantId == variant && x.IsProvisionalResolution).ToArrayAsync());
        Assert.Equal(2m * incomingCost, resolution.Amount); Assert.Equal(2m, resolution.EndQuantity - resolution.StartQuantity);
        Assert.Equal(finance, await c.FinanceAsync());
        var snapshot = await c.SnapshotAsync(); await PostTransfer(c, transfer.DocumentId, transfer.LineId);
        Assert.Equal(snapshot, await c.SnapshotAsync());
    }

    [Fact]
    public async Task V02_Failure_after_transfer_inbound_before_neutral_resolution_rolls_back_both_warehouses_and_retry_conserves_cost()
    {
        using var c = await DeliveryD05Case.CreateAsync(fixture, ordered: 1, stock: [(4m, 12m)]);
        await PromoteAndRelease(c); var transfer = await TransferDocument(c, 4m); var variant = await c.VariantAsync();
        await using (var db = c.Picking.Source.Context())
        {
            var negative = new InventoryMovementFactory().CreateSaleFinalize(transfer.WarehouseId, variant, 93502, 1, "Negative destination", 2m, 10m);
            negative.AllowNegativeBalance = true; await InventoryPosPostingContractTests.CreateRealMovementService(db).CreateAsync(negative);
        }
        var before = await c.SnapshotAsync();
        await Assert.ThrowsAsync<D05InjectedFailure>(() => PostTransfer(c, transfer.DocumentId, transfer.LineId, new NeutralResolutionFailure()));
        Assert.Equal(before, await c.SnapshotAsync());
        await PostTransfer(c, transfer.DocumentId, transfer.LineId);
        await using var verify = c.Picking.Source.Context();
        var balances = await verify.InventoryBalances.Where(x => x.ProductVariantId == variant).ToArrayAsync();
        Assert.Equal(2m, balances.Sum(x => x.OnHandQty)); Assert.Equal(24m, balances.Sum(x => x.InventoryValue));
        Assert.Equal(2, await verify.InventoryTransactions.CountAsync(x => x.ReferenceType == InventoryReferenceType.StockTransfer && x.ReferenceId == transfer.DocumentId.ToString()));
        Assert.Single(await verify.InventoryTransactions.Where(x => x.ProductVariantId == variant && x.TransactionType == InventoryTransactionType.Revaluation).ToArrayAsync());
    }

    [Fact]
    public async Task V03_Policy1_source_generic_reversal_is_fail_closed_and_policy0_legacy_mirror_still_works()
    {
        using var protectedCase = await DeliveryD05Case.CreateAsync(fixture); await protectedCase.ReadyAsync(); await protectedCase.DispatchAsync();
        await using (var db = protectedCase.Picking.Source.Context())
        {
            var part = await db.DeliveryDispatchCostFragments.Where(x => x.DeliveryOrderId == protectedCase.Id).OrderBy(x => x.Id).FirstAsync();
            var request = new InventoryMovementFactory().CreateSaleRefund(part.SourceWarehouseId, part.VariantId,
                93503, 1, 1m, part.UnitCost, "Generic unsupported mirror", DateTime.UtcNow,
                "D05-PROTECTED", part.InventoryValuationEntryId);
            var before = await protectedCase.SnapshotAsync();
            var error = await Assert.ThrowsAsync<BusinessRuleException>(() => InventoryPosPostingContractTests.CreateRealMovementService(db).CreateAsync(request));
            Assert.Contains("SOURCE_BUDGET_REVERSAL_UNSUPPORTED", error.Message, StringComparison.Ordinal);
            Assert.Equal(before, await protectedCase.SnapshotAsync());
        }
        using var legacyCase = await DeliveryD05Case.CreateAsync(fixture, ordered: 1, stock: [(4m, 10m)]);
        var variant = await legacyCase.VariantAsync();
        await using (var db = legacyCase.Picking.Source.Context())
        {
            var service = InventoryPosPostingContractTests.CreateRealMovementService(db); var factory = new InventoryMovementFactory();
            var source = await service.CreateAsync(factory.CreateSaleFinalize(legacyCase.Picking.Account.Store.WarehouseId, variant, 93504, 1, "Legacy issue", 2m, 10m));
            var valuation = await db.InventoryValuationEntries.SingleAsync(x => x.InventoryTransactionId == source.InventoryTransactionId && x.EntryType == InventoryValuationEntryType.Outbound);
            var request = factory.CreateSaleRefund(legacyCase.Picking.Account.Store.WarehouseId, variant, 93505, 1, 1m, 10m,
                "Legacy source0 mirror", DateTime.UtcNow, "D05-LEGACY", valuation.Id, valuation.ReferenceSubKey);
            var received = await service.CreateAsync(request); Assert.Equal(1m, received.QuantityChange); Assert.Equal(10m, received.ValueChange);
            Assert.NotNull(received.InventoryTransactionId);
            var receivedProof = await db.InventoryTransactions.AsNoTracking().SingleAsync(x => x.Id == received.InventoryTransactionId);
            var persisted = System.Text.Json.JsonSerializer.Serialize(receivedProof);
            var beforeReplay = await legacyCase.SnapshotAsync();
            var replay = await service.CreateAsync(request);
            Assert.True(replay.IsSkipped); Assert.False(replay.IsCreated); Assert.Null(replay.InventoryTransactionId);
            Assert.Equal(beforeReplay, await legacyCase.SnapshotAsync());
            Assert.Equal(1, await db.InventoryTransactions.CountAsync(x => x.IdempotencyKey == receivedProof.IdempotencyKey));
            Assert.Equal(persisted, System.Text.Json.JsonSerializer.Serialize(await db.InventoryTransactions.AsNoTracking().SingleAsync(x => x.Id == received.InventoryTransactionId)));
            Assert.Empty(await db.InventoryCostBudgetDebits.Where(x => x.ProductVariantId == variant).ToArrayAsync());
        }
    }

    internal static async Task PromoteAndRelease(DeliveryD05Case c)
    {
        await c.Picking.ClaimAsync(); await c.Picking.ReportAsync("1"); var detail = await c.Picking.GetAsync();
        await using var db = c.Picking.Source.Context();
        await c.Service(db).CancelBeforeHandoverAsync(c.Id, new(Guid.NewGuid(), detail.Delivery.Version, "Release fixture hold for transfer"));
    }
    internal static async Task<(int DocumentId, int LineId, int WarehouseId)> TransferDocument(DeliveryD05Case c, decimal quantity)
    {
        await using var db = c.Picking.Source.Context(); var variant = await c.VariantAsync();
        var product = await db.ProductVariants.Include(x => x.Product).SingleAsync(x => x.Id == variant);
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var warehouse = new Warehouse { StoreId = c.Picking.Account.Store.StoreId, LegalEntityId = c.Picking.Source.Detail.SourceLegalEntityId,
            Code = "D05-" + suffix, Name = "D05 owned destination " + suffix, AllowNegativeInventory = true };
        db.Warehouses.Add(warehouse); await db.SaveChangesAsync();
        var document = new StockTransferDocument { StoreId = warehouse.StoreId, DocumentNo = "D05-" + Guid.NewGuid().ToString("N"),
            DocumentDate = DateTime.UtcNow, FromWarehouseId = c.Picking.Account.Store.WarehouseId, ToWarehouseId = warehouse.Id,
            Status = StockTransferDocumentStatus.Confirmed };
        var line = new StockTransferLine { StoreId = warehouse.StoreId, StockTransferDocument = document, LineNo = 1,
            ProductVariantId = variant, UnitId = product.Product.BaseUnitId, UnitNameSnapshot = "D05 base", Factor = 1,
            Quantity = quantity, BaseQuantity = quantity, ProductNameSnapshot = "D05 source mirror" };
        document.Lines.Add(line); db.StockTransferDocuments.Add(document); await db.SaveChangesAsync();
        return (document.Id, line.Id, warehouse.Id);
    }
    internal static async Task PostTransfer(DeliveryD05Case c, int documentId, int lineId, IInterceptor? interceptor = null)
    {
        await using var db = c.Picking.Source.Context(interceptor); await using var tx = await db.Database.BeginTransactionAsync();
        Assert.True(await InventoryPosPostingContractTests.CreateRealMovementService(db).TryCreateSourcePreservingTransferAsync(new(documentId, lineId)));
        await tx.CommitAsync();
    }
    private sealed class NeutralResolutionFailure : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<InventoryTransaction>().Any(x =>
                x.State == EntityState.Added && x.Entity.TransactionType == InventoryTransactionType.Revaluation)) throw new D05InjectedFailure();
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
