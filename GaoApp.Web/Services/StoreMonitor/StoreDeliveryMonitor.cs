using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using GaoApp.Domain.Delivery;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Web.Services.StoreMonitor;

public sealed record StoreDeliveryLine(string ItemName, string UnitName, decimal Planned, decimal? Prepared);
public sealed record StoreDeliveryCard(int OrderId, string Code, string State, int Revision, string Recipient,
    string? Picker, string? Courier, bool CustodyConfirmed, int? TripId, string? TripCode,
    DateTimeOffset? DepartedAtUtc, DateTimeOffset? ReturnedAtUtc, DateTimeOffset UpdatedAtUtc, string? LastActor, string LastAction, string? Detail,
    int CheckedLines, int TotalLines, int ShortageLines, int TripOrders, decimal? SettledTotal,
    IReadOnlyList<StoreDeliveryLine> Lines, long? EventId, DateTimeOffset? CompletedAtUtc = null,
    int? PickerUserId = null, int? CourierUserId = null);
public sealed record StoreDeliverySnapshot(IReadOnlyList<StoreDeliveryCard> Orders, int ActiveCount,
    int PickingCount, int DeliveringCount, int WaitingCount, int SettledCount)
{
    [JsonIgnore] public IReadOnlyList<StoreActivityEvent> Events { get; init; } = [];
    public int CompletedCardLifetimeSeconds => StoreDeliveryMonitor.CompletedCardLifetimeSeconds;
}

/// <summary>Office-only projection of committed SQL facts. No business writes, checkpoints or GPS.</summary>
public sealed class StoreDeliveryMonitor(AppDbContext db, TimeProvider time)
{
    public const int MaxCards = 120;
    public const int CompletedCardLifetimeSeconds = 60;
    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
    private static string Clean(string? value, int max = 160)
    {
        var text = new string((value ?? "").Where(c => !char.IsControl(c)).ToArray()).Trim();
        return text.Length > max ? text[..max] : text;
    }
    public async Task<StoreDeliverySnapshot> ReadAsync(int store, CancellationToken ct)
    {
        // Never allow a caller-supplied store to escape the tenant resolved by HTTP.
        if (store <= 0 || db.CurrentStoreId != store) return new([], 0, 0, 0, 0, 0);
        var recent = time.GetUtcNow().UtcDateTime.AddHours(-4);
        var completedSince = time.GetUtcNow().UtcDateTime.AddSeconds(-CompletedCardLifetimeSeconds);
        var orders = db.DeliveryOrders.AsNoTracking().Where(x => x.StoreId == store && !x.IsDeleted);
        var counts = await orders.GroupBy(x => x.State).Select(g => new { State = g.Key, Count = g.Count() }).ToListAsync(ct);
        var headers = await orders.Where(x => (x.State != DeliveryState.Settled && x.State != DeliveryState.Cancelled)
                || (x.UpdatedAtUtc ?? x.CreatedAtUtc) > completedSince)
            .OrderByDescending(x => x.State != DeliveryState.Settled && x.State != DeliveryState.Cancelled)
            .ThenByDescending(x => x.UpdatedAtUtc ?? x.CreatedAtUtc).ThenByDescending(x => x.Id).Take(MaxCards)
            .Select(x => new { x.Id, x.Code, x.State, x.Revision, x.RecipientName, x.CreatedAtUtc, x.UpdatedAtUtc }).ToListAsync(ct);
        var ids = headers.Select(x => x.Id).ToArray();
        var work = await db.DeliveryPickingWorks.AsNoTracking().Where(x => x.StoreId == store && !x.IsDeleted && ids.Contains(x.DeliveryOrderId))
            .Select(x => new { x.DeliveryOrderId, x.PickerUserId }).ToDictionaryAsync(x => x.DeliveryOrderId, ct);
        var lines = await (from line in db.DeliveryPickingLines.AsNoTracking().Where(x => x.StoreId == store && !x.IsDeleted && x.IsActive && ids.Contains(x.DeliveryOrderId))
            join quote in db.DeliveryOrderLines.Where(x => x.StoreId == store && !x.IsDeleted)
                on line.DeliveryOrderLineId equals quote.Id
            select new { line.DeliveryOrderId, quote.ItemName, quote.UnitName, line.PlannedQuantity, line.ReportedQuantity }).ToListAsync(ct);
        // Before a picker claims an order, show the saved POS quote without
        // inventing prepared quantities. Once work exists, active picking lines own the plan.
        var unclaimed = ids.Where(id => !work.ContainsKey(id)).ToArray();
        lines.AddRange(await db.DeliveryOrderLines.AsNoTracking().Where(x => x.StoreId == store && !x.IsDeleted && unclaimed.Contains(x.DeliveryOrderId))
            .OrderBy(x => x.Id).Select(x => new { x.DeliveryOrderId, x.ItemName, x.UnitName,
                PlannedQuantity = x.OrderedQuantity, ReportedQuantity = (decimal?)null }).ToListAsync(ct));
        var trips = await (from member in db.DeliveryTripOrders.AsNoTracking().Where(x => x.StoreId == store && !x.IsDeleted && x.IsActive && ids.Contains(x.DeliveryOrderId))
            join trip in db.DeliveryTrips.Where(x => x.StoreId == store && !x.IsDeleted && x.State != DeliveryTripState.Abandoned) on member.TripId equals trip.Id
            join custody in db.DeliveryCustodyEvents.Where(x => x.StoreId == store && !x.IsDeleted) on member.CurrentCustodyEventId equals (int?)custody.Id into custodyRows
            from custody in custodyRows.DefaultIfEmpty()
            select new { member.DeliveryOrderId, TripId = trip.Id, trip.Code, trip.DepartedAtUtc,
                CourierId = custody == null ? trip.CourierUserId : custody.CourierUserId, Confirmed = custody != null,
                Orders = db.DeliveryTripOrders.Count(x => x.StoreId == store && !x.IsDeleted && x.IsActive && x.TripId == trip.Id) }).ToListAsync(ct);
        var settlements = await db.DeliveryJournalEntries.AsNoTracking().Where(x => x.StoreId == store && !x.IsDeleted &&
            x.Kind == DeliveryJournalKind.Settlement && ids.Contains(x.DeliveryOrderId))
            .Select(x => new { x.DeliveryOrderId, x.MoneyAmount, x.SaleOrderId }).ToDictionaryAsync(x => x.DeliveryOrderId, ct);
        var saleIds = settlements.Values.Where(x => x.SaleOrderId.HasValue).Select(x => x.SaleOrderId!.Value).ToArray();
        var soldLines = await db.OrderLines.AsNoTracking().Where(x => x.StoreId == store && saleIds.Contains(x.OrderId))
            .OrderBy(x => x.Id).Select(x => new { x.OrderId, x.ItemName, x.Quantity, UnitName = x.SellingUnitName ?? x.UnitName }).ToListAsync(ct);
        // The outbox is the durable event rail: polling these committed rows also
        // works after restart and does not depend on a transient worker receipt.
        var eventQuery = from message in db.DeliveryOutboxMessages.AsNoTracking().Where(x => x.StoreId == store && !x.IsDeleted)
            join revision in db.DeliveryRevisions.Where(x => x.StoreId == store && !x.IsDeleted)
                on new { message.DeliveryOrderId, message.Revision } equals new { revision.DeliveryOrderId, revision.Revision }
            join order in orders on message.DeliveryOrderId equals order.Id
            select new { message.Id, message.DeliveryOrderId, message.Revision, message.Action, message.CreatedAtUtc,
                message.PayloadJson, revision.ActorUserId, order.Code };
        var events = await eventQuery.Where(x => x.CreatedAtUtc >= recent).OrderByDescending(x => x.Id).Take(60).ToListAsync(ct);
        var currentEvents = await eventQuery.Where(x => ids.Contains(x.DeliveryOrderId) && orders.Any(o => o.Id == x.DeliveryOrderId && o.Revision == x.Revision))
            .ToListAsync(ct);
        // Trip-only edits do not revise an order, but must still reach the office.
        // Handover/departure already have per-order outbox facts, so do not duplicate them.
        var tripIds = trips.Select(x => x.TripId).Distinct().ToArray();
        var tripMessages = db.DeliveryTripOutboxMessages.AsNoTracking().Where(x => x.StoreId == store && !x.IsDeleted &&
            (x.Action == "create" || x.Action == "draft" || x.Action == "abandon" || x.Action == "return-to-store"));
        var tripEventQuery = from message in tripMessages
            join revision in db.DeliveryTripRevisions.Where(x => x.StoreId == store && !x.IsDeleted)
                on new { message.TripId, message.Revision } equals new { revision.TripId, revision.Revision }
            join trip in db.DeliveryTrips.Where(x => x.StoreId == store && !x.IsDeleted) on message.TripId equals trip.Id
            select new { message.Id, message.TripId, message.Action, message.CreatedAtUtc, revision.ActorUserId, trip.Code };
        var recentTripEvents = await tripEventQuery.Where(x => x.CreatedAtUtc >= recent).OrderByDescending(x => x.Id).Take(60).ToListAsync(ct);
        var currentTripEvents = await tripEventQuery.Where(x => tripIds.Contains(x.TripId) &&
            !tripMessages.Any(later => later.TripId == x.TripId && later.Id > x.Id)).ToListAsync(ct);
        var tripEvents = recentTripEvents.Concat(currentTripEvents).DistinctBy(x => x.Id).OrderByDescending(x => x.Id).ToArray();
        var actorIds = events.Concat(currentEvents).Select(x => x.ActorUserId).Concat(tripEvents.Select(x => x.ActorUserId))
            .Concat(work.Values.Select(x => x.PickerUserId)).Concat(trips.Select(x => x.CourierId)).Distinct().ToArray();
        var people = await db.UserInStores.AsNoTracking().Where(x => x.StoreId == store && !x.IsDeleted && !x.User.IsDeleted && actorIds.Contains(x.UserId))
            .Select(x => new { x.UserId, Name = string.IsNullOrWhiteSpace(x.User.FullName) ? x.User.UserName : x.User.FullName })
            .ToDictionaryAsync(x => x.UserId, x => Clean(x.Name, 100), ct);
        string Person(int id) => people.GetValueOrDefault(id, "Nhân viên #" + id);
        // Positive IDs belong to transient registry events; negative even/odd IDs
        // give the two SQL outboxes stable, disjoint identities across reconnects.
        var rail = events.Where(x => ActionText(x.Action) is not null).Select(e => new StoreActivityEvent(-2L * e.Id, e.ActorUserId,
            Person(e.ActorUserId), "delivery", ActionText(e.Action)!, Clean(e.Code, 70), null, Utc(e.CreatedAtUtc),
            "Delivery." + e.Action, e.Action == "handover" && settlements.TryGetValue(e.DeliveryOrderId, out var paid)
                ? "Giá trị hàng thực giao đã chốt: " + paid.MoneyAmount.ToString("N0", CultureInfo.GetCultureInfo("vi-VN")) + " đ"
                : EventDetail(e.PayloadJson), "delivery:" + e.DeliveryOrderId))
            .Concat(tripEvents.Where(x => x.CreatedAtUtc >= recent).Take(60).Select(e => new StoreActivityEvent(-2L * e.Id - 1, e.ActorUserId,
                Person(e.ActorUserId), "delivery", TripActionText(e.Action), Clean(e.Code, 70), null, Utc(e.CreatedAtUtc),
                "Delivery.trip-" + e.Action, null, "delivery-trip:" + e.TripId)))
            .OrderByDescending(x => x.OccurredAtUtc).Take(60).ToArray();
        var cards = headers.Select(h =>
        {
            work.TryGetValue(h.Id, out var picking);
            var trip = trips.SingleOrDefault(x => x.DeliveryOrderId == h.Id);
            var items = lines.Where(x => x.DeliveryOrderId == h.Id).ToArray();
            var latest = currentEvents.SingleOrDefault(x => x.DeliveryOrderId == h.Id && x.Revision == h.Revision);
            var tripEvent = trip is null ? null : tripEvents.FirstOrDefault(x => x.TripId == trip.TripId);
            var completed = h.State is DeliveryState.Settled or DeliveryState.Cancelled;
            // Edits to other orders in the same trip must not restart a completed card's lifetime.
            var tripChanged = !completed && tripEvent is not null && tripEvent.CreatedAtUtc > (latest?.CreatedAtUtc ?? h.UpdatedAtUtc ?? h.CreatedAtUtc);
            var detail = latest is null ? null : EventDetail(latest.PayloadJson);
            var shortage = items.Count(x => x.ReportedQuantity.HasValue && x.ReportedQuantity < x.PlannedQuantity);
            var summary = detail ?? (items.Length == 0 ? null : string.Join(" · ", items.Take(2).Select(x =>
                $"{Clean(x.ItemName, 80)}: {x.ReportedQuantity?.ToString("0.####", CultureInfo.GetCultureInfo("vi-VN")) ?? "chưa soạn"}/{x.PlannedQuantity.ToString("0.####", CultureInfo.GetCultureInfo("vi-VN"))} {Clean(x.UnitName, 20)}")));
            settlements.TryGetValue(h.Id, out var settlement);
            if (h.State == DeliveryState.Settled)
            {
                var delivered = soldLines.Where(x => x.OrderId == settlement?.SaleOrderId).ToArray();
                summary = delivered.Length > 0 ? "Thực giao: " + string.Join(" · ", delivered.Take(3).Select(x =>
                    $"{Clean(x.ItemName, 80)}: {x.Quantity.ToString("0.####", CultureInfo.GetCultureInfo("vi-VN"))} {Clean(x.UnitName, 20)}"))
                    : settlement is { MoneyAmount: 0, SaleOrderId: null } ? "Không có hàng thực giao · đã chốt 0 đ" : null;
            }
            else if (summary is not null) summary = "Lượng soạn đã ghi nhận: " + summary;
            return new StoreDeliveryCard(h.Id, Clean(h.Code, 70), h.State.ToString(), h.Revision, Clean(h.RecipientName, 100),
                picking is null ? null : Person(picking.PickerUserId), trip is null ? null : Person(trip.CourierId), trip?.Confirmed ?? false,
                trip?.TripId, trip?.Code, trip?.DepartedAtUtc is { } departure ? Utc(departure) : null,
                tripEvent?.Action == "return-to-store" ? Utc(tripEvent.CreatedAtUtc) : null,
                Utc(tripChanged ? tripEvent!.CreatedAtUtc : latest?.CreatedAtUtc ?? h.UpdatedAtUtc ?? h.CreatedAtUtc),
                tripChanged ? Person(tripEvent!.ActorUserId) : latest is null ? null : Person(latest.ActorUserId),
                tripChanged ? TripActionText(tripEvent!.Action) : latest is null ? "Trạng thái đã lưu" : ActionText(latest.Action) ?? "Trạng thái đã lưu",
                summary, items.Count(x => x.ReportedQuantity.HasValue), items.Length, shortage, trip?.Orders ?? 0,
                settlement?.MoneyAmount,
                items.Select(x => new StoreDeliveryLine(Clean(x.ItemName), Clean(x.UnitName, 30), x.PlannedQuantity, x.ReportedQuantity)).ToArray(),
                tripChanged ? -2L * tripEvent!.Id - 1 : latest is null ? null : -2L * latest.Id,
                completed ? Utc(h.UpdatedAtUtc ?? h.CreatedAtUtc) : null, picking?.PickerUserId, trip?.CourierId);
        }).ToArray();
        int Count(params DeliveryState[] states) => counts.Where(x => states.Contains(x.State)).Sum(x => x.Count);
        return new(cards, counts.Where(x => x.State != DeliveryState.Settled && x.State != DeliveryState.Cancelled).Sum(x => x.Count),
            Count(DeliveryState.Created, DeliveryState.Picking), Count(DeliveryState.HandedOver, DeliveryState.Delivering),
            Count(DeliveryState.AwaitingApproval, DeliveryState.ReadyForHandover, DeliveryState.AwaitingReconciliation, DeliveryState.ReadyToSettle, DeliveryState.CancellationPending),
            Count(DeliveryState.Settled)) { Events = rail };
    }
    public static string? ActionText(string action) => action switch
    {
        "foundation-create" => "vừa tạo đơn giao",
        "recipient" => "vừa cập nhật người nhận đơn giao",
        "picking-claim" => "vừa nhận soạn đơn",
        "picking-report" => "vừa ghi nhận lượng hàng đã soạn",
        "picking-submit" => "vừa gửi kết quả soạn hàng",
        "picking-plan" => "vừa xử lý hàng thiếu / thay hàng",
        "picking-approve" => "vừa duyệt và chốt soạn hàng",
        "picking-reopen" => "vừa mở lại đơn để soạn",
        "picking-reassign" => "vừa đổi người soạn hàng",
        "inventory-dispatch" => "vừa xuất hàng cho người giao",
        "inventory-receive" => "vừa xác nhận nhận lại hàng",
        "inventory-cancel" => "vừa hủy đơn giao",
        "trip-handover" => "vừa bàn giao hàng cho người giao",
        "trip-depart" => "vừa ghi nhận người giao xuất phát",
        "trip-reassign" => "vừa bàn giao cho người giao mới",
        "trip-cancel-request" => "vừa yêu cầu thu hồi đơn giao",
        "trip-outcome" => "vừa ghi nhận kết quả thực giao",
        "handover" => "vừa đối chiếu hàng, tiền và chốt đơn giao",
        _ => null
    };
    private static string TripActionText(string action) => action switch
    {
        "create" => "vừa xếp đơn vào chuyến giao",
        "draft" => "vừa cập nhật đơn và người giao trong chuyến",
        "abandon" => "vừa bỏ chuyến đang xếp",
        "return-to-store" => "vừa xác nhận người giao đã trở về tiệm",
        _ => "vừa cập nhật chuyến giao"
    };
    private static string? EventDetail(string json)
    {
        // Whitelist only saved product names and quantities. Phone/address, notes,
        // lookup tokens, payment metadata and entire payloads never leave this reader.
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("lines", out var lines) || lines.ValueKind != JsonValueKind.Array) return null;
            return Clean(string.Join(" · ", lines.EnumerateArray().Take(3).Select(line =>
            {
                var name = line.TryGetProperty("itemName", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : "Hàng trong đơn";
                var unit = line.TryGetProperty("unitName", out var u) && u.ValueKind == JsonValueKind.String ? u.GetString() : "";
                var planned = line.TryGetProperty("plannedQuantityText", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : "—";
                var prepared = line.TryGetProperty("reportedQuantityText", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString() : "chưa soạn";
                return $"{Clean(name, 80)}: {Clean(prepared, 25)}/{Clean(planned, 25)} {Clean(unit, 20)}";
            })), 240);
        }
        catch (JsonException) { return null; }
    }
}
