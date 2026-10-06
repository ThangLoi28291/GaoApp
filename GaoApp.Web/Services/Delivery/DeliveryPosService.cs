using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GaoApp.Application.Common.Interfaces;
using GaoApp.Application.Common.Security;
using GaoApp.Application.DTOs.Delivery;
using GaoApp.Application.Interfaces.Services.Delivery;
using GaoApp.Application.Interfaces.Services.Inventory;
using GaoApp.Application.Interfaces.Services.Orders;
using GaoApp.Application.Interfaces.Services.Security;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using GaoApp.Web.Services.Acb;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Web.Services.Delivery;

public sealed class DeliveryPosService(AppDbContext db, ICurrentUser user, ICurrentStorePermissionService permissions,
    IDeliveryFoundationService foundation, IPOSService pos, IInventoryReservationService reservations,
    IAcbOrderLockProvider bankLocks)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private int Store => db.CurrentStoreId ?? throw Error(403, "STORE_REQUIRED", "Cần vào cửa hàng.");
    private int Actor => user.UserId ?? throw Error(403, "ACTOR_REQUIRED", "Cần đăng nhập.");
    private int Terminal => user.TerminalId ?? throw Error(403, "TERMINAL_REQUIRED", "Cần chọn quầy POS.");
    private static DeliveryFoundationException Error(int status, string code, string message) => new(status, code, message);

    private async Task Authorize(CancellationToken ct)
    {
        if (!user.IsAuthenticated ||
            !await db.UserInStores.AnyAsync(x => x.StoreId == Store && x.UserId == Actor && x.IsActive && x.User.IsActive, ct) ||
            !await db.POSTerminals.AnyAsync(x => x.StoreId == Store && x.Id == Terminal && x.IsActive, ct))
            throw Error(403, "DELIVERY_FORBIDDEN", "Người thao tác/quầy không hợp lệ.");
        foreach (var code in new[] { PermissionCodes.Delivery.View, PermissionCodes.Delivery.Create,
            PermissionCodes.Pos.Order.View, PermissionCodes.Pos.Order.Create })
            if (!await permissions.HasPermissionAsync(Store, Actor, code, ct))
                throw Error(403, "DELIVERY_FORBIDDEN", "Không có quyền tạo đơn giao từ POS.");
    }

    private async Task<POSShift> OpenShift(CancellationToken ct)
        => await db.POSShifts.SingleOrDefaultAsync(x => x.StoreId == Store && x.TerminalId == Terminal &&
            x.OpenedByUserId == Actor && x.Status == POSShiftStatus.Open, ct)
            ?? throw Error(409, "SHIFT_NOT_OPEN", "Quầy cần ca đang mở của bạn.");

    private async Task Scope(POSShift shift, CancellationToken ct)
    {
        if (!await db.Warehouses.AnyAsync(x => x.StoreId == Store && x.Id == shift.WarehouseId && x.IsActive &&
            db.LegalEntities.Any(l => l.StoreId == Store && l.Id == x.LegalEntityId && l.IsActive), ct))
            throw Error(403, "SOURCE_SCOPE_INVALID", "Kho/chủ thể xuất của quầy không còn hoạt động.");
    }

    public async Task<DeliveryCartSnapshot> SnapshotAsync(CancellationToken ct)
    {
        await Authorize(ct); var shift = await OpenShift(ct); await Scope(shift, ct);
        var cart = await db.Orders.AsNoTracking().Include(x => x.Lines).Include(x => x.Customer)
            .SingleOrDefaultAsync(x => x.StoreId == Store && x.Id == shift.CurrentOrderId, ct)
            ?? throw Error(409, "CART_REQUIRED", "Chọn giỏ có hàng trước khi tạo đơn giao.");
        await Guard(cart, shift, ct);
        var warehouseName = await db.Warehouses.Where(x => x.Id == shift.WarehouseId && x.StoreId == Store).Select(x => x.Name).SingleAsync(ct);
        return new(cart.Id, Convert.ToBase64String(cart.RowVersion), Fingerprint(cart, shift), shift.Id, warehouseName,
            cart.GrandTotal, cart.Customer?.Name, cart.Customer?.Phone, cart.Customer?.Address,
            cart.Lines.OrderBy(x => x.Id).Select(x => new DeliveryCartLine(x.Id, x.ItemName,
                x.SellingUnitName ?? x.UnitName ?? "", x.Quantity, x.LineTotal)).ToArray());
    }

    public async Task<DeliveryPosResult> CreateAsync(DeliveryPosCreateRequest request, CancellationToken ct)
    {
        await Authorize(ct);
        request = Normalize(request);
        var hash = Hash(JsonSerializer.Serialize(new { operation = "delivery-from-current-cart", store = Store, actor = Actor, terminal = Terminal, request }, Json));
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            // Keep the terminal-before-bank lock order used by the existing POS operation filter.
            await Lock("pos-operation:" + Store + ":" + request.ClientRequestId.ToString("N"), ct);
            await Lock("pos-terminal:" + Store + ":" + Terminal, ct);
            var previous = await db.Set<PosOperationReceipt>().AsNoTracking().SingleOrDefaultAsync(x =>
                x.StoreId == Store && x.OperationId == request.ClientRequestId, ct);
            if (previous is not null)
            {
                if (previous.UserId != Actor || previous.TerminalId != Terminal || previous.RequestHash != hash)
                    throw Error(409, "REQUEST_KEY_CONFLICT", "Mã yêu cầu đã dùng cho nội dung hoặc người/quầy khác.");
                var replay = JsonSerializer.Deserialize<DeliveryPosResult>(previous.ResponseJson, Json)!;
                await foundation.GetAsync(replay.Delivery.Id, ct); // Fresh source scope before stored response.
                await tx.CommitAsync(ct); return replay;
            }
            await using var bank = await bankLocks.AcquireAsync(db, request.SourceCartId, ct);
            // Protect against ordinary POS writes that have no offline/operation headers.
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT Id FROM POSShifts WITH (UPDLOCK,HOLDLOCK) WHERE StoreId={Store} AND TerminalId={Terminal} AND Status={(int)POSShiftStatus.Open}; SELECT Id FROM Orders WITH (UPDLOCK,HOLDLOCK) WHERE StoreId={Store} AND Id={request.SourceCartId}; SELECT Id FROM OrderLines WITH (UPDLOCK,HOLDLOCK) WHERE StoreId={Store} AND OrderId={request.SourceCartId};", ct);
            var shift = await OpenShift(ct); await Scope(shift, ct);
            var cart = await db.Orders.Include(x => x.Lines).SingleOrDefaultAsync(x => x.StoreId == Store && x.Id == request.SourceCartId, ct)
                ?? throw Error(404, "SOURCE_NOT_FOUND", "Không tìm thấy giỏ nguồn trong cửa hàng.");
            await Guard(cart, shift, ct);
            if (Convert.ToBase64String(cart.RowVersion) != request.ExpectedVersion || Fingerprint(cart, shift) != request.ExpectedFingerprint)
                throw Error(409, "CART_CHANGED", "Giỏ đã đổi hàng/giá/ca; tải lại nội dung trước khi tạo đơn giao.");

            var delivery = await foundation.CreateInTransactionAsync(new(request.ClientRequestId, cart.Id,
                request.RecipientName, request.RecipientPhone, request.RecipientAddress, request.Note), ct);
            await reservations.ReleaseForOrderAsync(cart, "Chuyển sang đơn giao " + delivery.Code, ct);
            cart.Status = OrderStatus.Cancelled;
            var reason = "[DELIVERY] Chuyển sang " + delivery.Code;
            cart.Note = string.IsNullOrWhiteSpace(cart.Note) ? reason : cart.Note + Environment.NewLine + reason;
            shift.CurrentOrderId = null;
            await db.SaveChangesAsync(ct);
            var nextId = await pos.CreateDraftAsync(ct: ct); // Existing mode capture and empty-cart reuse, same DbContext/transaction.
            db.POSAuditLogs.Add(new() { StoreId = Store, Action = "DELIVERY_CREATED", OrderId = cart.Id, UserId = Actor,
                Note = "Chuyển giỏ sang đơn giao " + delivery.Code, MetadataJson = JsonSerializer.Serialize(new {
                    deliveryId = delivery.Id, delivery.Code, sourceCartId = cart.Id, shiftId = shift.Id,
                    terminalId = Terminal, nextCartId = nextId, sourceWarehouseId = delivery.SourceWarehouseId,
                    sourceLegalEntityId = delivery.SourceLegalEntityId }, Json) });
            var result = new DeliveryPosResult(delivery, nextId, "/admin/deliveries/" + delivery.Id + "/bill",
                "/admin/deliveries?key=" + delivery.LookupToken);
            db.Set<PosOperationReceipt>().Add(new() { StoreId = Store, OperationId = request.ClientRequestId,
                TerminalId = Terminal, UserId = Actor, RequestHash = hash, ResponseJson = JsonSerializer.Serialize(result, Json),
                WasOffline = false, OccurredAtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct); return result;
        }
        catch (DbUpdateConcurrencyException)
        {
            await tx.RollbackAsync(CancellationToken.None); db.ChangeTracker.Clear();
            throw Error(409, "CART_CHANGED", "Giỏ hoặc ca vừa thay đổi; tải lại POS.");
        }
        catch { await tx.RollbackAsync(CancellationToken.None); db.ChangeTracker.Clear(); throw; }
    }

    private async Task Guard(Order cart, POSShift shift, CancellationToken ct)
    {
        if (cart.POSShiftId != shift.Id || shift.CurrentOrderId != cart.Id || cart.Status != OrderStatus.Draft)
            throw Error(409, "NOT_CURRENT_CART", "Chỉ chuyển giỏ nháp đang phục vụ ở quầy của bạn.");
        if (cart.Lines.Count == 0) throw Error(400, "CART_EMPTY", "Giỏ chưa có hàng.");
        if (cart.PaidTotal != 0 || cart.PaymentStatus != PaymentStatus.Unpaid || cart.DepositAmount != 0 || cart.CustomerDepositId.HasValue ||
            await db.OrderPayments.AnyAsync(x => x.StoreId == Store && x.OrderId == cart.Id, ct))
            throw Error(409, "PAYMENT_EXISTS", "Giỏ đã có thanh toán hoặc tiền cọc; xử lý khoản đó trước khi tạo đơn giao.");
        if (await db.Set<PosPaymentQrRequest>().AnyAsync(x => x.StoreId == Store && x.OrderId == cart.Id && x.Status != PosPaymentQrStatus.Cancelled, ct) ||
            await db.Set<AcbQrSession>().AnyAsync(x => x.StoreId == Store && x.OrderId == cart.Id && x.Status != AcbSessionStatus.Cancelled, ct))
            throw Error(409, "BANK_QR_EXISTS", "Giỏ có QR ngân hàng chưa hủy/đối soát; kiểm tra QR trước khi chuyển sang giao hàng.");
        if (cart.VoucherDiscountTotal != 0 || cart.PromotionDiscountTotal != 0 || cart.ComboDiscountTotal != 0 ||
            cart.ComboPromotionId.HasValue || cart.Lines.Any(x => x.IsPromotionGift || x.PromotionId.HasValue || x.ComboPromotionId.HasValue ||
                x.PromotionDiscount != 0 || x.ComboAllocatedDiscount != 0) ||
            await db.Set<OrderRewardVoucher>().AnyAsync(x => x.StoreId == Store && x.OrderId == cart.Id, ct))
            throw Error(409, "PRICE_FEATURE_UNSUPPORTED", "Đơn giao hiện hỗ trợ giá và giảm giá thông thường; bỏ combo/quà tặng/voucher trước khi chuyển.");
        if (cart.HasMultipleLegalEntities || cart.LegalEntityCount != 0 ||
            await db.OrderLegalEntityAllocations.AnyAsync(x => x.StoreId == Store && x.OrderId == cart.Id, ct))
            throw Error(409, "SOURCE_ALREADY_ALLOCATED", "Giỏ đã có phân bổ xuất hàng; cần xử lý trước khi tạo đơn giao.");
    }
    private static DeliveryPosCreateRequest Normalize(DeliveryPosCreateRequest request)
    {
        var name = request.RecipientName?.Trim(); var phone = request.RecipientPhone?.Trim(); var address = request.RecipientAddress?.Trim();
        byte[] version;
        try { version = Convert.FromBase64String(request.ExpectedVersion ?? ""); }
        catch (FormatException) { throw Error(400, "VERSION_INVALID", "Phiên bản giỏ không hợp lệ."); }
        if (request.ClientRequestId == Guid.Empty || request.SourceCartId <= 0 || version.Length != 8 ||
            request.ExpectedFingerprint?.Length != 64 || request.ExpectedFingerprint.Any(x => !Uri.IsHexDigit(x)))
            throw Error(400, "REQUEST_INVALID", "Mã yêu cầu/giỏ/phiên bản không hợp lệ.");
        if (string.IsNullOrEmpty(name) || name.Length > 200 || string.IsNullOrEmpty(phone) || phone.Length > 30 ||
            string.IsNullOrEmpty(address) || address.Length > 1200 || request.Note?.Length > 1000)
            throw Error(400, "RECIPIENT_INVALID", "Nhập đủ tên, số điện thoại và địa chỉ giao.");
        return request with { ExpectedVersion = Convert.ToBase64String(version), ExpectedFingerprint = request.ExpectedFingerprint.ToUpperInvariant(),
            RecipientName = name, RecipientPhone = phone, RecipientAddress = address,
            Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim() };
    }
    private static string Fingerprint(Order cart, POSShift shift) => Hash(JsonSerializer.Serialize(new {
        cart.Id, cart.StoreId, cart.POSShiftId, cart.RowVersion, cart.CustomerId, cart.GrandTotal, cart.OrderDiscount,
        cart.DiscountTotal, cart.PaidTotal, cart.DepositAmount, cart.UseMultiLegalEntity,
        shiftVersion = shift.RowVersion, shiftId = shift.Id, shift.WarehouseId, shift.CurrentOrderId,
        lines = cart.Lines.OrderBy(x => x.Id).Select(x => new { x.Id, x.RowVersion, x.VariantId, x.ItemName, x.UnitName,
            x.SellingUnitId, x.SellingUnitName, x.BaseUnitId, x.BaseUnitName, x.ProductUnitConversionId,
            x.Multiplier, x.Quantity, x.BaseQuantity, x.UnitPrice, x.LineDiscount, x.LineTotal })
    }, Json));
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private Task Lock(string resource, CancellationToken ct) => db.Database.ExecuteSqlInterpolatedAsync(
        $"DECLARE @result int; EXEC @result=sys.sp_getapplock @Resource={resource}, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=15000; IF @result<0 THROW 51000, 'POS operation is busy; retry the same request id.', 1;", ct);
}
