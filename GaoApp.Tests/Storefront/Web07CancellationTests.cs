using GaoApp.Application.Storefront;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Domain.Storefront;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Storefront;

[Collection("StorefrontWeb07"), Trait("Category", "StorefrontWeb07")]
public sealed class Web07CancellationTests(Web01Fixture fixture)
{
    [Fact]
    public async Task Actual_delivered_request_conflict_blocks_real_cash_ready_PostSale_until_acknowledgement_then_posts_without_duplicate_stock_or_funds()
    {
        var c = await Web07Case.CreatePhysicalAsync(fixture); var physical = c.Physical!;
        await physical.PrepareAsync(); await physical.PickAllAsync(); await physical.DispatchAsync();
        await using (var db = c.Context()) await Web07Support.Cancellation(c, db, true).CancelGuestAsync(await c.CancelRequestAsync());
        await physical.OutcomeAllAsync(true);
        await Web06FinanceSupport.ServiceSettingAsync(physical); await Web06FinanceSupport.CollectAllAsync(physical);
        var terminal = await Web06FinanceSupport.OwnOpenShiftAsync(physical);
        await Web06FinanceSupport.ReceiveCashAsync(physical, await Web06FinanceSupport.ActualCashInAsync(physical, terminal, 230_000m));
        var sale = await physical.RequestAsync<StorefrontOperationRequest>(); var before = await c.SnapshotAsync();
        await using (var db = c.Context())
        {
            Assert.True((await Web07Support.Cancellation(c, db).GetAsync(c.OrderId)).NeedsDeliveryConflictReview);
            Assert.False((await Web06FinanceSupport.Settlement(physical, db, terminal).GetAsync(c.OrderId)).CanPostSale);
            await Assert.ThrowsAsync<ConflictAppException>(() => Web06FinanceSupport.Settlement(physical, db, terminal).PostSaleAsync(c.OrderId, sale));
        }
        Assert.Equal(before, await c.SnapshotAsync());
        int requestId; await using (var db = c.Context()) requestId = (await db.Set<StorefrontCancellationRequest>().SingleAsync()).Id;
        var acknowledge = await c.CancellationDecisionAsync(requestId, "Đã đối chiếu đủ giao thành công và liên hệ khách, không thay tiền/kho");
        await using (var db = c.Context()) await Web07Support.Cancellation(c, db).AcknowledgeDeliveryConflictAsync(c.OrderId, acknowledge);
        var flow = await Web07RefundExecutionTests.FlowAsync(c); sale = await physical.RequestAsync<StorefrontOperationRequest>();
        await using (var db = c.Context()) await Web06FinanceSupport.Settlement(physical, db, terminal).PostSaleAsync(c.OrderId, sale);
        await using (var restart = c.Context()) Assert.True((await Web06FinanceSupport.Settlement(physical, restart, terminal).PostSaleAsync(c.OrderId, sale)).Replayed);
        Assert.Equal(flow, await Web07RefundExecutionTests.FlowAsync(c)); await using var check = c.Context();
        Assert.Equal(StorefrontOrderPhase.Completed, (await check.StorefrontOrders.SingleAsync()).Phase);
        Assert.Equal(200_000m, (await check.Orders.SingleAsync()).GrandTotal); Assert.Equal(30_000m, (await check.Set<StorefrontServiceRevenue>().SingleAsync()).GrossAmount);
        Assert.Equal(40_000m, await check.DeliveryDispatchCostFragments.SumAsync(x => x.CostAmount)); Assert.Empty(await check.DeliveryReturnedCostFragments.ToListAsync());
        Assert.Equal(6m, (await check.InventoryBalances.SingleAsync()).OnHandQty); Assert.Equal(0m, (await check.InventoryBalances.SingleAsync()).ReservedQty);
        Assert.Single(await check.POSShiftCashTransactions.ToListAsync()); Assert.Equal(230_000m, await check.InvoiceHeads.SumAsync(x => x.GrandTotal));
        Assert.Empty(await check.Set<StorefrontRefundFunding>().Where(x => x.StorefrontOrderId == c.OrderId).ToListAsync());
        Assert.Empty(await check.Set<StorefrontRefundEvidence>().Where(x => x.StorefrontOrderId == c.OrderId).ToListAsync());
    }

    [Fact]
    public async Task Reason_only_rejection_of_a_pending_request_does_not_clear_an_actual_custody_incident_or_enable_dispatch()
    {
        var c = await Web07Case.CreatePhysicalAsync(fixture); var physical = c.Physical!;
        await physical.PrepareAsync(); await physical.PickAllAsync();
        await using (var db = c.Context()) await physical.Service(db).RecordIncidentAsync(c.OrderId, await physical.IncidentRequestAsync());
        await using (var db = c.Context()) await Web07Support.Cancellation(c, db, guest: true).CancelGuestAsync(await c.CancelRequestAsync());
        StorefrontCancellationRead state;
        await using (var db = c.Context()) state = await Web07Support.Cancellation(c, db).GetAsync(c.OrderId);
        Assert.True(state.HasCustodyIncident); Assert.True(state.CanReject); Assert.False(state.CanApprove);
        var physicalBefore = await c.StockAndCustodySnapshotAsync();
        var decision = await c.CancellationDecisionAsync(state.PendingRequestId!.Value, "Chưa chấp thuận yêu cầu; sự cố bàn giao vẫn phải đối soát riêng");
        await using (var db = c.Context()) await Web07Support.Cancellation(c, db).RejectAsync(c.OrderId, decision);
        Assert.Equal(physicalBefore, await c.StockAndCustodySnapshotAsync());
        await using (var db = c.Context())
        {
            state = await Web07Support.Cancellation(c, db).GetAsync(c.OrderId);
            Assert.Null(state.PendingRequestId); Assert.True(state.HasCustodyIncident); Assert.Equal(StorefrontOrderPhase.Preparing, state.Phase);
            Assert.Empty(await db.Set<StorefrontRefund>().Where(x => x.StorefrontOrderId == c.OrderId).ToListAsync());
        }
        var command = await physical.DispatchRequestAsync(); var after = await c.SnapshotAsync();
        await using (var db = c.Context()) await Assert.ThrowsAsync<ConflictAppException>(() => physical.Service(db).DispatchAsync(c.OrderId, command));
        Assert.Equal(after, await c.SnapshotAsync());
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Before_preparation_guest_cancels_real_COD_or_prepaid_QR_releases_only_own_holds_and_keeps_actual_money(bool prepaidQr)
    {
        var c = await Web07Case.CreatePhysicalAsync(fixture, prepaidQr: prepaidQr);
        var otherQuote = await c.Checkout.QuoteAsync(c.Checkout.Request(2m));
        var other = await c.Checkout.PlaceAsync(Web04Case.PlaceRequest(otherQuote));
        Assert.Equal(StorefrontPlacementStatus.Placed, other.Status); Assert.NotNull(other.Order);
        int otherId; decimal onHand; int movements;
        await using (var db = c.Context())
        {
            otherId = await db.StorefrontOrders.Where(x => x.Code == other.Order.Code).Select(x => x.Id).SingleAsync();
            onHand = (await db.InventoryBalances.SingleAsync()).OnHandQty; movements = await db.InventoryTransactions.CountAsync();
            Assert.Equal(6m, (await db.InventoryBalances.SingleAsync()).ReservedQty);
            Assert.Equal(230_000m, (await db.StorefrontOrders.SingleAsync(x => x.Id == c.OrderId)).TotalDue);
        }
        var command = await c.CancelRequestAsync();
        await using (var db = c.Context()) await Web07Support.Cancellation(c, db, guest: true).CancelGuestAsync(command);
        await using (var db = c.Context())
        {
            var order = await db.StorefrontOrders.SingleAsync(x => x.Id == c.OrderId);
            Assert.Equal(StorefrontOrderPhase.Cancelled, order.Phase);
            Assert.True(order.Revision > command.ExpectedRevision); Assert.NotEqual(command.ExpectedVersion, Convert.ToBase64String(order.RowVersion));
            var balance = await db.InventoryBalances.SingleAsync(); Assert.Equal(onHand, balance.OnHandQty); Assert.Equal(2m, balance.ReservedQty);
            Assert.Equal(movements, await db.InventoryTransactions.CountAsync());
            Assert.Empty(await db.InventoryReservations.Where(x => x.StorefrontOrderId == c.OrderId && x.Status == InventoryReservationStatus.Active).ToListAsync());
            Assert.Equal(2m, await db.InventoryReservations.Where(x => x.StorefrontOrderId == otherId && x.Status == InventoryReservationStatus.Active).SumAsync(x => x.ReservedQty));
            Assert.Empty(await db.Orders.ToListAsync()); Assert.Empty(await db.OrderPayments.ToListAsync());
            Assert.Empty(await db.TreasuryEntries.ToListAsync()); Assert.Empty(await db.POSShiftCashTransactions.ToListAsync());
            var refunds = await db.Set<StorefrontRefund>().Where(x => x.StorefrontOrderId == c.OrderId).ToListAsync();
            if (prepaidQr)
            {
                var refund = Assert.Single(refunds); Assert.Equal(StorefrontRefundState.Requested, refund.State);
                Assert.Equal(230_000m, refund.Amount); Assert.Equal(200_000m, refund.GoodsAmount); Assert.Equal(30_000m, refund.ShippingAmount);
                Assert.Equal(230_000m, await db.Set<StorefrontRefundFunding>().Where(x => x.RefundId == refund.Id).SumAsync(x => x.Amount));
                var actual = Assert.Single(await db.Set<BankVerifiedReceipt>().Where(x => x.OriginStoreId == c.StoreId).ToListAsync());
                Assert.Equal(230_000m, actual.Amount);
            }
            else Assert.Empty(refunds);
            Assert.Empty(await db.Set<StorefrontRefundEvidence>().Where(x => x.StorefrontOrderId == c.OrderId).ToListAsync());
            Assert.Single(await db.Set<StorefrontCommandReceipt>().Where(x => x.StorefrontOrderId == c.OrderId && x.ClientRequestId == command.ClientRequestId).ToListAsync());
        }
        var after = await c.SnapshotAsync();
        await using (var restarted = c.Context()) await Web07Support.Cancellation(c, restarted, guest: true).CancelGuestAsync(command);
        Assert.Equal(after, await c.SnapshotAsync());
        await using var read = c.Context(); var guest = await Web07Support.Cancellation(c, read, guest: true).GetGuestAsync(c.Token);
        Assert.NotNull(guest); Assert.Equal(StorefrontOrderPhase.Cancelled, guest.Phase); Assert.False(guest.CanSelfCancel); Assert.False(guest.CanRequestCancellation);
        Assert.All(guest.History, entry => Assert.Equal(DateTimeKind.Utc, entry.AtUtc.Kind));
    }

    [Fact]
    public async Task Preparing_request_blocks_real_handover_then_rejection_keeps_phase_and_approval_cancels_without_physical_return()
    {
        var c = await Web07Case.CreatePhysicalAsync(fixture); var physical = c.Physical!;
        await physical.PrepareAsync(); await physical.PickAllAsync();
        var guestCommand = await c.CancelRequestAsync();
        await using (var db = c.Context()) await Web07Support.Cancellation(c, db, guest: true).CancelGuestAsync(guestCommand);
        StorefrontCancellationRead pending;
        await using (var db = c.Context()) pending = await Web07Support.Cancellation(c, db).GetAsync(c.OrderId);
        Assert.Equal(StorefrontOrderPhase.Preparing, pending.Phase); Assert.NotNull(pending.PendingRequestId);
        var dispatch = await physical.DispatchRequestAsync();
        var blockedSnapshot = await c.SnapshotAsync();
        await using (var db = c.Context()) await Assert.ThrowsAsync<ConflictAppException>(() => physical.Service(db).DispatchAsync(c.OrderId, dispatch));
        Assert.Equal(blockedSnapshot, await c.SnapshotAsync());
        var reject = await c.CancellationDecisionAsync(pending.PendingRequestId.Value, "Khách xác nhận tiếp tục; chưa có bàn giao");
        await using (var db = c.Context()) await Web07Support.Cancellation(c, db).RejectAsync(c.OrderId, reject);
        await using (var db = c.Context())
        {
            var view = await Web07Support.Cancellation(c, db).GetAsync(c.OrderId);
            Assert.Equal(StorefrontOrderPhase.Preparing, view.Phase); Assert.Null(view.PendingRequestId);
            Assert.Equal(4m, (await db.InventoryBalances.SingleAsync()).ReservedQty);
            Assert.Empty(await db.InventoryTransactions.Where(x => x.ReferenceType == InventoryReferenceType.Delivery).ToListAsync());
        }
        guestCommand = await c.CancelRequestAsync("Khách có yêu cầu mới, hủy trước khi bàn giao");
        await using (var db = c.Context()) await Web07Support.Cancellation(c, db, guest: true).CancelGuestAsync(guestCommand);
        await using (var db = c.Context()) pending = await Web07Support.Cancellation(c, db).GetAsync(c.OrderId);
        var approve = await c.CancellationDecisionAsync(pending.PendingRequestId!.Value, "Xác nhận tất cả hàng vẫn ở kho");
        await using (var db = c.Context()) await Web07Support.Cancellation(c, db).ApproveAsync(c.OrderId, approve);
        await using var check = c.Context();
        Assert.Equal(StorefrontOrderPhase.Cancelled, (await check.StorefrontOrders.SingleAsync(x => x.Id == c.OrderId)).Phase);
        Assert.Equal(10m, (await check.InventoryBalances.SingleAsync()).OnHandQty); Assert.Equal(0m, (await check.InventoryBalances.SingleAsync()).ReservedQty);
        Assert.Empty(await check.InventoryTransactions.Where(x => x.ReferenceType == InventoryReferenceType.Delivery).ToListAsync());
        Assert.Empty(await check.Set<StorefrontRefund>().Where(x => x.StorefrontOrderId == c.OrderId).ToListAsync());
    }

    [Fact]
    public async Task Actual_delivery_wins_request_and_dual_permission_acknowledgement_is_rechecked_before_same_key_replay()
    {
        var c = await Web07Case.CreatePhysicalAsync(fixture); var physical = c.Physical!;
        await physical.PrepareAsync(); await physical.PickAllAsync(); await physical.DispatchAsync();
        await using (var db = c.Context()) await Web07Support.Cancellation(c, db, guest: true).CancelGuestAsync(await c.CancelRequestAsync());
        await physical.OutcomeAllAsync(true);
        StorefrontCancellationRead delivered;
        await using (var db = c.Context()) delivered = await Web07Support.Cancellation(c, db).GetAsync(c.OrderId);
        Assert.Equal(StorefrontOrderPhase.Delivered, delivered.Phase); Assert.True(delivered.NeedsDeliveryConflictReview);
        Assert.True(delivered.CanAcknowledgeDeliveryConflict); Assert.Null(delivered.PendingRequestId);
        await using var history = c.Context();
        var request = Assert.Single(await history.Set<StorefrontCancellationRequest>().Where(x => x.StorefrontOrderId == c.OrderId).ToListAsync());
        Assert.Contains(await history.Set<StorefrontCancellationDecision>().Where(x => x.CancellationRequestId == request.Id).ToListAsync(),
            x => x.Kind == StorefrontCancellationDecisionKind.SupersededByActualDelivery);
        var ack = await c.CancellationDecisionAsync(request.Id, "Đã xác minh khách thực nhận đủ mọi nguồn hàng");
        await using (var db = c.Context()) await Web07Support.Cancellation(c, db).AcknowledgeDeliveryConflictAsync(c.OrderId, ack);
        var after = await c.SnapshotAsync();
        var withoutReconcile = Web07Support.Permissions.Where(x => x != StorefrontPermissions.Reconcile).ToArray();
        await using (var revoked = c.Context()) await Assert.ThrowsAsync<ForbiddenAppException>(() =>
            Web07Support.Cancellation(c, revoked, permissions: withoutReconcile).AcknowledgeDeliveryConflictAsync(c.OrderId, ack));
        Assert.Equal(after, await c.SnapshotAsync());
        await using (var restored = c.Context()) await Web07Support.Cancellation(c, restored).AcknowledgeDeliveryConflictAsync(c.OrderId, ack);
        Assert.Equal(after, await c.SnapshotAsync());
        await using var check = c.Context(); var view = await Web07Support.Cancellation(c, check).GetAsync(c.OrderId);
        Assert.False(view.NeedsDeliveryConflictReview); Assert.Equal(StorefrontOrderPhase.Delivered, view.Phase);
        Assert.Empty(await check.Orders.ToListAsync()); Assert.Empty(await check.OrderPayments.ToListAsync());
        Assert.Empty(await check.Set<StorefrontRefund>().Where(x => x.StorefrontOrderId == c.OrderId).ToListAsync());
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Genuine_delivered_and_customer_paid_COD_cannot_be_relabelled_cancelled_or_given_an_invented_cash_refund_owner(bool cashRemitted)
    {
        var c = await Web07Case.CreatePhysicalAsync(fixture); var physical = c.Physical!;
        await Web06FinanceSupport.DeliveredAsync(physical); await Web06FinanceSupport.CollectAllAsync(physical);
        if (cashRemitted)
        {
            var terminal = await Web06FinanceSupport.OwnOpenShiftAsync(physical);
            var actualCash = await Web06FinanceSupport.ActualCashInAsync(physical, terminal, 230_000m);
            await Web06FinanceSupport.ReceiveCashAsync(physical, actualCash);
        }
        await using (var db = c.Context())
        {
            var guest = await Web07Support.Cancellation(c, db, guest: true).GetGuestAsync(c.Token);
            Assert.NotNull(guest); Assert.True(guest.HasDeliveredGoods);
            Assert.False(guest.CanSelfCancel); Assert.False(guest.CanRequestCancellation);
        }
        var command = await c.CancelRequestAsync("Không được đổi giao thành công thành hủy để tạo nguồn hoàn");
        var before = await c.SnapshotAsync();
        await using (var db = c.Context()) await Assert.ThrowsAsync<ConflictAppException>(() =>
            Web07Support.Cancellation(c, db, guest: true).CancelGuestAsync(command));
        Assert.Equal(before, await c.SnapshotAsync());
        await using var check = c.Context();
        Assert.Equal(StorefrontOrderPhase.Delivered, (await check.StorefrontOrders.SingleAsync(x => x.Id == c.OrderId)).Phase);
        Assert.Empty(await check.Set<StorefrontRefundFunding>().Where(x => x.StorefrontOrderId == c.OrderId).ToListAsync());
        Assert.Empty(await check.Set<StorefrontRefund>().Where(x => x.StorefrontOrderId == c.OrderId).ToListAsync());
        Assert.Empty(await check.Set<StorefrontRefundEvidence>().Where(x => x.StorefrontOrderId == c.OrderId).ToListAsync());
        if (cashRemitted)
        {
            var receipt = Assert.Single(await check.Set<StorefrontCodReceipt>().Where(x => x.StorefrontOrderId == c.OrderId).ToListAsync());
            Assert.Equal(230_000m, receipt.Amount); Assert.NotNull(receipt.POSShiftCashTransactionId); Assert.Null(receipt.GlobalBankReceiptId);
        }
    }
}
