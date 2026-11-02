using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Common;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.Delivery;
using GaoApp.Application.DTOs.Inventory;
using GaoApp.Application.Interfaces.Repositories.Inventory;
using GaoApp.Application.Interfaces.Services.Delivery;
using GaoApp.Application.Interfaces.Services.Inventory;
using GaoApp.Application.Interfaces.Services.Security;
using GaoApp.Application.Services.Inventory;
using GaoApp.Domain.Delivery;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Repositories.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;

namespace GaoApp.Infrastructure.Services.Delivery;

public sealed class DeliveryInventoryService(AppDbContext db, ICurrentUser user,
    ICurrentStorePermissionService permissions, IInventoryBalanceRepository balances,
    IInventoryMovementService movement) : IDeliveryInventoryService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private int Store => db.CurrentStoreId ?? throw Error(403, "STORE_REQUIRED", "Cần chọn cửa hàng.");
    private int Actor => user.UserId ?? throw Error(403, "ACTOR_REQUIRED", "Cần đăng nhập.");
    private static DeliveryFoundationException Error(int status, string code, string message) => new(status, code, message);
    private static string Qty(decimal value) => value.ToString("0.####", CultureInfo.InvariantCulture);
    private static string Cost(decimal value) => value.ToString("0.######", CultureInfo.InvariantCulture);
    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private void Ambient()
    {
        if (db.Database.CurrentTransaction is null || !db.Database.IsSqlServer())
            throw Error(409, "TRANSACTION_REQUIRED", "Thao tác kho cần giao dịch SQL đang mở.");
    }
    private async Task Authorize(string permission, CancellationToken ct)
    {
        if (!user.IsAuthenticated || !await db.UserInStores.AsNoTracking().AnyAsync(x =>
            x.StoreId == Store && x.UserId == Actor && x.IsActive && x.User.IsActive, ct) ||
            !await permissions.HasPermissionAsync(Store, Actor, permission, ct))
            throw Error(403, "DELIVERY_FORBIDDEN", "Không có quyền thao tác đơn giao trong cửa hàng.");
    }
    private async Task<DeliveryOrder> Load(int id, bool active, bool tracked, CancellationToken ct, bool storefront = false)
    {
        var query = db.DeliveryOrders.Include(x => x.Lines).Where(x => x.StoreId == Store && x.Id == id);
        var order = await (tracked ? query : query.AsNoTracking()).SingleOrDefaultAsync(ct)
            ?? throw Error(404, "DELIVERY_NOT_FOUND", "Không tìm thấy đơn giao trong cửa hàng.");
        if (!await db.Warehouses.AsNoTracking().AnyAsync(x => x.StoreId == Store && x.Id == order.SourceWarehouseId &&
            x.LegalEntityId == order.SourceLegalEntityId && (!active || x.IsActive), ct) ||
            !await db.LegalEntities.AsNoTracking().AnyAsync(x => x.StoreId == Store && x.Id == order.SourceLegalEntityId && (!active || x.IsActive), ct) ||
            (storefront ? order.SourceKind != DeliverySourceKind.Storefront || !await db.Set<GaoApp.Domain.Storefront.StorefrontFulfillmentSource>().AnyAsync(x => x.StoreId == Store && x.StorefrontOrderId == order.StorefrontOrderId && x.Id == order.StorefrontFulfillmentSourceId && x.DeliveryOrderId == order.Id, ct) : order.SourceKind != DeliverySourceKind.Pos || !await db.Orders.AsNoTracking().AnyAsync(x => x.StoreId == Store && x.Id == order.SourceCartId && x.POSShiftId == order.CreatedShiftId, ct)))
            throw Error(403, "SOURCE_SCOPE_INVALID", "Kho/chủ thể/giỏ nguồn không khớp hồ sơ giao.");
        return order;
    }
    private async Task Lock(int id, CancellationToken ct)
    {
        Ambient();
        await DeliverySqlLock.AcquireAsync(db, "order:" + Store + ":" + id, ct);
    }
    private async Task<List<InventoryReservation>> Holds(int id, CancellationToken ct)
        => await db.InventoryReservations.IgnoreQueryFilters().Where(x => x.StoreId == Store && x.DeliveryOrderId == id &&
            !x.IsDeleted && x.Status == InventoryReservationStatus.Active).ToListAsync(ct);
    private async Task<Dictionary<int, InventoryBalance>> LockBalances(DeliveryOrder order,
        IEnumerable<int> variants, CancellationToken ct)
    {
        var result = new Dictionary<int, InventoryBalance>();
        foreach (var variant in variants.Distinct().Order())
        {
            var balance = await balances.LockAndGetOrCreateAsync(Store, order.SourceWarehouseId, variant, ct);
            var rows = await new InventoryReservationRepository(db).GetActiveForBalanceAsync(Store, order.SourceWarehouseId, variant, ct);
            if (balance.ReservedQty < 0 || rows.Sum(x => x.ReservedQty) != balance.ReservedQty)
                throw Error(409, "RESERVATION_DRIFT", "Bộ đếm giữ hàng không khớp chứng cứ đang hoạt động.");
            result.Add(variant, balance);
        }
        return result;
    }
    private static void Release(InventoryReservation hold, InventoryBalance balance, string reason, bool consumed = false)
    {
        balance.ReservedQty = InventoryPostingPrecisionPolicy.Quantity(balance.ReservedQty - hold.ReservedQty);
        if (balance.ReservedQty < 0) throw Error(409, "RESERVATION_DRIFT", "Bộ đếm giữ hàng không khớp.");
        hold.Status = consumed ? InventoryReservationStatus.Consumed : InventoryReservationStatus.Released;
        hold.ReleasedAtUtc = DateTime.UtcNow; hold.ReleaseNote = reason;
    }
    private InventoryReservation NewHold(DeliveryOrder order, DeliveryPickingLine line, decimal quantity)
    {
        var hold = new InventoryReservation { StoreId = Store, WarehouseId = order.SourceWarehouseId,
            ProductVariantId = line.Quote.VariantId, ReferenceType = InventoryReferenceType.Delivery,
            ReferenceId = order.Id.ToString(CultureInfo.InvariantCulture), ReferenceLineId = line.DeliveryOrderLineId,
            DeliveryOrderId = order.Id, DeliveryOrderLineId = line.DeliveryOrderLineId,
            SourceLegalEntityId = order.SourceLegalEntityId, DeliveryRevision = order.Revision,
            ReportedByUserId = line.ReporterUserId, ReportedAtUtc = line.ReportedAtUtc,
            ReservedQty = quantity, Status = InventoryReservationStatus.Active, ReservedAtUtc = DateTime.UtcNow };
        db.InventoryReservations.Add(hold); InventoryPostingLockState.MarkOwned(db, hold); return hold;
    }
    public async Task ReconcilePickedReservationsInTransactionAsync(int id, CancellationToken ct = default)
    {
        try
        {
            await Lock(id, ct); await Authorize(PermissionCodes.Delivery.View, ct);
            if (!await permissions.HasPermissionAsync(Store, Actor, PermissionCodes.Delivery.Pick, ct) &&
                !await permissions.HasPermissionAsync(Store, Actor, PermissionCodes.Delivery.ApproveChanges, ct))
                throw Error(403, "DELIVERY_FORBIDDEN", "Không có quyền soạn/duyệt thay đổi.");
            var order = await Load(id, false, false, ct);
            if (await db.DeliveryJournalEntries.AnyAsync(x => x.StoreId == Store && x.DeliveryOrderId == id && x.Kind == DeliveryJournalKind.Dispatch, ct))
                throw Error(409, "STOCK_ALREADY_DISPATCHED", "Hàng đã bàn giao; không thể thay đổi kết quả soạn.");
            var lines = await db.DeliveryPickingLines.AsNoTracking().Include(x => x.Quote)
                .Where(x => x.StoreId == Store && x.DeliveryOrderId == id).ToListAsync(ct);
            var old = await Holds(id, ct);
            var desired = lines.ToDictionary(x => x.DeliveryOrderLineId, x => DeliveryInventoryPolicy.HeldBaseQuantity(x.IsActive, x.ReportedQuantity, x.Quote.BaseMultiplier));
            if (!old.Any() && desired.Values.All(x => x == 0)) return; // Claim never creates or promotes stock.
            if (desired.Values.Any(x => x > 0)) _ = await Load(id, true, false, ct);
            var locked = await LockBalances(order, old.Select(x => x.ProductVariantId).Concat(lines.Where(x => desired[x.DeliveryOrderLineId] > 0).Select(x => x.Quote.VariantId)), ct);
            foreach (var hold in old)
            {
                if (hold.WarehouseId != order.SourceWarehouseId || hold.SourceLegalEntityId != order.SourceLegalEntityId ||
                    !lines.Any(x => x.DeliveryOrderLineId == hold.DeliveryOrderLineId && x.Quote.VariantId == hold.ProductVariantId))
                    throw Error(409, "RESERVATION_DRIFT", "Chứng cứ giữ hàng không khớp nguồn/dòng.");
                var line = lines.Single(x => x.DeliveryOrderLineId == hold.DeliveryOrderLineId);
                if (hold.ReservedQty != desired[line.DeliveryOrderLineId] || hold.ReportedByUserId != line.ReporterUserId || hold.ReportedAtUtc != line.ReportedAtUtc)
                    Release(hold, locked[hold.ProductVariantId], "Kết quả soạn đã thay đổi.");
            }
            foreach (var line in lines.Where(x => desired[x.DeliveryOrderLineId] > 0))
            {
                var quantity = desired[line.DeliveryOrderLineId]; var balance = locked[line.Quote.VariantId];
                if (old.Any(x => x.Status == InventoryReservationStatus.Active && x.DeliveryOrderLineId == line.DeliveryOrderLineId)) continue;
                // Reported picked goods are a physical staff fact, not an ERP availability test.
                // Keep their holds even when recorded stock is negative, as normal POS allows.
                if (!await db.ProductVariants.AnyAsync(x => x.StoreId == Store && x.Id == line.Quote.VariantId && x.IsActive, ct))
                    throw Error(403, "SOURCE_SCOPE_INVALID", "Sản phẩm nguồn không còn hoạt động trong cửa hàng.");
                balance.QuantityPrecisionVersion = 1; balance.ReservedQty = InventoryPostingPrecisionPolicy.Quantity(balance.ReservedQty + quantity);
                _ = NewHold(order, line, quantity);
            }
            await db.SaveChangesAsync(ct);
        }
        catch (BusinessRuleException ex) { throw Translate(ex); }
        catch (ConcurrencyException ex) { throw Error(409, "INVENTORY_CONFLICT", ex.Message); }
        catch (SqlException ex) when (ex.Number is 1205 or 1222) { throw Error(409, "INVENTORY_CONFLICT", "Kho đang được cập nhật; tải lại và thử thao tác."); }
    }
    public async Task<DeliveryInventorySummaryDto> GetAsync(int id, CancellationToken ct = default)
    {
        await Authorize(PermissionCodes.Delivery.View, ct);
        var owns = db.Database.CurrentTransaction is null;
        await using var tx = owns ? await db.Database.BeginTransactionAsync(ct) : null;
        await Lock(id, ct); await Authorize(PermissionCodes.Delivery.View, ct);
        var order = await Load(id, false, false, ct);
        var holds = await db.InventoryReservations.AsNoTracking().Where(x => x.StoreId == Store && x.DeliveryOrderId == id && x.Status == InventoryReservationStatus.Active).ToListAsync(ct);
        var journals = await db.DeliveryJournalEntries.AsNoTracking().Where(x => x.StoreId == Store && x.DeliveryOrderId == id &&
            (x.Kind == DeliveryJournalKind.Dispatch || x.Kind == DeliveryJournalKind.Return)).ToListAsync(ct);
        var canCosts = await permissions.HasPermissionAsync(Store, Actor, PermissionCodes.Inventory.Valuation.View, ct);
        if (canCosts) await LockCostSources(order, ct);
        var fragments = canCosts ? await db.DeliveryDispatchCostFragments.AsNoTracking().Where(x => x.StoreId == Store && x.DeliveryOrderId == id).OrderBy(x => x.Id).ToListAsync(ct) : [];
        var returns = canCosts ? await db.DeliveryReturnedCostFragments.AsNoTracking().Where(x => x.StoreId == Store && x.DeliveryOrderId == id).ToListAsync(ct) : [];
        var qualities = new Dictionary<int, string>();
        var sourceEvidence = new InventorySourceCostEvidenceRepository(db);
        foreach (var fragment in fragments)
        {
            if (!fragment.IsProvisional) { qualities[fragment.Id] = "Final"; continue; }
            var resolutions = await sourceEvidence.GetProvisionalResolutionDebitsAsync(fragment, ct);
            var reversed = await sourceEvidence.GetAcceptedProvisionalReturnQuantityAsync(fragment.InventoryCostLayerAllocationId, ct);
            qualities[fragment.Id] = resolutions.Sum(x => x.ResolutionEndQuantity - x.ResolutionStartQuantity) + reversed == fragment.BaseQuantity
                ? "Resolved" : fragment.UnitCost == 0 ? "Unknown" : "Provisional";
        }
        var custody = await DeliveryCustodyProjection.ReadAsync(db, Store, order, ct);
        var rows = order.Lines.OrderBy(x => x.Id).Select(line =>
        {
            var held = holds.Where(x => x.DeliveryOrderLineId == line.Id).Sum(x => x.ReservedQty);
            var dispatched = journals.Where(x => x.DeliveryOrderLineId == line.Id && x.Kind == DeliveryJournalKind.Dispatch).Sum(x => x.BaseQuantity);
            var received = journals.Where(x => x.DeliveryOrderLineId == line.Id && x.Kind == DeliveryJournalKind.Return).Sum(x => x.BaseQuantity);
            return new DeliveryInventoryLineDto(line.Id, line.VariantId, line.ItemName, line.BaseUnitName, Qty(held), Qty(dispatched), Qty(received), Qty(dispatched - received),
                custody is not null ? Qty(custody.Quantities.GetValueOrDefault(line.Id)) : null,
                canCosts ? Qty(journals.Where(x => x.DeliveryOrderLineId == line.Id && x.Kind == DeliveryJournalKind.Dispatch).Sum(x => x.CostAmount)) : null,
                canCosts ? Qty(journals.Where(x => x.DeliveryOrderLineId == line.Id && x.Kind == DeliveryJournalKind.Return).Sum(x => x.CostAmount)) : null);
        }).ToArray();
        var wh = await db.Warehouses.AsNoTracking().SingleAsync(x => x.StoreId == Store && x.Id == order.SourceWarehouseId, ct);
        var le = await db.LegalEntities.AsNoTracking().SingleAsync(x => x.StoreId == Store && x.Id == order.SourceLegalEntityId, ct);
        var dispatchTotal = journals.Where(x => x.Kind == DeliveryJournalKind.Dispatch).Sum(x => x.BaseQuantity);
        var receivedTotal = journals.Where(x => x.Kind == DeliveryJournalKind.Return).Sum(x => x.BaseQuantity);
        var result = new DeliveryInventorySummaryDto(Store, id, order.Revision, Convert.ToBase64String(order.RowVersion), Utc(DateTime.UtcNow), wh.Id, wh.Name,
            le.Id, le.Name, DeliveryInventoryPolicy.CanCancelBeforeHandover(order.State, dispatchTotal != 0) && await permissions.HasPermissionAsync(Store, Actor, PermissionCodes.Delivery.Cancel, ct),
            canCosts, Qty(holds.Sum(x => x.ReservedQty)), Qty(dispatchTotal), Qty(receivedTotal), Qty(dispatchTotal - receivedTotal), null, rows,
            fragments.Select(x => new DeliveryInventoryCostFragmentDto(x.Id, x.DeliveryOrderLineId, x.DeliveryJournalEntryId, x.InventoryTransactionId,
                x.InventoryValuationEntryId, x.InventoryCostLayerId, x.InventoryCostLayerAllocationId, x.InventoryCostBudgetDebitId, Qty(x.BaseQuantity), Cost(x.UnitCost), Qty(x.CostAmount),
                Qty(returns.Where(r => r.DispatchCostFragmentId == x.Id).Sum(r => r.BaseQuantity)), Qty(returns.Where(r => r.DispatchCostFragmentId == x.Id).Sum(r => r.CostAmount)), qualities[x.Id])).ToArray(), custody?.Context);
        if (tx is not null) await tx.CommitAsync(ct); return result;
    }
    private async Task LockCostSources(DeliveryOrder order, CancellationToken ct)
    {
        // Inbound resolution does not mutate the order. Lock its balance scope in the same
        // global variant order so a projection cannot mix pre/post-resolution cost facts.
        foreach (var variant in order.Lines.Select(x => x.VariantId).Distinct().Order())
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT Id FROM InventoryBalances WITH (UPDLOCK,HOLDLOCK) WHERE StoreId={Store} AND WarehouseId={order.SourceWarehouseId} AND ProductVariantId={variant};", ct);
    }
    private void DetachAggregate(int id)
    {
        var entries = db.ChangeTracker.Entries().Where(x => x.Entity is DeliveryOrder o && o.Id == id ||
            x.Entity is DeliveryOrderLine l && l.DeliveryOrderId == id).ToArray();
        if (entries.Any(x => x.State != EntityState.Unchanged)) throw Error(409, "TRACKED_STATE_DIRTY", "Đơn có thay đổi chưa lưu.");
        foreach (var entry in entries) entry.State = EntityState.Detached;
    }
    private static string Version(string text)
    {
        try { var bytes = Convert.FromBase64String(text); if (bytes.Length != 8 || Convert.ToBase64String(bytes) != text) throw new FormatException(); return text; }
        catch (Exception ex) when (ex is FormatException or ArgumentNullException) { throw Error(400, "VERSION_INVALID", "Phiên bản không hợp lệ."); }
    }
    private static void Key(Guid key) { if (key == Guid.Empty) throw Error(400, "REQUEST_KEY_REQUIRED", "Cần mã yêu cầu khác rỗng."); }
    private static string Reason(string? reason, int maximum = 1000)
    { reason = reason?.Trim(); if (string.IsNullOrEmpty(reason) || reason.Length > maximum) throw Error(400, "REASON_REQUIRED", $"Cần lý do hợp lệ, tối đa {maximum} ký tự."); return reason; }
    private async Task<T?> Replay<T>(int id, Guid key, string operation, string hash, CancellationToken ct) where T : class
    {
        var receipt = await db.DeliveryCommandReceipts.AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == Store && x.ClientRequestId == key, ct);
        if (receipt is null) return null;
        if (receipt.ActorUserId != Actor || receipt.DeliveryOrderId != id || receipt.Operation != "inventory-" + operation || receipt.RequestHash != hash)
            throw Error(409, "REQUEST_KEY_CONFLICT", "Mã yêu cầu đã dùng cho nội dung/người/thao tác khác.");
        return JsonSerializer.Deserialize<T>(receipt.OutcomeJson, Json) ?? throw Error(409, "RECEIPT_INVALID", "Chứng cứ kết quả không hợp lệ.");
    }
    private async Task Record<T>(DeliveryOrder order, Guid key, string operation, string hash, T result, CancellationToken ct, string? inventoryReason = null)
    {
        var detail = JsonSerializer.SerializeToNode(DeliveryFoundationService.Detail(order), Json)!.AsObject();
        if (inventoryReason is not null) detail["inventoryReason"] = inventoryReason;
        var snapshot = detail.ToJsonString(Json); var eventId = Guid.NewGuid();
        db.DeliveryRevisions.Add(new() { StoreId = Store, DeliveryOrderId = order.Id, Revision = order.Revision, ActorUserId = Actor,
            Action = "inventory-" + operation, AggregateVersion = Convert.ToBase64String(order.RowVersion), SnapshotJson = snapshot, SnapshotHash = Hash(snapshot) });
        db.DeliveryCommandReceipts.Add(new() { StoreId = Store, DeliveryOrderId = order.Id, ActorUserId = Actor, ClientRequestId = key,
            Operation = "inventory-" + operation, RequestHash = hash, OutcomeJson = JsonSerializer.Serialize(result, Json) });
        db.DeliveryOutboxMessages.Add(new() { StoreId = Store, DeliveryOrderId = order.Id, Revision = order.Revision, EventId = eventId,
            Action = "inventory-" + operation, PayloadJson = JsonSerializer.Serialize(new { eventId, storeId = Store, deliveryId = order.Id,
                order.Code, order.Revision, action = "inventory-" + operation, actorUserId = Actor, recordedAtUtc = Utc(DateTime.UtcNow) }, Json) });
        await db.SaveChangesAsync(ct);
    }
    public async Task<DeliveryInventoryCommandAck> CancelBeforeHandoverAsync(int id, DeliveryInventoryCancelRequest request, CancellationToken ct = default)
    {
        await Authorize(PermissionCodes.Delivery.View, ct); await Authorize(PermissionCodes.Delivery.Cancel, ct);
        Key(request.ClientRequestId); var version = Version(request.ExpectedVersion); var reason = Reason(request.Reason, 500);
        if (db.Database.CurrentTransaction is not null) throw Error(409, "TRANSACTION_CONFLICT", "Hủy đơn cần giao dịch riêng.");
        DetachAggregate(id);
        var hash = Hash(JsonSerializer.Serialize(new { schema = "delivery-inventory-v1", store = Store, actor = Actor, id, operation = "cancel", request }, Json));
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            await DeliverySqlLock.AcquireAsync(db, "command:" + Store + ":" + request.ClientRequestId, ct); await Lock(id, ct);
            await Authorize(PermissionCodes.Delivery.View, ct); await Authorize(PermissionCodes.Delivery.Cancel, ct);
            var current = await Load(id, false, false, ct);
            var replay = await Replay<DeliveryInventoryCommandAck>(id, request.ClientRequestId, "cancel", hash, ct);
            if (replay is not null) { await tx.CommitAsync(ct); return replay with { Replayed = true }; }
            if (Convert.ToBase64String(current.RowVersion) != version) throw Error(409, "VERSION_CONFLICT", "Đơn đã thay đổi; tải lại trước khi thao tác.");
            if (!DeliveryInventoryPolicy.CanCancelBeforeHandover(current.State, await db.DeliveryJournalEntries.AnyAsync(x => x.StoreId == Store && x.DeliveryOrderId == id && x.Kind == DeliveryJournalKind.Dispatch, ct)))
                throw Error(409, "CANCEL_NOT_ALLOWED", "Chỉ hủy đơn chưa bàn giao hàng.");
            var order = await Load(id, false, true, ct); var holds = await Holds(id, ct);
            var locked = await LockBalances(order, holds.Select(x => x.ProductVariantId), ct);
            foreach (var hold in holds) Release(hold, locked[hold.ProductVariantId], reason);
            order.State = DeliveryState.Cancelled; order.Revision++; await db.SaveChangesAsync(ct);
            await DeliveryVoucherReservationService.ReleaseAsync(db, order, ct);
            var result = new DeliveryInventoryCommandAck(request.ClientRequestId, id, "cancel", order.Revision, Convert.ToBase64String(order.RowVersion), Utc(DateTime.UtcNow), false);
            await Record(order, request.ClientRequestId, "cancel", hash, result, ct, reason); await tx.CommitAsync(ct); return result;
        }
        catch (BusinessRuleException ex) { await tx.RollbackAsync(CancellationToken.None); db.ChangeTracker.Clear(); throw Translate(ex); }
        catch (ConcurrencyException ex) { await tx.RollbackAsync(CancellationToken.None); db.ChangeTracker.Clear(); throw Error(409, "INVENTORY_CONFLICT", ex.Message); }
        catch (SqlException ex) when (ex.Number is 1205 or 1222) { await tx.RollbackAsync(CancellationToken.None); db.ChangeTracker.Clear(); throw Error(409, "INVENTORY_CONFLICT", "Kho đang được cập nhật; tải lại và thử thao tác."); }
        catch (DbUpdateConcurrencyException) { await tx.RollbackAsync(CancellationToken.None); db.ChangeTracker.Clear(); throw Error(409, "VERSION_CONFLICT", "Đơn đã thay đổi."); }
        catch { await tx.RollbackAsync(CancellationToken.None); db.ChangeTracker.Clear(); throw; }
    }
    private async Task<(DeliveryOrder Order, string Hash, DeliveryInventoryPostingAck? Replay)> PreparePosting<T>(int id, Guid key, string version,
        string operation, string permission, T request, bool active, CancellationToken ct, bool storefront = false)
    {
        Ambient(); await Authorize(storefront ? GaoApp.Application.Storefront.StorefrontPermissions.ViewOrders : PermissionCodes.Delivery.View, ct); await Authorize(permission, ct); Key(key); Version(version);
        await DeliverySqlLock.AcquireAsync(db, "command:" + Store + ":" + key, ct); await Lock(id, ct);
        await Authorize(storefront ? GaoApp.Application.Storefront.StorefrontPermissions.ViewOrders : PermissionCodes.Delivery.View, ct); await Authorize(permission, ct);
        var current = await Load(id, false, false, ct, storefront);
        var hash = Hash(JsonSerializer.Serialize(new { schema = "delivery-inventory-v1", store = Store, actor = Actor, id, operation, request }, Json));
        var replay = await Replay<DeliveryInventoryPostingAck>(id, key, operation, hash, ct);
        if (replay is not null) return (current, hash, replay with { Replayed = true });
        if (Convert.ToBase64String(current.RowVersion) != version) throw Error(409, "VERSION_CONFLICT", "Đơn đã thay đổi; tải lại trước khi thao tác.");
        DetachAggregate(id);
        return (await Load(id, active, true, ct, storefront), hash, null);
    }
    public Task<DeliveryInventoryPostingAck> DispatchInTransactionAsync(int id, DeliveryInventoryDispatchRequest request, CancellationToken ct = default) => DispatchCore(id, request, false, ct);
    internal Task<DeliveryInventoryPostingAck> DispatchStorefrontInTransactionAsync(int id, DeliveryInventoryDispatchRequest request, CancellationToken ct) => DispatchCore(id, request, true, ct);
    private async Task<DeliveryInventoryPostingAck> DispatchCore(int id, DeliveryInventoryDispatchRequest request, bool storefront, CancellationToken ct)
    {
        try
        {
            var (order, hash, replay) = await PreparePosting(id, request.ClientRequestId, request.ExpectedVersion, "dispatch", storefront ? GaoApp.Application.Storefront.StorefrontPermissions.Dispatch : PermissionCodes.Delivery.Handover, request, true, ct, storefront);
            if (replay is not null) return replay;
            if (order.State != DeliveryState.ReadyForHandover || await db.DeliveryJournalEntries.AnyAsync(x => x.StoreId == Store && x.DeliveryOrderId == id && x.Kind == DeliveryJournalKind.Dispatch, ct))
                throw Error(409, "DISPATCH_NOT_ALLOWED", "Đơn chưa sẵn sàng hoặc hàng đã bàn giao.");
            var work = await db.DeliveryPickingWorks.AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == Store && x.DeliveryOrderId == id, ct);
            var lines = await db.DeliveryPickingLines.AsNoTracking().Include(x => x.Quote).Where(x => x.StoreId == Store && x.DeliveryOrderId == id && x.IsActive).OrderBy(x => x.DeliveryOrderLineId).ToListAsync(ct);
            if (work?.ApprovedRevision != request.ApprovedRevision || work.ApprovedTotal is null || !lines.Any(x => x.ApprovedQuantity is > 0))
                throw Error(409, "APPROVAL_REQUIRED", "Thiếu generation/số lượng đã duyệt.");
            var holds = await Holds(id, ct); var locked = await LockBalances(order, holds.Select(x => x.ProductVariantId).Concat(lines.Select(x => x.Quote.VariantId)), ct);
            foreach (var line in lines)
            {
                var approved = storefront ? line.ApprovedQuantity ?? 0 : DeliveryPickingPolicy.ToBase(line.ApprovedQuantity ?? 0, line.Quote.BaseMultiplier);
                var own = holds.SingleOrDefault(x => x.DeliveryOrderLineId == line.DeliveryOrderLineId);
                if (approved == 0) { if (own is not null) Release(own, locked[own.ProductVariantId], "Không bàn giao dòng đã loại."); continue; }
                if (own is null || own.ProductVariantId != line.Quote.VariantId || own.WarehouseId != order.SourceWarehouseId || own.SourceLegalEntityId != order.SourceLegalEntityId ||
                    own.ReservedQty < approved || own.ReportedByUserId != line.ReporterUserId || own.ReportedAtUtc != line.ReportedAtUtc)
                    throw Error(409, "RESERVATION_DRIFT", "Giữ hàng không khớp kết quả soạn đã duyệt.");
                if (own.ReservedQty != approved)
                {
                    Release(own, locked[own.ProductVariantId], "Bàn giao theo lượng đã duyệt, nhả phần soạn dư.");
                    own = NewHold(order, line, approved); locked[own.ProductVariantId].ReservedQty += approved;
                }
            }
            await db.SaveChangesAsync(ct);
            var journalIds = new List<int>();
            foreach (var line in lines.Where(x => x.ApprovedQuantity is > 0))
            {
                var posted = await movement.CreateDeliveryDispatchAsync(new(id, line.DeliveryOrderLineId, request.ApprovedRevision, request.ClientRequestId), ct);
                var debits = await db.InventoryCostBudgetDebits.AsNoTracking().Where(x => x.StoreId == Store && x.InventoryTransactionId == posted.InventoryTransactionId && !x.IsProvisionalResolution).OrderBy(x => x.Id).ToListAsync(ct);
                var provisional = await db.InventoryCostLayerAllocations.AsNoTracking().Include(x => x.InventoryValuationEntry)
                    .Where(x => x.StoreId == Store && x.InventoryValuationEntry.InventoryTransactionId == posted.InventoryTransactionId &&
                        x.InventoryValuationEntry.EntryType == InventoryValuationEntryType.Outbound && x.IsProvisional).OrderBy(x => x.Id).ToListAsync(ct);
                if (provisional.Any(x => x.InventoryCostLayerId.HasValue || !x.InventoryValuationEntry.IsProvisional ||
                    x.Quantity != -x.InventoryValuationEntry.Quantity || x.Amount != -x.InventoryValuationEntry.Amount ||
                    x.UnitCost != x.InventoryValuationEntry.UnitCost) ||
                    debits.Sum(x => x.EndQuantity - x.StartQuantity) + provisional.Sum(x => x.Quantity) != -posted.QuantityChange ||
                    debits.Sum(x => x.Amount) + provisional.Sum(x => x.Amount) != -posted.ValueChange)
                    throw Error(409, "COST_NOT_READY", "Chứng cứ giá vốn xuất không đầy đủ.");
                var journal = new DeliveryJournalEntry { StoreId = Store, DeliveryOrderId = id, DeliveryOrderLineId = line.DeliveryOrderLineId,
                    SourceWarehouseId = order.SourceWarehouseId, SourceLegalEntityId = order.SourceLegalEntityId, VariantId = line.Quote.VariantId,
                    InventoryTransactionId = posted.InventoryTransactionId, Kind = DeliveryJournalKind.Dispatch,
                    PostingKey = "dispatch:" + request.ClientRequestId.ToString("N") + ":" + line.DeliveryOrderLineId, BaseQuantity = -posted.QuantityChange, CostAmount = -posted.ValueChange };
                db.DeliveryJournalEntries.Add(journal); await db.SaveChangesAsync(ct); journalIds.Add(journal.Id);
                foreach (var debit in debits) db.DeliveryDispatchCostFragments.Add(new() { StoreId = Store, DeliveryOrderId = id,
                    DeliveryOrderLineId = line.DeliveryOrderLineId, SourceWarehouseId = order.SourceWarehouseId, SourceLegalEntityId = order.SourceLegalEntityId,
                    VariantId = line.Quote.VariantId, DeliveryJournalEntryId = journal.Id, InventoryTransactionId = debit.InventoryTransactionId,
                    InventoryValuationEntryId = debit.InventoryValuationEntryId, InventoryCostLayerId = debit.InventoryCostLayerId,
                    InventoryCostLayerAllocationId = debit.InventoryCostLayerAllocationId, InventoryCostBudgetDebitId = debit.Id,
                    BaseQuantity = debit.EndQuantity - debit.StartQuantity, UnitCost = debit.UnitCost, CostAmount = debit.Amount });
                foreach (var allocation in provisional) db.DeliveryDispatchCostFragments.Add(new() { StoreId = Store, DeliveryOrderId = id,
                    DeliveryOrderLineId = line.DeliveryOrderLineId, SourceWarehouseId = order.SourceWarehouseId, SourceLegalEntityId = order.SourceLegalEntityId,
                    VariantId = line.Quote.VariantId, DeliveryJournalEntryId = journal.Id,
                    InventoryTransactionId = posted.InventoryTransactionId ?? throw Error(409, "COST_NOT_READY", "Thiếu bút toán xuất."),
                    InventoryValuationEntryId = allocation.InventoryValuationEntryId, InventoryCostLayerAllocationId = allocation.Id,
                    IsProvisional = true, BaseQuantity = allocation.Quantity, UnitCost = allocation.UnitCost, CostAmount = allocation.Amount });
                var hold = (await Holds(id, ct)).Single(x => x.DeliveryOrderLineId == line.DeliveryOrderLineId);
                Release(hold, locked[hold.ProductVariantId], "Đã xuất bàn giao.", consumed: true);
            }
            order.Revision++; await db.SaveChangesAsync(ct);
            var result = new DeliveryInventoryPostingAck(request.ClientRequestId, id, "dispatch", journalIds, Utc(DateTime.UtcNow), false);
            await Record(order, request.ClientRequestId, "dispatch", hash, result, ct); return result;
        }
        catch (BusinessRuleException ex) { throw Translate(ex); }
        catch (ConcurrencyException ex) { throw Error(409, "INVENTORY_CONFLICT", ex.Message); }
        catch (SqlException ex) when (ex.Number is 1205 or 1222) { throw Error(409, "INVENTORY_CONFLICT", "Kho đang được cập nhật; tải lại và thử thao tác."); }
    }
    public Task<DeliveryInventoryPostingAck> ReceiveReturnsInTransactionAsync(int id, DeliveryInventoryReceiveRequest request, CancellationToken ct = default) => ReceiveCore(id, request, false, ct);
    internal Task<DeliveryInventoryPostingAck> ReceiveStorefrontReturnsInTransactionAsync(int id, DeliveryInventoryReceiveRequest request, CancellationToken ct) => ReceiveCore(id, request, true, ct);
    private async Task<DeliveryInventoryPostingAck> ReceiveCore(int id, DeliveryInventoryReceiveRequest request, bool storefront, CancellationToken ct)
    {
        try
        {
            _ = Reason(request.Reason);
            if (request.Lines is null || request.Lines.Count == 0 || request.Lines.Select(x => x.LineId).Distinct().Count() != request.Lines.Count)
                throw Error(400, "LINE_INVALID", "Cần các dòng nhận trả không trùng.");
            var requested = request.Lines.ToDictionary(x => x.LineId, x => DeliveryPickingPolicy.ParseQuantity(x.BaseQuantityText));
            if (requested.Any(x => x.Key <= 0 || x.Value <= 0)) throw Error(400, "LINE_INVALID", "Lượng nhận trả phải lớn hơn 0.");
            var (order, hash, replay) = await PreparePosting(id, request.ClientRequestId, request.ExpectedVersion, "receive", storefront ? GaoApp.Application.Storefront.StorefrontPermissions.ReceiveReturns : PermissionCodes.Delivery.ReceiveReturns, request, false, ct, storefront);
            if (replay is not null) return replay;
            var fragments = await db.DeliveryDispatchCostFragments.AsNoTracking().Where(x => x.StoreId == Store && x.DeliveryOrderId == id).OrderByDescending(x => x.Id).ToListAsync(ct);
            if (requested.Keys.Any(line => !fragments.Any(x => x.DeliveryOrderLineId == line))) throw Error(400, "LINE_INVALID", "Dòng chưa có xuất nguồn.");
            _ = await LockBalances(order, fragments.Where(x => requested.ContainsKey(x.DeliveryOrderLineId)).Select(x => x.VariantId), ct);
            var journalIds = new List<int>();
            foreach (var item in requested.OrderBy(x => x.Key))
            {
                var remaining = item.Value;
                foreach (var fragment in fragments.Where(x => x.DeliveryOrderLineId == item.Key))
                {
                    var already = await db.DeliveryReturnedCostFragments.AsNoTracking().Where(x => x.StoreId == Store && x.DispatchCostFragmentId == fragment.Id).SumAsync(x => x.BaseQuantity, ct);
                    var quantity = Math.Min(remaining, fragment.BaseQuantity - already); if (quantity <= 0) continue;
                    var start = fragment.BaseQuantity - already - quantity;
                    var proof = await new InventorySourceCostEvidenceRepository(db).GetDeliveryReturnProofAsync(new(id, fragment.Id, start, quantity, request.ClientRequestId), ct);
                    // Extend the physical tail backwards. Each resolution boundary has its own
                    // authoritative inbound transaction/journal, avoiding blended or guessed cost.
                    foreach (var part in proof.Parts.OrderByDescending(x => x.SourceStartQuantity ?? start))
                    {
                        var pieceStart = part.SourceStartQuantity ?? start;
                        var posted = await movement.CreateDeliveryReturnAsync(new(id, fragment.Id, pieceStart, part.Quantity, request.ClientRequestId), ct);
                        var entries = await db.InventoryValuationEntries.AsNoTracking().Where(x => x.StoreId == Store && x.InventoryTransactionId == posted.InventoryTransactionId && x.EntryType == InventoryValuationEntryType.Inbound).ToListAsync(ct);
                        if (entries.Count != 1 || entries[0].Quantity != part.Quantity || entries[0].Amount != posted.ValueChange ||
                            entries[0].UnitCost != part.UnitCost || entries[0].IsProvisional != part.IsProvisional ||
                            entries[0].SourceValuationEntryId != fragment.InventoryValuationEntryId)
                            throw Error(409, "COST_NOT_READY", "Chứng cứ nhập trả không khớp xuất nguồn.");
                        var journal = new DeliveryJournalEntry { StoreId = Store, DeliveryOrderId = id, DeliveryOrderLineId = item.Key,
                            SourceWarehouseId = order.SourceWarehouseId, SourceLegalEntityId = order.SourceLegalEntityId, VariantId = fragment.VariantId,
                            InventoryTransactionId = posted.InventoryTransactionId, Kind = DeliveryJournalKind.Return,
                            PostingKey = "return:" + request.ClientRequestId.ToString("N") + ":" + fragment.Id +
                                (part.SourceStartQuantity.HasValue ? ":S" + pieceStart.ToString(CultureInfo.InvariantCulture) : ""),
                            BaseQuantity = part.Quantity, UnitCost = part.UnitCost, CostAmount = posted.ValueChange };
                        db.DeliveryJournalEntries.Add(journal); await db.SaveChangesAsync(ct); journalIds.Add(journal.Id);
                        db.DeliveryReturnedCostFragments.Add(new() { StoreId = Store, DeliveryOrderId = id, DeliveryOrderLineId = item.Key,
                            SourceWarehouseId = order.SourceWarehouseId, SourceLegalEntityId = order.SourceLegalEntityId, VariantId = fragment.VariantId,
                            DeliveryJournalEntryId = journal.Id, DispatchCostFragmentId = fragment.Id, InventoryTransactionId = posted.InventoryTransactionId ?? throw Error(409, "COST_NOT_READY", "Thiếu bút toán nhập trả."),
                            InventoryValuationEntryId = entries[0].Id, StartQuantity = pieceStart, EndQuantity = pieceStart + part.Quantity,
                            BaseQuantity = part.Quantity, UnitCost = part.UnitCost, CostAmount = posted.ValueChange, IsProvisional = part.IsProvisional });
                        await db.SaveChangesAsync(ct);
                    }
                    remaining -= quantity; if (remaining == 0) break;
                }
                if (remaining != 0) throw Error(409, "RETURN_BUDGET_EXCEEDED", "Nhận trả vượt lượng chưa nhận về của đơn.");
            }
            order.Revision++; await db.SaveChangesAsync(ct);
            var result = new DeliveryInventoryPostingAck(request.ClientRequestId, id, "receive", journalIds, Utc(DateTime.UtcNow), false);
            await Record(order, request.ClientRequestId, "receive", hash, result, ct); return result;
        }
        catch (BusinessRuleException ex) { throw Translate(ex); }
        catch (DeliveryRuleException ex) { throw Error(400, ex.Code, ex.Message); }
        catch (ConcurrencyException ex) { throw Error(409, "INVENTORY_CONFLICT", ex.Message); }
        catch (SqlException ex) when (ex.Number is 1205 or 1222) { throw Error(409, "INVENTORY_CONFLICT", "Kho đang được cập nhật; tải lại và thử thao tác."); }
    }
    public async Task<IReadOnlyList<DeliveryInventoryResidualCostDto>> GetResidualCostsInTransactionAsync(int id,
        IReadOnlyList<DeliveryInventoryCostSelection> selections, CancellationToken ct = default)
    {
        await Lock(id, ct); await Authorize(PermissionCodes.Delivery.Finalize, ct); var order = await Load(id, false, false, ct);
        if (selections is null || selections.Count == 0 || selections.Select(x => x.LineId).Distinct().Count() != selections.Count)
            throw Error(400, "LINE_INVALID", "Cần lựa chọn dòng không trùng.");
        await LockCostSources(order, ct);
        var fragments = await db.DeliveryDispatchCostFragments.AsNoTracking().Where(x => x.StoreId == Store && x.DeliveryOrderId == id).OrderBy(x => x.Id).ToListAsync(ct);
        var returned = await db.DeliveryReturnedCostFragments.AsNoTracking().Where(x => x.StoreId == Store && x.DeliveryOrderId == id).ToListAsync(ct);
        var debitIds = fragments.Select(x => x.InventoryCostBudgetDebitId).ToArray();
        var debits = await db.InventoryCostBudgetDebits.AsNoTracking().Where(x => x.StoreId == Store && debitIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        var result = new List<DeliveryInventoryResidualCostDto>();
        foreach (var selection in selections)
        {
            var line = fragments.Where(x => x.DeliveryOrderLineId == selection.LineId).ToArray();
            if (line.Length == 0) throw Error(400, "LINE_INVALID", "Dòng chưa có xuất nguồn.");
            var delivered = DeliveryPickingPolicy.ParseQuantity(selection.DeliveredBaseQuantityText);
            var received = returned.Where(x => x.DeliveryOrderLineId == selection.LineId).Sum(x => x.BaseQuantity);
            var receivedCost = returned.Where(x => x.DeliveryOrderLineId == selection.LineId).Sum(x => x.CostAmount);
            var dispatched = line.Sum(x => x.BaseQuantity);
            if (delivered + received > dispatched) throw Error(409, "QUANTITY_BUDGET_EXCEEDED", "Lượng giao/nhận trả vượt xuất nguồn.");
            decimal cost = 0, outsideCost = 0, remaining = delivered;
            foreach (var fragment in line)
            {
                var available = fragment.BaseQuantity - returned.Where(x => x.DispatchCostFragmentId == fragment.Id).Sum(x => x.BaseQuantity);
                var quantity = Math.Min(remaining, available);
                IReadOnlyList<InventoryCostBudgetDebit> resolutions = fragment.IsProvisional
                    ? await new InventorySourceCostEvidenceRepository(db).GetProvisionalResolutionDebitsAsync(fragment, ct) : [];
                decimal Prefix(decimal count)
                {
                    if (count == 0) return 0;
                    if (!fragment.IsProvisional)
                    {
                        if (fragment.InventoryCostBudgetDebitId is not int debitId || !debits.TryGetValue(debitId, out var debit) || debit.IsProvisionalResolution ||
                            debit.InventoryValuationEntryId != fragment.InventoryValuationEntryId || debit.InventoryCostLayerId != fragment.InventoryCostLayerId ||
                            debit.Amount != fragment.CostAmount || debit.EndQuantity - debit.StartQuantity != fragment.BaseQuantity)
                            throw Error(409, "COST_NOT_READY", "Chứng cứ giá vốn xuất nguồn không đầy đủ.");
                        return InventoryCostBudgetPolicy.Slice(debit.AnchorQuantity, debit.AnchorAmount, debit.StartQuantity, count);
                    }
                    decimal result = 0, cursor = 0;
                    foreach (var resolved in resolutions)
                    {
                        var resolutionStart = resolved.ResolutionStartQuantity ?? throw Error(409, "COST_NOT_READY", "Thiếu đầu khoảng xác định giá vốn.");
                        var resolutionEnd = resolved.ResolutionEndQuantity ?? throw Error(409, "COST_NOT_READY", "Thiếu cuối khoảng xác định giá vốn.");
                        var take = Math.Min(count - cursor, resolutionEnd - resolutionStart);
                        if (take <= 0) break;
                        result += InventoryCostBudgetPolicy.Slice(resolved.AnchorQuantity, resolved.AnchorAmount, resolved.StartQuantity, take);
                        cursor += take;
                    }
                    if (cursor != count) throw Error(409, "COST_NOT_READY", "Giá vốn phần còn ngoài kho chưa có đủ nguồn nhập xác định; chưa thể chốt tài chính.");
                    return result;
                }
                cost += Prefix(quantity); outsideCost += Prefix(available); remaining -= quantity;
            }
            result.Add(new(selection.LineId, Qty(dispatched), Qty(delivered), Qty(cost), Qty(received), Qty(receivedCost),
                Qty(dispatched - delivered - received), Qty(outsideCost - cost)));
        }
        return result;
    }
    private static DeliveryFoundationException Translate(BusinessRuleException ex)
    {
        var code = ex.Message.Split(':', 2)[0];
        if (code.Length > 64 || code.Any(x => !(char.IsAsciiLetterUpper(x) || x == '_'))) code = "INVENTORY_CONFLICT";
        return Error(409, code, ex.Message);
    }
}
