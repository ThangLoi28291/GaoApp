using GaoApp.Domain.Entities;
using GaoApp.Application.Storefront;
using GaoApp.Domain.Enums;
using GaoApp.Domain.Storefront;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Storefront;

[Collection("StorefrontWeb07"), Trait("Category", "StorefrontWeb07")]
public sealed class Web07ReturnTests(Web01Fixture fixture)
{
    [Theory]
    [InlineData(false, false)] [InlineData(true, false)]
    [InlineData(false, true)] [InlineData(true, true)]
    public async Task Pending_guest_request_is_superseded_by_full_actual_return_without_cancellation_approval_and_preserves_all_received_money(
        bool incident, bool additionalMoneyReview)
    {
        var c = await Web07Case.CreatePhysicalAsync(fixture, prepaidQr: true);
        var physical = c.Physical!; await physical.PrepareAsync(); await physical.PickAllAsync(); await physical.DispatchAsync();
        var guestCommand = await c.CancelRequestAsync("Khách đề nghị hủy khi hàng thực tế đang trên đường");
        await using (var db = c.Context()) await Web07Support.Cancellation(c, db, guest: true).CancelGuestAsync(guestCommand);
        int requestId;
        await using (var db = c.Context()) requestId = (await Web07Support.Cancellation(c, db).GetAsync(c.OrderId)).PendingRequestId!.Value;
        if (additionalMoneyReview)
        {
            physical.QrTransport!.Pay(physical.ProviderReference!, 1_000m);
            await physical.QrCase!.ProcessAsync(physical.IntentId!.Value, physical.QrTransport);
            Assert.Equal(StorefrontPaymentState.Review, (await physical.DetailAsync()).PaymentState);
        }
        // This actor can record actual custody and returns, but cannot approve a cancellation.
        var returnRights = new[] { StorefrontPermissions.ViewOrders, StorefrontPermissions.Dispatch, StorefrontPermissions.RecordDelivery,
            StorefrontPermissions.ReceiveReturns, StorefrontPermissions.Reconcile };
        StorefrontOperationRequest replay;
        if (incident)
        {
            var report = await physical.IncidentRequestAsync();
            await using (var db = c.Context()) await physical.Service(db, returnRights).RecordIncidentAsync(c.OrderId, report);
            int id; await using (var db = c.Context()) id = (await db.Set<StorefrontCustodyIncident>().SingleAsync()).Id;
            var recover = await physical.ReconcileRequestAsync(id, StorefrontIncidentResolution.AllGoodsRecoveredAtSource);
            await using (var db = c.Context()) await physical.Service(db, returnRights).ReconcileIncidentAsync(c.OrderId, recover);
            replay = recover;
        }
        else
        {
            await physical.OutcomeAllAsync(false);
            var recover = await physical.ReturnRequestAsync();
            await using (var db = c.Context()) await physical.Service(db, returnRights).ReceiveReturnAsync(c.OrderId, recover);
            replay = recover;
        }
        await using (var db = c.Context())
        {
            var view = await Web07Support.Cancellation(c, db).GetAsync(c.OrderId);
            Assert.Equal(StorefrontOrderPhase.Cancelled, view.Phase); Assert.Null(view.PendingRequestId);
            Assert.False(view.NeedsDeliveryConflictReview); Assert.False(view.CanAcknowledgeDeliveryConflict);
            var decision = Assert.Single(await db.Set<StorefrontCancellationDecision>().Where(x => x.CancellationRequestId == requestId).ToListAsync());
            Assert.Equal(StorefrontCancellationDecisionKind.SupersededByActualReturn, decision.Kind);
            Assert.NotNull(decision.ActualReturnEvidenceId); Assert.Null(decision.ActualDeliveryEvidenceId);
            Assert.False(decision.AuthorisesPhysicalReturn);
            Assert.Equal(4m, await db.DeliveryReturnedCostFragments.SumAsync(x => x.BaseQuantity));
            Assert.Equal(40_000m, await db.DeliveryReturnedCostFragments.SumAsync(x => x.CostAmount));
            Assert.Equal(10m, (await db.InventoryBalances.SingleAsync()).OnHandQty);
            Assert.Equal(0m, (await db.InventoryBalances.SingleAsync()).ReservedQty);
            var expected = additionalMoneyReview ? 231_000m : 230_000m;
            Assert.Equal(expected, await db.Set<BankVerifiedReceipt>().Where(x => x.OriginStoreId == c.StoreId).SumAsync(x => x.Amount));
            Assert.Equal(expected, await db.Set<StorefrontRefund>().Where(x => x.StorefrontOrderId == c.OrderId).SumAsync(x => x.Amount));
            Assert.Equal(expected, await db.Set<StorefrontRefundFunding>().Where(x => x.StorefrontOrderId == c.OrderId).SumAsync(x => x.Amount));
            Assert.All(await db.Set<StorefrontRefund>().Where(x => x.StorefrontOrderId == c.OrderId).ToListAsync(), x => Assert.Equal(StorefrontRefundState.Requested, x.State));
            Assert.Empty(await db.Set<StorefrontRefundEvidence>().Where(x => x.StorefrontOrderId == c.OrderId).ToListAsync());
            Assert.Empty(await db.Orders.ToListAsync()); Assert.Empty(await db.OrderPayments.ToListAsync());
            Assert.Empty(await db.POSShiftCashTransactions.ToListAsync()); Assert.Empty(await db.TreasuryEntries.ToListAsync());
            if (additionalMoneyReview) Assert.Equal(StorefrontPaymentState.Review, (await db.StorefrontOrders.SingleAsync(x => x.Id == c.OrderId)).PaymentState);
        }
        var after = await c.SnapshotAsync();
        await using (var db = c.Context())
        {
            if (incident) await physical.Service(db, returnRights).ReconcileIncidentAsync(c.OrderId, (StorefrontIncidentReconcileRequest)replay);
            else await physical.Service(db, returnRights).ReceiveReturnAsync(c.OrderId, (StorefrontReceiveReturnRequest)replay);
        }
        Assert.Equal(after, await c.SnapshotAsync());
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public async Task Shipping_recall_waits_for_actual_partial_then_full_source_returns_preserves_cost_budgets_and_durable_refund(
        bool prepaidQr, bool multipleSources, bool fourDecimals)
    {
        var quantity = fourDecimals ? 4.0001m : 4m;
        var c = await Web07Case.CreatePhysicalAsync(fixture, prepaidQr: prepaidQr, multipleSources: multipleSources, quantity: quantity);
        var physical = c.Physical!; await physical.PrepareAsync(); await physical.PickAllAsync(); await physical.DispatchAsync();
        await using (var db = c.Context()) await Web07Support.Cancellation(c, db, guest: true).CancelGuestAsync(await c.CancelRequestAsync());
        int requestId; await using (var db = c.Context()) requestId = (await Web07Support.Cancellation(c, db).GetAsync(c.OrderId)).PendingRequestId!.Value;
        var approve = await c.CancellationDecisionAsync(requestId, "Đã xác minh còn đang giao, yêu cầu thu hồi đủ các nguồn");
        await using (var db = c.Context()) await Web07Support.Cancellation(c, db).ApproveAsync(c.OrderId, approve);
        await using (var db = c.Context())
        {
            var view = await Web07Support.Cancellation(c, db).GetAsync(c.OrderId);
            Assert.Equal(StorefrontOrderPhase.Shipping, view.Phase); Assert.True(view.ReturnAuthorised);
            Assert.Equal(quantity, await db.DeliveryDispatchCostFragments.SumAsync(x => x.BaseQuantity));
            Assert.Empty(await db.DeliveryReturnedCostFragments.ToListAsync());
            Assert.Empty(await db.Set<StorefrontRefundEvidence>().Where(x => x.StorefrontOrderId == c.OrderId).ToListAsync());
        }
        var first = (await physical.ReturnRequestAsync()).Lines.First();
        var partial = await c.CommandAsync<StorefrontReceiveReturnRequest>();
        partial.Lines = [first with { BaseQuantity = "0.0001" }];
        await using (var db = c.Context()) await physical.Service(db).ReceiveReturnAsync(c.OrderId, partial);
        await using (var db = c.Context())
        {
            Assert.Equal(StorefrontOrderPhase.Shipping, (await db.StorefrontOrders.SingleAsync(x => x.Id == c.OrderId)).Phase);
            Assert.Equal(.0001m, await db.DeliveryReturnedCostFragments.SumAsync(x => x.BaseQuantity));
            Assert.Empty(await db.Set<StorefrontRefundEvidence>().Where(x => x.StorefrontOrderId == c.OrderId).ToListAsync());
        }
        var remaining = await physical.ReturnRequestAsync();
        Assert.Equal(quantity - .0001m, remaining.Lines.Sum(x => decimal.Parse(x.BaseQuantity, System.Globalization.CultureInfo.InvariantCulture)));
        await using (var db = c.Context()) await physical.Service(db).ReceiveReturnAsync(c.OrderId, remaining);
        await using (var db = c.Context())
        {
            Assert.Equal(StorefrontOrderPhase.Cancelled, (await db.StorefrontOrders.SingleAsync(x => x.Id == c.OrderId)).Phase);
            Assert.Equal(quantity, await db.DeliveryReturnedCostFragments.SumAsync(x => x.BaseQuantity));
            var expectedCost = multipleSources ? (fourDecimals ? 55_002m : 55_000m) : (fourDecimals ? 40_001m : 40_000m);
            Assert.Equal(expectedCost, await db.DeliveryReturnedCostFragments.SumAsync(x => x.CostAmount));
            foreach (var (warehouse, stock) in physical.InitialStock)
            {
                var balance = await db.InventoryBalances.SingleAsync(x => x.WarehouseId == warehouse);
                Assert.Equal(stock, balance.OnHandQty); Assert.Equal(0m, balance.ReservedQty);
            }
            Assert.All(await db.InventoryReservations.Where(x => x.StorefrontOrderId == c.OrderId).ToListAsync(),
                x => Assert.Equal(InventoryReservationStatus.Consumed, x.Status));
            var refunds = await Web07Support.Refunds(c, db).GetAsync(c.OrderId);
            if (prepaidQr)
            {
                var refund = Assert.Single(refunds); Assert.Equal(fourDecimals ? 230_005m : 230_000m, refund.Amount);
                Assert.Equal(StorefrontRefundState.Requested, refund.State); Assert.Equal(0m, refund.ExecutedAmount);
                Assert.Equal(refund.Amount, await db.Set<StorefrontRefundFunding>().Where(x => x.RefundId == refund.Id).SumAsync(x => x.Amount));
            }
            else Assert.Empty(refunds);
            Assert.Empty(await db.Orders.ToListAsync()); Assert.Empty(await db.OrderPayments.ToListAsync());
        }
        var after = await c.SnapshotAsync();
        await using (var restarted = c.Context()) await physical.Service(restarted).ReceiveReturnAsync(c.OrderId, remaining);
        Assert.Equal(after, await c.SnapshotAsync());
    }

    [Fact]
    public async Task Zero_net_gift_owner_still_requires_real_physical_recovery_before_cancel_and_full_paid_shipping_refund()
    {
        var c = await Web07Case.CreatePhysicalAsync(fixture, prepaidQr: true, multipleSources: true, quantity: 2m, gift: true);
        var physical = c.Physical!; await physical.PrepareAsync(); await physical.PickAllAsync(); await physical.DispatchAsync();
        await using (var db = c.Context())
        {
            var lines = await db.Set<StorefrontOrderLine>().Where(x => x.StorefrontOrderId == c.OrderId).ToListAsync();
            Assert.Equal(2m, Assert.Single(lines, x => !x.IsGift).Quantity); Assert.Equal(1m, Assert.Single(lines, x => x.IsGift).Quantity);
            Assert.Equal(3m, lines.Sum(x => x.BaseQuantity)); Assert.Equal(130_000m, (await db.StorefrontOrders.SingleAsync(x => x.Id == c.OrderId)).TotalDue);
        }
        await using (var db = c.Context()) await Web07Support.Cancellation(c, db, guest: true).CancelGuestAsync(await c.CancelRequestAsync());
        int request; await using (var db = c.Context()) request = (await Web07Support.Cancellation(c, db).GetAsync(c.OrderId)).PendingRequestId!.Value;
        await using (var db = c.Context()) await Web07Support.Cancellation(c, db).ApproveAsync(c.OrderId,
            await c.CancellationDecisionAsync(request, "Thu hồi cả hàng tặng không có tiền bán ở nguồn chủ thểB"));
        await using (var db = c.Context()) await physical.Service(db).ReceiveReturnAsync(c.OrderId, await physical.ReturnRequestAsync());
        await using var check = c.Context();
        Assert.Equal(StorefrontOrderPhase.Cancelled, (await check.StorefrontOrders.SingleAsync(x => x.Id == c.OrderId)).Phase);
        Assert.Equal(3m, await check.DeliveryReturnedCostFragments.SumAsync(x => x.BaseQuantity));
        Assert.Equal(35_000m, await check.DeliveryReturnedCostFragments.SumAsync(x => x.CostAmount));
        Assert.Equal(130_000m, Assert.Single(await Web07Support.Refunds(c, check).GetAsync(c.OrderId)).Amount);
        Assert.Empty(await check.Orders.ToListAsync()); Assert.Empty(await check.InvoiceHeads.ToListAsync());
        Assert.Empty(await check.Set<StorefrontRefundEvidence>().Where(x => x.StorefrontOrderId == c.OrderId).ToListAsync());
    }
}
