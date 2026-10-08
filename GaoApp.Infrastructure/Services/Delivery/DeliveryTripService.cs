using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GaoApp.Application.Common;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.Delivery;
using GaoApp.Application.Interfaces.Repositories.Inventory;
using GaoApp.Application.Interfaces.Services.Delivery;
using GaoApp.Application.Interfaces.Services.Security;
using GaoApp.Domain.Delivery;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Services.Delivery;

public sealed class DeliveryTripService(AppDbContext db, ICurrentUser user,
    ICurrentStorePermissionService permissions, IDeliveryInventoryService inventory,
    IInventoryBalanceRepository balances) : IDeliveryTripService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private int Store => db.CurrentStoreId ?? throw Error(403, "STORE_REQUIRED", "Cần chọn cửa hàng.");
    private int Actor => user.UserId ?? throw Error(403, "ACTOR_REQUIRED", "Cần đăng nhập.");
    private static DeliveryFoundationException Error(int status, string code, string message) => new(status, code, message);
    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
    private static string Version(byte[] value) => Convert.ToBase64String(value);
    private static string Qty(decimal value) => value.ToString("0.####", CultureInfo.InvariantCulture);
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static void Key(Guid value) { if (value == Guid.Empty) throw Error(400, "REQUEST_KEY_REQUIRED", "Cần mã yêu cầu khác rỗng."); }
    private static void Match(string expected, byte[] actual)
    { if (Version(actual) != expected) throw Error(409, "VERSION_CONFLICT", "Hồ sơ đã đổi; tải lại trước khi thao tác."); }
    private static void RequireState(DeliveryTrip trip, DeliveryTripState state)
    { if (trip.State != state) throw Error(409, "TRIP_STATE_INVALID", "Trạng thái chuyến không cho phép thao tác."); }
    private static void Selections(IReadOnlyList<DeliveryTripOrderSelection>? selection)
    {
        try
        {
            DeliveryTripPolicy.Selection(selection?.Select(x => x?.DeliveryOrderId ?? 0).ToArray());
            foreach (var item in selection!) _ = DeliveryTripPolicy.Version(item.ExpectedVersion);
        }
        catch (DeliveryRuleException ex) { throw Error(400, ex.Code, ex.Message); }
    }
    private async Task<DateTime> SqlUtcAtOrAfter(DateTime minimum, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null)
            throw Error(409, "TRANSACTION_CONFLICT", "Giờ bàn giao cần cùng giao dịch đang ghi chứng cứ.");
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(ct);
        bounded.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            while (true)
            {
                var now = DateTime.SpecifyKind(await db.Database.SqlQueryRaw<DateTime>(
                    "SELECT SYSUTCDATETIME() AS [Value]").SingleAsync(bounded.Token), DateTimeKind.Utc);
                if (now >= minimum) return now;
                await Task.Delay(20, bounded.Token);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { throw Error(409, "SERVER_CLOCK_INVALID", "Giờ SQL chưa theo kịp chứng cứ nguồn; chưa ghi bàn giao hoặc xuất phát."); }
    }
    private static readonly (DeliveryCapability Capability, string Code)[] PermissionMap =
    [
        (DeliveryCapability.View, PermissionCodes.Delivery.View),
        (DeliveryCapability.Handover, PermissionCodes.Delivery.Handover),
        (DeliveryCapability.RecordDeparture, PermissionCodes.Delivery.RecordDeparture),
        (DeliveryCapability.ReassignCourier, PermissionCodes.Delivery.ReassignCourier),
        (DeliveryCapability.Cancel, PermissionCodes.Delivery.Cancel)
    ];
    private async Task Authorize(DeliveryCapability required, CancellationToken ct)
    {
        const DeliveryCapability known = DeliveryCapability.View | DeliveryCapability.Handover |
            DeliveryCapability.RecordDeparture | DeliveryCapability.ReassignCourier | DeliveryCapability.Cancel;
        if ((required & ~known) != 0 || (required & DeliveryCapability.View) == 0)
            throw Error(403, "DELIVERY_FORBIDDEN", "Phạm vi quyền chứng cứ không hợp lệ.");
        if (!user.IsAuthenticated || !await db.UserInStores.AsNoTracking().AnyAsync(x => x.StoreId == Store && x.UserId == Actor &&
            x.IsActive && x.User.IsActive, ct)) throw Error(403, "DELIVERY_FORBIDDEN", "Nhân viên không hoạt động trong cửa hàng.");
        foreach (var (capability, code) in PermissionMap)
            if ((required & capability) != 0 && !await permissions.HasPermissionAsync(Store, Actor, code, ct))
                throw Error(403, "DELIVERY_FORBIDDEN", "Không có quyền thao tác chuyến giao.");
    }
    private async Task<bool> Has(DeliveryCapability capability, CancellationToken ct)
        => await permissions.HasPermissionAsync(Store, Actor, PermissionMap.Single(x => x.Capability == capability).Code, ct);
    private async Task Courier(int id, CancellationToken ct)
    {
        if (id <= 0 || !await db.UserInStores.AsNoTracking().AnyAsync(x => x.StoreId == Store && x.UserId == id && x.IsActive && x.User.IsActive, ct))
            throw Error(409, "COURIER_NOT_ELIGIBLE", "Người giao cần đang hoạt động trong cửa hàng.");
    }
    private async Task<DeliveryOrder> Order(int id, bool active, bool tracked, CancellationToken ct)
    {
        var query = db.DeliveryOrders.Include(x => x.Lines).Where(x => x.StoreId == Store && x.Id == id);
        var order = await (tracked ? query : query.AsNoTracking()).SingleOrDefaultAsync(ct)
            ?? throw Error(404, "DELIVERY_NOT_FOUND", "Không tìm thấy đơn trong cửa hàng.");
        if (!await db.Warehouses.AsNoTracking().AnyAsync(x => x.StoreId == Store && x.Id == order.SourceWarehouseId &&
            x.LegalEntityId == order.SourceLegalEntityId && (!active || x.IsActive), ct) ||
            !await db.LegalEntities.AsNoTracking().AnyAsync(x => x.StoreId == Store && x.Id == order.SourceLegalEntityId && (!active || x.IsActive), ct) ||
            !await db.Orders.AsNoTracking().AnyAsync(x => x.StoreId == Store && x.Id == order.SourceCartId && x.POSShiftId == order.CreatedShiftId, ct))
            throw Error(403, "SOURCE_SCOPE_INVALID", "Kho/chủ thể/giỏ nguồn không khớp hồ sơ.");
        return order;
    }
    private void Fresh(IEnumerable<int> orderIds, int? tripId)
    {
        var ids = orderIds.ToHashSet();
        var entries = db.ChangeTracker.Entries().Where(x => x.Entity is DeliveryOrder o && ids.Contains(o.Id) ||
            x.Entity is DeliveryOrderLine l && ids.Contains(l.DeliveryOrderId) ||
            x.Entity is DeliveryTrip t && t.Id == tripId || x.Entity is DeliveryTripOrder b && b.TripId == tripId).ToArray();
        if (entries.Any(x => x.State != EntityState.Unchanged)) throw Error(409, "TRACKED_STATE_DIRTY", "Hồ sơ có thay đổi chưa lưu.");
        foreach (var entry in entries) entry.State = EntityState.Detached;
    }
    private async Task<List<DeliveryTripOrder>> Members(int id, bool tracked, CancellationToken ct)
    {
        var query = db.DeliveryTripOrders.Where(x => x.StoreId == Store && x.TripId == id && x.IsActive).OrderBy(x => x.Position);
        return await (tracked ? query : query.AsNoTracking()).ToListAsync(ct);
    }
    private async Task<List<DeliveryTripOrder>> AllBindings(int id, CancellationToken ct)
        => await db.DeliveryTripOrders.AsNoTracking().Where(x => x.StoreId == Store && x.TripId == id).OrderBy(x => x.Id).ToListAsync(ct);
    private async Task<DeliveryTrip> Trip(int id, bool tracked, CancellationToken ct)
    {
        var query = db.DeliveryTrips.Where(x => x.StoreId == Store && x.Id == id);
        return await (tracked ? query : query.AsNoTracking()).SingleOrDefaultAsync(ct)
            ?? throw Error(404, "TRIP_NOT_FOUND", "Không tìm thấy chuyến trong cửa hàng.");
    }
    private async Task<DeliveryPickingWork> Approved(DeliveryOrder order, CancellationToken ct)
    {
        var work = await db.DeliveryPickingWorks.AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == Store && x.DeliveryOrderId == order.Id, ct);
        var lines = await db.DeliveryPickingLines.AsNoTracking().Include(x => x.Quote).Where(x => x.StoreId == Store &&
            x.DeliveryOrderId == order.Id && x.IsActive).ToListAsync(ct);
        if (order.State != DeliveryState.ReadyForHandover || work?.ApprovedRevision is null || work.ApprovedTotal is null ||
            !lines.Any(x => x.ApprovedQuantity is > 0) || lines.Any(x => x.ApprovedQuantity is null) ||
            await db.DeliveryJournalEntries.AsNoTracking().AnyAsync(x => x.StoreId == Store && x.DeliveryOrderId == order.Id && x.Kind == DeliveryJournalKind.Dispatch, ct))
            throw Error(409, "ORDER_NOT_ELIGIBLE", "Đơn cần sẵn sàng, đã duyệt và chưa xuất kho.");
        var holds = await db.InventoryReservations.AsNoTracking().Where(x => x.StoreId == Store && x.DeliveryOrderId == order.Id &&
            x.Status == InventoryReservationStatus.Active).ToListAsync(ct);
        foreach (var line in lines.Where(x => x.ApprovedQuantity is > 0))
        {
            var quantity = DeliveryPickingPolicy.ToBase(line.ApprovedQuantity!.Value, line.Quote.BaseMultiplier);
            var hold = holds.SingleOrDefault(x => x.DeliveryOrderLineId == line.DeliveryOrderLineId);
            if (hold is null || hold.ReservedQty < quantity || hold.ProductVariantId != line.Quote.VariantId ||
                hold.WarehouseId != order.SourceWarehouseId || hold.SourceLegalEntityId != order.SourceLegalEntityId ||
                hold.ReportedByUserId != line.ReporterUserId || hold.ReportedAtUtc != line.ReportedAtUtc)
                throw Error(409, "RESERVATION_DRIFT", "Giữ hàng không khớp kết quả soạn đã duyệt.");
        }
        return work;
    }
    private async Task ReadySelections(IReadOnlyList<DeliveryTripOrderSelection> selection, int? ownTrip, CancellationToken ct)
    {
        foreach (var item in selection)
        {
            var order = await Order(item.DeliveryOrderId, true, false, ct); Match(item.ExpectedVersion, order.RowVersion);
            _ = await Approved(order, ct);
            if (await db.DeliveryTripOrders.AsNoTracking().AnyAsync(x => x.StoreId == Store && x.DeliveryOrderId == order.Id &&
                x.IsActive && (!ownTrip.HasValue || x.TripId != ownTrip), ct))
                throw Error(409, "ORDER_ALREADY_ASSIGNED", "Đơn đã thuộc chuyến khác.");
        }
    }
    private async Task Bind(DeliveryTrip trip, IReadOnlyList<DeliveryTripOrderSelection> selections,
        List<DeliveryTripOrder> previous, CancellationToken ct)
    {
        // Release the filtered position index before any swap. This transient state is private to
        // the same locked SQL transaction; only the final membership enters history/outbox.
        foreach (var old in previous)
        {
            if (old.CurrentCustodyEventId.HasValue) throw Error(409, "CUSTODY_ALREADY_STARTED", "Không bỏ đơn đã bàn giao.");
            old.IsActive = false;
        }
        if (previous.Count != 0) await db.SaveChangesAsync(ct);
        var reusable = await db.DeliveryTripOrders.Where(x => x.StoreId == Store && x.TripId == trip.Id &&
            x.CurrentCustodyEventId == null).OrderByDescending(x => x.Id).ToListAsync(ct);
        for (var position = 0; position < selections.Count; position++)
        {
            var item = selections[position]; var order = await Order(item.DeliveryOrderId, true, false, ct);
            var work = await Approved(order, ct);
            var binding = reusable.FirstOrDefault(x => x.DeliveryOrderId == order.Id);
            if (binding is null)
            {
                binding = new() { StoreId = Store, TripId = trip.Id, DeliveryOrderId = order.Id,
                    SourceWarehouseId = order.SourceWarehouseId, SourceLegalEntityId = order.SourceLegalEntityId };
                db.DeliveryTripOrders.Add(binding);
            }
            binding.IsActive = true; binding.Position = position + 1; binding.AssignedOrderVersion = Version(order.RowVersion);
            binding.AssignedApprovedRevision = work.ApprovedRevision!.Value;
        }
    }
    private async Task<DeliveryTripCommandAck> Command<T>(int? routeId, Guid key, string operation, string? expectedVersion,
        T request, DeliveryCapability required, IReadOnlyList<DeliveryTripOrderSelection>? selections,
        Func<DeliveryTrip?, List<DeliveryTripOrder>, DateTime, Task<DeliveryTrip>> apply, CancellationToken ct)
    {
        await Authorize(required, ct); Key(key);
        if (expectedVersion is not null) _ = DeliveryTripPolicy.Version(expectedVersion);
        if (selections is not null) Selections(selections);
        if (db.Database.CurrentTransaction is not null || !db.Database.IsSqlServer())
            throw Error(409, "TRANSACTION_CONFLICT", "Chuyến cần giao dịch SQL riêng.");
        if (db.ChangeTracker.HasChanges())
            throw Error(409, "TRACKED_STATE_DIRTY", "Cần lưu hoặc hủy thay đổi đang theo dõi trước thao tác chuyến.");
        // This pre-read determines the complete lock set; a changed membership is rejected after the trip lock.
        var hintReceipt = await db.DeliveryTripCommandReceipts.AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == Store && x.ClientRequestId == key, ct);
        var tripId = routeId ?? hintReceipt?.TripId;
        var hinted = tripId.HasValue ? await AllBindings(tripId.Value, ct) : [];
        var orderIds = hinted.Select(x => x.DeliveryOrderId).Concat(selections?.Select(x => x.DeliveryOrderId) ?? []).Distinct().Order().ToArray();
        Fresh(orderIds, tripId);
        var hash = Hash(JsonSerializer.Serialize(new { schema = "delivery-trip-v1", store = Store, actor = Actor, id = routeId, operation, request }, Json));
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var keys = orderIds.Select(id => DeliveryTripPolicy.ChildCommandId(key, id)).Append(key).Distinct().OrderBy(x => x.ToString("D"), StringComparer.Ordinal);
            foreach (var command in keys) await DeliverySqlLock.AcquireAsync(db, $"command:{Store}:{command}", ct);
            if (tripId.HasValue) await DeliverySqlLock.AcquireAsync(db, $"trip:{Store}:{tripId}", ct);
            var currentMembers = tripId.HasValue ? await AllBindings(tripId.Value, ct) : [];
            if (!currentMembers.Select(x => x.Id).Order().SequenceEqual(hinted.Select(x => x.Id).Order()))
                throw Error(409, "TRIP_CHANGED", "Danh sách chuyến đã đổi; tải lại và thử thao tác.");
            foreach (var id in orderIds) await DeliverySqlLock.AcquireAsync(db, $"order:{Store}:{id}", ct);
            await Authorize(required, ct);
            foreach (var id in orderIds) _ = await Order(id, false, false, ct);
            var receipt = await db.DeliveryTripCommandReceipts.AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == Store && x.ClientRequestId == key, ct);
            if (receipt is not null)
            {
                if (receipt.ActorUserId != Actor || receipt.Operation != operation || receipt.RequestHash != hash ||
                    routeId.HasValue && receipt.TripId != routeId)
                    throw Error(409, "REQUEST_KEY_CONFLICT", "Mã yêu cầu đã dùng cho nội dung/người/thao tác khác.");
                await Authorize(receipt.RequiredCapabilities, ct);
                var stored = JsonSerializer.Deserialize<DeliveryTripCommandAck>(receipt.OutcomeJson, Json)
                    ?? throw Error(409, "RECEIPT_INVALID", "Chứng cứ kết quả không hợp lệ.");
                await tx.CommitAsync(ct); return stored with { Replayed = true };
            }
            if (await db.DeliveryCommandReceipts.AsNoTracking().AnyAsync(x => x.StoreId == Store && x.ClientRequestId == key, ct))
                throw Error(409, "REQUEST_KEY_CONFLICT", "Mã yêu cầu đã dùng cho lệnh đơn giao.");
            var childKeys = orderIds.Select(x => DeliveryTripPolicy.ChildCommandId(key, x)).ToArray();
            if (childKeys.Contains(key) || childKeys.Distinct().Count() != childKeys.Length ||
                await db.DeliveryTripCommandReceipts.AsNoTracking().AnyAsync(x => x.StoreId == Store && childKeys.Contains(x.ClientRequestId), ct) ||
                await db.DeliveryCommandReceipts.AsNoTracking().AnyAsync(x => x.StoreId == Store && childKeys.Contains(x.ClientRequestId), ct))
                throw Error(409, "REQUEST_KEY_CONFLICT", "Khóa lệnh con đã được sử dụng; không nhận lại chứng cứ xuất cũ.");
            var trip = tripId.HasValue ? await Trip(tripId.Value, true, ct) : null;
            if (trip is not null && expectedVersion is not null) Match(expectedVersion, trip.RowVersion);
            var members = tripId.HasValue ? await Members(tripId.Value, true, ct) : [];
            // Original conditional capabilities are captured before mutation and persisted for all future replays.
            var requiredOriginal = required;
            if (request is DeliveryTripEditDraftRequest edit && trip?.CourierUserId != edit.CourierUserId)
            { requiredOriginal |= DeliveryCapability.ReassignCourier; await Authorize(requiredOriginal, ct); }
            var now = DateTime.UtcNow;
            trip = await apply(trip, members, now);
            await db.SaveChangesAsync(ct);
            var applied = await Members(trip.Id, false, ct);
            var outcomes = new List<DeliveryTripAppliedOrder>();
            foreach (var member in applied)
            {
                var order = await Order(member.DeliveryOrderId, false, false, ct);
                outcomes.Add(new(order.Id, order.Revision, Version(order.RowVersion), order.State.ToString()));
            }
            var recorded = DateTime.UtcNow;
            var ack = new DeliveryTripCommandAck(key, trip.Id, operation, trip.Revision, Version(trip.RowVersion), Utc(recorded), false, outcomes);
            await RecordTrip(trip, key, operation, hash, requiredOriginal, request, ack, recorded, ct);
            await tx.CommitAsync(ct); return ack;
        }
        catch (DbUpdateConcurrencyException) { await tx.RollbackAsync(CancellationToken.None); db.ChangeTracker.Clear(); throw Error(409, "VERSION_CONFLICT", "Hồ sơ đã đổi."); }
        catch (BusinessRuleException ex)
        {
            await tx.RollbackAsync(CancellationToken.None); db.ChangeTracker.Clear();
            var code = ex.Message.Split(':', 2)[0];
            if (code.Length > 64 || code.Any(x => !(char.IsAsciiLetterUpper(x) || x == '_'))) code = "INVENTORY_CONFLICT";
            throw Error(409, code, ex.Message);
        }
        catch (ConcurrencyException ex) { await tx.RollbackAsync(CancellationToken.None); db.ChangeTracker.Clear(); throw Error(409, "INVENTORY_CONFLICT", ex.Message); }
        catch (SqlException ex) when (ex.Number is 1205 or 1222) { await tx.RollbackAsync(CancellationToken.None); db.ChangeTracker.Clear(); throw Error(409, "DELIVERY_BUSY", "Hồ sơ đang được cập nhật; thử lại cùng mã yêu cầu."); }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException sql && sql.Number is 2601 or 2627 or 51034)
        { await tx.RollbackAsync(CancellationToken.None); db.ChangeTracker.Clear(); throw Error(409, "REQUEST_KEY_CONFLICT", "Mã yêu cầu hoặc phân công đã được dùng."); }
        catch { await tx.RollbackAsync(CancellationToken.None); db.ChangeTracker.Clear(); throw; }
    }
    private async Task RecordTrip<T>(DeliveryTrip trip, Guid key, string operation, string hash,
        DeliveryCapability required, T request, DeliveryTripCommandAck ack, DateTime now, CancellationToken ct)
    {
        var bindings = await db.DeliveryTripOrders.AsNoTracking().Where(x => x.StoreId == Store && x.TripId == trip.Id).OrderBy(x => x.Id).ToListAsync(ct);
        var snapshot = JsonSerializer.Serialize(new { trip.Id, trip.Code, state = trip.State.ToString(), trip.Revision, trip.CourierUserId,
            trip.Note, trip.HandedOverByUserId, trip.HandedOverAtUtc, trip.DepartedByUserId, trip.DepartedAtUtc,
            trip.AbandonedByUserId, trip.AbandonedAtUtc, action = operation, actorUserId = Actor, recordedAtUtc = Utc(now),
            attestationKind = operation is "handover" or "reassign" ? "AtStoreByStaff" : null, request,
            bindings = bindings.Select(x => new { x.Id, x.DeliveryOrderId, x.SourceWarehouseId, x.SourceLegalEntityId,
                x.IsActive, x.Position, x.AssignedOrderVersion, x.AssignedApprovedRevision, x.CurrentCustodyEventId }) }, Json);
        db.DeliveryTripRevisions.Add(new() { StoreId = Store, TripId = trip.Id, Revision = trip.Revision, ActorUserId = Actor,
            Action = operation, AggregateVersion = Version(trip.RowVersion), SnapshotJson = snapshot, SnapshotHash = Hash(snapshot) });
        db.DeliveryTripCommandReceipts.Add(new() { StoreId = Store, TripId = trip.Id, ClientRequestId = key, ActorUserId = Actor,
            Operation = operation, RequestHash = hash, RequiredCapabilities = required, OutcomeJson = JsonSerializer.Serialize(ack, Json) });
        var eventId = Guid.NewGuid();
        db.DeliveryTripOutboxMessages.Add(new() { StoreId = Store, TripId = trip.Id, Revision = trip.Revision, EventId = eventId,
            Action = operation, PayloadJson = JsonSerializer.Serialize(new { eventId, storeId = Store, tripId = trip.Id,
                trip.Code, trip.Revision, action = operation, actorUserId = Actor, recordedAtUtc = Utc(now) }, Json) });
        await db.SaveChangesAsync(ct);
    }
    public Task<DeliveryTripCommandAck> CreateDraftAsync(DeliveryTripCreateDraftRequest request, CancellationToken ct = default)
    {
        Selections(request.Orders);
        request = request with { Note = DeliveryTripPolicy.NormalizeNote(request.Note) };
        return Command(null, request.ClientRequestId, "create", null, request, DeliveryCapability.View | DeliveryCapability.Handover,
            request.Orders, async (_, _, now) =>
            {
                await Courier(request.CourierUserId, ct); await ReadySelections(request.Orders, null, ct);
                var trip = new DeliveryTrip { StoreId = Store, Code = "GH-" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(),
                    CourierUserId = request.CourierUserId, CreatedByUserId = Actor, Note = request.Note, State = DeliveryTripState.Draft };
                db.DeliveryTrips.Add(trip); await db.SaveChangesAsync(ct); await Bind(trip, request.Orders, [], ct); return trip;
            }, ct);
    }
    public Task<DeliveryTripCommandAck> EditDraftAsync(int id, DeliveryTripEditDraftRequest request, CancellationToken ct = default)
    {
        Selections(request.Orders);
        request = request with { Reason = DeliveryTripPolicy.Reason(request.Reason) };
        return Command(id, request.ClientRequestId, "draft", request.ExpectedTripVersion, request, DeliveryCapability.View | DeliveryCapability.Handover,
            request.Orders, async (value, members, _) =>
            {
                var trip = value!; RequireState(trip, DeliveryTripState.Draft);
                await Courier(request.CourierUserId, ct); await ReadySelections(request.Orders, id, ct);
                await Bind(trip, request.Orders, members, ct); trip.CourierUserId = request.CourierUserId; trip.Revision++; return trip;
            }, ct);
    }
    public Task<DeliveryTripCommandAck> AbandonDraftAsync(int id, DeliveryTripAbandonRequest request, CancellationToken ct = default)
    {
        request = request with { Reason = DeliveryTripPolicy.Reason(request.Reason) };
        return Command(id, request.ClientRequestId, "abandon", request.ExpectedTripVersion, request, DeliveryCapability.View | DeliveryCapability.Handover,
            null, async (value, members, now) =>
            {
                var trip = value!; RequireState(trip, DeliveryTripState.Draft);
                if (members.Any(x => x.CurrentCustodyEventId.HasValue)) throw Error(409, "CUSTODY_ALREADY_STARTED", "Chuyến đã bàn giao không thể bỏ.");
                foreach (var member in members) member.IsActive = false;
                await db.SaveChangesAsync(ct);
                trip.State = DeliveryTripState.Abandoned; trip.AbandonedByUserId = Actor; trip.AbandonedAtUtc = now; trip.Revision++;
                return trip;
            }, ct);
    }
    private async Task PhysicalSelections(DeliveryTrip trip, List<DeliveryTripOrder> members,
        IReadOnlyList<DeliveryTripOrderSelection> selections, DeliveryState requiredState, CancellationToken ct)
    {
        if (!members.Select(x => x.DeliveryOrderId).SequenceEqual(selections.Select(x => x.DeliveryOrderId)))
            throw Error(409, "ORDER_SET_CHANGED", "Cần đúng toàn bộ thứ tự đơn trong chuyến.");
        foreach (var member in members)
        {
            var order = await Order(member.DeliveryOrderId, true, false, ct);
            Match(selections.Single(x => x.DeliveryOrderId == order.Id).ExpectedVersion, order.RowVersion);
            if (order.State != requiredState || member.SourceWarehouseId != order.SourceWarehouseId || member.SourceLegalEntityId != order.SourceLegalEntityId)
                throw Error(409, "ORDER_NOT_ELIGIBLE", "Đơn/trách nhiệm nguồn không còn phù hợp thao tác.");
            if (requiredState == DeliveryState.ReadyForHandover)
            {
                var approved = await Approved(order, ct);
                if (member.AssignedOrderVersion != Version(order.RowVersion) || member.AssignedApprovedRevision != approved.ApprovedRevision)
                    throw Error(409, "ASSIGNMENT_STALE", "Đơn đã đổi sau phân công; cập nhật chuyến nháp trước bàn giao.");
            }
            else
            {
                var custody = await DeliveryCustodyProjection.ReadAsync(db, Store, order, ct);
                if (custody is null || custody.Context.TripId != trip.Id || custody.Context.CourierUserId != trip.CourierUserId)
                    throw Error(409, "CUSTODY_PROOF_INVALID", "Thiếu chứng cứ người giao đang giữ hàng.");
            }
        }
    }
    private async Task RecordOrder(DeliveryOrder order, string operation, Guid root, string? reason, DateTime now, CancellationToken ct)
    {
        var detail = JsonSerializer.SerializeToNode(DeliveryFoundationService.Detail(order), Json)!.AsObject();
        detail["tripAction"] = operation; detail["rootClientRequestId"] = root.ToString("D");
        detail["tripReason"] = reason; detail["tripActorUserId"] = Actor;
        detail["tripRecordedAtUtc"] = Utc(now).ToString("O", CultureInfo.InvariantCulture);
        var snapshot = detail.ToJsonString(Json);
        db.DeliveryRevisions.Add(new() { StoreId = Store, DeliveryOrderId = order.Id, Revision = order.Revision, ActorUserId = Actor,
            Action = "trip-" + operation, AggregateVersion = Version(order.RowVersion), SnapshotJson = snapshot, SnapshotHash = Hash(snapshot) });
        var eventId = Guid.NewGuid();
        db.DeliveryOutboxMessages.Add(new() { StoreId = Store, DeliveryOrderId = order.Id, Revision = order.Revision, EventId = eventId,
            Action = "trip-" + operation, PayloadJson = JsonSerializer.Serialize(new { eventId, storeId = Store, deliveryId = order.Id,
                order.Code, order.Revision, action = "trip-" + operation, actorUserId = Actor, recordedAtUtc = Utc(now) }, Json) });
        await db.SaveChangesAsync(ct);
    }
    private async Task<DateTime> Custody(DeliveryTrip trip, DeliveryTripOrder member, DeliveryOrder order, Guid root,
        DeliveryCustodyKind kind, int courier, int? previousCourier, string? reason, string? note, CancellationToken ct)
    {
        var journals = await db.DeliveryJournalEntries.AsNoTracking().Where(x => x.StoreId == Store && x.DeliveryOrderId == order.Id &&
            (x.Kind == DeliveryJournalKind.Dispatch || x.Kind == DeliveryJournalKind.Return)).ToListAsync(ct);
        var dispatch = journals.Where(x => x.Kind == DeliveryJournalKind.Dispatch).ToArray();
        if (dispatch.Length == 0 || dispatch.Any(x => x.DeliveryOrderLineId is null || x.VariantId is null))
            throw Error(409, "DISPATCH_PROOF_INVALID", "Thiếu chứng cứ xuất nguồn thật.");
        var previous = member.CurrentCustodyEventId.HasValue ? await db.DeliveryCustodyEvents.AsNoTracking().SingleAsync(x =>
            x.StoreId == Store && x.Id == member.CurrentCustodyEventId, ct) : null;
        var minimum = dispatch.Select(x => x.CreatedAtUtc).Append(previous?.ConfirmedAtUtc ?? DateTime.MinValue).Max();
        var now = await SqlUtcAtOrAfter(minimum, ct);
        var custody = new DeliveryCustodyEvent { StoreId = Store, EventId = Guid.NewGuid(), TripId = trip.Id,
            TripOrderId = member.Id, DeliveryOrderId = order.Id, Kind = kind, PreviousCustodyEventId = member.CurrentCustodyEventId,
            CourierUserId = courier, PreviousCourierUserId = previousCourier, ActorUserId = Actor, ConfirmedAtUtc = now,
            ConfirmationNote = note ?? DeliveryTripPolicy.StaffAttestation, Reason = reason, SourceWarehouseId = order.SourceWarehouseId,
            SourceLegalEntityId = order.SourceLegalEntityId, RootClientRequestId = root };
        db.DeliveryCustodyEvents.Add(custody); await db.SaveChangesAsync(ct);
        foreach (var journal in dispatch)
        {
            var received = journals.Where(x => x.Kind == DeliveryJournalKind.Return && x.DeliveryOrderLineId == journal.DeliveryOrderLineId).Sum(x => x.BaseQuantity);
            if (received < 0 || received > journal.BaseQuantity) throw Error(409, "CUSTODY_PROOF_INVALID", "Lượng nhận trả vượt nguồn xuất.");
            db.DeliveryCustodyLines.Add(new() { StoreId = Store, CustodyEventId = custody.Id, DeliveryOrderLineId = journal.DeliveryOrderLineId!.Value,
                DispatchJournalEntryId = journal.Id, VariantId = journal.VariantId!.Value, SourceWarehouseId = journal.SourceWarehouseId,
                SourceLegalEntityId = journal.SourceLegalEntityId, ConfirmedBaseQuantity = journal.BaseQuantity - received,
                CumulativeAcceptedReturnAtConfirmation = received });
        }
        await db.SaveChangesAsync(ct); member.CurrentCustodyEventId = custody.Id; return now;
    }
    public Task<DeliveryTripCommandAck> HandoverAsync(int id, DeliveryTripHandoverRequest request, CancellationToken ct = default)
    {
        Selections(request.Orders);
        request = request with { ConfirmationNote = DeliveryTripPolicy.NormalizeNote(request.ConfirmationNote) };
        return Command(id, request.ClientRequestId, "handover", request.ExpectedTripVersion, request,
            DeliveryCapability.View | DeliveryCapability.Handover, request.Orders, async (value, members, now) =>
            {
                var trip = value!; RequireState(trip, DeliveryTripState.Draft); await Courier(trip.CourierUserId, ct);
                DeliveryTripPolicy.PhysicalReceipt(request.PhysicalReceiptConfirmed, request.ConfirmedCourierUserId, trip.CourierUserId);
                await PhysicalSelections(trip, members, request.Orders, DeliveryState.ReadyForHandover, ct);
                var union = new HashSet<(int Warehouse, int Variant)>();
                foreach (var member in members)
                {
                    var lines = await db.DeliveryPickingLines.AsNoTracking().Include(x => x.Quote).Where(x => x.StoreId == Store &&
                        x.DeliveryOrderId == member.DeliveryOrderId && x.IsActive).ToListAsync(ct);
                    foreach (var line in lines) union.Add((member.SourceWarehouseId, line.Quote.VariantId));
                    var heldVariants = await db.InventoryReservations.AsNoTracking().Where(x => x.StoreId == Store &&
                        x.DeliveryOrderId == member.DeliveryOrderId && x.Status == InventoryReservationStatus.Active).Select(x => x.ProductVariantId).ToListAsync(ct);
                    foreach (var variant in heldVariants) union.Add((member.SourceWarehouseId, variant));
                }
                foreach (var pair in union.OrderBy(x => x.Warehouse).ThenBy(x => x.Variant))
                    _ = await balances.LockAndGetOrCreateAsync(Store, pair.Warehouse, pair.Variant, ct);
                DateTime? lastConfirmation = null;
                foreach (var member in members.OrderBy(x => x.DeliveryOrderId))
                {
                    var before = await Order(member.DeliveryOrderId, true, false, ct);
                    await inventory.DispatchInTransactionAsync(before.Id, new(DeliveryTripPolicy.ChildCommandId(request.ClientRequestId, before.Id),
                        Version(before.RowVersion), member.AssignedApprovedRevision), ct);
                    // D05 refreshed its tracked order and increased revision. Read that persisted version, not the old reference.
                    Fresh([before.Id], null); var order = await Order(before.Id, true, true, ct);
                    order.State = DeliveryWorkflowPolicy.Apply(order.State, DeliveryCommand.Handover,
                        new(PickingComplete: true, HasPickedGoods: true, CourierAssigned: true, CustodyHandoverConfirmed: true));
                    order.Revision++; await db.SaveChangesAsync(ct);
                    var confirmed = await Custody(trip, member, order, request.ClientRequestId, DeliveryCustodyKind.Handover,
                        trip.CourierUserId, null, null, request.ConfirmationNote, ct);
                    await RecordOrder(order, "handover", request.ClientRequestId, null, confirmed, ct);
                    lastConfirmation = confirmed;
                }
                trip.State = DeliveryTripState.HandedOver; trip.HandedOverByUserId = Actor;
                trip.HandedOverAtUtc = lastConfirmation; trip.Revision++;
                return trip;
            }, ct);
    }
    public Task<DeliveryTripCommandAck> DepartAsync(int id, DeliveryTripDepartureRequest request, CancellationToken ct = default)
    {
        Selections(request.Orders);
        return Command(id, request.ClientRequestId, "depart", request.ExpectedTripVersion, request,
            DeliveryCapability.View | DeliveryCapability.RecordDeparture, request.Orders, async (value, members, now) =>
            {
                var trip = value!; RequireState(trip, DeliveryTripState.HandedOver); await Courier(trip.CourierUserId, ct);
                await PhysicalSelections(trip, members, request.Orders, DeliveryState.HandedOver, ct);
                var latestCustody = await db.DeliveryCustodyEvents.AsNoTracking().Where(x => x.StoreId == Store && x.TripId == id)
                    .MaxAsync(x => (DateTime?)x.ConfirmedAtUtc, ct)
                    ?? throw Error(409, "CUSTODY_PROOF_INVALID", "Chưa có chứng cứ bàn giao cho chuyến.");
                now = await SqlUtcAtOrAfter(latestCustody, ct);
                foreach (var member in members.OrderBy(x => x.DeliveryOrderId))
                {
                    var order = await Order(member.DeliveryOrderId, true, true, ct);
                    order.State = DeliveryWorkflowPolicy.Apply(order.State, DeliveryCommand.Depart,
                        new(CourierAssigned: true, CustodyHandoverConfirmed: true));
                    order.Revision++; await db.SaveChangesAsync(ct); await RecordOrder(order, "depart", request.ClientRequestId, null, now, ct);
                }
                trip.State = DeliveryTripState.Departed; trip.DepartedByUserId = Actor; trip.DepartedAtUtc = now; trip.Revision++; return trip;
            }, ct);
    }
    public Task<DeliveryTripCommandAck> ReassignCourierAsync(int id, DeliveryTripReassignRequest request, CancellationToken ct = default)
    {
        Selections(request.Orders);
        request = request with { Reason = DeliveryTripPolicy.Reason(request.Reason), ConfirmationNote = DeliveryTripPolicy.NormalizeNote(request.ConfirmationNote) };
        return Command(id, request.ClientRequestId, "reassign", request.ExpectedTripVersion, request,
            DeliveryCapability.View | DeliveryCapability.Handover | DeliveryCapability.ReassignCourier, request.Orders, async (value, members, now) =>
            {
                var trip = value!; RequireState(trip, DeliveryTripState.HandedOver); await Courier(request.NewCourierUserId, ct);
                if (request.NewCourierUserId == trip.CourierUserId) throw Error(400, "COURIER_UNCHANGED", "Chọn người giao khác.");
                DeliveryTripPolicy.PhysicalReceipt(request.PhysicalReceiptConfirmed, request.ConfirmedCourierUserId, request.NewCourierUserId);
                await PhysicalSelections(trip, members, request.Orders, DeliveryState.HandedOver, ct);
                var previous = trip.CourierUserId;
                foreach (var member in members.OrderBy(x => x.DeliveryOrderId))
                {
                    var order = await Order(member.DeliveryOrderId, true, true, ct);
                    order.State = DeliveryWorkflowPolicy.Apply(order.State, DeliveryCommand.ReassignCourier,
                        new(CourierAssigned: true, CustodyHandoverConfirmed: true));
                    order.Revision++; await db.SaveChangesAsync(ct);
                    var confirmed = await Custody(trip, member, order, request.ClientRequestId, DeliveryCustodyKind.Transfer,
                        request.NewCourierUserId, previous, request.Reason, request.ConfirmationNote, ct);
                    await RecordOrder(order, "reassign", request.ClientRequestId, request.Reason, confirmed, ct);
                }
                trip.CourierUserId = request.NewCourierUserId; trip.Revision++; return trip;
            }, ct);
    }
    public Task<DeliveryTripCommandAck> RequestCancellationAsync(int id, int orderId, DeliveryTripCancellationRequest request, CancellationToken ct = default)
    {
        request = request with { Reason = DeliveryTripPolicy.Reason(request.Reason) }; _ = DeliveryTripPolicy.Version(request.ExpectedOrderVersion);
        return Command(id, request.ClientRequestId, "cancel-request", request.ExpectedTripVersion, new { orderId, request },
            DeliveryCapability.View | DeliveryCapability.Cancel, null, async (value, members, now) =>
            {
                var trip = value!;
                if (trip.State is not (DeliveryTripState.HandedOver or DeliveryTripState.Departed))
                    throw Error(409, "TRIP_STATE_INVALID", "Chỉ yêu cầu thu hồi đơn đã bàn giao.");
                if (!members.Any(x => x.DeliveryOrderId == orderId && x.CurrentCustodyEventId.HasValue))
                    throw Error(404, "DELIVERY_NOT_FOUND", "Đơn không thuộc trách nhiệm chuyến.");
                var order = await Order(orderId, false, true, ct); Match(request.ExpectedOrderVersion, order.RowVersion);
                if (order.State is not (DeliveryState.HandedOver or DeliveryState.Delivering) ||
                    await DeliveryCustodyProjection.ReadAsync(db, Store, order, ct) is null)
                    throw Error(409, "CANCEL_NOT_ALLOWED", "Đơn không còn cho phép yêu cầu thu hồi.");
                order.State = DeliveryWorkflowPolicy.Apply(order.State, DeliveryCommand.RequestCancellation, new(HasDeliveredGoods: false));
                order.Revision++; await db.SaveChangesAsync(ct); await RecordOrder(order, "cancel-request", request.ClientRequestId, request.Reason, now, ct);
                trip.Revision++; return trip;
            }, ct);
    }
    private async Task<DeliveryTripLineDto[]> Lines(DeliveryOrder order, CancellationToken ct,
        DeliveryCustodyProjection.Result? custody = null)
    {
        var picking = await db.DeliveryPickingLines.AsNoTracking().Where(x => x.StoreId == Store && x.DeliveryOrderId == order.Id && x.IsActive).ToListAsync(ct);
        var journals = await db.DeliveryJournalEntries.AsNoTracking().Where(x => x.StoreId == Store && x.DeliveryOrderId == order.Id &&
            (x.Kind == DeliveryJournalKind.Dispatch || x.Kind == DeliveryJournalKind.Return)).ToListAsync(ct);
        return order.Lines.Where(line => picking.Any(x => x.DeliveryOrderLineId == line.Id) || journals.Any(x => x.DeliveryOrderLineId == line.Id)).Select(line =>
        {
            var approved = picking.SingleOrDefault(x => x.DeliveryOrderLineId == line.Id)?.ApprovedQuantity ?? 0;
            return new DeliveryTripLineDto(line.Id, line.VariantId, line.ItemName, line.BaseUnitName,
                Qty(DeliveryPickingPolicy.ToBase(approved, line.BaseMultiplier)),
                Qty(journals.Where(x => x.DeliveryOrderLineId == line.Id && x.Kind == DeliveryJournalKind.Dispatch).Sum(x => x.BaseQuantity)),
                Qty(journals.Where(x => x.DeliveryOrderLineId == line.Id && x.Kind == DeliveryJournalKind.Return).Sum(x => x.BaseQuantity)),
                custody is not null ? Qty(custody.Quantities.GetValueOrDefault(line.Id)) : null);
        }).ToArray();
    }
    private async Task<string> Name(int id, CancellationToken ct)
        => await db.Users.AsNoTracking().Where(x => x.Id == id).Select(x => x.FullName ?? x.UserName).SingleOrDefaultAsync(ct)
            ?? id.ToString(CultureInfo.InvariantCulture);
    private async Task<DeliveryTripDetailDto> Project(DeliveryTrip trip, CancellationToken ct)
    {
        var bindings = await db.DeliveryTripOrders.AsNoTracking().Where(x => x.StoreId == Store && x.TripId == trip.Id).ToListAsync(ct);
        // Revisions preserve every binding fact. The current screen represents each order once,
        // preferring its active membership, without borrowing another trip's later custody.
        var members = bindings.GroupBy(x => x.DeliveryOrderId).Select(group => group.OrderByDescending(x => x.IsActive)
            .ThenByDescending(x => x.Id).First()).OrderBy(x => x.Position).ThenBy(x => x.Id).ToArray();
        var canHandover = await Has(DeliveryCapability.Handover, ct); var canDepart = await Has(DeliveryCapability.RecordDeparture, ct);
        var canReassign = await Has(DeliveryCapability.ReassignCourier, ct); var canCancel = await Has(DeliveryCapability.Cancel, ct);
        var rows = new List<DeliveryTripOrderDto>(); var eligible = true;
        foreach (var member in members)
        {
            var order = await Order(member.DeliveryOrderId, false, false, ct);
            var wh = await db.Warehouses.AsNoTracking().SingleAsync(x => x.StoreId == Store && x.Id == member.SourceWarehouseId, ct);
            var le = await db.LegalEntities.AsNoTracking().SingleAsync(x => x.StoreId == Store && x.Id == member.SourceLegalEntityId, ct);
            var work = await db.DeliveryPickingWorks.AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == Store && x.DeliveryOrderId == order.Id, ct);
            var stale = member.IsActive && trip.State == DeliveryTripState.Draft && (order.State != DeliveryState.ReadyForHandover ||
                member.AssignedOrderVersion != Version(order.RowVersion) || work?.ApprovedRevision != member.AssignedApprovedRevision || work?.ApprovedTotal is null);
            if (member.IsActive && (stale || trip.State == DeliveryTripState.HandedOver && order.State != DeliveryState.HandedOver)) eligible = false;
            var custody = member.IsActive && member.CurrentCustodyEventId.HasValue
                ? await DeliveryCustodyProjection.ReadAsync(db, Store, order, ct) : null;
            if (member.CurrentCustodyEventId.HasValue && (custody is null || custody.Context.TripId != trip.Id ||
                custody.Context.CustodyEventId != member.CurrentCustodyEventId))
                throw Error(409, "CUSTODY_PROOF_INVALID", "Chứng cứ giữ hàng không thuộc thành viên của chuyến.");
            rows.Add(new(order.Id, order.Code, order.State.ToString(), order.Revision, Version(order.RowVersion), member.Position, member.IsActive,
                member.AssignedOrderVersion, member.AssignedApprovedRevision, stale, wh.Id, wh.Name, le.Id, le.Name,
                order.RecipientName, order.RecipientPhone, order.RecipientAddress, await Lines(order, ct, custody), custody?.Context,
                member.IsActive && canCancel && custody is not null && order.State is DeliveryState.HandedOver or DeliveryState.Delivering));
        }
        var history = await db.DeliveryTripRevisions.AsNoTracking().Where(x => x.StoreId == Store && x.TripId == trip.Id).OrderByDescending(x => x.Revision).Take(100).ToListAsync(ct);
        var historyRows = new List<DeliveryTripHistoryDto>();
        foreach (var revision in history) historyRows.Add(new(revision.Revision, revision.Action, revision.ActorUserId,
            await Name(revision.ActorUserId, ct), Utc(revision.CreatedAtUtc)));
        var courierActive = await db.UserInStores.AsNoTracking().AnyAsync(x => x.StoreId == Store && x.UserId == trip.CourierUserId && x.IsActive && x.User.IsActive, ct);
        return new(Store, trip.Id, trip.Code, trip.State.ToString(), trip.Revision, Version(trip.RowVersion), Utc(DateTime.UtcNow), trip.CourierUserId,
            await Name(trip.CourierUserId, ct), trip.CreatedByUserId, await Name(trip.CreatedByUserId, ct), Utc(trip.CreatedAtUtc), trip.Note,
            trip.HandedOverByUserId, trip.HandedOverByUserId.HasValue ? await Name(trip.HandedOverByUserId.Value, ct) : null,
            trip.HandedOverAtUtc.HasValue ? Utc(trip.HandedOverAtUtc.Value) : null, trip.DepartedByUserId,
            trip.DepartedByUserId.HasValue ? await Name(trip.DepartedByUserId.Value, ct) : null,
            trip.DepartedAtUtc.HasValue ? Utc(trip.DepartedAtUtc.Value) : null, trip.AbandonedByUserId,
            trip.AbandonedAtUtc.HasValue ? Utc(trip.AbandonedAtUtc.Value) : null, rows,
            new(trip.State == DeliveryTripState.Draft && canHandover, trip.State == DeliveryTripState.Draft && canHandover,
                trip.State == DeliveryTripState.Draft && canHandover && courierActive && eligible,
                trip.State == DeliveryTripState.HandedOver && canDepart && courierActive && eligible,
                canHandover && canReassign && (trip.State == DeliveryTripState.Draft || trip.State == DeliveryTripState.HandedOver && eligible),
                rows.Any(x => x.CanRequestCancellation)), historyRows);
    }
    public async Task<DeliveryTripDetailDto> GetAsync(int id, CancellationToken ct = default)
    {
        await Authorize(DeliveryCapability.View, ct);
        if (db.Database.CurrentTransaction is not null) throw Error(409, "TRANSACTION_CONFLICT", "Đọc chuyến cần giao dịch riêng.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await DeliverySqlLock.AcquireAsync(db, $"trip:{Store}:{id}", ct);
        var members = await db.DeliveryTripOrders.AsNoTracking().Where(x => x.StoreId == Store && x.TripId == id).ToListAsync(ct);
        foreach (var orderId in members.Select(x => x.DeliveryOrderId).Distinct().Order())
            await DeliverySqlLock.AcquireAsync(db, $"order:{Store}:{orderId}", ct);
        await Authorize(DeliveryCapability.View, ct); var trip = await Trip(id, false, ct);
        var detail = await Project(trip, ct); await tx.CommitAsync(ct); return detail;
    }
    public async Task<DeliveryTripOptionsDto> GetOptionsAsync(CancellationToken ct = default)
    {
        await Authorize(DeliveryCapability.View | DeliveryCapability.Handover, ct);
        var couriers = await db.UserInStores.AsNoTracking().Where(x => x.StoreId == Store && x.IsActive && x.User.IsActive)
            .OrderBy(x => x.User.FullName ?? x.User.UserName).ThenBy(x => x.UserId).Take(200)
            .Select(x => new DeliveryTripCourierOption(x.UserId, x.User.FullName ?? x.User.UserName)).ToListAsync(ct);
        var candidates = await db.DeliveryOrders.AsNoTracking().Where(x => x.StoreId == Store && x.State == DeliveryState.ReadyForHandover &&
            !db.DeliveryTripOrders.Any(b => b.StoreId == Store && b.DeliveryOrderId == x.Id && b.IsActive) &&
            !db.DeliveryJournalEntries.Any(j => j.StoreId == Store && j.DeliveryOrderId == x.Id && j.Kind == DeliveryJournalKind.Dispatch))
            .OrderByDescending(x => x.Id).Take(100).Select(x => x.Id).ToListAsync(ct);
        var result = new List<DeliveryTripEligibleOrderDto>();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        foreach (var id in candidates.Order()) await DeliverySqlLock.AcquireAsync(db, $"order:{Store}:{id}", ct);
        await Authorize(DeliveryCapability.View | DeliveryCapability.Handover, ct);
        foreach (var id in candidates)
        {
            try
            {
                var order = await Order(id, true, false, ct); var work = await Approved(order, ct);
                if (await db.DeliveryTripOrders.AsNoTracking().AnyAsync(x => x.StoreId == Store && x.DeliveryOrderId == id && x.IsActive, ct)) continue;
                var wh = await db.Warehouses.AsNoTracking().SingleAsync(x => x.StoreId == Store && x.Id == order.SourceWarehouseId, ct);
                var le = await db.LegalEntities.AsNoTracking().SingleAsync(x => x.StoreId == Store && x.Id == order.SourceLegalEntityId, ct);
                result.Add(new(id, order.Code, order.Revision, Version(order.RowVersion), work.ApprovedRevision!.Value,
                    wh.Id, wh.Name, le.Id, le.Name, order.RecipientName, order.RecipientAddress, await Lines(order, ct)));
            }
            catch (DeliveryFoundationException ex) when (ex.Code is "ORDER_NOT_ELIGIBLE" or "RESERVATION_DRIFT" or "SOURCE_SCOPE_INVALID") { }
        }
        await tx.CommitAsync(ct); return new(Store, Utc(DateTime.UtcNow), couriers, result);
    }
    public async Task<DeliveryTripPageDto> ListAsync(int take = 30, int? beforeId = null, string? state = null,
        int? courierUserId = null, CancellationToken ct = default)
    {
        await Authorize(DeliveryCapability.View, ct);
        if (take is < 1 or > 100 || beforeId is <= 0 || courierUserId is <= 0) throw Error(400, "PAGE_INVALID", "Bộ lọc trang không hợp lệ.");
        var query = db.DeliveryTrips.AsNoTracking().Where(x => x.StoreId == Store && (!beforeId.HasValue || x.Id < beforeId) &&
            (!courierUserId.HasValue || x.CourierUserId == courierUserId));
        if (!string.IsNullOrWhiteSpace(state))
        {
            if (!Enum.TryParse<DeliveryTripState>(state, false, out var parsed) || !Enum.IsDefined(parsed) || parsed.ToString() != state)
                throw Error(400, "STATE_INVALID", "Trạng thái lọc không hợp lệ.");
            query = query.Where(x => x.State == parsed);
        }
        var candidates = await query.OrderByDescending(x => x.Id).Take(take + 1).Select(x => x.Id).ToListAsync(ct);
        if (db.Database.CurrentTransaction is not null) throw Error(409, "TRANSACTION_CONFLICT", "Đọc chuyến cần giao dịch riêng.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        foreach (var id in candidates.Order()) await DeliverySqlLock.AcquireAsync(db, $"trip:{Store}:{id}", ct);
        await Authorize(DeliveryCapability.View, ct);
        var trips = await query.Where(x => candidates.Contains(x.Id)).OrderByDescending(x => x.Id).ToListAsync(ct);
        var rows = new List<DeliveryTripSummaryDto>();
        foreach (var trip in trips.Take(take)) rows.Add(new(trip.Id, trip.Code, trip.State.ToString(), trip.Revision, Version(trip.RowVersion),
            trip.CourierUserId, await Name(trip.CourierUserId, ct), await db.DeliveryTripOrders.AsNoTracking().CountAsync(x => x.StoreId == Store && x.TripId == trip.Id && x.IsActive, ct),
            Utc(trip.CreatedAtUtc), trip.HandedOverAtUtc.HasValue ? Utc(trip.HandedOverAtUtc.Value) : null,
            trip.DepartedAtUtc.HasValue ? Utc(trip.DepartedAtUtc.Value) : null));
        await tx.CommitAsync(ct);
        return new(Store, Utc(DateTime.UtcNow), rows, trips.Count > take ? rows[^1].TripId : null);
    }
}
