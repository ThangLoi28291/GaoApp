using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.Delivery;
using GaoApp.Domain.Delivery;
using GaoApp.Tests.Security;
using GaoApp.Web.Services.StoreMonitor;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Delivery;

[Collection("DeliveryD08")]
public sealed class DeliveryD10MonitorTests(DeliveryD02Fixture fixture)
{
    private static async Task<JsonElement> Snapshot(FullApplicationFixture.Client client)
    {
        // Saved snapshots are shared for one second across office viewers.
        await Task.Delay(1100);
        return await client.JsonAsync(HttpMethod.Get, "/admin/api/store-activity/snapshot");
    }
    private static JsonElement Card(JsonElement snapshot, int id) => Assert.Single(snapshot.GetProperty("delivery").GetProperty("orders")
        .EnumerateArray(), x => x.GetProperty("orderId").GetInt32() == id);
    private static async Task<JsonElement> Frame(StreamReader reader, int orderId, string state, CancellationToken ct)
    {
        var name = "";
        while (true)
        {
            var line = await reader.ReadLineAsync(ct) ?? throw new InvalidOperationException("Office stream closed.");
            if (line.StartsWith("event: ")) name = line[7..];
            if (name == "snapshot" && line.StartsWith("data: "))
            {
                using var document = JsonDocument.Parse(line[6..]);
                var data = document.RootElement.Clone();
                if (Card(data, orderId).GetProperty("state").GetString() == state) return data;
            }
        }
    }

    [Fact]
    public async Task Committed_create_pick_shortage_departure_are_live_durable_private_and_read_only()
    {
        using var order = await DeliveryD06Order.CreateAsync(fixture.Web);
        var manager = await fixture.Web.AddAccountAsync(order.Account.Store, PermissionCodes.Admin.StoreMonitorView);
        using var office = await fixture.Web.LoginAsync(manager);
        using var foreign = await fixture.Web.LoginAsync(await fixture.Web.AddAccountAsync(fixture.Web.Stores[1], PermissionCodes.Admin.StoreMonitorView));
        var initial = await Snapshot(office);
        Assert.Equal("Created", Card(initial, order.Id).GetProperty("state").GetString());
        Assert.Contains("cập nhật người nhận", Card(initial, order.Id).GetProperty("lastAction").GetString());
        Assert.Single(initial.GetProperty("events").EnumerateArray(), x => x.GetProperty("action").GetString() == "Delivery.foundation-create"
            && x.GetProperty("workKey").GetString() == "delivery:" + order.Id);
        Assert.Equal(1, Card(initial, order.Id).GetProperty("totalLines").GetInt32());
        Assert.Equal(JsonValueKind.Null, Card(initial, order.Id).GetProperty("lines")[0].GetProperty("prepared").ValueKind);
        var beforeRead = await order.StockAndFinanceAsync();
        await Snapshot(office); await Snapshot(office);
        Assert.Equal(beforeRead, await order.StockAndFinanceAsync());
        using var noPermission = await fixture.Web.LoginAsync(await fixture.Web.AddAccountAsync(order.Account.Store, PermissionCodes.Delivery.View));
        using var denied = await noPermission.Http.GetAsync("/admin/api/store-activity/snapshot");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        using var request = new HttpRequestMessage(HttpMethod.Get, "/admin/api/store-activity/stream");
        using var stream = await office.Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
        Assert.Equal(HttpStatusCode.OK, stream.StatusCode);
        using var reader = new StreamReader(await stream.Content.ReadAsStreamAsync(deadline.Token));
        await Frame(reader, order.Id, "Created", deadline.Token);
        await order.Picking.ClaimAsync();
        var watch = Stopwatch.StartNew();
        var picking = await Frame(reader, order.Id, "Picking", deadline.Token);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(3), "Local office update target is under three seconds.");
        Assert.NotNull(Card(picking, order.Id).GetProperty("picker").GetString());
        using var otherOrder = await DeliveryD06Order.CreateAsync(fixture.Web);
        await using (var db = fixture.Web.Database.CreateTenantContext(otherOrder.Account.Store.StoreId))
        {
            var employee = await db.Users.SingleAsync(x => x.Id == otherOrder.Account.UserId);
            employee.FullName = "Nhân viên soạn thứ hai D10";
            await db.SaveChangesAsync();
        }
        // Changing a profile invalidates its old session; authenticate the test
        // employee again instead of weakening the normal session validator.
        using var otherPicker = await fixture.Web.LoginAsync(otherOrder.Account);
        await otherOrder.Picking.ClaimAsync(otherPicker);
        var simultaneous = await Snapshot(office);
        Assert.Equal("Picking", Card(simultaneous, otherOrder.Id).GetProperty("state").GetString());
        Assert.NotEqual(Card(simultaneous, order.Id).GetProperty("picker").GetString(),
            Card(simultaneous, otherOrder.Id).GetProperty("picker").GetString());
        await order.Picking.ReportAsync("1", "Chưa lấy đủ hàng");
        var report = await Snapshot(office);
        Assert.Equal(1, Card(report, order.Id).GetProperty("checkedLines").GetInt32());
        Assert.Equal(1, Card(report, order.Id).GetProperty("shortageLines").GetInt32());
        Assert.Equal(1, Card(report, order.Id).GetProperty("lines")[0].GetProperty("prepared").GetDecimal());
        var eventIds = report.GetProperty("events").EnumerateArray().Where(x => x.GetProperty("module").GetString() == "delivery")
            .Select(x => x.GetProperty("id").GetInt64()).ToArray();
        var detail = await order.Picking.GetAsync();
        using (var failed = await order.Client.Http.PostAsJsonAsync(order.Picking.Path("report"),
            new DeliveryPickingReportRequest(Guid.NewGuid(), detail.Delivery.Version,
                [new(detail.Lines.Single(x => x.IsActive).LineId, "9999", null)])))
            Assert.False(failed.IsSuccessStatusCode);
        var afterFailure = await Snapshot(office);
        Assert.Equal(eventIds, afterFailure.GetProperty("events").EnumerateArray().Where(x => x.GetProperty("module").GetString() == "delivery")
            .Select(x => x.GetProperty("id").GetInt64()).ToArray());
        await order.Picking.ReportAsync("2"); await order.Picking.SubmitAsync();
        Assert.Equal("ReadyForHandover", Card(await Snapshot(office), order.Id).GetProperty("state").GetString());
        var courier = await fixture.Web.AddAccountAsync(order.Account.Store);
        using var trip = new DeliveryD06Case(fixture.Web, [order], courier, courier);
        await trip.CreateDraftAsync();
        Assert.Contains("xếp đơn vào chuyến", Card(await Snapshot(office), order.Id).GetProperty("lastAction").GetString());
        await trip.HandoverAsync(); await trip.DepartAsync();
        var departed = await Frame(reader, order.Id, "Delivering", deadline.Token);
        var live = Card(departed, order.Id);
        Assert.True(live.GetProperty("custodyConfirmed").GetBoolean());
        Assert.Equal(trip.TripId, live.GetProperty("tripId").GetInt32());
        Assert.NotEqual(JsonValueKind.Null, live.GetProperty("departedAtUtc").ValueKind);
        var raw = departed.GetProperty("delivery").GetRawText();
        foreach (var privateField in new[] { "recipientPhone", "recipientAddress", "lookupToken", "payloadJson", "latitude", "longitude" })
            Assert.DoesNotContain(privateField, raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain((await Snapshot(foreign)).GetProperty("delivery").GetProperty("orders").EnumerateArray(),
            x => x.GetProperty("orderId").GetInt32() == order.Id);
        var persistedTime = live.GetProperty("updatedAtUtc").GetString();
        var persistedDeparture = live.GetProperty("departedAtUtc").GetString();
        var streamEventIds = departed.GetProperty("events").EnumerateArray().Where(x => x.GetProperty("module").GetString() == "delivery")
            .Select(x => x.GetProperty("id").GetInt64()).ToArray();
        stream.Dispose();
        await fixture.Web.RestartAsync(enableDeliveryOutbox: false);
        var restored = await Snapshot(office);
        Assert.Equal("Delivering", Card(restored, order.Id).GetProperty("state").GetString());
        Assert.Equal(persistedTime, Card(restored, order.Id).GetProperty("updatedAtUtc").GetString());
        Assert.Equal(persistedDeparture, Card(restored, order.Id).GetProperty("departedAtUtc").GetString());
        Assert.Equal(streamEventIds, restored.GetProperty("events").EnumerateArray().Where(x => x.GetProperty("module").GetString() == "delivery")
            .Select(x => x.GetProperty("id").GetInt64()).ToArray());
        Assert.Equal(Card(restored, order.Id).GetRawText(), Card(await Snapshot(office), order.Id).GetRawText());
    }

    [Fact]
    public async Task Counter_confirmation_emits_one_saved_settlement_with_actual_amount_and_replay_has_no_duplicate()
    {
        using var c = await DeliveryD08Case.CreateAsync(fixture);
        using var office = await fixture.Web.LoginAsync(await fixture.Web.AddAccountAsync(c.Closer.Store, PermissionCodes.Admin.StoreMonitorView));
        var body = await c.RequestAsync(delivered: 2);
        var ack = await c.ConfirmAsync(body);
        var snapshot = await Snapshot(office);
        var card = Card(snapshot, c.Id);
        Assert.Equal("Settled", card.GetProperty("state").GetString());
        Assert.Equal(40000, card.GetProperty("settledTotal").GetDecimal());
        Assert.StartsWith("Thực giao:", card.GetProperty("detail").GetString());
        Assert.Contains(": 2 ", card.GetProperty("detail").GetString());
        Assert.Contains("đối chiếu hàng, tiền", card.GetProperty("lastAction").GetString());
        Assert.NotNull(card.GetProperty("lastActor").GetString());
        var saved = Assert.Single(snapshot.GetProperty("events").EnumerateArray(), x =>
            x.GetProperty("action").GetString() == "Delivery.handover" && x.GetProperty("workKey").GetString() == "delivery:" + c.Id);
        Assert.Equal(c.Closer.UserId, saved.GetProperty("userId").GetInt32());
        Assert.Contains("40.000", saved.GetProperty("detail").GetString());
        var financial = await c.Trip.StockAsync();
        var replay = await c.ConfirmAsync(body); Assert.True(replay.Replayed);
        Assert.Equal(ack.Settlement, replay.Settlement);
        Assert.Equal(financial, await c.Trip.StockAsync());
        var repeated = await Snapshot(office);
        Assert.Single(repeated.GetProperty("events").EnumerateArray(), x => x.GetProperty("id").GetInt64() == saved.GetProperty("id").GetInt64());
    }

    [Fact]
    public async Task Completed_card_expires_after_sixty_seconds_without_hiding_unfinished_work_or_removing_history()
    {
        using var c = await DeliveryD08Case.CreateAsync(fixture);
        await c.ConfirmAsync(await c.RequestAsync(delivered: 2));
        await using var db = c.Context();
        var order = await db.DeliveryOrders.AsNoTracking().SingleAsync(x => x.Id == c.Id);
        var completedAt = new DateTimeOffset(DateTime.SpecifyKind(order.UpdatedAtUtc!.Value, DateTimeKind.Utc));
        var unfinishedId = c.Trip.Orders.Single(x => x.Id != c.Id).Id;
        var before = await c.Trip.StockAsync();
        async Task<StoreDeliverySnapshot> At(TimeSpan offset)
            => await new StoreDeliveryMonitor(db, new MonitorClock(completedAt + offset)).ReadAsync(c.Closer.Store.StoreId, default);

        var visible = await At(TimeSpan.FromMilliseconds(59999));
        var card = Assert.Single(visible.Orders, x => x.OrderId == c.Id);
        Assert.Equal(completedAt, card.CompletedAtUtc);
        Assert.Equal(60, visible.CompletedCardLifetimeSeconds);
        Assert.Equal("Settled", card.State);
        Assert.Equal(40000, card.SettledTotal);
        Assert.Contains(visible.Orders, x => x.OrderId == unfinishedId && x.State == "Delivering");

        foreach (var offset in new[] { TimeSpan.FromSeconds(60), TimeSpan.FromHours(5) })
        {
            var expired = await At(offset);
            Assert.DoesNotContain(expired.Orders, x => x.OrderId == c.Id);
            Assert.Contains(expired.Orders, x => x.OrderId == unfinishedId && x.State == "Delivering");
            Assert.Equal(visible.ActiveCount, expired.ActiveCount);
        }
        Assert.Equal(before, await c.Trip.StockAsync());
        var history = await c.Order.Inventory.Picking.Source.Service(db).GetAsync(c.Id);
        Assert.Equal("Settled", history.State);
    }

    private sealed class MonitorClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
