using GaoApp.Domain.Storefront;
using GaoApp.Application.Storefront;
using GaoApp.Domain.Delivery;
using GaoApp.Domain.Enums;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Storefront;

[Collection("StorefrontWeb01"), Trait("Category", "StorefrontWeb01")]
public sealed class Web01CommerceSchemaTests(Web01Fixture fixture)
{
    [Fact]
    public async Task Quotes_orders_and_receipts_are_independent_of_pos_shift_and_physical_stock()
    {
        var c = await fixture.CaseAsync(); var commerce = await c.CommerceAsync(); await using var db = c.Context();
        Assert.Single(await db.StorefrontQuotes.ToListAsync()); Assert.Single(await db.StorefrontOrders.ToListAsync());
        Assert.Single(await db.Set<StorefrontPaymentIntent>().ToListAsync()); Assert.Single(await db.Set<StorefrontBankReceipt>().ToListAsync());
        Assert.Equal(230_000, (await db.StorefrontOrders.SingleAsync()).TotalDue);
        Assert.Empty(await db.POSTerminals.ToListAsync()); Assert.Empty(await db.POSShifts.ToListAsync()); Assert.Empty(await db.Orders.ToListAsync());
        Assert.Empty(await db.InventoryTransactions.ToListAsync()); Assert.Empty(await db.OrderPayments.ToListAsync());
        Assert.Null((await db.Set<StorefrontFulfillmentSource>().SingleAsync(x => x.Id == commerce.FulfillmentId)).DeliveryOrderId);
    }

    [Theory]
    [InlineData("quoteScope")]
    [InlineData("orderLineScope")]
    [InlineData("intentScope")]
    [InlineData("receiptScope")]
    [InlineData("lineSourceOrder")]
    [InlineData("refundOrder")]
    [InlineData("outboxRevision")]
    public async Task Commerce_composite_foreign_keys_reject_cross_store_and_cross_order_links(string target)
    {
        var c = await fixture.CaseAsync(); var own = await c.CommerceAsync(); var other = await fixture.CaseAsync(); var foreign = await other.CommerceAsync();
        await using var db = c.Context();
        FormattableString command = target switch
        {
            "quoteScope" => $"UPDATE StorefrontOrders SET QuoteId={foreign.QuoteId} WHERE Id={own.OrderId}",
            "orderLineScope" => $"INSERT INTO StorefrontOrderLines (StoreId,StorefrontOrderId,ProductId,VariantId,ProductUnitConversionId,SellingUnitId,ItemName,UnitName,Quantity,BaseMultiplier,BaseQuantity,UnitPrice,Discount,NetAmount,WeightKgPerSellingUnit,IsGift,SnapshotJson,IsDeleted) SELECT StoreId,{foreign.OrderId},ProductId,VariantId,ProductUnitConversionId,SellingUnitId,ItemName,UnitName,Quantity,BaseMultiplier,BaseQuantity,UnitPrice,Discount,NetAmount,WeightKgPerSellingUnit,IsGift,SnapshotJson,0 FROM StorefrontOrderLines WHERE Id={own.LineId}",
            "intentScope" => $"INSERT INTO StorefrontPaymentIntents (StoreId,StorefrontOrderId,IsActive,Provider,ProviderReference,BankAccountId,ReceivingLegalEntityId,AmountDue,ExpiresAtUtc,BankTerminalCode,BankUserCode,BindingSnapshotJson,IsDeleted) SELECT StoreId,{foreign.OrderId},0,Provider,'W999999999991',BankAccountId,ReceivingLegalEntityId,AmountDue,ExpiresAtUtc,BankTerminalCode,BankUserCode,BindingSnapshotJson,0 FROM StorefrontPaymentIntents WHERE Id={own.IntentId}",
            "receiptScope" => $"INSERT INTO StorefrontPaymentAllocations (StoreId,ReceiptVerified,BankReceiptId,PaymentIntentId,StorefrontOrderId,BankAccountId,ReceivingLegalEntityId,Amount,IsDeleted) VALUES ({c.StoreId},1,{foreign.ReceiptId},{own.IntentId},{own.OrderId},{c.BankAccountId},{c.LegalEntityId},230000,0)",
            "lineSourceOrder" => $"INSERT INTO StorefrontLineSources (StoreId,OrderLineId,FulfillmentSourceId,StorefrontOrderId,BaseQuantity,RecordedCost,IsProvisionalCost,IsDeleted) VALUES ({c.StoreId},{own.LineId},{foreign.FulfillmentId},{own.OrderId},1,10,0,0)",
            "refundOrder" => $"INSERT INTO StorefrontRefunds (StoreId,StorefrontOrderId,State,Reason,GoodsAmount,ShippingAmount,UnallocatedReceiptAmount,Amount,IsDeleted) VALUES ({c.StoreId},{foreign.OrderId},1,'Synthetic',200000,30000,0,230000,0)",
            _ => $"INSERT INTO StorefrontOutboxMessages (StoreId,EventId,StorefrontOrderId,Revision,Kind,PayloadJson,Attempts,IsDeleted) VALUES ({c.StoreId},{Guid.NewGuid()},{own.OrderId},999,'synthetic','{{}}',0,0)"
        };
        var error = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync(command)); Assert.Equal(547, error.Number);
    }

    [Theory]
    [InlineData("receiptAmount")]
    [InlineData("intentAmount")]
    [InlineData("unverifiedReceipt")]
    [InlineData("duplicateReceipt")]
    [InlineData("duplicateIntent")]
    public async Task Allocation_requires_exact_verified_bound_receipt_and_is_unique(string invalid)
    {
        var c = await fixture.CaseAsync(); var own = await c.CommerceAsync(); await using var db = c.Context();
        if (invalid == "receiptAmount")
        {
            await c.AdditionalSchemaReceiptAsync(db, own.IntentId, 100_000);
            var shortId = await db.Set<StorefrontBankReceipt>().Where(x => x.Amount == 100_000).Select(x => x.Id).SingleAsync();
            var failure = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO StorefrontPaymentAllocations (StoreId,ReceiptVerified,BankReceiptId,PaymentIntentId,StorefrontOrderId,BankAccountId,ReceivingLegalEntityId,Amount,IsDeleted) VALUES ({c.StoreId},1,{shortId},{own.IntentId},{own.OrderId},{c.BankAccountId},{c.LegalEntityId},230000,0)"));
            Assert.Equal(547, failure.Number); return;
        }
        if (invalid is "duplicateReceipt" or "duplicateIntent")
        {
            await Allocate(c, own, db);
            if (invalid == "duplicateReceipt")
            {
                var anotherIntent = await c.AdditionalInactiveSchemaIntentAsync(db, own.IntentId); own = own with { IntentId = anotherIntent.Id };
            }
            else
            {
                var anotherReceipt = await c.AdditionalSchemaReceiptAsync(db, own.IntentId, 230_000); own = own with { ReceiptId = anotherReceipt.Id };
            }
        }
        var amount = invalid == "intentAmount" ? 100_000m : 230_000m;
        var verified = invalid != "unverifiedReceipt";
        var error = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO StorefrontPaymentAllocations (StoreId,ReceiptVerified,BankReceiptId,PaymentIntentId,StorefrontOrderId,BankAccountId,ReceivingLegalEntityId,Amount,IsDeleted) VALUES ({c.StoreId},{verified},{own.ReceiptId},{own.IntentId},{own.OrderId},{c.BankAccountId},{c.LegalEntityId},{amount},0)"));
        if (invalid.StartsWith("duplicate", StringComparison.Ordinal)) Assert.Contains(error.Number, new[] { 2601, 2627 }); else Assert.Equal(547, error.Number);
    }

    [Theory]
    [InlineData("orderTotal")]
    [InlineData("orderPhase")]
    [InlineData("guestHash")]
    [InlineData("orderFee")]
    [InlineData("intentTotal")]
    [InlineData("receiptUnverified")]
    [InlineData("refundTotal")]
    public async Task Sql_checks_protect_money_hashes_states_and_bank_provenance(string invalid)
    {
        var c = await fixture.CaseAsync(); var own = await c.CommerceAsync(); await using var db = c.Context();
        FormattableString command = invalid switch
        {
            "orderTotal" => $"UPDATE StorefrontOrders SET TotalDue=1 WHERE Id={own.OrderId}",
            "orderPhase" => $"UPDATE StorefrontOrders SET Phase=99 WHERE Id={own.OrderId}",
            "guestHash" => $"UPDATE StorefrontOrders SET GuestTokenHash='public-code-is-not-a-secret' WHERE Id={own.OrderId}",
            "orderFee" => $"UPDATE StorefrontOrders SET ShippingFeeTreatment=2 WHERE Id={own.OrderId}",
            "intentTotal" => $"INSERT INTO StorefrontPaymentIntents (StoreId,StorefrontOrderId,IsActive,Provider,ProviderReference,BankAccountId,ReceivingLegalEntityId,AmountDue,ExpiresAtUtc,BankTerminalCode,BankUserCode,BindingSnapshotJson,IsDeleted) SELECT StoreId,StorefrontOrderId,0,Provider,'W999999999992',BankAccountId,ReceivingLegalEntityId,1,ExpiresAtUtc,BankTerminalCode,BankUserCode,BindingSnapshotJson,0 FROM StorefrontPaymentIntents WHERE Id={own.IntentId}",
            "receiptUnverified" => $"INSERT INTO StorefrontBankReceipts (StoreId,Provider,BankAccountId,ReceivingLegalEntityId,ExternalTransactionId,EvidenceFingerprint,ProtectedEvidence,Amount,OccurredAtUtc,IsVerified,NeedsReview,IsDeleted) VALUES ({c.StoreId},'ACB',{c.BankAccountId},{c.LegalEntityId},'UNVERIFIED','synthetic','synthetic',230000,SYSUTCDATETIME(),0,0,0)",
            _ => $"INSERT INTO StorefrontRefunds (StoreId,StorefrontOrderId,State,Reason,GoodsAmount,ShippingAmount,UnallocatedReceiptAmount,Amount,IsDeleted) VALUES ({c.StoreId},{own.OrderId},1,'Synthetic',200000,30000,0,1,0)"
        };
        var error = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync(command)); Assert.Equal(547, error.Number);
    }

    [Theory]
    [InlineData("quote")]
    [InlineData("line")]
    [InlineData("intent")]
    [InlineData("receipt")]
    [InlineData("rate")]
    [InlineData("allocation")]
    [InlineData("command")]
    [InlineData("event")]
    [InlineData("lineSource")]
    [InlineData("fulfillment")]
    [InlineData("refundEvidence")]
    [InlineData("outbox")]
    [InlineData("receiptCase")]
    [InlineData("receiptSpace")]
    [InlineData("intentCase")]
    [InlineData("intentSpace")]
    [InlineData("outboxCase")]
    [InlineData("outboxSpace")]
    [InlineData("fulfillmentCase")]
    [InlineData("fulfillmentSpace")]
    public async Task Sql_triggers_reject_rewriting_financial_evidence_and_snapshots(string target)
    {
        if (target == "lineSource")
        {
            var physical = await Web06Case.CreateAsync(fixture, quantity: 1m); await physical.PrepareAsync(); await physical.PickAllAsync(); await physical.DispatchAsync();
            await using var sourceDb = physical.Context(); var actualSource = await sourceDb.Set<StorefrontLineSource>().AsNoTracking().SingleAsync();
            var transactionId = Assert.IsType<int>(actualSource.InventoryTransactionId);
            var transaction = await sourceDb.InventoryTransactions.AsNoTracking().SingleAsync(x => x.Id == transactionId);
            Assert.Equal(InventoryReferenceType.Delivery, transaction.ReferenceType); Assert.Equal(InventoryTransactionType.DeliveryDispatchOut, transaction.TransactionType);
            Assert.Equal(-actualSource.BaseQuantity, transaction.QuantityChange);
            var journal = await sourceDb.DeliveryJournalEntries.AsNoTracking().SingleAsync(x => x.InventoryTransactionId == transactionId && x.Kind == DeliveryJournalKind.Dispatch);
            Assert.Equal(actualSource.BaseQuantity, journal.BaseQuantity); Assert.Equal(actualSource.RecordedCost, journal.CostAmount);
            var slices = await sourceDb.DeliveryDispatchCostFragments.AsNoTracking().Where(x => x.DeliveryJournalEntryId == journal.Id).ToListAsync();
            Assert.NotEmpty(slices); Assert.Equal(actualSource.RecordedCost, slices.Sum(x => x.CostAmount));
            var beforePhysical = await physical.PhysicalSnapshotAsync();
            var beforeSource = await sourceDb.Database.SqlQueryRaw<string>("SELECT (SELECT * FROM StorefrontLineSources WHERE Id={0} FOR JSON PATH) AS [Value]", actualSource.Id).SingleAsync();
            var denied = await Assert.ThrowsAsync<SqlException>(() => sourceDb.Database.ExecuteSqlInterpolatedAsync($"UPDATE StorefrontLineSources SET RecordedCost={actualSource.RecordedCost + 10m} WHERE Id={actualSource.Id}"));
            Assert.Equal(51090, denied.Number);
            Assert.Equal(beforeSource, await sourceDb.Database.SqlQueryRaw<string>("SELECT (SELECT * FROM StorefrontLineSources WHERE Id={0} FOR JSON PATH) AS [Value]", actualSource.Id).SingleAsync());
            Assert.Equal(beforePhysical, await physical.PhysicalSnapshotAsync()); return;
        }
        if (target == "refundEvidence")
        {
            var paid = await Web07Case.CreatePhysicalAsync(fixture, prepaidQr: true);
            await using (var current = paid.Context()) await Web07Support.Cancellation(paid, current, guest: true).CancelGuestAsync(await paid.CancelRequestAsync());
            StorefrontRefundRead approved;
            await using (var current = paid.Context())
            {
                var requested = Assert.Single(await Web07Support.Refunds(paid, current).GetAsync(paid.OrderId));
                Assert.Equal(230_000m, requested.Amount);
                await Web07Support.Refunds(paid, current).ApproveAsync(paid.OrderId, await paid.RefundDecisionAsync(requested.Id, "WEB01 immutable evidence with actual approved funding"));
                approved = Assert.Single(await Web07Support.Refunds(paid, current).GetAsync(paid.OrderId));
                Assert.Equal(StorefrontRefundState.Approved, approved.State);
            }
            var debit = await Web07RecordedDebit.CashAsync(paid, 230_000m);
            await using (var current = paid.Context()) await Web07Support.Refunds(paid, current).ConfirmExecutionAsync(paid.OrderId,
                await paid.ExecutionRequestAsync(approved.Id, debit.Source, paid.Seed.LegalEntityId, null, debit.ExternalReference, null));
            await using var evidenceDb = paid.Context();
            var actualEvidence = Assert.Single(await evidenceDb.Set<StorefrontRefundEvidence>().AsNoTracking().ToListAsync());
            Assert.Equal(230_000m, actualEvidence.Amount); Assert.NotNull(actualEvidence.FundingId); Assert.NotNull(actualEvidence.PayoutSourceKind);
            var beforeEvidence = await evidenceDb.Database.SqlQueryRaw<string>("SELECT (SELECT * FROM StorefrontRefundEvidence WHERE Id={0} FOR JSON PATH) AS [Value]", actualEvidence.Id).SingleAsync();
            var beforeMoney = await paid.SnapshotAsync(); var beforeStock = await paid.Physical!.PhysicalSnapshotAsync(); var beforeFlow = await Web07RefundExecutionTests.FlowAsync(paid);
            var immutable = await Assert.ThrowsAsync<SqlException>(() => evidenceDb.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM StorefrontRefundEvidence WHERE Id={actualEvidence.Id}"));
            Assert.Equal(51090, immutable.Number);
            Assert.Equal(beforeEvidence, await evidenceDb.Database.SqlQueryRaw<string>("SELECT (SELECT * FROM StorefrontRefundEvidence WHERE Id={0} FOR JSON PATH) AS [Value]", actualEvidence.Id).SingleAsync());
            Assert.Equal(beforeMoney, await paid.SnapshotAsync()); Assert.Equal(beforeStock, await paid.Physical.PhysicalSnapshotAsync()); Assert.Equal(beforeFlow, await Web07RefundExecutionTests.FlowAsync(paid)); return;
        }
        var c = await fixture.CaseAsync(); var own = await c.CommerceAsync(); await using var db = c.Context();
        await Allocate(c, own, db);
        var refund = new StorefrontRefund { StoreId = c.StoreId, StorefrontOrderId = own.OrderId, Reason = "Synthetic cancellation", GoodsAmount = 200_000, ShippingAmount = 30_000, Amount = 230_000 };
        db.Add(refund); await db.SaveChangesAsync();
        var commandReceipt = new StorefrontCommandReceipt { StoreId = c.StoreId, ClientRequestId = Guid.NewGuid(), Operation = "synthetic", RequestHash = new string('h', 64), StorefrontOrderId = own.OrderId };
        var ev = new StorefrontEvent { StoreId = c.StoreId, EventId = Guid.NewGuid(), StorefrontOrderId = own.OrderId, Revision = 1, Kind = "synthetic", CausationId = Guid.NewGuid() };
        db.AddRange(commandReceipt, ev); await db.SaveChangesAsync();
        var outbox = new StorefrontOutboxMessage { StoreId = c.StoreId, EventId = ev.EventId, StorefrontOrderId = own.OrderId, Revision = 1, Kind = "synthetic", PayloadJson = "{\"kind\":\"synthetic\"}" };
        db.Add(outbox); await db.SaveChangesAsync();
        FormattableString command = target switch
        {
            "quote" => $"UPDATE StorefrontQuotes SET SnapshotJson='{{\"changed\":true}}' WHERE Id={own.QuoteId}",
            "line" => $"UPDATE StorefrontOrderLines SET ItemName='Changed' WHERE Id={own.LineId}",
            "intent" => $"UPDATE StorefrontPaymentIntents SET BankTerminalCode='Changed' WHERE Id={own.IntentId}",
            "receipt" => $"UPDATE StorefrontBankReceipts SET ProtectedEvidence='Changed' WHERE Id={own.ReceiptId}",
            "rate" => $"UPDATE StorefrontShippingRates SET BaseFee=1 WHERE Id={own.RateId}",
            "allocation" => $"DELETE FROM StorefrontPaymentAllocations WHERE StorefrontOrderId={own.OrderId}",
            "command" => $"UPDATE StorefrontCommandReceipts SET OutcomeJson='{{\"changed\":true}}' WHERE Id={commandReceipt.Id}",
            "event" => $"UPDATE StorefrontEvents SET PayloadJson='{{\"changed\":true}}' WHERE Id={ev.Id}",
            "fulfillment" => $"UPDATE StorefrontFulfillmentSources SET DispatchPostingKey='Changed' WHERE Id={own.FulfillmentId}",
            "receiptCase" => $"UPDATE StorefrontBankReceipts SET ProtectedEvidence=UPPER(ProtectedEvidence) WHERE Id={own.ReceiptId}",
            "receiptSpace" => $"UPDATE StorefrontBankReceipts SET ProtectedEvidence=ProtectedEvidence+' ' WHERE Id={own.ReceiptId}",
            "intentCase" => $"UPDATE StorefrontPaymentIntents SET BankTerminalCode=LOWER(BankTerminalCode) WHERE Id={own.IntentId}",
            "intentSpace" => $"UPDATE StorefrontPaymentIntents SET BankTerminalCode=BankTerminalCode+' ' WHERE Id={own.IntentId}",
            "outboxCase" => $"UPDATE StorefrontOutboxMessages SET PayloadJson=UPPER(PayloadJson) WHERE Id={outbox.Id}",
            "outboxSpace" => $"UPDATE StorefrontOutboxMessages SET PayloadJson=PayloadJson+' ' WHERE Id={outbox.Id}",
            "fulfillmentCase" => $"UPDATE StorefrontFulfillmentSources SET DispatchPostingKey=LOWER(DispatchPostingKey) WHERE Id={own.FulfillmentId}",
            "fulfillmentSpace" => $"UPDATE StorefrontFulfillmentSources SET DispatchPostingKey=DispatchPostingKey+' ' WHERE Id={own.FulfillmentId}",
            _ => $"UPDATE StorefrontOutboxMessages SET PayloadJson='{{\"changed\":true}}' WHERE Id={outbox.Id}"
        };
        var error = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync(command)); Assert.Equal(51090, error.Number);
    }

    [Fact]
    public async Task Immutable_data_guards_still_allow_documented_transport_status_updates()
    {
        var c = await fixture.CaseAsync(); var own = await c.CommerceAsync(); await using var db = c.Context();
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE StorefrontBankReceipts SET NeedsReview=1 WHERE Id={own.ReceiptId}");
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE StorefrontBankReceipts SET ProtectedEvidence=ProtectedEvidence WHERE Id={own.ReceiptId}");
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE StorefrontPaymentIntents SET IsActive=0 WHERE Id={own.IntentId}");
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE StorefrontShippingRates SET IsCurrent=0 WHERE Id={own.RateId}");
        Assert.True((await db.Set<StorefrontBankReceipt>().AsNoTracking().SingleAsync()).NeedsReview);
        Assert.False((await db.Set<StorefrontPaymentIntent>().AsNoTracking().SingleAsync()).IsActive);
        Assert.False((await db.StorefrontShippingRates.AsNoTracking().SingleAsync()).IsCurrent);
    }

    private static async Task Allocate(Web01Case c, Web01Commerce own, GaoApp.Infrastructure.Data.AppDbContext db)
    {
        db.Add(new StorefrontPaymentAllocation { StoreId = c.StoreId, BankReceiptId = own.ReceiptId, PaymentIntentId = own.IntentId,
            StorefrontOrderId = own.OrderId, BankAccountId = c.BankAccountId, ReceivingLegalEntityId = c.LegalEntityId, Amount = 230_000 });
        await db.SaveChangesAsync();
    }
}
