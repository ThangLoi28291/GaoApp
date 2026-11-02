using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.DTOs.Delivery;
using GaoApp.Application.DTOs.Inventory;
using GaoApp.Application.Storefront;
using GaoApp.Domain.Delivery;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Domain.Storefront;
using GaoApp.Infrastructure.Repositories.Inventory;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Services.Delivery;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Services.Storefront;

public sealed partial class StorefrontFulfillmentService
{
    public Task<StorefrontOperationAck> DispatchAsync(int id, StorefrontDispatchRequest request, CancellationToken ct = default)
        => Command(id, request, "Dispatch", StorefrontPermissions.Dispatch, async (order, token) =>
        {
            Need(!await Incident(id, token), "Đang có sự cố hàng thực tế. Dừng bàn giao và đối soát sự cố trước.");
            await DispatchAll(order, request, request.Carriers, token);
        }, ct);

    private async Task DispatchAll(StorefrontOrder order, StorefrontOperationRequest request,
        IReadOnlyList<StorefrontCarrierAssignment> assignments, CancellationToken ct)
    {
        await StorefrontCancellationFacts.RequireNewDispatchAllowedAsync(db, Store, order.Id, ct);
        await RequireCurrentPaymentProof(order, ct);
        Need(order.Phase == StorefrontOrderPhase.Preparing && PaymentReady(order) && !string.IsNullOrWhiteSpace(request.Note),
            "Chỉ bàn giao đơn đã soạn đủ, thanh toán hợp lệ; phải ghi chứng cứ bàn giao thực tế.");
        var works = await Works(order.Id, ct); var sources = await Sources(order.Id, ct);
        Need(works.Count == sources.Count && works.Count > 0 && works.All(x => x.Stage is StorefrontFulfillmentStage.Picked or StorefrontFulfillmentStage.Incident), "Chưa soạn đủ mọi nguồn.");
        if (assignments is null || assignments.Count != works.Count || assignments.Select(x => x.FulfillmentSourceId).Distinct().Count() != works.Count)
            throw new ValidationAppException("Cần chọn bên nhận giao cho đủ từng nguồn, không trùng.");
        var children = await db.DeliveryOrders.Include(x => x.Lines).Where(x => x.StoreId == Store && x.StorefrontOrderId == order.Id).OrderBy(x => x.Id).ToListAsync(ct);
        Need(children.Count == works.Count && children.All(x => x.State == DeliveryState.ReadyForHandover), "Nguồn hàng chưa sẵn sàng hoặc đã xuất.");
        foreach (var child in children) await DeliverySqlLock.AcquireAsync(db, "order:" + Store + ":" + child.Id, ct);
        var childIds = children.Select(x => x.Id).ToList();
        Need(!await db.DeliveryJournalEntries.AnyAsync(x => x.StoreId == Store && childIds.Contains(x.DeliveryOrderId) && x.Kind == DeliveryJournalKind.Dispatch, ct), "Đã có xuất kho; không bàn giao lại.");
        foreach (var work in works)
        {
            var a = assignments.SingleOrDefault(x => x.FulfillmentSourceId == work.FulfillmentSourceId) ?? throw new ValidationAppException("Bên nhận giao không thuộc nguồn.");
            if (order.DeliveryMethod == StorefrontDeliveryMethod.Local)
            {
                Need(a.CourierUserId is > 0 && string.IsNullOrWhiteSpace(a.CarrierName) && string.IsNullOrWhiteSpace(a.TrackingCode) &&
                    await db.UserInStores.AnyAsync(x => x.StoreId == Store && x.UserId == a.CourierUserId && x.IsActive && x.User.IsActive, ct), "Giao nội vùng cần nhân viên giao còn hoạt động của cửa hàng.");
                work.CourierUserId = a.CourierUserId;
            }
            else
            {
                Need(a.CourierUserId is null && !string.IsNullOrWhiteSpace(a.CarrierName) && a.CarrierName.Length <= 200 &&
                    !string.IsNullOrWhiteSpace(a.TrackingCode) && a.TrackingCode.Length <= 200, "Giao toàn quốc cần tên đơn vị và mã vận đơn thực tế (tối đa 200 ký tự).");
                work.CarrierName = a.CarrierName!.Trim(); work.TrackingCode = a.TrackingCode!.Trim();
            }
        }
        var holds = await db.InventoryReservations.Where(x => x.StoreId == Store && x.StorefrontOrderId == order.Id && x.ReferenceType == InventoryReferenceType.Storefront && x.Status == InventoryReservationStatus.Active).OrderBy(x => x.Id).ToListAsync(ct);
        var keys = holds.Select(x => new InventoryPostingLockKey(Store, x.WarehouseId, x.ProductVariantId)).Distinct().OrderBy(x => x.WarehouseId).ThenBy(x => x.ProductVariantId).ToList();
        await balances.LockBatchAsync(keys, ct);
        var locked = new Dictionary<(int, int), InventoryBalance>();
        foreach (var key in keys)
        {
            var balance = await balances.LockAndGetOrCreateAsync(Store, key.WarehouseId, key.ProductVariantId, ct);
            var active = await new InventoryReservationRepository(db).GetActiveForBalanceAsync(Store, key.WarehouseId, key.ProductVariantId, ct);
            Need(balance.ReservedQty >= 0 && balance.ReservedQty == active.Sum(x => x.ReservedQty), "Sổ giữ chung không khớp; cần đối soát kho trước.");
            locked.Add((key.WarehouseId, key.ProductVariantId), balance);
        }
        foreach (var child in children)
        {
            var picking = await db.DeliveryPickingLines.AsNoTracking().Where(x => x.StoreId == Store && x.DeliveryOrderId == child.Id && x.IsActive).ToListAsync(ct);
            foreach (var line in child.Lines)
            {
                var picked = picking.Single(x => x.DeliveryOrderLineId == line.Id);
                Need(picked.ReportedQuantity == line.StorefrontBaseQuantity && picked.ApprovedQuantity == line.StorefrontBaseQuantity && picked.ReporterUserId is > 0 && picked.ReportedAtUtc.HasValue, "Thiếu chứng cứ soạn đủ dòng nguồn.");
                var own = holds.SingleOrDefault(x => x.StorefrontFulfillmentSourceId == child.StorefrontFulfillmentSourceId && x.StorefrontOrderLineId == line.StorefrontOrderLineId);
                Need(own is not null && own.ReservedQty == line.StorefrontBaseQuantity && own.ProductVariantId == line.VariantId && own.SourceLegalEntityId == child.SourceLegalEntityId, "Giữ website không khớp phân bổ nguồn.");
                var hold = own!; var b = locked[(hold.WarehouseId, hold.ProductVariantId)];
                hold.Status = InventoryReservationStatus.Consumed; hold.ReleasedAtUtc = Now; hold.ReleaseNote = "Chuyển giữ nguyên nguồn sang giao hàng; xem StorefrontReservationTransfers.";
                b.ReservedQty -= hold.ReservedQty;
                var transfer = new InventoryReservation { StoreId = Store, WarehouseId = hold.WarehouseId, ProductVariantId = hold.ProductVariantId,
                    ReferenceType = InventoryReferenceType.Delivery, ReferenceId = child.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), ReferenceLineId = line.Id,
                    DeliveryOrderId = child.Id, DeliveryOrderLineId = line.Id, SourceLegalEntityId = child.SourceLegalEntityId, DeliveryRevision = child.Revision,
                    ReportedByUserId = picked.ReporterUserId, ReportedAtUtc = picked.ReportedAtUtc, ReservedQty = hold.ReservedQty,
                    Status = InventoryReservationStatus.Active, ReservedAtUtc = Now };
                db.InventoryReservations.Add(transfer); InventoryPostingLockState.MarkOwned(db, transfer); b.ReservedQty += transfer.ReservedQty;
                await db.SaveChangesAsync(ct);
                db.Set<StorefrontReservationTransfer>().Add(new() { StoreId = Store, StorefrontOrderId = order.Id, FulfillmentSourceId = child.StorefrontFulfillmentSourceId!.Value,
                    OrderLineId = line.StorefrontOrderLineId!.Value, WebReservationId = hold.Id, DeliveryReservationId = transfer.Id, BaseQuantity = hold.ReservedQty });
                await db.SaveChangesAsync(ct);
            }
        }
        var inventory = new DeliveryInventoryService(db, user, permissions, balances, movement);
        foreach (var child in children)
        {
            var picking = await db.DeliveryPickingWorks.AsNoTracking().SingleAsync(x => x.StoreId == Store && x.DeliveryOrderId == child.Id, ct);
            await inventory.DispatchStorefrontInTransactionAsync(child.Id, new(ChildKey(request.ClientRequestId, child.Id), Convert.ToBase64String(child.RowVersion), picking.ApprovedRevision!.Value), ct);
            // The common executor deliberately reloads the child before posting.
            var current = await db.DeliveryOrders.SingleAsync(x => x.StoreId == Store && x.Id == child.Id, ct); current.State = DeliveryState.HandedOver;
            var work = works.Single(x => x.DeliveryOrderId == child.Id); work.Stage = StorefrontFulfillmentStage.Dispatched;
            work.LatestEvidence = Evidence(order, request, "HandedOver", assignments.Single(x => x.FulfillmentSourceId == work.FulfillmentSourceId), work.FulfillmentSourceId);
            var journals = await db.DeliveryJournalEntries.AsNoTracking().Where(x => x.StoreId == Store && x.DeliveryOrderId == child.Id && x.Kind == DeliveryJournalKind.Dispatch).ToListAsync(ct);
            foreach (var journal in journals)
            {
                var line = child.Lines.Single(x => x.Id == journal.DeliveryOrderLineId);
                db.Set<StorefrontLineSource>().Add(new() { StoreId = Store, StorefrontOrderId = order.Id, FulfillmentSourceId = work.FulfillmentSourceId,
                    OrderLineId = line.StorefrontOrderLineId!.Value, BaseQuantity = journal.BaseQuantity, InventoryTransactionId = journal.InventoryTransactionId,
                    RecordedCost = journal.CostAmount, IsProvisionalCost = await db.DeliveryDispatchCostFragments.AnyAsync(x => x.StoreId == Store && x.DeliveryJournalEntryId == journal.Id && x.IsProvisional, ct) });
            }
        }
        await db.SaveChangesAsync(ct);
        order.Phase = StorefrontOrderPhase.Shipping; order.DeliveryState = StorefrontDeliveryState.HandedOver;
        Evidence(order, request, "AllGoodsHandedOver", new { assignments, sourceIds = sources.Select(x => x.Id), actualCustodyAttestedBy = Actor });
    }
}
