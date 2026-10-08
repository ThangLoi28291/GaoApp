using System.Globalization;
using GaoApp.Application.DTOs.Delivery;
using GaoApp.Domain.Delivery;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Services.Delivery;

// The caller owns the order lock in an ambient transaction. Never acquire a trip lock here.
internal static class DeliveryCustodyProjection
{
    internal sealed record Result(DeliveryCustodyContextDto Context, IReadOnlyDictionary<int, decimal> Quantities);
    internal static async Task<Result?> ReadAsync(AppDbContext db, int store, DeliveryOrder order, CancellationToken ct)
    {
        var binding = await db.DeliveryTripOrders.AsNoTracking().SingleOrDefaultAsync(x =>
            x.StoreId == store && x.DeliveryOrderId == order.Id && x.IsActive, ct);
        if (binding?.CurrentCustodyEventId is null) return null;
        var custody = await db.DeliveryCustodyEvents.AsNoTracking().SingleOrDefaultAsync(x =>
            x.StoreId == store && x.Id == binding.CurrentCustodyEventId && x.TripId == binding.TripId &&
            x.TripOrderId == binding.Id && x.DeliveryOrderId == order.Id, ct);
        var trip = await db.DeliveryTrips.AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == store && x.Id == binding.TripId, ct);
        if (custody is null || trip is null || trip.State is not (DeliveryTripState.HandedOver or DeliveryTripState.Departed) ||
            custody.CourierUserId != trip.CourierUserId || binding.SourceWarehouseId != order.SourceWarehouseId ||
            binding.SourceLegalEntityId != order.SourceLegalEntityId || custody.SourceWarehouseId != order.SourceWarehouseId ||
            custody.SourceLegalEntityId != order.SourceLegalEntityId)
            throw Error();
        var lines = await db.DeliveryCustodyLines.AsNoTracking().Where(x => x.StoreId == store && x.CustodyEventId == custody.Id).ToListAsync(ct);
        var journals = await db.DeliveryJournalEntries.AsNoTracking().Where(x => x.StoreId == store && x.DeliveryOrderId == order.Id &&
            (x.Kind == DeliveryJournalKind.Dispatch || x.Kind == DeliveryJournalKind.Return)).ToListAsync(ct);
        var dispatch = journals.Where(x => x.Kind == DeliveryJournalKind.Dispatch).ToArray();
        if (lines.Count == 0 || lines.Select(x => x.DeliveryOrderLineId).Distinct().Count() != lines.Count || lines.Count != dispatch.Length)
            throw Error();
        var quantities = new Dictionary<int, decimal>();
        foreach (var line in lines)
        {
            var source = dispatch.SingleOrDefault(x => x.Id == line.DispatchJournalEntryId && x.DeliveryOrderLineId == line.DeliveryOrderLineId &&
                x.VariantId == line.VariantId && x.SourceWarehouseId == line.SourceWarehouseId && x.SourceLegalEntityId == line.SourceLegalEntityId);
            var received = journals.Where(x => x.Kind == DeliveryJournalKind.Return && x.DeliveryOrderLineId == line.DeliveryOrderLineId).Sum(x => x.BaseQuantity);
            var current = line.ConfirmedBaseQuantity - (received - line.CumulativeAcceptedReturnAtConfirmation);
            if (source is null || line.SourceWarehouseId != order.SourceWarehouseId || line.SourceLegalEntityId != order.SourceLegalEntityId ||
                line.ConfirmedBaseQuantity < 0 || received < line.CumulativeAcceptedReturnAtConfirmation || current < 0 ||
                line.ConfirmedBaseQuantity + line.CumulativeAcceptedReturnAtConfirmation != source.BaseQuantity)
                throw Error();
            quantities.Add(line.DeliveryOrderLineId, current);
        }
        var userIds = new[] { custody.CourierUserId, custody.ActorUserId, trip.DepartedByUserId ?? 0 };
        var names = await db.Users.AsNoTracking().Where(x => userIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id,
            x => x.FullName ?? x.UserName, ct);
        string Name(int id) => names.GetValueOrDefault(id, id.ToString(CultureInfo.InvariantCulture));
        DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
        return new(new(trip.Id, trip.Code, trip.State.ToString(), custody.Id, custody.Kind.ToString(), custody.CourierUserId,
            Name(custody.CourierUserId), custody.ActorUserId, Name(custody.ActorUserId), Utc(custody.ConfirmedAtUtc),
            trip.DepartedByUserId, trip.DepartedByUserId.HasValue ? Name(trip.DepartedByUserId.Value) : null,
            trip.DepartedAtUtc.HasValue ? Utc(trip.DepartedAtUtc.Value) : null), quantities);
    }
    private static DeliveryFoundationException Error() => new(409, "CUSTODY_PROOF_INVALID", "Chứng cứ trách nhiệm giữ hàng không khớp nguồn bàn giao.");
}
