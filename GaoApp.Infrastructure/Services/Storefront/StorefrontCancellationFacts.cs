using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.DTOs.Inventory;
using GaoApp.Application.Interfaces.Repositories.Inventory;
using GaoApp.Domain.Delivery;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Domain.Storefront;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Services.Storefront;

internal sealed record StorefrontCancellationPhysicalFacts(bool HasDispatch, bool HasDeliveredGoods, bool HasIncident,
    bool CompleteDispatchProof, bool AllChildrenDelivered, bool AllDispatchReturned, bool ReturnAuthorised, bool NeedsDeliveryReview,
    int? DeliveryConflictRequestId,
    StorefrontCancellationRequest? PendingRequest, IReadOnlyList<StorefrontCancellationRequest> Requests,
    IReadOnlyList<StorefrontCancellationDecision> Decisions);

internal static class StorefrontCancellationFacts
{
    internal static async Task<StorefrontCancellationPhysicalFacts> ReadAsync(AppDbContext db, int store, int id, CancellationToken ct)
    {
        var requests = await db.Set<StorefrontCancellationRequest>().IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.StoreId == store && x.StorefrontOrderId == id).OrderBy(x => x.Id).Take(1001).ToListAsync(ct);
        if (requests.Count > 1000) throw new ConflictAppException("Lịch sử yêu cầu cần đối soát; không thực hiện thêm tự động.");
        var decisions = await db.Set<StorefrontCancellationDecision>().IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.StoreId == store && x.StorefrontOrderId == id).OrderBy(x => x.Id).Take(1001).ToListAsync(ct);
        if (decisions.Count > 1000) throw new ConflictAppException("Lịch sử quyết định cần đối soát.");
        var pending = requests.Where(x => !x.IsImmediate && decisions.All(d => d.CancellationRequestId != x.Id)).ToList();
        if (pending.Count > 1) throw new ConflictAppException("Có nhiều yêu cầu hủy chưa xử lý; cần kiểm tra nguồn.");
        var childIds = db.DeliveryOrders.IgnoreQueryFilters().Where(x => x.StoreId == store && x.SourceKind == DeliverySourceKind.Storefront && x.StorefrontOrderId == id).Select(x => x.Id);
        var work = await db.Set<StorefrontFulfillmentWork>().IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.StoreId == store && x.StorefrontOrderId == id).ToListAsync(ct);
        var childStates = await db.DeliveryOrders.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.StoreId == store && x.SourceKind == DeliverySourceKind.Storefront && x.StorefrontOrderId == id)
            .Select(x => x.State).ToListAsync(ct);
        var dispatch = await db.DeliveryDispatchCostFragments.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.StoreId == store && childIds.Contains(x.DeliveryOrderId)).ToListAsync(ct);
        var returns = await db.DeliveryReturnedCostFragments.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.StoreId == store && childIds.Contains(x.DeliveryOrderId)).ToListAsync(ct);
        var hasDispatch = dispatch.Count > 0 || await db.DeliveryJournalEntries.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(x => x.StoreId == store && childIds.Contains(x.DeliveryOrderId) && x.Kind == DeliveryJournalKind.Dispatch, ct);
        // Missing journal/cost proof is never evidence that custody did not move.
        hasDispatch |= work.Any(x => x.Stage is StorefrontFulfillmentStage.Dispatched or StorefrontFulfillmentStage.Delivered or
            StorefrontFulfillmentStage.ReturnPending or StorefrontFulfillmentStage.Returned or StorefrontFulfillmentStage.Incident) ||
            childStates.Any(x => x is DeliveryState.HandedOver or DeliveryState.Delivering or DeliveryState.AwaitingReconciliation or
                DeliveryState.ReadyToSettle or DeliveryState.CancellationPending or DeliveryState.Settled) ||
            await db.InventoryTransactions.IgnoreQueryFilters().AsNoTracking().AnyAsync(x => x.StoreId == store && x.ReferenceType == InventoryReferenceType.Delivery &&
                childIds.Any(child => child.ToString() == x.ReferenceId), ct);
        var hasDelivered = work.Any(x => x.Stage == StorefrontFulfillmentStage.Delivered) || await db.Set<StorefrontFulfillmentEvidence>().IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(x => x.StoreId == store && x.StorefrontOrderId == id && x.Kind == "Delivered", ct);
        var incident = await db.Set<StorefrontCustodyIncident>().IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(x => x.StoreId == store && x.StorefrontOrderId == id && x.ReconciledByEvidenceId == null, ct);
        var deliveredAll = work.Count > 0 && work.All(x => x.Stage == StorefrontFulfillmentStage.Delivered) &&
            work.Count == await childIds.CountAsync(ct);
        var allReturned = dispatch.Count > 0 && dispatch.All(fragment =>
            returns.Where(r => r.DispatchCostFragmentId == fragment.Id).Sum(r => r.BaseQuantity) == fragment.BaseQuantity) &&
            returns.All(r => dispatch.Any(d => d.Id == r.DispatchCostFragmentId));
        var expected = await db.DeliveryOrderLines.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.StoreId == store && childIds.Contains(x.DeliveryOrderId))
            .Select(x => new { x.Id, x.DeliveryOrderId, x.StorefrontBaseQuantity }).ToListAsync(ct);
        var sealedSources = await db.Set<StorefrontFulfillmentSource>().IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.StoreId == store && x.StorefrontOrderId == id).Select(x => new { x.Id, x.DeliveryOrderId }).ToListAsync(ct);
        var completeDispatch = work.Count > 0 && work.Count == childStates.Count && work.Count == sealedSources.Count &&
            sealedSources.All(source => source.DeliveryOrderId.HasValue && work.Any(w => w.FulfillmentSourceId == source.Id && w.DeliveryOrderId == source.DeliveryOrderId)) && expected.Count > 0 &&
            expected.All(line => line.StorefrontBaseQuantity.HasValue && line.StorefrontBaseQuantity > 0 &&
                dispatch.Where(x => x.DeliveryOrderLineId == line.Id).Sum(x => x.BaseQuantity) == line.StorefrontBaseQuantity) &&
            dispatch.All(x => expected.Any(line => line.Id == x.DeliveryOrderLineId));
        var returnAuthorised = decisions.Any(x => x.Kind == StorefrontCancellationDecisionKind.Approved && x.AuthorisesPhysicalReturn);
        var superseded = decisions.Where(x => x.Kind == StorefrontCancellationDecisionKind.SupersededByActualDelivery).Select(x => x.Id).ToArray();
        var acknowledged = await db.Set<StorefrontCancellationReviewAcknowledgement>().IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.StoreId == store && x.StorefrontOrderId == id).Select(x => x.CancellationDecisionId).ToListAsync(ct);
        var conflictRequest = decisions.Where(x => x.Kind == StorefrontCancellationDecisionKind.SupersededByActualDelivery && !acknowledged.Contains(x.Id))
            .OrderBy(x => x.Id).Select(x => (int?)x.CancellationRequestId).FirstOrDefault();
        return new(hasDispatch, hasDelivered, incident, completeDispatch, deliveredAll, allReturned, returnAuthorised,
            superseded.Any(x => !acknowledged.Contains(x)), conflictRequest, pending.SingleOrDefault(), requests, decisions);
    }

    internal static async Task RequireNewDispatchAllowedAsync(AppDbContext db, int store, int id, CancellationToken ct)
    {
        var pending = await db.Set<StorefrontCancellationRequest>().IgnoreQueryFilters().AnyAsync(r => r.StoreId == store && r.StorefrontOrderId == id && !r.IsImmediate &&
            !db.Set<StorefrontCancellationDecision>().IgnoreQueryFilters().Any(d => d.StoreId == store && d.CancellationRequestId == r.Id), ct);
        var recall = await db.Set<StorefrontCancellationDecision>().IgnoreQueryFilters().AnyAsync(d => d.StoreId == store && d.StorefrontOrderId == id && d.AuthorisesPhysicalReturn, ct);
        if (pending || recall) throw new ConflictAppException("Có yêu cầu hủy hoặc thu hồi đang chờ xử lý; dừng bàn giao hàng mới.");
    }

    internal static async Task ReleaseOwnHoldsAsync(AppDbContext db, IInventoryBalanceRepository balances, StorefrontOrder order,
        DateTime now, string reason, CancellationToken ct)
    {
        var keys = await db.InventoryReservations.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.StoreId == order.StoreId && x.StorefrontOrderId == order.Id && x.ReferenceType == InventoryReferenceType.Storefront && x.Status == InventoryReservationStatus.Active)
            .Select(x => new InventoryPostingLockKey(x.StoreId, x.WarehouseId, x.ProductVariantId)).Distinct().ToListAsync(ct);
        await balances.LockBatchAsync(keys.OrderBy(x => x.WarehouseId).ThenBy(x => x.ProductVariantId).ToList(), ct);
        var reservations = await db.InventoryReservations.FromSqlInterpolated($"SELECT * FROM [InventoryReservations] WITH(UPDLOCK,HOLDLOCK) WHERE [StoreId]={order.StoreId} AND [StorefrontOrderId]={order.Id}")
            .IgnoreQueryFilters().Where(x => x.ReferenceType == InventoryReferenceType.Storefront).ToListAsync(ct);
        if (reservations.Any(x => x.IsDeleted || x.Status == InventoryReservationStatus.Consumed))
            throw new ConflictAppException("Có giữ hàng đã dùng hoặc nguồn không còn nguyên vẹn; cần đối soát hàng thực tế.");
        foreach (var group in reservations.Where(x => x.Status == InventoryReservationStatus.Active).GroupBy(x => new { x.WarehouseId, x.ProductVariantId }).OrderBy(x => x.Key.WarehouseId).ThenBy(x => x.Key.ProductVariantId))
        {
            var balance = await balances.LockAndGetOrCreateAsync(order.StoreId, group.Key.WarehouseId, group.Key.ProductVariantId, ct);
            balance.ReservedQty = StorefrontInventoryPolicy.ReleaseOwn(balance.ReservedQty, group.Sum(x => x.ReservedQty));
            balance.QuantityPrecisionVersion = 1;
            foreach (var reservation in group)
            {
                if (reservation.ReferenceId != order.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) ||
                    !await db.Set<StorefrontReservationLink>().AnyAsync(x => x.StoreId == order.StoreId && x.StorefrontOrderId == order.Id && x.InventoryReservationId == reservation.Id, ct))
                    throw new ConflictAppException("Nguồn giữ hàng không khớp đơn; không giải phóng giữ của chứng từ khác.");
                reservation.Status = InventoryReservationStatus.Released; reservation.ReleasedAtUtc = now;
                reservation.ReleaseNote = reason.Length <= 500 ? reason : reason[..500];
            }
        }
    }
}
