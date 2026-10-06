using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.Delivery;
using GaoApp.Application.Interfaces.Services.Delivery;
using GaoApp.Application.Interfaces.Services.Security;
using GaoApp.Domain.Delivery;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GaoApp.Infrastructure.Services.Delivery;

public sealed class DeliveryFoundationService(AppDbContext db, ICurrentUser user,
    ICurrentStorePermissionService permissions) : IDeliveryFoundationService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private int Store => db.CurrentStoreId ?? throw Error(403, "STORE_REQUIRED", "Cần thao tác trong cửa hàng.");
    private int Actor => user.UserId ?? throw Error(403, "ACTOR_REQUIRED", "Cần đăng nhập.");
    private static DeliveryFoundationException Error(int status, string code, string message) => new(status, code, message);
    private async Task Authorize(string permission, CancellationToken ct)
    {
        if (!user.IsAuthenticated || !await db.UserInStores.AnyAsync(x =>
            x.StoreId == Store && x.UserId == Actor && x.IsActive && x.User.IsActive, ct) ||
            !await permissions.HasPermissionAsync(Store, Actor, permission, ct))
            throw Error(403, "DELIVERY_FORBIDDEN", "Không có quyền giao hàng trong cửa hàng.");
    }
    private async Task Scope(DeliveryOrder order, CancellationToken ct)
    {
        if (order.StoreId != Store || !await db.Warehouses.AnyAsync(x =>
            x.StoreId == Store && x.Id == order.SourceWarehouseId && x.IsActive && x.LegalEntityId == order.SourceLegalEntityId, ct) ||
            !await db.LegalEntities.AnyAsync(x => x.StoreId == Store && x.Id == order.SourceLegalEntityId && x.IsActive, ct))
            throw Error(403, "SOURCE_SCOPE_INVALID", "Kho/chủ thể nguồn không còn hợp lệ trong cửa hàng.");
    }
    private async Task<DeliveryOrder> Load(int id, bool tracked, CancellationToken ct)
    {
        var query = db.DeliveryOrders.Include(x => x.Lines).Where(x => x.StoreId == Store && x.Id == id);
        var order = await (tracked ? query : query.AsNoTracking()).SingleOrDefaultAsync(ct)
            ?? throw Error(404, "DELIVERY_NOT_FOUND", "Không tìm thấy đơn giao trong cửa hàng.");
        await Scope(order, ct);
        return order;
    }
    public async Task<IReadOnlyList<DeliverySummaryDto>> ListAsync(int take, CancellationToken ct = default)
    {
        await Authorize(PermissionCodes.Delivery.View, ct);
        return await db.DeliveryOrders.AsNoTracking().Where(x => x.StoreId == Store &&
            db.Warehouses.Any(w => w.StoreId == Store && w.Id == x.SourceWarehouseId && w.IsActive && w.LegalEntityId == x.SourceLegalEntityId) &&
            db.LegalEntities.Any(l => l.StoreId == Store && l.Id == x.SourceLegalEntityId && l.IsActive))
            .OrderByDescending(x => x.Id).Take(Math.Clamp(take, 1, 100))
            .Select(x => new DeliverySummaryDto(x.Id, x.Code, x.State.ToString(), x.Revision, x.RecipientName, x.QuotedTotal, x.CreatedAtUtc))
            .ToListAsync(ct);
    }
    public async Task<DeliveryDetailDto> GetAsync(int id, CancellationToken ct = default)
    {
        await Authorize(PermissionCodes.Delivery.View, ct);
        return Detail(await Load(id, false, ct));
    }
    public async Task<DeliveryDetailDto> LookupAsync(string tokenOrCode, CancellationToken ct = default)
    {
        await Authorize(PermissionCodes.Delivery.View, ct);
        var value = tokenOrCode?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(value) || value.Length > 64) throw Error(400, "LOOKUP_INVALID", "Mã tra cứu không hợp lệ.");
        var id = await db.DeliveryOrders.Where(x => x.StoreId == Store && (x.LookupToken == value || x.Code == value))
            .Select(x => (int?)x.Id).SingleOrDefaultAsync(ct);
        if (!id.HasValue) throw Error(404, "DELIVERY_NOT_FOUND", "Không tìm thấy đơn giao trong cửa hàng.");
        return Detail(await Load(id.Value, false, ct));
    }
    public async Task<IReadOnlyList<DeliveryHistoryDto>> HistoryAsync(int id, CancellationToken ct = default)
    {
        await Authorize(PermissionCodes.Delivery.View, ct); await Load(id, false, ct);
        return await db.DeliveryRevisions.AsNoTracking().Where(x => x.StoreId == Store && x.DeliveryOrderId == id)
            .OrderBy(x => x.Revision).Select(x => new DeliveryHistoryDto(x.Revision, x.ActorUserId, x.Action,
                x.CreatedAtUtc, x.AggregateVersion, x.SnapshotJson, x.SnapshotHash)).ToListAsync(ct);
    }
    public async Task<DeliveryDetailDto> UpdateRecipientAsync(int id, DeliveryRecipientRequest request, CancellationToken ct = default)
    {
        await Authorize(PermissionCodes.Delivery.View, ct); await Authorize(PermissionCodes.Delivery.Create, ct);
        var input = Normalize(request.RecipientName, request.RecipientPhone, request.RecipientAddress, request.Note);
        var version = Version(request.ExpectedVersion);
        var hash = Hash(JsonSerializer.Serialize(new { operation = "recipient", store = Store, actor = Actor, id, version, input }, Json));
        RequireKey(request.ClientRequestId);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            await DeliverySqlLock.AcquireAsync(db, "command:" + Store + ":" + request.ClientRequestId, ct);
            // Authorization and current source scope precede replay; state/version checks follow it.
            await Load(id, false, ct);
            var replay = await Replay(request.ClientRequestId, hash, ct);
            if (replay is not null) { await tx.CommitAsync(ct); return replay; }
            await DeliverySqlLock.AcquireAsync(db, "order:" + Store + ":" + id, ct);
            var order = await Load(id, true, ct);
            if (Convert.ToBase64String(order.RowVersion) != version)
                throw Error(409, "VERSION_CONFLICT", "Đơn đã thay đổi; tải lại trước khi sửa.");
            if (order.State != DeliveryState.Created) throw Error(409, "STATE_CONFLICT", "Chỉ sửa người nhận trước khi bắt đầu soạn.");
            order.RecipientName = input.Name; order.RecipientPhone = input.Phone;
            order.RecipientAddress = input.Address; order.Note = input.Note; order.Revision++;
            await db.SaveChangesAsync(ct);
            var result = await Record(order, request.ClientRequestId, "recipient", hash, ct);
            await tx.CommitAsync(ct);
            return result;
        }
        catch (DbUpdateConcurrencyException)
        {
            await tx.RollbackAsync(CancellationToken.None); db.ChangeTracker.Clear();
            throw Error(409, "VERSION_CONFLICT", "Đơn đã thay đổi; tải lại trước khi sửa.");
        }
        catch { await tx.RollbackAsync(CancellationToken.None); db.ChangeTracker.Clear(); throw; }
    }

    public async Task<DeliveryDetailDto> CreateInTransactionAsync(DeliveryFoundationCreate request, CancellationToken ct = default)
    {
        if (db.Database.CurrentTransaction is null) throw Error(409, "TRANSACTION_REQUIRED", "Tạo hồ sơ phải nằm trong giao dịch chuyển giỏ của server.");
        await Authorize(PermissionCodes.Delivery.View, ct); await Authorize(PermissionCodes.Delivery.Create, ct);
        RequireKey(request.ClientRequestId);
        var input = Normalize(request.RecipientName, request.RecipientPhone, request.RecipientAddress, request.Note);
        var hash = Hash(JsonSerializer.Serialize(new { operation = "foundation-create", store = Store, actor = Actor, request.SourceCartId, input }, Json));
        await DeliverySqlLock.AcquireAsync(db, "command:" + Store + ":" + request.ClientRequestId, ct);
        var replay = await Replay(request.ClientRequestId, hash, ct);
        if (replay is not null) { await Load(replay.Id, false, ct); return replay; }
        await DeliverySqlLock.AcquireAsync(db, "source:" + Store + ":" + request.SourceCartId, ct);
        if (await db.DeliveryOrders.AnyAsync(x => x.StoreId == Store && x.SourceCartId == request.SourceCartId, ct))
            throw Error(409, "SOURCE_ALREADY_USED", "Giỏ đã có hồ sơ giao hàng.");
        var cart = await db.Orders.Include(x => x.Lines).SingleOrDefaultAsync(x => x.StoreId == Store && x.Id == request.SourceCartId, ct)
            ?? throw Error(404, "SOURCE_NOT_FOUND", "Không tìm thấy giỏ nguồn.");
        var shift = await db.POSShifts.SingleOrDefaultAsync(x => x.StoreId == Store && x.Id == cart.POSShiftId, ct);
        if (shift is null || shift.OpenedByUserId != Actor || shift.TerminalId != user.TerminalId ||
            shift.Status != POSShiftStatus.Open ||
            !await db.POSTerminals.AnyAsync(x => x.StoreId == Store && x.Id == shift.TerminalId && x.IsActive, ct))
            throw Error(403, "ORIGIN_FORBIDDEN", "Ca/quầy nguồn không thuộc người thao tác hoặc đã đóng.");
        if (cart.Status != OrderStatus.Draft || cart.PaidTotal != 0 || cart.DepositAmount != 0 ||
            await db.OrderPayments.AnyAsync(x => x.StoreId == Store && x.OrderId == cart.Id, ct))
            throw Error(409, "SOURCE_NOT_DRAFT", "Chỉ chụp giỏ nháp chưa nhận tiền.");
        var warehouse = await db.Warehouses.SingleOrDefaultAsync(x => x.StoreId == Store && x.Id == shift.WarehouseId, ct)
            ?? throw Error(403, "SOURCE_SCOPE_INVALID", "Kho nguồn không hợp lệ.");
        var approved = DeliveryPricingPolicy.Approve(cart.Lines.Select(x => new DeliveryPriceLine(x.Id, x.Quantity, x.UnitPrice, x.LineDiscount)).ToArray(),
            cart.OrderDiscount, cart.VoucherDiscountTotal != 0 || cart.PromotionDiscountTotal != 0 || cart.ComboDiscountTotal != 0 ||
            cart.Lines.Any(x => x.PromotionId.HasValue || x.ComboPromotionId.HasValue));
        if (approved.Sum(x => x.Net) != cart.GrandTotal) throw Error(409, "QUOTE_MISMATCH", "Tổng snapshot khác giỏ nguồn.");
        var now = DateTime.UtcNow;
        var order = new DeliveryOrder { StoreId = Store, Code = "GH-" + now.AddHours(7).ToString("yyyyMMdd") + "-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(8)),
            LookupToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)), SourceCartId = cart.Id,
            SourceWarehouseId = warehouse.Id, SourceLegalEntityId = warehouse.LegalEntityId,
            CreatedTerminalId = shift.TerminalId, CreatedShiftId = shift.Id, CreatedByUserId = Actor, CustomerId = cart.CustomerId,
            RecipientName = input.Name, RecipientPhone = input.Phone, RecipientAddress = input.Address, Note = input.Note,
            QuotedTotal = approved.Sum(x => x.Net) };
        await Scope(order, ct);
        foreach (var line in cart.Lines)
        {
            if (line.StoreId != Store || !await db.ProductVariants.AnyAsync(x => x.StoreId == Store && x.Id == line.VariantId, ct))
                throw Error(403, "LINE_SCOPE_INVALID", "Dòng hàng không thuộc cửa hàng.");
            if (line.Multiplier <= 0 || line.Multiplier >= 1000000000000m || decimal.Round(line.Multiplier, 6) != line.Multiplier)
                throw Error(400, "MULTIPLIER_INVALID", "Quy đổi phải dương và tối đa 6 số lẻ.");
            DeliveryValues.Quantity(line.Quantity * line.Multiplier, "Số lượng cơ sở");
            var price = approved.Single(x => x.LineId == line.Id);
            order.Lines.Add(new DeliveryOrderLine { StoreId = Store, SourceCartId = cart.Id, SourceOrderLineId = line.Id,
                VariantId = line.VariantId, ItemName = line.ItemName, UnitName = line.SellingUnitName ?? line.UnitName ?? "",
                BaseUnitName = line.BaseUnitName ?? line.UnitName ?? "", OrderedQuantity = line.Quantity, BaseMultiplier = line.Multiplier,
                UnitPrice = line.UnitPrice, Gross = price.Gross, LineDiscount = price.LineDiscount,
                AllocatedOrderDiscount = price.AllocatedOrderDiscount, Net = price.Net });
        }
        db.DeliveryOrders.Add(order); await db.SaveChangesAsync(ct);
        return await Record(order, request.ClientRequestId, "foundation-create", hash, ct);
    }
    private async Task<DeliveryDetailDto?> Replay(Guid key, string hash, CancellationToken ct)
    {
        var receipt = await db.DeliveryCommandReceipts.AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == Store && x.ClientRequestId == key, ct);
        if (receipt is null) return null;
        if (receipt.ActorUserId != Actor || receipt.RequestHash != hash)
            throw Error(409, "REQUEST_KEY_CONFLICT", "Mã yêu cầu đã dùng cho nội dung/người thao tác khác.");
        return JsonSerializer.Deserialize<DeliveryDetailDto>(receipt.OutcomeJson, Json)!;
    }
    private async Task<DeliveryDetailDto> Record(DeliveryOrder order, Guid key, string operation, string hash, CancellationToken ct)
    {
        var result = Detail(order);
        var snapshot = JsonSerializer.Serialize(result, Json);
        db.DeliveryRevisions.Add(new() { StoreId = Store, DeliveryOrderId = order.Id, Revision = order.Revision,
            ActorUserId = Actor, Action = operation, AggregateVersion = result.Version, SnapshotJson = snapshot, SnapshotHash = Hash(snapshot) });
        db.DeliveryCommandReceipts.Add(new() { StoreId = Store, DeliveryOrderId = order.Id, ClientRequestId = key,
            ActorUserId = Actor, Operation = operation, RequestHash = hash, OutcomeJson = snapshot });
        var eventId = Guid.NewGuid();
        db.DeliveryOutboxMessages.Add(new() { StoreId = Store, DeliveryOrderId = order.Id, Revision = order.Revision,
            EventId = eventId, Action = operation, PayloadJson = JsonSerializer.Serialize(new { eventId, storeId = Store,
                deliveryId = order.Id, order.Code, order.Revision, version = result.Version, state = result.State,
                action = operation, actorUserId = Actor, recordedAtUtc = DateTime.UtcNow }, Json) });
        await db.SaveChangesAsync(ct);
        return result;
    }
    internal static DeliveryDetailDto Detail(DeliveryOrder x) => new(x.Id, x.Code, x.LookupToken, x.State.ToString(), x.Revision,
        Convert.ToBase64String(x.RowVersion), x.SourceWarehouseId, x.SourceLegalEntityId, x.SourceCartId, x.CreatedTerminalId,
        x.CreatedShiftId, x.CreatedByUserId, x.CustomerId, x.RecipientName, x.RecipientPhone, x.RecipientAddress, x.Note,
        x.QuotedTotal, x.CreatedAtUtc, x.Lines.OrderBy(l => l.Id).Select(l => new DeliveryLineDto(l.Id, l.SourceOrderLineId,
            l.ItemName, l.UnitName, l.BaseUnitName, l.OrderedQuantity, l.BaseMultiplier, l.UnitPrice, l.Gross,
            l.LineDiscount, l.AllocatedOrderDiscount, l.Net)).ToArray());
    private sealed record Recipient(string Name, string Phone, string Address, string? Note);
    private static Recipient Normalize(string? name, string? phone, string? address, string? note)
    {
        name = name?.Trim(); phone = phone?.Trim(); address = address?.Trim(); note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > 200 || string.IsNullOrEmpty(phone) || phone.Length > 30 ||
            string.IsNullOrEmpty(address) || address.Length > 1200 || note?.Length > 1000)
            throw Error(400, "RECIPIENT_INVALID", "Nhập người nhận, điện thoại, địa chỉ hợp lệ.");
        return new(name, phone, address, note);
    }
    private static string Version(string? value)
    {
        try { var bytes = Convert.FromBase64String(value ?? ""); if (bytes.Length == 8) return Convert.ToBase64String(bytes); }
        catch (FormatException) { }
        throw Error(400, "VERSION_INVALID", "Phiên bản đơn không hợp lệ.");
    }
    private static void RequireKey(Guid key)
    {
        if (key == Guid.Empty) throw Error(400, "REQUEST_KEY_REQUIRED", "Cần mã yêu cầu khác rỗng.");
    }
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

internal static class DeliverySqlLock
{
    internal static async Task AcquireAsync(AppDbContext db, string resource, CancellationToken ct)
    {
        var tx = db.Database.CurrentTransaction ?? throw new InvalidOperationException("SQL lock requires transaction.");
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.Transaction = tx.GetDbTransaction();
        command.CommandText = "DECLARE @result int; EXEC @result = sys.sp_getapplock @Resource=@resource, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000; SELECT @result;";
        var parameter = command.CreateParameter(); parameter.ParameterName = "@resource"; parameter.Value = "GaoApp:Delivery:" + resource; command.Parameters.Add(parameter);
        var result = Convert.ToInt32(await command.ExecuteScalarAsync(ct));
        if (result < 0) throw new DeliveryFoundationException(409, "DELIVERY_BUSY", "Đơn đang được xử lý; thử lại cùng mã yêu cầu.");
    }
}
