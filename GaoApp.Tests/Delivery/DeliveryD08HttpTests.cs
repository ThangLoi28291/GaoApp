using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.Delivery;
using GaoApp.Domain.Delivery;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Delivery;

[Collection("DeliveryD08"), Trait("Category", "DeliveryD08")]
public sealed class DeliveryD08HttpTests(DeliveryD02Fixture fixture)
{
    [Theory]
    [InlineData(4)]
    [InlineData(2.5)]
    [InlineData(0)]
    public async Task H01_One_counter_confirmation_from_departure_uses_till_B_preserves_A_and_only_returns_new_goods(double quantity)
    {
        using var c = await DeliveryD08Case.CreateAsync(fixture); var delivered = (decimal)quantity;
        var source = await c.Legacy.SourceAsync(); var other = await c.Legacy.OtherOrderAsync();
        var cart = await c.ClosingCartAsync(); var original = await c.DispatchProofAsync();
        var workspace = await c.WorkspaceAsync(); Assert.Null(workspace.Trip.ReturnToStore);
        Assert.All(workspace.Trip.Orders, x => Assert.Null(x.Outcome));
        var counter = Assert.IsType<DeliveryHandoverCounterDto>(workspace.Counter);
        Assert.Equal(c.Closer.Store.TerminalId, counter.TerminalId); Assert.Equal(c.ShiftId, counter.ShiftId);
        Assert.NotEqual(c.Order.Account.Store.WarehouseId, counter.WarehouseId);
        var finance = await c.Order.Inventory.FinanceAsync();
        var request = await c.RequestAsync(delivered);
        var ack = await c.ConfirmAsync(request);
        Assert.Equal("handover", ack.Operation); Assert.Equal(request.ClientRequestId, ack.ClientRequestId);
        Assert.False(ack.Replayed); Assert.Equal(c.Id, ack.DeliveryOrderId); Assert.Equal(c.Trip.TripId, ack.TripId);
        Assert.Equal("Settled", ack.State); Assert.Equal(DeliveryD08Case.Text(delivered * 20000), ack.Settlement.PayableText);
        Assert.Equal(counter.TerminalId, ack.Settlement.TerminalId); Assert.Equal(counter.ShiftId, ack.Settlement.ShiftId);
        Assert.Equal(c.Order.Account.Store.WarehouseId, ack.Settlement.SourceWarehouseId);
        var fresh = await c.WorkspaceAsync();
        Assert.Equal(ack.Settlement, fresh.Orders.Single(x => x.DeliveryOrderId == c.Id).Settlement);
        Assert.False(fresh.Orders.Single(x => x.DeliveryOrderId == c.Id).CanConfirm);
        Assert.Equal("Settled", fresh.Trip.Orders.Single(x => x.DeliveryOrderId == c.Id).State);
        await using (var db = c.Context())
        {
            var variant = await c.Order.Inventory.VariantAsync();
            var balance = await db.InventoryBalances.SingleAsync(x => x.WarehouseId == c.Order.Account.Store.WarehouseId && x.ProductVariantId == variant);
            Assert.Equal(4 - delivered, balance.OnHandQty); Assert.Equal((4 - delivered) * 9000, balance.InventoryValue);
            var returns = await db.DeliveryReturnedCostFragments.Where(x => x.DeliveryOrderId == c.Id).ToArrayAsync();
            Assert.Equal(4 - delivered, returns.Sum(x => x.BaseQuantity)); Assert.Equal((4 - delivered) * 9000, returns.Sum(x => x.CostAmount));
            Assert.Equal(0, await db.InventoryTransactions.CountAsync(x => x.WarehouseId == counter.WarehouseId));
            if (ack.Settlement.SaleOrderId is int saleId)
                Assert.Empty(await db.InventoryTransactions.Where(x => x.ReferenceType == InventoryReferenceType.Order && x.ReferenceId == saleId.ToString()).ToArrayAsync());
            var originShift = await db.POSShifts.SingleAsync(x => x.TerminalId == c.Order.Account.Store.TerminalId);
            Assert.Equal(POSShiftStatus.Closed, originShift.Status); Assert.Equal(0, originShift.CashSalesTotal);
            var closing = await db.POSShifts.SingleAsync(x => x.Id == c.ShiftId);
            Assert.Equal(c.CurrentCartId, closing.CurrentOrderId); Assert.Equal(delivered * 20000, closing.CashSalesTotal);
            var revision = Assert.Single(await db.DeliveryRevisions.Where(x => x.DeliveryOrderId == c.Id && x.Action == "counter-handover").ToArrayAsync());
            var saved = JsonSerializer.Deserialize<DeliveryHandoverSnapshot>(revision.SnapshotJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            Assert.Equal("delivery-handover-v1", saved.Schema); Assert.Equal("AtStore", saved.ReportSource);
            Assert.Equal(c.Closer.UserId, saved.ActorUserId); Assert.Equal(ack.Settlement, saved.Settlement);
            Assert.Equal(delivered, saved.SaleLines.Sum(x => x.DeliveredBaseQuantity));
        }
        if (delivered > 0)
        {
            var sale = await c.SaleAsync(ack); Assert.Equal(OrderStatus.Completed, sale.Status);
            Assert.Equal(delivered * 20000, sale.GrandTotal); Assert.Equal(sale.GrandTotal, sale.PaidTotal); Assert.Equal(0, sale.BalanceDue);
            Assert.Equal(c.ShiftId, sale.POSShiftId); Assert.Equal(delivered, sale.Lines.Sum(x => x.BaseQuantity));
            Assert.Equal(delivered * 9000, sale.Lines.Sum(x => x.LineCostTotal)); Assert.All(sale.Lines, x => Assert.False(x.IsProvisionalCost));
            var payment = Assert.Single(sale.Payments); Assert.Equal(PaymentMethod.Cash, payment.Method); Assert.Equal(sale.GrandTotal, payment.Amount);
            var receipt = await c.Client.JsonAsync(HttpMethod.Get, "/admin/pos/orders/" + sale.Id);
            Assert.Equal(sale.GrandTotal, receipt.GetProperty("grandTotal").GetDecimal());
        }
        else { Assert.Null(ack.Settlement.SaleOrderId); Assert.Equal(finance, await c.Order.Inventory.FinanceAsync()); }
        Assert.Equal(source, await c.Legacy.SourceAsync()); Assert.Equal(other, await c.Legacy.OtherOrderAsync());
        Assert.Equal(cart, await c.ClosingCartAsync()); Assert.Equal(original, await c.DispatchProofAsync());
    }

    [Fact]
    public async Task H02_Existing_D07_report_arrival_and_accepted_return_are_reused_without_duplicate_receive()
    {
        using var c = await DeliveryD08Case.CreateAsync(fixture);
        await c.Order.Inventory.ReceiveAsync("0.5");
        await c.Legacy.ReportAsync(await c.Legacy.RequestAsync(outcome: "Partial", delivered: "3", returned: "1"));
        await c.Legacy.ArrivalAsync();
        await using var db = c.Context();
        var old = await db.DeliveryRevisions.AsNoTracking().Where(x => x.DeliveryOrderId == c.Id && x.Action == "trip-outcome")
            .Select(x => new { x.Id, x.SnapshotJson, x.SnapshotHash }).ToArrayAsync();
        var workspace = await c.WorkspaceAsync(); Assert.NotNull(workspace.Trip.ReturnToStore);
        Assert.NotNull(workspace.Orders.Single(x => x.DeliveryOrderId == c.Id).SavedPreview);
        var request = await c.RequestAsync(3); var ack = await c.ConfirmAsync(request);
        Assert.Equal("60000", ack.Settlement.PayableText);
        var returns = await db.DeliveryReturnedCostFragments.AsNoTracking().Where(x => x.DeliveryOrderId == c.Id).ToArrayAsync();
        Assert.Equal(1, returns.Sum(x => x.BaseQuantity)); Assert.Equal(9000, returns.Sum(x => x.CostAmount));
        Assert.Equal(JsonSerializer.Serialize(old), JsonSerializer.Serialize(await db.DeliveryRevisions.AsNoTracking()
            .Where(x => x.DeliveryOrderId == c.Id && x.Action == "trip-outcome").Select(x => new { x.Id, x.SnapshotJson, x.SnapshotHash }).ToArrayAsync()));
        var after = await c.SnapshotAsync(); Assert.True((await c.ConfirmAsync(request)).Replayed); Assert.Equal(after, await c.SnapshotAsync());
    }

    [Theory]
    [InlineData("BankTransfer")]
    [InlineData("CustomerCredit")]
    public async Task H03_Bank_and_credit_use_real_POS_ledgers_and_never_generate_payment_QR_or_cash(string method)
    {
        using var c = await DeliveryD08Case.CreateAsync(fixture); var request = await c.RequestAsync(3, method);
        await using var db = c.Context(); var qr = await db.PosPaymentQrRequests.CountAsync();
        if (method == "BankTransfer")
        {
            request = request with { BankAccountId = await c.BankAsync(), TransferReference = "D08-verified-" + c.Id };
            var before = await c.SnapshotAsync(); await c.RejectAsync(request with { TransferConfirmed = false }, HttpStatusCode.BadRequest, "BANK_UNCONFIRMED");
            Assert.Equal(before, await c.SnapshotAsync());
        }
        else
        {
            await c.CustomerAsync(false); var before = await c.SnapshotAsync();
            await c.RejectAsync(request, HttpStatusCode.BadRequest, "CUSTOMER_CREDIT_FORBIDDEN"); Assert.Equal(before, await c.SnapshotAsync());
            await c.CustomerAsync(true);
        }
        var cart = await c.ClosingCartAsync(); var ack = await c.ConfirmAsync(request); var sale = await c.SaleAsync(ack);
        Assert.Equal(60000, sale.GrandTotal); Assert.Equal(c.ShiftId, sale.POSShiftId);
        Assert.Equal(qr, await db.PosPaymentQrRequests.CountAsync());
        var shift = await db.POSShifts.AsNoTracking().SingleAsync(x => x.Id == c.ShiftId); Assert.Equal(0, shift.CashSalesTotal);
        if (method == "BankTransfer")
        {
            var payment = Assert.Single(sale.Payments); Assert.Equal(PaymentMethod.BankTransfer, payment.Method); Assert.Equal(60000, payment.Amount);
            Assert.Equal(request.TransferReference, payment.ReferenceCode); Assert.Equal(60000, shift.NonCashSalesTotal);
            Assert.Equal(60000, sale.PaidTotal); Assert.Equal(0, sale.BalanceDue);
            Assert.Empty(await db.Set<CustomerReceivableEntry>().Where(x => x.OrderId == sale.Id).ToArrayAsync());
        }
        else
        {
            Assert.True(sale.IsCreditSale); Assert.Empty(sale.Payments); Assert.Equal(0, sale.PaidTotal); Assert.Equal(60000, sale.BalanceDue);
            var debt = Assert.Single(await db.Set<CustomerReceivableEntry>().Where(x => x.OrderId == sale.Id).ToArrayAsync());
            Assert.Equal(sale.CustomerId, debt.CustomerId); Assert.Equal("Sale", debt.Kind); Assert.Equal(60000, debt.Amount);
            Assert.Equal(0, shift.NonCashSalesTotal);
        }
        Assert.Equal(cart, await c.ClosingCartAsync()); var snapshot = await c.SnapshotAsync();
        Assert.True((await c.ConfirmAsync(request)).Replayed); Assert.Equal(snapshot, await c.SnapshotAsync());
    }

    [Theory]
    [InlineData("combo", 11000)]
    [InlineData("gift", 16000)]
    public async Task H04_Actual_POS_promotions_voucher_and_header_discount_keep_saved_net_without_repricing(string kind, int total)
    {
        using var c = await DeliveryD08Case.CreateAsync(fixture, promotion: kind);
        await using var db = c.Context();
        var sourceId = c.Order.Picking.Source.CartId;
        var source = await db.Orders.AsNoTracking().Include(x => x.Lines).Include(x => x.RewardVouchers).SingleAsync(x => x.Id == sourceId);
        Assert.Equal(total, source.GrandTotal); Assert.Equal(1000, source.OrderDiscount); Assert.Equal(3000, source.VoucherDiscountTotal);
        Assert.Equal(source.Subtotal - source.DiscountTotal, source.GrandTotal);
        if (kind == "combo") Assert.Equal(5000, source.ComboDiscountTotal);
        else
        {
            var gift = Assert.Single(source.Lines, x => x.IsPromotionGift && !x.IsDeleted);
            Assert.Equal(1, gift.BaseQuantity); Assert.Equal(0, gift.UnitPrice); Assert.Equal(0, gift.LineTotal);
            Assert.Equal(2, source.Lines.Where(x => !x.IsPromotionGift && !x.IsDeleted).Sum(x => x.BaseQuantity));
        }
        var sourceRows = await c.Legacy.SourceAsync();
        var workspace = await c.WorkspaceAsync(); var row = workspace.Trip.Orders.Single(x => x.DeliveryOrderId == c.Id);
        var lines = row.Lines.Select(x => new DeliveryHandoverLineRequest(x.LineId, x.DispatchedBaseQuantityText, "0")).ToArray();
        var preview = await c.PreviewAsync(new(workspace.Trip.Version, row.Version, lines));
        Assert.Equal(total, DeliveryD08Case.Number(preview.PayableText));
        foreach (var line in source.Lines)
        {
            (await db.ProductVariants.SingleAsync(x => x.Id == line.VariantId)).Price = 999999;
            foreach (var conversion in await db.ProductUnitConversions.Where(x => x.ProductVariantId == line.VariantId).ToArrayAsync()) conversion.Price = 999999;
        }
        await db.SaveChangesAsync();
        var request = await c.RequestLinesAsync(workspace, lines); var ack = await c.ConfirmAsync(request); var sale = await c.SaleAsync(ack);
        Assert.Equal(total, sale.GrandTotal);
        Assert.Equal(total, sale.Subtotal - sale.DiscountTotal);
        Assert.Equal(total, sale.Lines.Sum(x => x.LineTotal) - sale.ComboDiscountTotal - sale.OrderDiscount - sale.VoucherDiscountTotal);
        Assert.Equal(source.Lines.Sum(x => x.BaseQuantity), sale.Lines.Sum(x => x.BaseQuantity));
        Assert.Equal(sourceRows, await c.Legacy.SourceAsync());
        var voucherId = Assert.Single(source.RewardVouchers).VoucherId;
        var voucher = await db.CustomerRewardVouchers.AsNoTracking().SingleAsync(x => x.Id == voucherId);
        Assert.Equal(CustomerRewardVoucherStatus.Used, voucher.Status); Assert.Equal(sale.Id, voucher.UsedOrderId); Assert.NotNull(voucher.UsedAtUtc);
        var after = await c.SnapshotAsync(); Assert.True((await c.ConfirmAsync(request)).Replayed); Assert.Equal(after, await c.SnapshotAsync());
        Assert.True(await db.CustomerRewardLedgers.CountAsync(x => x.OrderId == sale.Id && x.Type == CustomerRewardLedgerType.SaleEarned) <= 1);
    }

    [Theory]
    [InlineData(0, "Unknown", 0)]
    [InlineData(1, "Provisional", 15000)]
    public async Task H05_Genuine_negative_export_and_return_can_settle_with_truthful_cost_and_no_second_outbound(int stock, string quality, int cost)
    {
        using var c = await DeliveryD08Case.CreateAsync(fixture, stock: stock);
        var original = await c.DispatchProofAsync(); var request = await c.RequestAsync(3);
        var ack = await c.ConfirmAsync(request); var sale = await c.SaleAsync(ack);
        var workspace = await c.WorkspaceAsync(); Assert.Equal(quality, workspace.Orders.Single(x => x.DeliveryOrderId == c.Id).CostQuality);
        var line = Assert.Single(sale.Lines); Assert.True(line.IsProvisionalCost); Assert.Equal(cost, line.LineCostTotal);
        Assert.Equal(60000, sale.GrandTotal); Assert.Equal(original, await c.DispatchProofAsync());
        await using var db = c.Context(); var variant = await c.Order.Inventory.VariantAsync();
        Assert.Equal(stock - 3, (await db.InventoryBalances.SingleAsync(x => x.WarehouseId == c.Order.Account.Store.WarehouseId && x.ProductVariantId == variant)).OnHandQty);
        Assert.Empty(await db.InventoryTransactions.Where(x => x.ReferenceType == InventoryReferenceType.Order && x.ReferenceId == sale.Id.ToString()).ToArrayAsync());
        Assert.Equal(1, await db.DeliveryReturnedCostFragments.Where(x => x.DeliveryOrderId == c.Id).SumAsync(x => x.BaseQuantity));
        var after = await c.SnapshotAsync(); Assert.True((await c.ConfirmAsync(request)).Replayed); Assert.Equal(after, await c.SnapshotAsync());
        // Generic refund must not receive already accepted D05 goods or reverse an
        // immutable delivery cost slice through an unsupported ordinary POS path.
        using var refund = await c.Client.Http.PostAsJsonAsync("/admin/pos/orders/" + sale.Id + "/refund",
            new { reason = "Delivery source requires dedicated return", refundMethod = 0, allowPendingRestock = true });
        Assert.Equal(HttpStatusCode.BadRequest, refund.StatusCode);
        Assert.Contains("DELIVERY_SALE_RETURN_UNSUPPORTED", await refund.Content.ReadAsStringAsync());
        Assert.Equal(after, await c.SnapshotAsync());
    }

    [Fact]
    public async Task H06_Cash_discrepancy_refuses_every_goods_and_financial_effect_without_creating_customer_debt()
    {
        using var c = await DeliveryD08Case.CreateAsync(fixture); var request = await c.RequestAsync(3);
        var before = await c.SnapshotAsync();
        await c.RejectAsync(request with { ActualReceivedCashText = "50000" }, HttpStatusCode.BadRequest, "CASH_HANDOVER_DIFFERENCE");
        Assert.Equal(before, await c.SnapshotAsync());
        await c.RejectAsync(request with { ReportedCashText = "50000", ActualReceivedCashText = "50000" }, HttpStatusCode.BadRequest, "PAYMENT_AMOUNT_MISMATCH");
        Assert.Equal(before, await c.SnapshotAsync());
    }

    [Fact]
    public async Task H07_Exact_retry_survives_old_versions_but_changed_body_identity_and_revoked_right_cannot_reuse_it()
    {
        using var c = await DeliveryD08Case.CreateAsync(fixture); var request = await c.RequestAsync(3); var ack = await c.ConfirmAsync(request);
        var after = await c.SnapshotAsync(); var replay = await c.ConfirmAsync(request);
        Assert.True(replay.Replayed); Assert.Equal(ack.Settlement, replay.Settlement); Assert.Equal(ack.AppliedOrderVersion, replay.AppliedOrderVersion);
        foreach (var changed in new[] { request with { ActualReceivedCashText = "1" }, request with { CourierUserId = c.Trip.OtherCourier.UserId },
            request with { ExpectedTerminalId = c.Order.Account.Store.TerminalId }, request with { Reason = "Changed after commit" } })
            await c.RejectAsync(changed, HttpStatusCode.Conflict, "REQUEST_KEY_CONFLICT");
        Assert.Equal(after, await c.SnapshotAsync());
        await using (var db = c.Context())
        {
            var permission = await db.Permissions.SingleAsync(x => x.Code == PermissionCodes.Delivery.Finalize);
            db.RolePermissions.RemoveRange(await db.RolePermissions.Where(x => x.RoleId == c.Closer.RoleId && x.PermissionId == permission.Id).ToArrayAsync()); await db.SaveChangesAsync();
        }
        // The existing endpoint authorization middleware returns 403 before the
        // domain handler; it has no JSON domain-error body.
        await c.RejectAsync(request, HttpStatusCode.Forbidden); Assert.Equal(after, await c.SnapshotAsync());
    }

    [Fact]
    public async Task H08_View_courier_foreign_store_and_missing_CSRF_cannot_confirm_or_inject_server_authority()
    {
        using var c = await DeliveryD08Case.CreateAsync(fixture); var request = await c.RequestAsync(3); var before = await c.SnapshotAsync();
        var viewer = await fixture.Web.AddAccountAsync(c.Closer.Store, PermissionCodes.Delivery.View);
        var foreign = await fixture.Web.AddAccountAsync(fixture.Web.Stores[1], "*");
        foreach (var actor in new[] { viewer, c.Trip.Courier, foreign })
        {
            using var http = await fixture.Web.LoginAsync(actor);
            await c.RejectAsync(request, actor == foreign ? HttpStatusCode.NotFound : HttpStatusCode.Forbidden, http: http);
        }
        var token = c.Client.Http.DefaultRequestHeaders.GetValues("RequestVerificationToken").Single();
        c.Client.Http.DefaultRequestHeaders.Remove("RequestVerificationToken");
        try { await c.RejectAsync(request, HttpStatusCode.BadRequest); }
        finally { c.Client.Http.DefaultRequestHeaders.Add("RequestVerificationToken", token); }
        foreach (var field in new[] { "sourceWarehouseId", "actorUserId", "creditCustomerId", "prices", "recordedAtUtc" })
        {
            var body = JsonSerializer.SerializeToNode(request, new JsonSerializerOptions(JsonSerializerDefaults.Web))!; body[field] = 123;
            await c.RejectAsync(body, HttpStatusCode.BadRequest);
        }
        Assert.Equal(before, await c.SnapshotAsync());
    }

    [Fact]
    public async Task H09_Invalid_conservation_physical_confirmation_money_and_foreign_lines_leave_the_whole_form_unapplied()
    {
        using var c = await DeliveryD08Case.CreateAsync(fixture); var request = await c.RequestAsync(3); var before = await c.SnapshotAsync();
        foreach (var counter in new[] { request with { ExpectedTerminalId = c.Order.Account.Store.TerminalId },
            request with { ExpectedShiftId = c.ShiftId + 1000000 } })
        { await c.RejectAsync(counter, HttpStatusCode.Conflict, "CLOSING_COUNTER_FORBIDDEN"); Assert.Equal(before, await c.SnapshotAsync()); }
        var foreign = Assert.Single((await c.Trip.DetailAsync()).Orders.Single(x => x.DeliveryOrderId == c.Legacy.Id(1)).Lines);
        foreach (var invalid in new[] { request with { PhysicalGoodsConfirmed = false }, request with { Lines = [] },
            request with { Lines = [new(request.Lines[0].LineId, "3", "0")] }, request with { Lines = [new(foreign.LineId, "3", "1")] },
            request with { ReportedCashText = "1.5" }, request with { Lines = [new(request.Lines[0].LineId, "-1", "5")] },
            request with { CustomerReceivedAtUtc = DateTimeOffset.UtcNow.AddDays(1) } })
        { await c.RejectAsync(invalid, HttpStatusCode.BadRequest); Assert.Equal(before, await c.SnapshotAsync()); }
    }
}
