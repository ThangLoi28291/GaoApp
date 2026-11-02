using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.Delivery;
using GaoApp.Domain.Delivery;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace GaoApp.Infrastructure.Services.Delivery;

public sealed partial class DeliveryFoundationService
{
    public async Task<DeliveryWorkspaceContext> WorkspaceContextAsync(int id, CancellationToken ct = default)
    {
        await Authorize(PermissionCodes.Delivery.View, ct);
        var order = await Load(id, false, ct);
        var tripId = await (from member in db.DeliveryTripOrders.AsNoTracking()
                            join trip in db.DeliveryTrips.AsNoTracking() on member.TripId equals trip.Id
                            where member.StoreId == Store && trip.StoreId == Store && member.IsActive && member.DeliveryOrderId == id
                            select (int?)trip.Id).SingleOrDefaultAsync(ct);
        string? reason = null;
        if (order.State is not (DeliveryState.Settled or DeliveryState.Cancelled))
        {
            var snapshot = await db.DeliveryRevisions.AsNoTracking().Where(x => x.StoreId == Store && x.DeliveryOrderId == id &&
                x.Action == "trip-counter-issue").OrderByDescending(x => x.Revision).Select(x => x.SnapshotJson).FirstOrDefaultAsync(ct);
            if (snapshot is not null)
            {
                using var document = JsonDocument.Parse(snapshot);
                if (document.RootElement.TryGetProperty("tripReason", out var value)) reason = value.GetString();
            }
        }
        return new(id, tripId, reason);
    }
    public async Task<DeliveryWorkspacePage> WorkspaceAsync(DeliveryWorkspaceQuery request, CancellationToken ct = default)
    {
        await Authorize(PermissionCodes.Delivery.View, ct);
        if (request.Stage is not ("picking" or "ready" or "delivering" or "completed" or "attention") ||
            (request.Search?.Length ?? 0) > 200)
            throw Error(400, "QUERY_INVALID", "Bộ lọc đơn giao không hợp lệ.");
        var query = db.DeliveryOrders.AsNoTracking().Where(x => x.StoreId == Store &&
            db.Warehouses.Any(w => w.StoreId == Store && w.Id == x.SourceWarehouseId && w.IsActive && w.LegalEntityId == x.SourceLegalEntityId) &&
            db.LegalEntities.Any(l => l.StoreId == Store && l.Id == x.SourceLegalEntityId && l.IsActive));
        var search = request.Search?.Trim();
        if (!string.IsNullOrEmpty(search)) query = query.Where(x => x.Code.Contains(search) || x.RecipientName.Contains(search) ||
            x.RecipientPhone.Contains(search) || x.RecipientAddress.Contains(search));
        var issues = db.DeliveryRevisions.Where(x => x.StoreId == Store && x.Action == "trip-counter-issue")
            .Select(x => x.DeliveryOrderId);
        var states = await query.GroupBy(x => x.State).Select(g => new { State = g.Key, Count = g.Count() }).ToListAsync(ct);
        int Count(params DeliveryState[] values) => states.Where(x => values.Contains(x.State)).Sum(x => x.Count);
        var attention = query.Where(x => x.State == DeliveryState.AwaitingApproval || x.State == DeliveryState.CancellationPending ||
            (x.State != DeliveryState.Settled && x.State != DeliveryState.Cancelled && issues.Contains(x.Id)));
        var counts = new DeliveryWorkspaceCounts(Count(DeliveryState.Created, DeliveryState.Picking, DeliveryState.AwaitingApproval),
            Count(DeliveryState.ReadyForHandover), Count(DeliveryState.HandedOver, DeliveryState.Delivering,
                DeliveryState.AwaitingReconciliation, DeliveryState.ReadyToSettle, DeliveryState.CancellationPending),
            Count(DeliveryState.Settled, DeliveryState.Cancelled), await attention.CountAsync(ct));
        query = request.Stage switch
        {
            "picking" => query.Where(x => x.State == DeliveryState.Created || x.State == DeliveryState.Picking || x.State == DeliveryState.AwaitingApproval),
            "ready" => query.Where(x => x.State == DeliveryState.ReadyForHandover),
            "delivering" => query.Where(x => x.State == DeliveryState.HandedOver || x.State == DeliveryState.Delivering ||
                x.State == DeliveryState.AwaitingReconciliation || x.State == DeliveryState.ReadyToSettle || x.State == DeliveryState.CancellationPending),
            "completed" => query.Where(x => x.State == DeliveryState.Settled || x.State == DeliveryState.Cancelled),
            _ => attention
        };
        var total = await query.CountAsync(ct);
        var pages = Math.Max(1, (total + 29) / 30);
        var page = Math.Clamp(request.Page, 1, pages);
        // Open work is oldest-first; finished orders are newest-first. Every stage is paged independently.
        var ordered = request.Stage == "completed" ? query.OrderByDescending(x => x.Id) : query.OrderBy(x => x.Id);
        var orders = await ordered.Skip((page - 1) * 30).Take(30).Include(x => x.Lines).ToListAsync(ct);
        var ids = orders.Select(x => x.Id).ToArray();
        var work = await db.DeliveryPickingWorks.AsNoTracking().Where(x => x.StoreId == Store && ids.Contains(x.DeliveryOrderId)).ToListAsync(ct);
        var priceChanges = await DeliverySalePrices.ReadManyAsync(db, orders, ct);
        var changedIds = priceChanges.Keys.ToArray();
        var pricedLines = changedIds.Length == 0 ? [] : await db.DeliveryPickingLines.AsNoTracking()
            .Where(x => x.StoreId == Store && changedIds.Contains(x.DeliveryOrderId)).ToListAsync(ct);
        var trips = await (from member in db.DeliveryTripOrders.AsNoTracking()
                           join trip in db.DeliveryTrips.AsNoTracking() on member.TripId equals trip.Id
                           where member.StoreId == Store && trip.StoreId == Store && member.IsActive && ids.Contains(member.DeliveryOrderId)
                           select new { member.DeliveryOrderId, TripId = trip.Id, trip.Code, trip.CourierUserId }).ToListAsync(ct);
        var nameIds = work.Select(x => x.PickerUserId).Concat(trips.Select(x => x.CourierUserId)).Distinct().ToArray();
        var names = await db.Users.IgnoreQueryFilters().AsNoTracking().Where(x => nameIds.Contains(x.Id))
            .Select(x => new { x.Id, Name = x.FullName ?? x.UserName }).ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var issueIds = await issues.Where(id => ids.Contains(id)).Distinct().ToListAsync(ct);
        var settlements = await db.DeliveryJournalEntries.AsNoTracking().Where(x => x.StoreId == Store &&
            ids.Contains(x.DeliveryOrderId) && x.Kind == DeliveryJournalKind.Settlement)
            .Select(x => new { x.DeliveryOrderId, x.MoneyAmount }).ToDictionaryAsync(x => x.DeliveryOrderId, x => x.MoneyAmount, ct);
        var rows = orders.Select(order =>
        {
            var picking = work.SingleOrDefault(x => x.DeliveryOrderId == order.Id);
            var trip = trips.SingleOrDefault(x => x.DeliveryOrderId == order.Id);
            var flagged = order.State is DeliveryState.AwaitingApproval or DeliveryState.CancellationPending ||
                (order.State is not (DeliveryState.Settled or DeliveryState.Cancelled) && issueIds.Contains(order.Id));
            decimal? changedTotal = null;
            if (priceChanges.TryGetValue(order.Id, out var change))
            {
                changedTotal = order.Lines.Where(quote => pricedLines.SingleOrDefault(x => x.DeliveryOrderId == order.Id && x.DeliveryOrderLineId == quote.Id)?.IsActive ?? true).Sum(quote =>
                {
                    var line = pricedLines.SingleOrDefault(x => x.DeliveryOrderId == order.Id && x.DeliveryOrderLineId == quote.Id);
                    var quantity = line?.ApprovedQuantity ?? line?.PlannedQuantity ?? quote.OrderedQuantity;
                    var originalNet = line?.ApprovedNet ?? DeliveryPickingPolicy.Prorate(quote.Net, quote.OrderedQuantity, quantity);
                    return DeliverySalePrices.Calculate(quote, quantity, originalNet, change.Prices).Net;
                });
            }
            return new DeliveryWorkspaceRow(order.Id, order.Code, order.State.ToString(), order.Revision,
                Convert.ToBase64String(order.RowVersion), order.RecipientName, order.RecipientPhone, order.RecipientAddress,
                (settlements.TryGetValue(order.Id, out var actual) ? actual : changedTotal ?? picking?.ApprovedTotal ?? order.QuotedTotal)
                    .ToString("0", System.Globalization.CultureInfo.InvariantCulture), order.CreatedAtUtc, trip?.TripId, trip?.Code,
                trip is null ? null : names.GetValueOrDefault(trip.CourierUserId),
                picking is null ? null : names.GetValueOrDefault(picking.PickerUserId), flagged);
        }).ToArray();
        var canDispatch = await permissions.HasPermissionAsync(Store, Actor, PermissionCodes.Delivery.Handover, ct) &&
            await permissions.HasPermissionAsync(Store, Actor, PermissionCodes.Delivery.RecordDeparture, ct);
        return new(Store, counts, page, pages, total, rows, canDispatch);
    }
}
