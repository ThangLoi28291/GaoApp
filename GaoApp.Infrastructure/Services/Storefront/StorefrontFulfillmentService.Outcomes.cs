using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.DTOs.Delivery;
using GaoApp.Application.Storefront;
using GaoApp.Domain.Delivery;
using GaoApp.Domain.Storefront;
using GaoApp.Infrastructure.Services.Delivery;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Services.Storefront;

public sealed partial class StorefrontFulfillmentService
{
    public Task<StorefrontOperationAck> RecordOutcomeAsync(int id, StorefrontDeliveryOutcomeRequest request, CancellationToken ct = default)
        => Command(id, request, "RecordDeliveryOutcome", StorefrontPermissions.RecordDelivery, async (order, token) =>
        {
            Need(order.Phase == StorefrontOrderPhase.Shipping && !await Incident(id, token), "Đơn chưa bàn giao đủ hoặc đang có sự cố cần đối soát.");
            if (string.IsNullOrWhiteSpace(request.EvidenceReference) || request.EvidenceReference.Length > 500 || string.IsNullOrWhiteSpace(request.Note) ||
                request.ResultOccurredAt == default || request.ResultOccurredAt.UtcDateTime > Now.AddMinutes(2) || request.ResultOccurredAt.UtcDateTime < order.CreatedAtUtc)
                throw new ValidationAppException("Cần thời điểm kết quả thực tế, nội dung và mã chứng cứ giao.");
            var works = await Works(id, token); var work = works.SingleOrDefault(x => x.FulfillmentSourceId == request.FulfillmentSourceId)
                ?? throw new NotFoundAppException("Nguồn giao không thuộc đơn.");
            Need(work.Stage == StorefrontFulfillmentStage.Dispatched, "Nguồn chưa được bàn giao hoặc đã có kết quả.");
            work.Stage = request.Delivered ? StorefrontFulfillmentStage.Delivered : StorefrontFulfillmentStage.ReturnPending;
            var child = await db.DeliveryOrders.SingleAsync(x => x.StoreId == Store && x.Id == work.DeliveryOrderId, token);
            Need(await db.DeliveryJournalEntries.AnyAsync(x => x.StoreId == Store && x.DeliveryOrderId == child.Id && x.Kind == DeliveryJournalKind.Dispatch, token), "Thiếu chứng cứ xuất bàn giao.");
            child.State = DeliveryState.AwaitingReconciliation;
            work.LatestEvidence = Evidence(order, request, request.Delivered ? "Delivered" : "DeliveryFailed", new { request.ResultOccurredAt, request.EvidenceReference, actualActor = Actor }, work.FulfillmentSourceId);
            await db.SaveChangesAsync(token);
            if (request.Delivered)
            {
                var cancellation = await StorefrontCancellationFacts.ReadAsync(db, Store, id, token);
                if (cancellation.PendingRequest is { } pending)
                {
                    Need(!cancellation.ReturnAuthorised && work.LatestEvidence is { Id: > 0, Kind: "Delivered" }, "Cần đối soát bằng chứng giao và yêu cầu thu hồi trước.");
                    db.Add(new StorefrontCancellationDecision { StoreId = Store, StorefrontOrderId = id, CancellationRequestId = pending.Id,
                        Kind = StorefrontCancellationDecisionKind.SupersededByActualDelivery, ActorUserId = Actor,
                        Reason = "Đã nhận bằng chứng giao thành công khi yêu cầu hủy đang chờ. Cửa hàng tiếp tục kiểm tra yêu cầu theo hàng thực tế.",
                        DecidedAtUtc = Now, AuthorisesPhysicalReturn = false, ActualDeliveryEvidenceId = work.LatestEvidence!.Id });
                    await db.SaveChangesAsync(token);
                }
            }
            if (works.All(x => x.Stage == StorefrontFulfillmentStage.Delivered))
            { order.Phase = StorefrontOrderPhase.Delivered; order.DeliveryState = StorefrontDeliveryState.Delivered; }
            else if (works.All(x => x.Stage is StorefrontFulfillmentStage.ReturnPending or StorefrontFulfillmentStage.Returned))
                order.DeliveryState = StorefrontDeliveryState.ReturnPending;
            // A mixed real outcome remains Shipping, visible for manual incident reconciliation.
        }, ct);

    public Task<StorefrontOperationAck> ReceiveReturnAsync(int id, StorefrontReceiveReturnRequest request, CancellationToken ct = default)
        => Command(id, request, "ReceiveFailedDeliveryReturn", StorefrontPermissions.ReceiveReturns, async (order, token) =>
        {
            Need(order.Phase == StorefrontOrderPhase.Shipping && !await Incident(id, token) && !string.IsNullOrWhiteSpace(request.Note), "Cần đơn giao thất bại và chứng cứ nhận thực tế, không có sự cố chưa đối soát.");
            if (request.Lines is null || request.Lines.Count is < 1 or > 20000 || request.Lines.Select(x => (x.FulfillmentSourceId, x.OrderLineId)).Distinct().Count() != request.Lines.Count)
                throw new ValidationAppException("Dòng nhận trả phải không trùng và trong giới hạn.");
            var works = await Works(id, token); var sources = await Sources(id, token);
            var requested = request.Lines.ToDictionary(x => (x.FulfillmentSourceId, x.OrderLineId), x => Quantity(x.BaseQuantity));
            Need(requested.Values.All(x => x > 0), "Số lượng nhận trả phải dương.");
            var children = await db.DeliveryOrders.Include(x => x.Lines).Where(x => x.StoreId == Store && x.StorefrontOrderId == id).ToListAsync(token);
            var applicable = children.Where(x => requested.Keys.Any(k => k.FulfillmentSourceId == x.StorefrontFulfillmentSourceId)).OrderBy(x => x.Id).ToList();
            Need(applicable.Sum(x => x.Lines.Count(l => requested.ContainsKey((x.StorefrontFulfillmentSourceId!.Value, l.StorefrontOrderLineId!.Value)))) == requested.Count, "Dòng nhận không thuộc nguồn đã xuất.");
            foreach (var child in applicable) await DeliverySqlLock.AcquireAsync(db, "order:" + Store + ":" + child.Id, token);
            await balances.LockBatchAsync(applicable.SelectMany(child => child.Lines.Where(l => requested.ContainsKey((child.StorefrontFulfillmentSourceId!.Value, l.StorefrontOrderLineId!.Value)))
                .Select(l => new GaoApp.Application.DTOs.Inventory.InventoryPostingLockKey(Store, child.SourceWarehouseId, l.VariantId))).Distinct().ToList(), token);
            var inventory = new DeliveryInventoryService(db, user, permissions, balances, movement);
            foreach (var child in applicable)
            {
                var work = works.Single(x => x.DeliveryOrderId == child.Id); Need(work.Stage == StorefrontFulfillmentStage.ReturnPending, "Chỉ nhận về nguồn có kết quả giao thất bại; không nhận hàng đã giao khách.");
                var rows = child.Lines.Where(l => requested.ContainsKey((work.FulfillmentSourceId, l.StorefrontOrderLineId!.Value)))
                    .Select(l => new DeliveryInventoryReturnLine(l.Id, Qty(requested[(work.FulfillmentSourceId, l.StorefrontOrderLineId!.Value)]))).ToList();
                await inventory.ReceiveStorefrontReturnsInTransactionAsync(child.Id, new(ChildKey(request.ClientRequestId, child.Id), Convert.ToBase64String(child.RowVersion), rows, request.Note), token);
                var dispatched = await db.DeliveryJournalEntries.Where(x => x.StoreId == Store && x.DeliveryOrderId == child.Id && x.Kind == DeliveryJournalKind.Dispatch).SumAsync(x => x.BaseQuantity, token);
                var returned = await db.DeliveryReturnedCostFragments.Where(x => x.StoreId == Store && x.DeliveryOrderId == child.Id).SumAsync(x => x.BaseQuantity, token);
                if (dispatched == returned) work.Stage = StorefrontFulfillmentStage.Returned;
                work.LatestEvidence = Evidence(order, request, "ReturnReceived", rows, work.FulfillmentSourceId);
            }
            await db.SaveChangesAsync(token);
            var returnedEvidence = Evidence(order, request, "FailedDeliveryReturnReceived", request.Lines);
            await db.SaveChangesAsync(token);
            if (works.All(x => x.Stage == StorefrontFulfillmentStage.Returned))
            {
                order.Phase = StorefrontOrderPhase.Cancelled; order.DeliveryState = StorefrontDeliveryState.Returned;
                await db.SaveChangesAsync(token);
                await CloseCancellationAfterActualReturn(order, returnedEvidence, token);
                await StorefrontRefundObligations.EnsureAsync(db, order, clock, token);
                if (order.RefundState == StorefrontRefundState.None && await HasRetainedFunds(order.Id, token)) order.RefundState = StorefrontRefundState.Requested;
            }
        }, ct);

    public Task<StorefrontOperationAck> RecordIncidentAsync(int id, StorefrontIncidentRequest request, CancellationToken ct = default)
        => Command(id, request, "RecordCustodyIncident", StorefrontPermissions.Dispatch, async (order, token) =>
        {
            Need(order.Phase is StorefrontOrderPhase.Preparing or StorefrontOrderPhase.Shipping && !await Incident(id, token), "Chỉ ghi sự cố chuẩn bị/giao khi chưa có sự cố mở.");
            if (string.IsNullOrWhiteSpace(request.Note) || string.IsNullOrWhiteSpace(request.EvidenceReference) || request.EvidenceReference.Length > 500 ||
                request.CustodySourceIds is null || request.CustodySourceIds.Count is < 1 or > 100 || request.CustodySourceIds.Distinct().Count() != request.CustodySourceIds.Count)
                throw new ValidationAppException("Cần nguồn đang thực tế ngoài kho và chứng cứ sự cố.");
            var sources = await Sources(id, token); Need(request.CustodySourceIds.All(x => sources.Any(s => s.Id == x)), "Nguồn sự cố không thuộc đơn.");
            var evidence = new StorefrontFulfillmentEvidence { StoreId = Store, StorefrontOrderId = id, ActorUserId = Actor, ClientRequestId = request.ClientRequestId,
                Kind = "CustodyIncident", OccurredAtUtc = Now, Note = request.Note.Trim(), SnapshotJson = Serialize(new { request.CustodySourceIds, request.EvidenceReference }) };
            db.Add(evidence); await db.SaveChangesAsync(token);
            db.Add(new StorefrontCustodyIncident { StoreId = Store, StorefrontOrderId = id, EvidenceId = evidence.Id, ActualCustodySourceIdsJson = Serialize(request.CustodySourceIds.Order()) });
        }, ct);

    public Task<StorefrontOperationAck> ReconcileIncidentAsync(int id, StorefrontIncidentReconcileRequest request, CancellationToken ct = default)
        => Command(id, request, "ReconcileCustodyIncident", StorefrontPermissions.Reconcile, async (order, token) =>
        {
            var incident = await db.Set<StorefrontCustodyIncident>().SingleOrDefaultAsync(x => x.StoreId == Store && x.StorefrontOrderId == id && x.Id == request.IncidentId && x.ReconciledByEvidenceId == null, token)
                ?? throw new ConflictAppException("Không có sự cố mở này.");
            var cancelAfterActualRecovery = false;
            Need(Enum.IsDefined(request.Resolution) && !string.IsNullOrWhiteSpace(request.Note) && !string.IsNullOrWhiteSpace(request.EvidenceReference) && request.EvidenceReference.Length <= 500, "Cần cách xử lý và chứng cứ kiểm đếm thực tế.");
            var works = await Works(id, token); var childIds = works.Select(x => x.DeliveryOrderId).ToList();
            var lines = await db.DeliveryOrderLines.AsNoTracking().Where(x => x.StoreId == Store && childIds.Contains(x.DeliveryOrderId)).ToListAsync(token);
            Need(request.CountedLines is not null && request.CountedLines.Count == lines.Count && request.CountedLines.Select(x => (x.FulfillmentSourceId, x.OrderLineId)).Distinct().Count() == lines.Count &&
                lines.All(line => request.CountedLines.Any(x => works.Any(w => w.DeliveryOrderId == line.DeliveryOrderId && w.FulfillmentSourceId == x.FulfillmentSourceId) &&
                    x.OrderLineId == line.StorefrontOrderLineId && Quantity(x.BaseQuantity) == line.StorefrontBaseQuantity)), "Phải kiểm đếm đủ nguyên lượng mọi nguồn; sự cố giao một phần tiếp tục dừng để đối soát thực tế.");
            var exports = await db.DeliveryJournalEntries.AsNoTracking().Where(x => x.StoreId == Store && childIds.Contains(x.DeliveryOrderId) && x.Kind == DeliveryJournalKind.Dispatch).ToListAsync(token);
            if (exports.Count > 0)
            {
                Need(lines.All(l => exports.Where(x => x.DeliveryOrderLineId == l.Id).Sum(x => x.BaseQuantity) == l.StorefrontBaseQuantity), "Chứng từ xuất chưa đủ mọi nguồn. Giữ dừng để kiểm tra riêng từng chứng từ; không xuất bù tự động.");
                if (request.Resolution == StorefrontIncidentResolution.AllGoodsRecoveredAtSource)
                {
                    await Guard(StorefrontPermissions.ReceiveReturns, token); await Guard(StorefrontPermissions.RecordDelivery, token);
                    Need(works.All(x => x.Stage != StorefrontFulfillmentStage.Delivered), "Đã có nguồn giao khách; không thể xác nhận toàn bộ hàng đã về kho.");
                    var children = await db.DeliveryOrders.Include(x => x.Lines).Where(x => x.StoreId == Store && x.StorefrontOrderId == id).OrderBy(x => x.Id).ToListAsync(token);
                    foreach (var child in children) await DeliverySqlLock.AcquireAsync(db, "order:" + Store + ":" + child.Id, token);
                    await balances.LockBatchAsync(children.SelectMany(c => c.Lines.Select(l => new GaoApp.Application.DTOs.Inventory.InventoryPostingLockKey(Store, c.SourceWarehouseId, l.VariantId))).Distinct().ToList(), token);
                    var inventory = new DeliveryInventoryService(db, user, permissions, balances, movement);
                    foreach (var child in children)
                    {
                        var returns = await db.DeliveryReturnedCostFragments.AsNoTracking().Where(x => x.StoreId == Store && x.DeliveryOrderId == child.Id).ToListAsync(token);
                        var remaining = child.Lines.Select(l => new DeliveryInventoryReturnLine(l.Id,
                            Qty(l.StorefrontBaseQuantity!.Value - returns.Where(x => x.DeliveryOrderLineId == l.Id).Sum(x => x.BaseQuantity))))
                            .Where(x => Quantity(x.BaseQuantityText) > 0).ToList();
                        if (remaining.Count > 0) await inventory.ReceiveStorefrontReturnsInTransactionAsync(child.Id,
                            new(ChildKey(request.ClientRequestId, child.Id), Convert.ToBase64String(child.RowVersion), remaining, request.Note), token);
                        var work = works.Single(x => x.DeliveryOrderId == child.Id); work.Stage = StorefrontFulfillmentStage.Returned;
                        work.LatestEvidence = Evidence(order, request, "IncidentGoodsRecovered", new { request.IncidentId, request.CountedLines, request.EvidenceReference }, work.FulfillmentSourceId);
                    }
                    await db.SaveChangesAsync(token);
                    // Resolve the actual incident before persisting the final cancellation.
                    // There is still one owning transaction; no stock/fund effect commits here.
                    order.DeliveryState = StorefrontDeliveryState.Returned;
                    cancelAfterActualRecovery = true;
                }
                else
                {
                    Need(!await db.DeliveryReturnedCostFragments.AnyAsync(x => x.StoreId == Store && childIds.Contains(x.DeliveryOrderId), token), "Đã có hàng nhận về; không thể xác nhận toàn bộ còn giao ngoài kho.");
                    Need(works.All(x => x.Stage is StorefrontFulfillmentStage.Dispatched or StorefrontFulfillmentStage.Delivered) &&
                        works.All(x => order.DeliveryMethod == StorefrontDeliveryMethod.Local ? x.CourierUserId.HasValue : !string.IsNullOrWhiteSpace(x.CarrierName) && !string.IsNullOrWhiteSpace(x.TrackingCode)), "Thiếu hồ sơ nhận giao thực tế của từng nguồn.");
                    // The existing dispatches are the proof. Never dispatch these goods a second time.
                }
            }
            else if (request.Resolution == StorefrontIncidentResolution.AllGoodsHandedToCarrier)
            {
                Need(order.Phase == StorefrontOrderPhase.Preparing, "Trạng thái chưa cho phép đối soát bàn giao chưa ghi sổ.");
                await Guard(StorefrontPermissions.Dispatch, token);
                await DispatchAll(order, request, request.Carriers, token);
            }
            else Need(order.Phase == StorefrontOrderPhase.Preparing, "Chỉ phục hồi hàng chưa ghi xuất trong lúc chuẩn bị.");
            var resolution = new StorefrontFulfillmentEvidence { StoreId = Store, StorefrontOrderId = id, ActorUserId = Actor, ClientRequestId = request.ClientRequestId,
                Kind = request.Resolution.ToString(), OccurredAtUtc = Now, Note = request.Note.Trim(), SnapshotJson = Serialize(new { request.IncidentId, request.CountedLines, request.EvidenceReference }) };
            db.Add(resolution); await db.SaveChangesAsync(token); incident.ReconciledByEvidenceId = resolution.Id;
            await db.SaveChangesAsync(token);
            if (cancelAfterActualRecovery)
            {
                order.Phase = StorefrontOrderPhase.Cancelled;
                await db.SaveChangesAsync(token);
                await CloseCancellationAfterActualReturn(order, resolution, token);
                await StorefrontRefundObligations.EnsureAsync(db, order, clock, token);
                if (order.RefundState == StorefrontRefundState.None && await HasRetainedFunds(order.Id, token)) order.RefundState = StorefrontRefundState.Requested;
            }
        }, ct);

    private async Task CloseCancellationAfterActualReturn(StorefrontOrder order, StorefrontFulfillmentEvidence evidence, CancellationToken ct)
    {
        var facts = await StorefrontCancellationFacts.ReadAsync(db, Store, order.Id, ct);
        if (facts.PendingRequest is not { } pending) return;
        Need(order.Phase == StorefrontOrderPhase.Cancelled && facts.HasDispatch && facts.CompleteDispatchProof && facts.AllDispatchReturned &&
            !facts.HasDeliveredGoods && !facts.HasIncident && evidence.Id > 0 && evidence.StoreId == Store && evidence.StorefrontOrderId == order.Id &&
            evidence.Kind is "FailedDeliveryReturnReceived" or "AllGoodsRecoveredAtSource", "Chưa đủ chứng cứ nhận lại toàn bộ hàng thực tế để kết thúc yêu cầu hủy.");
        // The real return supersedes a pending request; it is not a fabricated
        // ApproveCancellation command or a grant of cancellation-approval authority.
        db.Add(new StorefrontCancellationDecision { StoreId = Store, StorefrontOrderId = order.Id, CancellationRequestId = pending.Id,
            Kind = StorefrontCancellationDecisionKind.SupersededByActualReturn, ActorUserId = Actor,
            Reason = "Đã nhận lại đủ hàng theo chứng từ xuất và kết thúc hủy đơn. Khoản cần hoàn tiếp tục được theo dõi riêng.",
            DecidedAtUtc = Now, AuthorisesPhysicalReturn = false, ActualReturnEvidenceId = evidence.Id });
        await db.SaveChangesAsync(ct);
    }
}
