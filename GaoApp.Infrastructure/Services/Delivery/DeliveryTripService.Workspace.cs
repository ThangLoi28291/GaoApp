using GaoApp.Application.DTOs.Delivery;
using GaoApp.Domain.Delivery;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Services.Delivery;

public sealed partial class DeliveryTripService
{
    // One transaction owns assignment, physical custody, stock export and departure.
    public Task<DeliveryTripCommandAck> DispatchAsync(DeliveryDispatchRequest request, CancellationToken ct = default)
    {
        Selections(request.Orders);
        request = request with { Note = DeliveryTripPolicy.NormalizeNote(request.Note) };
        return Command(null, request.ClientRequestId, "dispatch", null, request,
            DeliveryCapability.View | DeliveryCapability.Handover | DeliveryCapability.RecordDeparture,
            request.Orders, async (_, _, _) =>
            {
                DeliveryTripPolicy.PhysicalReceipt(request.PhysicalReceiptConfirmed, request.CourierUserId, request.CourierUserId);
                await Courier(request.CourierUserId, ct);
                await ReadySelections(request.Orders, null, ct);
                var trip = new DeliveryTrip { StoreId = Store, Code = "GH-" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(),
                    CourierUserId = request.CourierUserId, CreatedByUserId = Actor, Note = request.Note, State = DeliveryTripState.Draft };
                db.DeliveryTrips.Add(trip); await db.SaveChangesAsync(ct);
                await Bind(trip, request.Orders, [], ct); await db.SaveChangesAsync(ct);
                var members = await Members(trip.Id, true, ct);
                var handover = new DeliveryTripHandoverRequest(request.ClientRequestId, Version(trip.RowVersion), request.Orders,
                    request.CourierUserId, request.PhysicalReceiptConfirmed, request.Note);
                await HandoverCore(trip, members, handover, ct);
                await db.SaveChangesAsync(ct);
                await RecordDispatchPhase(trip, members, request.ClientRequestId, "handover", handover, trip.HandedOverAtUtc!.Value, ct);
                var departedOrders = new List<DeliveryTripOrderSelection>();
                foreach (var member in members)
                {
                    var order = await Order(member.DeliveryOrderId, false, false, ct);
                    departedOrders.Add(new(order.Id, Version(order.RowVersion)));
                }
                var departure = new DeliveryTripDepartureRequest(request.ClientRequestId, Version(trip.RowVersion), departedOrders);
                await DepartCore(trip, members, departure, ct);
                await db.SaveChangesAsync(ct);
                await RecordDispatchPhase(trip, members, request.ClientRequestId, "depart", departure, trip.DepartedAtUtc!.Value, ct);
                trip.Revision++;
                return trip;
            }, ct);
    }

    private async Task RecordDispatchPhase<T>(DeliveryTrip trip, List<DeliveryTripOrder> members, Guid key,
        string operation, T request, DateTime now, CancellationToken ct)
    {
        var orders = new List<DeliveryTripAppliedOrder>();
        foreach (var member in members)
        {
            var order = await Order(member.DeliveryOrderId, false, false, ct);
            orders.Add(new(order.Id, order.Revision, Version(order.RowVersion), order.State.ToString()));
        }
        var ack = new DeliveryTripCommandAck(key, trip.Id, operation, trip.Revision, Version(trip.RowVersion), Utc(now), false, orders);
        await RecordTrip(trip, key, operation, "", DeliveryCapability.View | DeliveryCapability.Handover | DeliveryCapability.RecordDeparture,
            request, ack, now, ct, phaseOnly: true);
    }

    private sealed record CounterIssueEnvelope(int OrderId, DeliveryCounterIssueRequest Request);

    // Record an unresolved counter handover without posting goods, money or a sale.
    public Task<DeliveryTripCommandAck> RecordCounterIssueAsync(int id, int orderId,
        DeliveryCounterIssueRequest request, CancellationToken ct = default)
    {
        request = request with { Reason = DeliveryTripPolicy.Reason(request.Reason) };
        return Command(id, request.ClientRequestId, "counter-issue", request.ExpectedTripVersion,
            new CounterIssueEnvelope(orderId, request), DeliveryCapability.View | DeliveryCapability.Finalize,
            null, async (value, members, _) =>
            {
                var trip = value!; await DepartedProof(trip, members, ct);
                if (!members.Any(x => x.DeliveryOrderId == orderId))
                    throw Error(404, "ORDER_NOT_IN_TRIP", "Đơn không thuộc chuyến hiện tại.");
                var order = await Order(orderId, true, true, ct);
                Match(request.ExpectedOrderVersion, order.RowVersion);
                if (!Reportable(order.State)) throw Error(409, "REPORT_LOCKED", "Đơn đã khóa hoặc chưa xuất phát.");
                var custody = await DeliveryCustodyProjection.ReadAsync(db, Store, order, ct);
                if (custody?.Context.TripId != id || custody.Context.CourierUserId != trip.CourierUserId)
                    throw Error(409, "CUSTODY_PROOF_INVALID", "Trách nhiệm giữ hàng chưa khớp chuyến.");
                if (order.State != DeliveryState.CancellationPending) order.State = DeliveryState.AwaitingReconciliation;
                order.Revision++; await db.SaveChangesAsync(ct);
                var now = await SqlUtcAtOrAfter(trip.DepartedAtUtc!.Value, ct);
                await RecordOrder(order, "counter-issue", request.ClientRequestId, request.Reason, now, ct);
                trip.Revision++; return trip;
            }, ct);
    }
}
