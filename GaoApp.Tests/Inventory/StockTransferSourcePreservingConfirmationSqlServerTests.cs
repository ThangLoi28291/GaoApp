using GaoApp.Application.Services.Inventory;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Repositories.Inventory;
using GaoApp.Tests.Delivery;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Inventory;

[Collection("DeliveryD05"), Trait("Category", "DeliveryD05")]
public sealed class StockTransferSourcePreservingConfirmationSqlServerTests(DeliveryD02Fixture fixture)
{
    [Theory]
    [InlineData(10)]
    [InlineData(12)]
    public async Task Document_lock_and_confirmation_preserve_promoted_source_fragments_and_reject_replay(decimal cost)
    {
        using var c = await DeliveryD05Case.CreateAsync(fixture, ordered: 1, stock: [(4m, cost)]);
        await InventorySourceMirrorSqlServerTests.PromoteAndRelease(c);
        var transfer = await InventorySourceMirrorSqlServerTests.TransferDocument(c, 4m);
        // The shared source-evidence fixture normally starts with a confirmed
        // header; this case exercises the real PendingApproval -> Confirmed flow.
        await using (var db = c.Picking.Source.Context())
        {
            var header = await db.StockTransferDocuments.SingleAsync(x => x.Id == transfer.DocumentId);
            header.Status = StockTransferDocumentStatus.PendingApproval;
            await db.SaveChangesAsync();
            var service = new StockTransferService(new StockTransferRepository(db), new InventoryMovementFactory(),
                InventoryPosPostingContractTests.CreateRealMovementService(db), null!);
            await service.ConfirmAsync(transfer.DocumentId);
            Assert.Null(db.Database.CurrentTransaction);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.ConfirmAsync(transfer.DocumentId));
            Assert.Null(db.Database.CurrentTransaction);
        }
        await using var verify = c.Picking.Source.Context();
        Assert.Equal(StockTransferDocumentStatus.Confirmed,
            (await verify.StockTransferDocuments.SingleAsync(x => x.Id == transfer.DocumentId)).Status);
        var ledger = await verify.InventoryTransactions.Where(x => x.ReferenceType == InventoryReferenceType.StockTransfer &&
            x.ReferenceId == transfer.DocumentId.ToString()).ToArrayAsync();
        Assert.Equal(2, ledger.Length);
        var outbound = Assert.Single(ledger, x => x.TransactionType == InventoryTransactionType.TransferOut);
        var inbound = Assert.Single(ledger, x => x.TransactionType == InventoryTransactionType.TransferIn);
        Assert.Equal(-4m, outbound.QuantityChange); Assert.Equal(4m, inbound.QuantityChange);
        Assert.Equal(-4m * cost, outbound.TotalCost); Assert.Equal(4m * cost, inbound.TotalCost);
        var debits = await verify.InventoryCostBudgetDebits.Where(x => x.InventoryTransactionId == outbound.Id).ToArrayAsync();
        var fragments = await verify.InventoryValuationEntries.Where(x => x.InventoryTransactionId == inbound.Id).ToArrayAsync();
        Assert.NotEmpty(debits); Assert.NotEmpty(fragments);
        Assert.Equal(4m, debits.Sum(x => x.EndQuantity - x.StartQuantity));
        Assert.Equal(4m * cost, debits.Sum(x => x.Amount));
        Assert.Equal(4m * cost, fragments.Sum(x => x.Amount));
        Assert.All(fragments, part => Assert.Contains(debits, source =>
            source.InventoryValuationEntryId == part.SourceValuationEntryId && source.Amount == part.Amount));
    }
}
