using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.Delivery;
using GaoApp.Application.Interfaces.Services.Delivery;
using GaoApp.Application.Interfaces.Services.Security;
using GaoApp.Domain.Delivery;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using static GaoApp.Domain.Delivery.DeliveryPickingPolicy;

namespace GaoApp.Infrastructure.Services.Delivery;

public sealed class DeliveryPickingService(AppDbContext db, ICurrentUser user,
    ICurrentStorePermissionService permissions) : IDeliveryPickingService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private int Store => db.CurrentStoreId ?? throw Error(403, "STORE_REQUIRED", "Cần thao tác trong cửa hàng.");
    private int Actor => user.UserId ?? throw Error(403, "ACTOR_REQUIRED", "Cần đăng nhập.");
    private static DeliveryFoundationException Error(int status, string code, string message) => new(status, code, message);
    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
    private static DateTimeOffset? Utc(DateTime? value) => value.HasValue ? Utc(value.Value) : null;
    private async Task Authorize(string permission, CancellationToken ct)
    {
        if (!user.IsAuthenticated || !await db.UserInStores.AsNoTracking().AnyAsync(x =>
            x.StoreId == Store && x.UserId == Actor && x.IsActive && x.User.IsActive, ct) ||
            !await permissions.HasPermissionAsync(Store, Actor, permission, ct))
            throw Error(403, "DELIVERY_FORBIDDEN", "Không có quyền soạn hàng trong cửa hàng.");
    }
    private async Task Scope(DeliveryOrder order, CancellationToken ct)
    {
        if (order.StoreId != Store || !await db.Warehouses.AsNoTracking().AnyAsync(x =>
            x.StoreId == Store && x.Id == order.SourceWarehouseId && x.IsActive && x.LegalEntityId == order.SourceLegalEntityId, ct) ||
            !await db.LegalEntities.AsNoTracking().AnyAsync(x => x.StoreId == Store && x.Id == order.SourceLegalEntityId && x.IsActive, ct))
            throw Error(403, "SOURCE_SCOPE_INVALID", "Kho/chủ thể nguồn không còn hợp lệ.");
        if (!await db.Orders.AsNoTracking().AnyAsync(x => x.StoreId == Store && x.Id == order.SourceCartId &&
            x.POSShiftId == order.CreatedShiftId, ct))
            throw Error(403, "SOURCE_SCOPE_INVALID", "Nguồn giỏ/ca không khớp hồ sơ giao.");
    }
    private async Task<DeliveryOrder> Load(int id, bool tracked, CancellationToken ct)
    {
        var q = db.DeliveryOrders.Include(x => x.Lines).Where(x => x.StoreId == Store && x.Id == id);
        var order = await (tracked ? q : q.AsNoTracking()).SingleOrDefaultAsync(ct)
            ?? throw Error(404, "DELIVERY_NOT_FOUND", "Không tìm thấy đơn giao trong cửa hàng.");
        await Scope(order, ct);
        return order;
    }
    private async Task<bool> Operational(DeliveryOrder order, CancellationToken ct)
        => await db.Orders.AsNoTracking().AnyAsync(x => x.StoreId == Store && x.Id == order.SourceCartId &&
            x.Status == OrderStatus.Cancelled && x.POSShiftId == order.CreatedShiftId, ct);
    private static string Permission(string operation) => ForOperation(operation) == DeliveryCapability.Pick
        ? PermissionCodes.Delivery.Pick : PermissionCodes.Delivery.ApproveChanges;

    public async Task<DeliveryPickingDetailDto> GetAsync(int id, CancellationToken ct = default)
    {
        await Authorize(PermissionCodes.Delivery.View, ct);
        var ownsTransaction = db.Database.CurrentTransaction is null;
        await using var tx = ownsTransaction ? await db.Database.BeginTransactionAsync(ct) : null;
        await DeliverySqlLock.AcquireAsync(db, "order:" + Store + ":" + id, ct);
        await Authorize(PermissionCodes.Delivery.View, ct);
        var result = await ReadLocked(id, ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return result;
    }
    private async Task<DeliveryPickingDetailDto> ReadLocked(int id, CancellationToken ct)
    {
        var order = await Load(id, false, ct);
        var work = await db.DeliveryPickingWorks.AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == Store && x.DeliveryOrderId == id, ct);
        var lines = await db.DeliveryPickingLines.AsNoTracking().Include(x => x.Quote)
            .Where(x => x.StoreId == Store && x.DeliveryOrderId == id).OrderBy(x => x.DeliveryOrderLineId).ToListAsync(ct);
        var canPick = await permissions.HasPermissionAsync(Store, Actor, PermissionCodes.Delivery.Pick, ct);
        var canManage = await permissions.HasPermissionAsync(Store, Actor, PermissionCodes.Delivery.ApproveChanges, ct);
        var operational = await Operational(order, ct);
        var picking = order.State == DeliveryState.Picking && work?.PickerUserId == Actor && canPick;
        var managed = operational && canManage;
        var caps = new DeliveryPickingCapabilitiesDto(operational && canPick && order.State == DeliveryState.Created,
            picking && operational, picking && operational,
            managed && order.State is DeliveryState.Picking or DeliveryState.AwaitingApproval or DeliveryState.ReadyForHandover,
            managed && order.State == DeliveryState.AwaitingApproval,
            managed && order.State is DeliveryState.AwaitingApproval or DeliveryState.ReadyForHandover,
            managed && order.State is DeliveryState.Picking or DeliveryState.AwaitingApproval or DeliveryState.ReadyForHandover);
        var names = await Names(lines.Select(x => x.ReporterUserId).Append(work?.PickerUserId), ct);
        var options = canManage ? await db.UserInStores.AsNoTracking().Where(x => x.StoreId == Store && x.IsActive && x.User.IsActive)
            .OrderBy(x => x.User.FullName).ThenBy(x => x.UserId)
            .Select(x => new DeliveryPickingPickerOptionDto(x.UserId, x.User.FullName ?? x.User.UserName)).ToListAsync(ct) : [];
        return Project(order, work, lines, caps, options, names);
    }
    public async Task<IReadOnlyList<DeliveryPickingReplacementOptionDto>> ReplacementOptionsAsync(int id, string? query, int take = 25, CancellationToken ct = default)
    {
        await Authorize(PermissionCodes.Delivery.View, ct); await Authorize(PermissionCodes.Delivery.ApproveChanges, ct);
        var order = await Load(id, false, ct);
        query = query?.Trim();
        if (query?.Length > 200) throw Error(400, "QUERY_INVALID", "Từ khóa quá dài.");
        var tier = await Tier(order, ct);
        var variants = await db.ProductVariants.AsNoTracking().Include(x => x.Product).ThenInclude(x => x.BaseUnit)
            .Include(x => x.UnitConversions).ThenInclude(x => x.Unit)
            .Where(x => x.StoreId == Store && x.IsActive && x.Product.StoreId == Store && x.Product.IsActive && x.Product.IsSellable &&
                (query == null || query == "" || x.Sku.Contains(query) || (x.ProductVariantName ?? x.Product.Name).Contains(query)))
            .OrderBy(x => x.Id).Take(Math.Clamp(take, 1, 100)).ToListAsync(ct);
        var result = new List<DeliveryPickingReplacementOptionDto>();
        foreach (var variant in variants)
        {
            var conversions = variant.UnitConversions.Where(x => x.StoreId == Store && x.IsActive && !x.IsDeleted &&
                x.Unit.StoreId == Store && x.Unit.IsActive && !x.Unit.IsDeleted).OrderBy(x => x.Id).ToArray();
            if (conversions.Length == 0) result.Add(Option(variant, null, tier));
            else foreach (var conversion in conversions) result.Add(Option(variant, conversion, tier));
        }
        return result;
    }
    public Task<DeliveryPickingCommandAck> ClaimAsync(int id, DeliveryPickingEnvelope request, CancellationToken ct = default)
        => Command(id, "claim", request.ClientRequestId, request.ExpectedVersion, request, async (order, _, _, now) =>
        {
            if (!await Operational(order, ct)) throw Error(409, "SOURCE_NOT_CONVERTED", "Giỏ chưa chuyển thành đơn giao từ POS.");
            var work = new DeliveryPickingWork { StoreId = Store, DeliveryOrderId = id, PickerUserId = Actor, AssignedAtUtc = now, StartedAtUtc = now };
            db.DeliveryPickingWorks.Add(work);
            await db.SaveChangesAsync(ct);
            foreach (var quote in order.Lines.Where(x => x.SourceOrderLineId.HasValue))
                db.DeliveryPickingLines.Add(new() { StoreId = Store, DeliveryOrderId = id, DeliveryOrderLineId = quote.Id,
                    Quote = quote, PlannedQuantity = quote.OrderedQuantity, PlannedOriginalCoverage = 0 });
            order.State = DeliveryState.Picking;
        }, ct);

    public Task<DeliveryPickingCommandAck> ReportAsync(int id, DeliveryPickingReportRequest request, CancellationToken ct = default)
        => Command(id, "report", request.ClientRequestId, request.ExpectedVersion, request, (order, work, lines, now) =>
        {
            RequirePicker(work);
            var patch = request.Lines ?? throw Error(400, "LINES_REQUIRED", "Thiếu kết quả soạn.");
            UniqueIds(patch.Select(x => x?.LineId ?? 0), false);
            foreach (var item in patch)
            {
                if (item is null) throw Error(400, "LINE_INVALID", "Dòng không hợp lệ.");
                var line = Effective(lines).SingleOrDefault(x => x.DeliveryOrderLineId == item.LineId)
                    ?? throw Error(400, "LINE_INVALID", "Dòng không thuộc kế hoạch hiện tại.");
                var qty = ParseQuantity(item.PickedQuantityText);
                var reason = Reason(item.ShortageReason, false);
                RequireReported(line.PlannedQuantity, qty, reason);
                _ = ToBase(qty, line.Quote.BaseMultiplier);
                line.ReportedQuantity = qty; line.ShortageReason = reason; line.ReporterUserId = Actor;
                line.ReportedAtUtc = now; line.ReportFactKind = "picker-report";
            }
            return Task.CompletedTask;
        }, ct);

    public Task<DeliveryPickingCommandAck> SubmitAsync(int id, DeliveryPickingEnvelope request, CancellationToken ct = default)
        => Command(id, "submit", request.ClientRequestId, request.ExpectedVersion, request, (order, work, lines, now) =>
        {
            RequirePicker(work); var active = Effective(lines);
            foreach (var line in active) RequireReported(line.PlannedQuantity, line.ReportedQuantity, line.ShortageReason);
            var changed = work!.ApprovalRequired || active.Any(x => x.Quote.OriginalRootLineId.HasValue ||
                x.PlannedQuantity != x.Quote.OrderedQuantity || x.ReportedQuantity != x.PlannedQuantity);
            work.SubmittedAtUtc = now;
            if (changed) { work.ApprovalRequired = true; order.State = DeliveryState.AwaitingApproval; }
            else
            {
                foreach (var line in active) ApproveLine(line, line.ReportedQuantity!.Value, 0);
                FinishApproval(order, work, active, now);
            }
            return Task.CompletedTask;
        }, ct);

    public Task<DeliveryPickingCommandAck> PlanAsync(int id, DeliveryPickingPlanRequest request, CancellationToken ct = default)
        => Command(id, "plan", request.ClientRequestId, request.ExpectedVersion, request, async (order, work, lines, now) =>
        {
            RequireWork(work);
            var reason = Reason(request.Reason, true)!; var confirmation = Reason(request.CustomerConfirmationNote, true)!;
            var input = request.Lines ?? throw Error(400, "LINES_REQUIRED", "Thiếu kế hoạch đầy đủ.");
            var proposed = request.NewReplacements ?? throw Error(400, "LINES_REQUIRED", "Thiếu danh sách hàng thay.");
            var effective = Effective(lines);
            CompleteIds(input.Select(x => x?.LineId ?? 0), effective);
            var plans = new List<(DeliveryPickingLine Line, decimal Quantity, decimal Coverage, string? Reason)>();
            foreach (var item in input)
            {
                if (item is null) throw Error(400, "LINE_INVALID", "Dòng không hợp lệ.");
                var line = effective.Single(x => x.DeliveryOrderLineId == item.LineId);
                var qty = ParseQuantity(item.PlannedQuantityText);
                if (qty > line.Quote.OrderedQuantity) throw Error(400, "QUANTITY_EXCEEDS_SOURCE", "Kế hoạch vượt snapshot.");
                _ = ToBase(qty, line.Quote.BaseMultiplier);
                var coverage = Coverage(line.Quote, item.OriginalCoverageText, qty);
                plans.Add((line, qty, coverage, Reason(item.Reason, qty == 0) ?? reason));
            }
            var replacements = new List<(DeliveryOrderLine Quote, decimal Coverage)>();
            var tier = await Tier(order, ct);
            foreach (var item in proposed)
            {
                if (item is null) throw Error(400, "LINE_INVALID", "Hàng thay không hợp lệ.");
                var root = order.Lines.SingleOrDefault(x => x.Id == item.OriginalRootLineId && x.SourceOrderLineId.HasValue)
                    ?? throw Error(400, "ROOT_INVALID", "Hàng thay phải nối trực tiếp dòng gốc của đơn.");
                var quantity = ParseQuantity(item.PlannedQuantityText);
                var coverage = ParseQuantity(item.OriginalCoverageText);
                if (quantity <= 0 || coverage > root.OrderedQuantity) throw Error(400, "REPLACEMENT_INVALID", "Lượng hàng thay/bao phủ không hợp lệ.");
                var variant = await Variant(item.VariantId, ct);
                var conversion = ResolveConversion(variant, item.ProductUnitConversionId);
                var option = Option(variant, conversion, tier);
                var factor = decimal.Parse(option.BaseMultiplierText, System.Globalization.CultureInfo.InvariantCulture);
                _ = ToBase(quantity, factor);
                var unitPrice = decimal.Parse(option.UnitPriceText, System.Globalization.CultureInfo.InvariantCulture);
                var net = QuotedNet(quantity, unitPrice);
                replacements.Add((new DeliveryOrderLine { StoreId = Store, DeliveryOrderId = id, SourceCartId = order.SourceCartId,
                    OriginalRootLineId = root.Id, VariantId = variant.Id, ProductUnitConversionId = conversion?.Id,
                    SellingUnitId = option.SellingUnitId, BaseUnitId = option.BaseUnitId, ItemName = option.ItemName,
                    UnitName = option.UnitName, BaseUnitName = option.BaseUnitName, OrderedQuantity = quantity, BaseMultiplier = factor,
                    UnitPrice = unitPrice, Gross = net, Net = net }, coverage));
            }
            foreach (var root in order.Lines.Where(x => x.SourceOrderLineId.HasValue))
                RequireRootCoverage(root.OrderedQuantity, plans.Single(x => x.Line.Quote.Id == root.Id).Quantity,
                    plans.Where(x => x.Line.Quote.OriginalRootLineId == root.Id).Select(x => x.Coverage)
                        .Concat(replacements.Where(x => x.Quote.OriginalRootLineId == root.Id).Select(x => x.Coverage)));
            // Free budget in one transaction before inserting replacement quotes/current rows.
            // SQL bounds triggers must never observe a transient over-covered root.
            var reports = plans.ToDictionary(x => x.Line.Id, x => (x.Line.ReportedQuantity, x.Line.ReporterUserId,
                x.Line.ReportedAtUtc, x.Line.ShortageReason, x.Line.ReportFactKind));
            Invalidate(work!, lines);
            foreach (var line in effective) { line.PlannedQuantity = 0; line.PlannedOriginalCoverage = 0; ClearReport(line); }
            await db.SaveChangesAsync(ct);
            foreach (var plan in plans)
            {
                var line = plan.Line; line.PlannedQuantity = plan.Quantity; line.PlannedOriginalCoverage = plan.Coverage;
                line.IsActive = !line.Quote.OriginalRootLineId.HasValue || plan.Quantity > 0;
                var old = reports[line.Id];
                if (plan.Quantity == 0)
                {
                    line.ReportedQuantity = 0; line.ReporterUserId = Actor; line.ReportedAtUtc = now;
                    line.ShortageReason = plan.Reason; line.ReportFactKind = "plan-removal";
                }
                else if (old.ReportedQuantity.HasValue && old.ReportedQuantity <= plan.Quantity &&
                    (old.ReportedQuantity > 0 && old.ReportedQuantity == plan.Quantity || !string.IsNullOrWhiteSpace(old.ShortageReason)))
                {
                    line.ReportedQuantity = old.ReportedQuantity; line.ReporterUserId = old.ReporterUserId;
                    line.ReportedAtUtc = old.ReportedAtUtc; line.ShortageReason = old.ShortageReason; line.ReportFactKind = old.ReportFactKind;
                }
            }
            await db.SaveChangesAsync(ct);
            foreach (var replacement in replacements)
            {
                order.Lines.Add(replacement.Quote);
                await db.SaveChangesAsync(ct);
                var line = new DeliveryPickingLine { StoreId = Store, DeliveryOrderId = id, Quote = replacement.Quote,
                    DeliveryOrderLineId = replacement.Quote.Id, PlannedQuantity = replacement.Quote.OrderedQuantity,
                    PlannedOriginalCoverage = replacement.Coverage };
                db.DeliveryPickingLines.Add(line); lines.Add(line);
            }
            work!.Reason = reason; work.CustomerConfirmationNote = confirmation;
            order.State = DeliveryState.Picking;
        }, ct);

    public Task<DeliveryPickingCommandAck> ApproveAsync(int id, DeliveryPickingApproveRequest request, CancellationToken ct = default)
        => Command(id, "approve", request.ClientRequestId, request.ExpectedVersion, request, (order, work, lines, now) =>
        {
            RequireWork(work);
            work!.Reason = Reason(request.Reason, true); work.CustomerConfirmationNote = Reason(request.CustomerConfirmationNote, true);
            var input = request.Lines ?? throw Error(400, "LINES_REQUIRED", "Thiếu danh sách duyệt.");
            var active = Effective(lines); CompleteIds(input.Select(x => x?.LineId ?? 0), active);
            foreach (var item in input)
            {
                if (item is null) throw Error(400, "LINE_INVALID", "Dòng không hợp lệ.");
                var line = active.Single(x => x.DeliveryOrderLineId == item.LineId);
                RequireReported(line.PlannedQuantity, line.ReportedQuantity, line.ShortageReason);
                var quantity = ParseQuantity(item.AllowedQuantityText);
                if (quantity > line.ReportedQuantity) throw Error(400, "QUANTITY_EXCEEDS_SOURCE", "Không duyệt hàng chưa được soạn.");
                _ = ToBase(quantity, line.Quote.BaseMultiplier);
                var coverage = Coverage(line.Quote, item.ConfirmedOriginalCoverageText, quantity);
                if (coverage > line.PlannedOriginalCoverage) throw Error(400, "ROOT_COVERAGE_EXCEEDED", "Bao phủ duyệt vượt kế hoạch.");
                ApproveLine(line, quantity, coverage);
            }
            foreach (var root in active.Where(x => x.Quote.SourceOrderLineId.HasValue))
                RequireRootCoverage(root.Quote.OrderedQuantity, root.ApprovedQuantity!.Value,
                    active.Where(x => x.Quote.OriginalRootLineId == root.Quote.Id).Select(x => x.ApprovedOriginalCoverage!.Value));
            FinishApproval(order, work, active, now);
            return Task.CompletedTask;
        }, ct);

    public Task<DeliveryPickingCommandAck> ReopenAsync(int id, DeliveryPickingReopenRequest request, CancellationToken ct = default)
        => Command(id, "reopen", request.ClientRequestId, request.ExpectedVersion, request, (order, work, lines, _) =>
        {
            RequireWork(work); work!.Reason = Reason(request.Reason, true); Invalidate(work, lines);
            order.State = DeliveryState.Picking; return Task.CompletedTask;
        }, ct);
    public Task<DeliveryPickingCommandAck> ReassignAsync(int id, DeliveryPickingReassignRequest request, CancellationToken ct = default)
        => Command(id, "reassign", request.ClientRequestId, request.ExpectedVersion, request, async (_, work, _, now) =>
        {
            RequireWork(work); var reason = Reason(request.Reason, true);
            if (!await db.UserInStores.AsNoTracking().AnyAsync(x => x.StoreId == Store && x.UserId == request.PickerUserId && x.IsActive && x.User.IsActive, ct))
                throw Error(400, "PICKER_INVALID", "Nhân viên phải đang hoạt động trong cửa hàng.");
            work!.PickerUserId = request.PickerUserId; work.AssignedAtUtc = now; work.Reason = reason;
        }, ct);

    private async Task<DeliveryPickingCommandAck> Command<T>(int id, string operation, Guid key, string expectedVersion,
        T request, Func<DeliveryOrder, DeliveryPickingWork?, List<DeliveryPickingLine>, DateTime, Task> apply, CancellationToken ct)
    {
        await Authorize(PermissionCodes.Delivery.View, ct); await Authorize(Permission(operation), ct);
        if (key == Guid.Empty) throw Error(400, "REQUEST_KEY_REQUIRED", "Cần mã yêu cầu khác rỗng.");
        var version = Version(expectedVersion);
        var hash = Hash(JsonSerializer.Serialize(new { schema = "delivery-picking-v1", store = Store, actor = Actor, id, operation, version, request }, Json));
        if (db.Database.CurrentTransaction is not null) throw Error(409, "TRANSACTION_CONFLICT", "Thao tác soạn cần giao dịch riêng.");
        DetachStale(id);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            await DeliverySqlLock.AcquireAsync(db, "command:" + Store + ":" + key, ct);
            await DeliverySqlLock.AcquireAsync(db, "order:" + Store + ":" + id, ct);
            // Resolve current membership/permissions/source under the aggregate lock, never from a pretracked instance.
            await Authorize(PermissionCodes.Delivery.View, ct); await Authorize(Permission(operation), ct);
            var current = await Load(id, false, ct);
            var receipt = await db.DeliveryCommandReceipts.AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == Store && x.ClientRequestId == key, ct);
            if (receipt is not null)
            {
                if (receipt.ActorUserId != Actor || receipt.DeliveryOrderId != id || receipt.Operation != "picking-" + operation || receipt.RequestHash != hash)
                    throw Error(409, "REQUEST_KEY_CONFLICT", "Mã yêu cầu đã dùng cho nội dung/người/thao tác khác.");
                var ack = JsonSerializer.Deserialize<DeliveryPickingCommandAck>(receipt.OutcomeJson, Json)!;
                await tx.CommitAsync(ct); return ack with { Replayed = true };
            }
            if (Convert.ToBase64String(current.RowVersion) != version)
                throw Error(409, "VERSION_CONFLICT", "Đơn đã thay đổi; tải lại trước khi thao tác.");
            try { EnsureState(current.State, operation); }
            catch (DeliveryRuleException ex) { throw Error(409, ex.Code, ex.Message); }
            if (!await Operational(current, ct)) throw Error(409, "SOURCE_NOT_CONVERTED", "Giỏ chưa được chuyển giao từ POS.");
            var order = await Load(id, true, ct);
            var work = await db.DeliveryPickingWorks.SingleOrDefaultAsync(x => x.StoreId == Store && x.DeliveryOrderId == id, ct);
            var lines = await db.DeliveryPickingLines.Include(x => x.Quote)
                .Where(x => x.StoreId == Store && x.DeliveryOrderId == id).OrderBy(x => x.DeliveryOrderLineId).ToListAsync(ct);
            var now = DateTime.UtcNow;
            await apply(order, work, lines, now);
            order.Revision++;
            await db.SaveChangesAsync(ct);
            // Claim created the work/lines during apply; reload the authoritative final facts for history.
            work = await db.DeliveryPickingWorks.SingleAsync(x => x.StoreId == Store && x.DeliveryOrderId == id, ct);
            lines = await db.DeliveryPickingLines.Include(x => x.Quote).Where(x => x.StoreId == Store && x.DeliveryOrderId == id).ToListAsync(ct);
            var result = new DeliveryPickingCommandAck(key, id, operation, order.Revision, Convert.ToBase64String(order.RowVersion), Utc(now), false);
            var snapshot = JsonSerializer.Serialize(new { delivery = DeliveryFoundationService.Detail(order),
                picking = new { work.PickerUserId, work.AssignedAtUtc, work.StartedAtUtc, work.SubmittedAtUtc, work.ApprovalRequired,
                    work.ApprovedRevision, work.ApprovedByUserId, work.ApprovedAtUtc, work.ApprovedTotal, work.Reason, work.CustomerConfirmationNote },
                lines = lines.OrderBy(x => x.DeliveryOrderLineId).Select(x => new { lineId = x.DeliveryOrderLineId, x.IsActive,
                    x.PlannedQuantity, x.PlannedOriginalCoverage, x.ReportedQuantity, x.ReporterUserId, x.ReportedAtUtc,
                    x.ShortageReason, x.ReportFactKind, x.ApprovedQuantity, x.ApprovedOriginalCoverage, x.ApprovedNet }) }, Json);
            db.DeliveryRevisions.Add(new() { StoreId = Store, DeliveryOrderId = id, Revision = order.Revision, ActorUserId = Actor,
                Action = "picking-" + operation, AggregateVersion = result.AppliedVersion, SnapshotJson = snapshot, SnapshotHash = Hash(snapshot) });
            db.DeliveryCommandReceipts.Add(new() { StoreId = Store, DeliveryOrderId = id, ActorUserId = Actor, ClientRequestId = key,
                Operation = "picking-" + operation, RequestHash = hash, OutcomeJson = JsonSerializer.Serialize(result, Json) });
            var eventId = Guid.NewGuid();
            db.DeliveryOutboxMessages.Add(new() { StoreId = Store, DeliveryOrderId = id, Revision = order.Revision, EventId = eventId,
                Action = "picking-" + operation, PayloadJson = JsonSerializer.Serialize(new { eventId, storeId = Store, deliveryId = id,
                    order.Code, order.Revision, version = result.AppliedVersion, state = order.State.ToString(),
                    action = "picking-" + operation, actorUserId = Actor, recordedAtUtc = Utc(now), work.PickerUserId,
                    lines = lines.Where(x => x.IsActive).Select(x => new { lineId = x.DeliveryOrderLineId, x.Quote.ItemName,
                        x.Quote.UnitName, plannedQuantityText = FormatQuantity(x.PlannedQuantity),
                        reportedQuantityText = x.ReportedQuantity.HasValue ? FormatQuantity(x.ReportedQuantity.Value) : null }) }, Json) });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct); return result;
        }
        catch (DbUpdateConcurrencyException)
        {
            await tx.RollbackAsync(CancellationToken.None); db.ChangeTracker.Clear();
            throw Error(409, "VERSION_CONFLICT", "Đơn đã thay đổi; tải lại trước khi thao tác.");
        }
        catch { await tx.RollbackAsync(CancellationToken.None); db.ChangeTracker.Clear(); throw; }
    }
    private void DetachStale(int id)
    {
        var relevant = db.ChangeTracker.Entries().Where(x =>
            x.Entity is DeliveryOrder o && o.Id == id ||
            x.Entity is DeliveryOrderLine l && l.DeliveryOrderId == id ||
            x.Entity is DeliveryPickingWork w && w.DeliveryOrderId == id ||
            x.Entity is DeliveryPickingLine p && p.DeliveryOrderId == id).ToArray();
        if (relevant.Any(x => x.State != EntityState.Unchanged))
            throw Error(409, "TRACKED_STATE_DIRTY", "Ngữ cảnh có thay đổi chưa lưu của đơn giao.");
        // The service does not save/discard unrelated caller changes.
        if (db.ChangeTracker.Entries().Any(x => x.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
            throw Error(409, "TRACKED_STATE_DIRTY", "Cần lưu/xử lý thay đổi trước khi soạn.");
        foreach (var entry in relevant) entry.State = EntityState.Detached;
    }
    private static List<DeliveryPickingLine> Effective(List<DeliveryPickingLine> lines)
        => lines.Where(x => x.IsActive || x.Quote.SourceOrderLineId.HasValue).ToList();
    private void RequirePicker(DeliveryPickingWork? work)
    {
        RequireWork(work);
        if (work!.PickerUserId != Actor) throw Error(409, "PICKER_CONFLICT", "Đơn đang do nhân viên khác soạn.");
    }
    private static void RequireWork(DeliveryPickingWork? work)
    { if (work is null) throw Error(409, "PICKING_NOT_STARTED", "Chưa nhận việc soạn đơn."); }
    private static void UniqueIds(IEnumerable<int> ids, bool allowEmpty)
    {
        var a = ids.ToArray();
        if ((!allowEmpty && a.Length == 0) || a.Any(x => x <= 0) || a.Distinct().Count() != a.Length)
            throw Error(400, "LINE_INVALID", "Dòng hàng thiếu, trùng hoặc không hợp lệ.");
    }
    private static void CompleteIds(IEnumerable<int> ids, List<DeliveryPickingLine> effective)
    {
        var a = ids.ToArray(); UniqueIds(a, false);
        if (!a.Order().SequenceEqual(effective.Select(x => x.DeliveryOrderLineId).Order()))
            throw Error(400, "PLAN_INCOMPLETE", "Phải gửi đủ các dòng hiện tại của đơn.");
    }
    private static string? Reason(string? value, bool required)
    {
        value = value?.Trim();
        if (value?.Length > 1000 || required && string.IsNullOrEmpty(value)) throw Error(400, "REASON_REQUIRED", "Cần lý do/ghi nhận khách hợp lệ, tối đa 1000 ký tự.");
        return string.IsNullOrEmpty(value) ? null : value;
    }
    private static decimal Coverage(DeliveryOrderLine quote, string? text, decimal quantity)
    {
        if (!quote.OriginalRootLineId.HasValue)
        {
            if (text is not null) throw Error(400, "COVERAGE_INVALID", "Dòng gốc không gửi bao phủ thay thế.");
            return 0;
        }
        var coverage = ParseQuantity(text);
        if (quantity == 0 && coverage != 0) throw Error(400, "COVERAGE_INVALID", "Hàng thay bằng 0 phải có bao phủ bằng 0.");
        return coverage;
    }
    private static void ClearReport(DeliveryPickingLine line)
    { line.ReportedQuantity = null; line.ReporterUserId = null; line.ReportedAtUtc = null; line.ShortageReason = null; line.ReportFactKind = "unreported"; }
    private static void Invalidate(DeliveryPickingWork work, List<DeliveryPickingLine> lines)
    {
        work.ApprovalRequired = true; work.ApprovedRevision = null; work.ApprovedByUserId = null;
        work.ApprovedAtUtc = null; work.ApprovedTotal = null; work.SubmittedAtUtc = null;
        foreach (var line in lines) { line.ApprovedQuantity = null; line.ApprovedOriginalCoverage = null; line.ApprovedNet = null; }
    }
    private static void ApproveLine(DeliveryPickingLine line, decimal quantity, decimal coverage)
    { line.ApprovedQuantity = quantity; line.ApprovedOriginalCoverage = coverage; line.ApprovedNet = Prorate(line.Quote.Net, line.Quote.OrderedQuantity, quantity); }
    private void FinishApproval(DeliveryOrder order, DeliveryPickingWork work, List<DeliveryPickingLine> lines, DateTime now)
    {
        var total = lines.Sum(x => x.ApprovedNet ?? 0);
        DeliveryValues.Money(total, "Tổng duyệt");
        if (!lines.Any(x => x.ApprovedQuantity > 0) || total <= 0) throw Error(409, "NO_READY_GOODS", "Chưa có hàng/giá trị để bàn giao.");
        work.ApprovedRevision = order.Revision + 1; work.ApprovedByUserId = Actor;
        work.ApprovedAtUtc = now; work.ApprovedTotal = total; order.State = DeliveryState.ReadyForHandover;
    }
    private async Task<string> Tier(DeliveryOrder order, CancellationToken ct)
    {
        if (!order.CustomerId.HasValue) return "RETAIL";
        var customer = await db.Customers.AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == Store && x.Id == order.CustomerId && x.IsActive, ct)
            ?? throw Error(409, "CUSTOMER_INVALID", "Khách gắn với đơn không còn hoạt động.");
        return string.Equals(customer.PriceTier?.Trim(), "WHOLESALE", StringComparison.OrdinalIgnoreCase) ? "WHOLESALE" : "RETAIL";
    }
    private async Task<ProductVariant> Variant(int id, CancellationToken ct)
        => await db.ProductVariants.AsNoTracking().Include(x => x.Product).ThenInclude(x => x.BaseUnit)
            .Include(x => x.UnitConversions).ThenInclude(x => x.Unit).SingleOrDefaultAsync(x =>
                x.StoreId == Store && x.Id == id && x.IsActive && x.Product.StoreId == Store && x.Product.IsActive && x.Product.IsSellable, ct)
            ?? throw Error(400, "VARIANT_INVALID", "Hàng thay không hoạt động/không được bán trong cửa hàng.");
    private ProductUnitConversion? ResolveConversion(ProductVariant variant, int? id)
    {
        var conversions = variant.UnitConversions.Where(x => x.StoreId == Store && x.IsActive && !x.IsDeleted &&
            x.Unit.StoreId == Store && x.Unit.IsActive && !x.Unit.IsDeleted).OrderByDescending(x => x.IsDefaultForSale)
            .ThenByDescending(x => x.IsBaseUnit).ThenBy(x => x.SortOrder).ThenBy(x => x.Factor).ThenBy(x => x.Id).ToArray();
        if (id.HasValue) return conversions.SingleOrDefault(x => x.Id == id)
            ?? throw Error(400, "UNIT_INVALID", "Quy đổi không thuộc hàng/cửa hàng hoặc không hoạt động.");
        return conversions.FirstOrDefault();
    }
    private DeliveryPickingReplacementOptionDto Option(ProductVariant variant, ProductUnitConversion? conversion, string tier)
    {
        var product = variant.Product; var baseUnit = product.BaseUnit;
        if (baseUnit.StoreId != Store || !baseUnit.IsActive || baseUnit.IsDeleted) throw Error(400, "UNIT_INVALID", "Đơn vị cơ sở không còn hợp lệ.");
        var factor = conversion?.Factor ?? 1; _ = ToBase(0, factor);
        var price = tier == "WHOLESALE" && conversion?.WholesalePrice is > 0 ? conversion.WholesalePrice.Value
            : tier == "WHOLESALE" && conversion is null && variant.WholesalePrice is > 0 ? variant.WholesalePrice.Value
            : conversion?.Price is > 0 ? conversion.Price.Value : variant.Price is > 0 ? variant.Price.Value : product.BasePrice;
        DeliveryValues.Money(price, "Đơn giá", false);
        var name = variant.ProductVariantName ?? product.Name;
        if (name.Length > 200 || (conversion?.Unit.Name ?? baseUnit.Name).Length > 100 || baseUnit.Name.Length > 100)
            throw Error(400, "QUOTE_INVALID", "Tên hàng/đơn vị vượt giới hạn snapshot.");
        return new(variant.Id, conversion?.Id, conversion?.UnitId ?? product.BaseUnitId, product.BaseUnitId, name,
            conversion?.Unit.Name ?? baseUnit.Name, baseUnit.Name, FormatMultiplier(factor), FormatPrice(price), tier);
    }
    private async Task<Dictionary<int, string>> Names(IEnumerable<int?> ids, CancellationToken ct)
    {
        var keys = ids.Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToArray();
        return await db.UserInStores.IgnoreQueryFilters().AsNoTracking().Where(x => x.StoreId == Store && keys.Contains(x.UserId))
            .Select(x => new { x.UserId, Name = x.User.FullName ?? x.User.UserName }).ToDictionaryAsync(x => x.UserId, x => x.Name, ct);
    }
    private DeliveryPickingDetailDto Project(DeliveryOrder order, DeliveryPickingWork? work, List<DeliveryPickingLine> lines,
        DeliveryPickingCapabilitiesDto caps, IReadOnlyList<DeliveryPickingPickerOptionDto> options, Dictionary<int, string> names)
    {
        string? Name(int? id) => id.HasValue && names.TryGetValue(id.Value, out var name) ? name : null;
        var map = lines.ToDictionary(x => x.DeliveryOrderLineId);
        var output = order.Lines.OrderBy(x => x.Id).Select(quote =>
        {
            map.TryGetValue(quote.Id, out var line);
            var qty = line?.PlannedQuantity ?? quote.OrderedQuantity;
            return new DeliveryPickingLineDto(quote.Id, quote.OriginalRootLineId ?? quote.Id, quote.OriginalRootLineId.HasValue,
                quote.SourceOrderLineId, quote.VariantId, quote.ProductUnitConversionId, quote.SellingUnitId, quote.BaseUnitId,
                quote.ItemName, quote.UnitName, quote.BaseUnitName, FormatQuantity(quote.OrderedQuantity), FormatMultiplier(quote.BaseMultiplier),
                FormatPrice(quote.UnitPrice), FormatMoney(quote.Gross), FormatMoney(quote.LineDiscount), FormatMoney(quote.AllocatedOrderDiscount),
                FormatMoney(quote.Net), line?.IsActive ?? true, FormatQuantity(qty), FormatQuantity(ToBase(qty, quote.BaseMultiplier)),
                FormatQuantity(line?.PlannedOriginalCoverage ?? 0), FormatMoney(Prorate(quote.Net, quote.OrderedQuantity, qty)),
                line?.ReportedQuantity is decimal reported ? FormatQuantity(reported) : null,
                line?.ReportedQuantity is decimal reportedBase ? FormatQuantity(ToBase(reportedBase, quote.BaseMultiplier)) : null,
                line?.ReporterUserId, Name(line?.ReporterUserId), Utc(line?.ReportedAtUtc), line?.ShortageReason, line?.ReportFactKind ?? "unreported",
                line?.ApprovedQuantity is decimal approved ? FormatQuantity(approved) : null,
                line?.ApprovedQuantity is decimal approvedBase ? FormatQuantity(ToBase(approvedBase, quote.BaseMultiplier)) : null,
                line?.ApprovedOriginalCoverage is decimal coverage ? FormatQuantity(coverage) : null,
                line?.ApprovedNet is decimal net ? FormatMoney(net) : null,
                line?.ApprovedQuantity is not null ? work?.ApprovedRevision : null);
        }).ToArray();
        var roots = order.Lines.Where(x => x.SourceOrderLineId.HasValue).OrderBy(x => x.Id).Select(root =>
        {
            var original = output.Single(x => x.LineId == root.Id);
            var replacements = lines.Where(x => x.Quote.OriginalRootLineId == root.Id && x.IsActive).ToArray();
            var retained = map.TryGetValue(root.Id, out var line) ? line.PlannedQuantity : root.OrderedQuantity;
            var draftCoverage = replacements.Sum(x => x.PlannedOriginalCoverage);
            var valid = work?.ApprovedRevision is not null;
            var approvedRetained = valid ? line?.ApprovedQuantity ?? 0 : 0;
            var approvedCoverage = valid ? replacements.Sum(x => x.ApprovedOriginalCoverage ?? 0) : 0;
            return new DeliveryPickingRootCoverageDto(root.Id, original.OrderedQuantityText, FormatQuantity(retained), FormatQuantity(draftCoverage),
                FormatQuantity(root.OrderedQuantity - retained - draftCoverage), valid ? FormatQuantity(approvedRetained) : null,
                valid ? FormatQuantity(approvedCoverage) : null, valid ? FormatQuantity(root.OrderedQuantity - approvedRetained - approvedCoverage) : null);
        }).ToArray();
        var draft = order.Lines.Sum(x => map.TryGetValue(x.Id, out var line) && !line.IsActive ? 0 : Prorate(x.Net, x.OrderedQuantity,
            map.TryGetValue(x.Id, out line) ? line.PlannedQuantity : x.OrderedQuantity));
        return new(DeliveryFoundationService.Detail(order), DateTimeOffset.UtcNow, work?.PickerUserId, Name(work?.PickerUserId),
            Utc(work?.AssignedAtUtc), Utc(work?.StartedAtUtc), Utc(work?.SubmittedAtUtc), work?.ApprovalRequired ?? false,
            work?.ApprovedRevision, work?.ApprovedByUserId, Utc(work?.ApprovedAtUtc), FormatMoney(order.QuotedTotal), FormatMoney(draft),
            work?.ApprovedTotal is decimal approvedTotal ? FormatMoney(approvedTotal) : null, output, roots, caps, options);
    }
    private static string Version(string? value)
    {
        try { var b = Convert.FromBase64String(value ?? ""); if (b.Length == 8) return Convert.ToBase64String(b); }
        catch (FormatException) { }
        throw Error(400, "VERSION_INVALID", "Phiên bản phải là rowversion hợp lệ.");
    }
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
