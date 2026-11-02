using System.Net;
using System.Net.Http.Json;
using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.Delivery;
using GaoApp.Domain.Delivery;
using GaoApp.Domain.Enums;
using GaoApp.Tests.Security;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Delivery;

[Collection("DeliveryD06")]
public sealed class DeliverySimpleWorkspaceTests(DeliveryD02Fixture fixture)
{
    [Theory]
    [InlineData(10)]
    [InlineData(8)]
    [InlineData(0)]
    public async Task One_picking_and_dispatch_command_then_counter_close_posts_only_actual_goods_and_money(int delivered)
    {
        using var order = await DeliveryD06Order.CreateAsync(fixture, quantity: 10, distinctSource: true);
        var id = order.Id;
        var detail = await order.Picking.GetAsync();
        var line = Assert.Single(detail.Lines);
        var invalid = new DeliveryPickingReportRequest(Guid.NewGuid(), detail.Delivery.Version, [new(line.LineId, "11", null)]);
        using (var response = await order.Client.Http.PostAsJsonAsync($"/admin/api/deliveries/{id}/picking/complete", invalid))
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await using (var db = order.Picking.Source.Context())
        {
            Assert.Equal(DeliveryState.Created, (await db.DeliveryOrders.SingleAsync(x => x.Id == id)).State);
            Assert.False(await db.DeliveryPickingWorks.AnyAsync(x => x.DeliveryOrderId == id));
        }
        var complete = new DeliveryPickingReportRequest(Guid.NewGuid(), detail.Delivery.Version, [new(line.LineId, "10", null)]);
        var picked = await Post<DeliveryPickingCommandAck>(order.Client, $"/admin/api/deliveries/{id}/picking/complete", complete);
        Assert.False(picked.Replayed);
        Assert.True((await Post<DeliveryPickingCommandAck>(order.Client, $"/admin/api/deliveries/{id}/picking/complete", complete)).Replayed);
        detail = await order.Picking.GetAsync();
        Assert.Equal("ReadyForHandover", detail.Delivery.State);
        var ready = await Get<DeliveryWorkspacePage>(order.Client, "/admin/api/deliveries/workspace?stage=ready");
        Assert.Contains(ready.Items, x => x.Id == id && x.TotalText == "200000");
        var courier = await fixture.Web.AddAccountAsync(order.Account.Store);
        var dispatch = new DeliveryDispatchRequest(Guid.NewGuid(), courier.UserId, [new(id, detail.Delivery.Version)], false);
        using (var response = await order.Client.Http.PostAsJsonAsync("/admin/api/delivery-trips/dispatch", dispatch))
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await using (var db = order.Picking.Source.Context()) Assert.False(await db.DeliveryTripOrders.AnyAsync(x => x.DeliveryOrderId == id));
        dispatch = dispatch with { ClientRequestId = Guid.NewGuid(), PhysicalReceiptConfirmed = true };
        var sent = await Post<DeliveryTripCommandAck>(order.Client, "/admin/api/delivery-trips/dispatch", dispatch);
        Assert.Equal("Delivering", Assert.Single(sent.Orders).State);
        var replay = await Post<DeliveryTripCommandAck>(order.Client, "/admin/api/delivery-trips/dispatch", dispatch);
        Assert.True(replay.Replayed); Assert.Equal(sent.TripId, replay.TripId);
        var counter = await DeliveryD08Case.CounterAsync(fixture.Web, order.Account.Store);
        using var closing = counter.Client;
        var workspacePath = $"/admin/api/delivery-trips/{sent.TripId}/handover";
        var workspace = await Get<DeliveryHandoverWorkspaceDto>(closing, workspacePath);
        Assert.Equal("Departed", workspace.Trip.State);
        Assert.Null(workspace.Trip.ReturnToStore);
        Assert.All(workspace.Trip.Orders, x => Assert.Null(x.Outcome));
        if (delivered == 8)
        {
            var issue = new DeliveryCounterIssueRequest(Guid.NewGuid(), workspace.Trip.Version,
                workspace.Trip.Orders.Single(x => x.DeliveryOrderId == id).Version, "Khách nhận 8, nhận về 2; đang kiểm tiền bàn giao");
            await Post<DeliveryTripCommandAck>(closing, $"/admin/api/delivery-trips/{sent.TripId}/orders/{id}/counter-issue", issue);
            var attention = await Get<DeliveryWorkspacePage>(closing, "/admin/api/deliveries/workspace?stage=attention");
            Assert.Contains(attention.Items, x => x.Id == id && x.NeedsAttention);
            Assert.Contains("nhận về 2", (await Get<DeliveryWorkspaceContext>(closing, $"/admin/api/deliveries/{id}/workspace")).IssueReason);
            workspace = await Get<DeliveryHandoverWorkspaceDto>(closing, workspacePath);
        }
        var current = workspace.Trip.Orders.Single(x => x.DeliveryOrderId == id);
        var quantities = new[] { new DeliveryHandoverLineRequest(line.LineId, delivered.ToString(), (10 - delivered).ToString()) };
        var preview = await Post<DeliveryHandoverPreviewDto>(closing, workspacePath + $"/{id}/preview",
            new DeliveryHandoverPreviewRequest(workspace.Trip.Version, current.Version, quantities));
        Assert.Equal((delivered * 20000).ToString(), preview.PayableText);
        var confirm = new DeliveryHandoverRequest(Guid.NewGuid(), workspace.Trip.Version, current.Version,
            counter.Account.Store.TerminalId, counter.ShiftId, courier.UserId, true,
            delivered == 10 ? "Full" : delivered == 0 ? "NotDelivered" : "Partial", delivered == 10 ? null : "Khách nhận theo lượng thực tế, quầy đã kiểm hàng mang về",
            null, quantities, "Cash", preview.PayableText, preview.PayableText, "0", false);
        if (delivered == 8)
        {
            using var mismatch = await closing.Http.PostAsJsonAsync(workspacePath + $"/{id}", confirm with { ActualReceivedCashText = "150000" });
            Assert.Equal(HttpStatusCode.BadRequest, mismatch.StatusCode);
            await using var db = order.Picking.Source.Context();
            Assert.False(await db.DeliveryJournalEntries.AnyAsync(x => x.DeliveryOrderId == id && x.Kind == DeliveryJournalKind.Return));
            Assert.False(await db.DeliveryJournalEntries.AnyAsync(x => x.DeliveryOrderId == id && x.Kind == DeliveryJournalKind.Settlement));
        }
        confirm = confirm with { ClientRequestId = Guid.NewGuid() };
        var closed = await Post<DeliveryHandoverAck>(closing, workspacePath + $"/{id}", confirm);
        Assert.Equal("Settled", closed.State);
        Assert.True((await Post<DeliveryHandoverAck>(closing, workspacePath + $"/{id}", confirm)).Replayed);
        await using (var db = order.Picking.Source.Context())
        {
            var variant = await order.Inventory.VariantAsync();
            var balance = await db.InventoryBalances.SingleAsync(x => x.WarehouseId == order.Account.Store.WarehouseId && x.ProductVariantId == variant);
            Assert.Equal(10 - delivered, balance.OnHandQty); Assert.Equal(0, balance.ReservedQty);
            Assert.Equal(1, await db.DeliveryJournalEntries.CountAsync(x => x.DeliveryOrderId == id && x.Kind == DeliveryJournalKind.Dispatch));
            Assert.Equal(10 - delivered, await db.DeliveryReturnedCostFragments.Where(x => x.DeliveryOrderId == id).SumAsync(x => x.BaseQuantity));
            Assert.Equal(1, await db.DeliveryJournalEntries.CountAsync(x => x.DeliveryOrderId == id && x.Kind == DeliveryJournalKind.Settlement));
            if (delivered > 0)
            {
                var sale = await db.Orders.Include(x => x.Lines).Include(x => x.Payments).SingleAsync(x => x.Id == closed.Settlement.SaleOrderId);
                Assert.Equal(delivered, sale.Lines.Sum(x => x.BaseQuantity)); Assert.Equal(delivered * 20000, sale.GrandTotal);
                Assert.Equal(sale.GrandTotal, Assert.Single(sale.Payments).Amount);
                Assert.Equal(counter.ShiftId, sale.POSShiftId);
                Assert.False(await db.InventoryTransactions.AnyAsync(x => x.ReferenceType == InventoryReferenceType.Order && x.ReferenceId == sale.Id.ToString()));
            }
            else Assert.Null(closed.Settlement.SaleOrderId);
        }
        var finished = await Get<DeliveryWorkspacePage>(closing, "/admin/api/deliveries/workspace?stage=completed");
        Assert.Contains(finished.Items, x => x.Id == id && x.TotalText == preview.PayableText && !x.NeedsAttention);
        workspace = await Get<DeliveryHandoverWorkspaceDto>(closing, workspacePath);
        var saved = Assert.Single(workspace.Orders.Single(x => x.DeliveryOrderId == id).SettledLines!);
        Assert.Equal(delivered.ToString(), saved.DeliveredBaseQuantityText); Assert.Equal((10 - delivered).ToString(), saved.ActualReturnedBaseQuantityText);
        var viewAccount = await fixture.Web.AddAccountAsync(order.Account.Store, PermissionCodes.Delivery.View);
        using var viewer = await fixture.Web.LoginAsync(viewAccount);
        using (var denied = await viewer.Http.PostAsJsonAsync("/admin/api/delivery-trips/dispatch", dispatch)) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using (var denied = await viewer.Http.PostAsJsonAsync($"/admin/api/deliveries/{id}/picking/complete", complete)) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        var other = await fixture.Web.AddAccountAsync(fixture.Web.Stores[1], "*");
        using var foreign = await fixture.Web.LoginAsync(other);
        using var hidden = await foreign.Http.GetAsync($"/admin/api/deliveries/{id}/workspace");
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
    }

    [Fact]
    public async Task Short_picking_requires_approval_and_bad_bulk_selection_never_dispatches_the_first_order()
    {
        using var first = await DeliveryD06Order.CreateAsync(fixture, quantity: 10, distinctSource: true);
        var detail = await first.Picking.GetAsync(); var line = Assert.Single(detail.Lines);
        await Post<DeliveryPickingCommandAck>(first.Client, $"/admin/api/deliveries/{first.Id}/picking/complete",
            new DeliveryPickingReportRequest(Guid.NewGuid(), detail.Delivery.Version, [new(line.LineId, "8", "Còn 8, thiếu 2")]));
        Assert.Equal("AwaitingApproval", (await first.Picking.GetAsync()).Delivery.State);
        await first.Picking.ApproveAsync();
        using var second = await DeliveryD06Order.CreateAsync(fixture, quantity: 2, distinctSource: true);
        await second.ReadyAsync();
        var courier = await fixture.Web.AddAccountAsync(first.Account.Store);
        var one = await first.Picking.GetAsync(); var two = await second.Picking.GetAsync();
        var bad = new DeliveryDispatchRequest(Guid.NewGuid(), courier.UserId,
            [new(first.Id, one.Delivery.Version), new(second.Id, "AAAAAAAAAAA=")], true);
        using (var response = await first.Client.Http.PostAsJsonAsync("/admin/api/delivery-trips/dispatch", bad)) Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await using (var db = first.Picking.Source.Context()) Assert.False(await db.DeliveryJournalEntries.AnyAsync(x => x.DeliveryOrderId == first.Id && x.Kind == DeliveryJournalKind.Dispatch));
        var good = bad with { ClientRequestId = Guid.NewGuid(), Orders = [new(first.Id, one.Delivery.Version), new(second.Id, two.Delivery.Version)] };
        var sent = await Post<DeliveryTripCommandAck>(first.Client, "/admin/api/delivery-trips/dispatch", good);
        Assert.Equal(2, sent.Orders.Count); Assert.All(sent.Orders, x => Assert.Equal("Delivering", x.State));
    }

    internal static async Task<T> Post<T>(FullApplicationFixture.Client client, string path, object body)
    {
        using var response = await client.Http.PostAsJsonAsync(path, body);
        Assert.True(response.IsSuccessStatusCode, $"{path}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }
    internal static async Task<T> Get<T>(FullApplicationFixture.Client client, string path)
    {
        using var response = await client.Http.GetAsync(path);
        Assert.True(response.IsSuccessStatusCode, $"{path}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }
}
