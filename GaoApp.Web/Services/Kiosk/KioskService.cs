using System.Text.Json;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.DTOs.POSPaymentQrs;
using GaoApp.Application.DTOs.POSShifts;
using GaoApp.Application.Interfaces.Services.Orders;
using GaoApp.Application.Interfaces.Services.Invoices;
using GaoApp.Application.Interfaces.Services.POSShifts;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using GaoApp.Web.Services.Acb;
using Microsoft.EntityFrameworkCore;
namespace GaoApp.Web.Services.Kiosk;

public sealed class KioskService(AppDbContext db, IPOSService pos, IPOSShiftService shifts, AcbPaymentService bank,
    IInvoiceIssuanceRouteService invoiceRoutes)
{
    private int Store => db.CurrentStoreId ?? 0;
    private IQueryable<AcbQrSession> Sessions(KioskStation station) => db.Set<AcbQrSession>()
        .Where(x => x.StoreId == Store && x.OrderId == station.OrderId && x.TerminalId == station.TerminalId);

    public async Task<object> StateAsync(KioskStation station, CancellationToken ct)
    {
        var order = station.OrderId is int id ? await db.Orders.AsNoTracking().Include(x => x.POSShift)
            .Include(x => x.Lines.Where(l => !l.IsDeleted)).SingleOrDefaultAsync(x => x.StoreId == Store && x.Id == id, ct) : null;
        if (order != null && order.POSShift.TerminalId != station.TerminalId) throw new ConflictAppException("Giỏ hàng không thuộc quầy này.");
        var qr = order == null ? null : await Sessions(station).AsNoTracking().OrderByDescending(x => x.Id).FirstOrDefaultAsync(ct);
        var qrInfo = qr == null ? null : await db.PosPaymentQrRequests.AsNoTracking().Where(x => x.StoreId == Store && x.Id == qr.QrRequestId)
            .Select(x => new { x.Id, x.QrDataUrl, x.Amount, x.Content, x.ExpireAtUtc }).SingleAsync(ct);
        var mode = order?.Status == OrderStatus.Completed ? "success" :
            qr?.Status is AcbSessionStatus.Received or AcbSessionStatus.ReviewRequired ? "review" :
            qr != null && qr.Status != AcbSessionStatus.Cancelled ? "payment" : order?.Status == OrderStatus.Draft ? "shop" : "idle";
        var variantIds = order?.Lines.Select(x => x.VariantId).Distinct().ToArray() ?? [];
        var imagePaths = await db.ProductVariants.AsNoTracking().Where(x => x.StoreId == Store && variantIds.Contains(x.Id))
            .Select(x => new { x.Id, Path = x.PrimaryProductImage != null && !x.PrimaryProductImage.IsDeleted &&
                x.PrimaryProductImage.MediaAsset != null && !x.PrimaryProductImage.MediaAsset.IsDeleted && x.PrimaryProductImage.MediaAsset.StoragePath != ""
                ? x.PrimaryProductImage.MediaAsset.StoragePath
                : x.Product != null ? x.Product.ProductImages.Where(i => !i.IsDeleted && i.MediaAsset != null && !i.MediaAsset.IsDeleted && i.MediaAsset.StoragePath != "")
                    .OrderByDescending(i => i.IsPrimary).ThenBy(i => i.SortOrder).ThenBy(i => i.Id).Select(i => i.MediaAsset.StoragePath).FirstOrDefault() : null })
            .ToDictionaryAsync(x => x.Id, x => string.IsNullOrWhiteSpace(x.Path) ? null : "/" + x.Path.Replace("\\", "/").TrimStart('/'), ct);
        var lines = order?.Lines.Select(x => new { x.Id, x.VariantId, x.ItemName, x.Quantity, x.UnitPrice, x.LineTotal,
            UnitName = x.SellingUnitName, x.Barcode, x.Multiplier, x.IsPromotionGift, ImageUrl = imagePaths.GetValueOrDefault(x.VariantId) }).ToList();
        return new { station.SessionKey, station.Revision, station.IsPaused, terminal = station.Terminal.Name,
            store = await db.Stores.Where(x => x.Id == Store).Select(x => x.Name).SingleAsync(ct), mode,
            order = order == null ? null : new { order.Id, order.OrderNumber, order.Subtotal, order.DiscountTotal,
                order.GrandTotal, order.PaidTotal, invoiceIssuanceRoute = order.InvoiceIssuanceRoute.ToString(), lines }, qr = qrInfo,
            paymentStatus = qr?.Status.ToString(), message = qr?.ReviewReason,
            paymentCheckAfterUtc = qr == null ? (DateTime?)null : qr.LastRetrievedAtUtc?.AddSeconds(8) ?? qr.CreatedAtUtc.AddSeconds(30),
            completedAtUtc = station.CompletedAtUtc, station.HelpRequestedAtUtc };
    }

    public async Task<object> PollAsync(KioskStation station, CancellationToken ct, bool refreshBank = false)
    {
        if (station.CustomerExpiresAtUtc < DateTime.UtcNow) { station.CustomerId = null; station.CustomerExpiresAtUtc = null; }
        var session = await Sessions(station).OrderByDescending(x => x.Id).FirstOrDefaultAsync(ct);
        if (session != null && session.Status != AcbSessionStatus.Cancelled)
        {
            // Persisted bank session is recovered after reload/restart. No client-reported payment state is trusted.
            try {
                // Like POS: read callback-confirmed state independently of the scheduled bank lookup.
                await bank.StatusAsync(session.QrRequestId, refreshBank, ct);
                if (session.Status is AcbSessionStatus.Received or AcbSessionStatus.Completed)
                {
                    await bank.CompleteAsync(session.QrRequestId, ct);
                }
                // Expiry is not assumed to mean no payment. Bank cancellation checks for racing receipts first.
                else if (session.Status == AcbSessionStatus.Pending &&
                    await db.PosPaymentQrRequests.AnyAsync(x => x.Id == session.QrRequestId && x.ExpireAtUtc < DateTime.UtcNow, ct))
                {
                    if (await bank.CancelAsync(session.QrRequestId, ct)) await CancelCartAsync(station, ct);
                }
            } catch (Exception error) when (error is InvalidOperationException or GaoApp.Application.Common.Exceptions.AppException)
            {
                // Keep the order and QR for recovery. Staff can inspect the ACB history; never report false success.
                station.HelpRequestedAtUtc ??= DateTime.UtcNow;
            }
            if (session.Status == AcbSessionStatus.ReviewRequired) station.HelpRequestedAtUtc ??= DateTime.UtcNow;
        }
        // Also recovers orders completed by staff or before a kiosk reload. Save the invoice choice before clearing the session.
        if (await EnsureCompletedInvoiceRouteAsync(station, ct)) station.CompletedAtUtc ??= DateTime.UtcNow;
        if (station.CompletedAtUtc is DateTime done && done <= DateTime.UtcNow.AddSeconds(-15)) Reset(station);
        else if (station.OrderId != null && (session == null || session.Status == AcbSessionStatus.Cancelled) && station.CartTouchedAtUtc < DateTime.UtcNow.AddMinutes(-2))
            await CancelCartAsync(station, ct);
        await db.SaveChangesAsync(ct);
        return await StateAsync(station, ct);
    }

    public async Task<object> CommandAsync(KioskStation station, KioskCommand command, CancellationToken ct)
    {
        if (command.CommandId == Guid.Empty) throw new ValidationAppException("Thiếu mã thao tác.");
        var hash = KioskAccess.Hash(JsonSerializer.Serialize(command));
        if (station.LastCommandId == command.CommandId)
        {
            if (station.LastCommandHash != hash) throw new ConflictAppException("Mã thao tác đã được sử dụng.");
            return await StateAsync(station, ct);
        }
        if (station.SessionKey != command.SessionKey || station.Revision != command.Revision)
            throw new ConflictAppException("Giỏ hàng đã thay đổi. Vui lòng tải lại trước khi thao tác tiếp.");
        if (station.IsPaused && command.Action is not ("help" or "finish" or "cancel" or "cancel-payment"))
            throw new ConflictAppException("Quầy đang tạm dừng. Vui lòng liên hệ nhân viên.");

        if (command.Action is "checkout" or "retry-payment")
        {
            if (station.OrderId == null) throw new ValidationAppException("Giỏ hàng đang trống.");
            if (command.Action == "retry-payment" && !await PreparePaymentRetryAsync(station, ct))
                return await StateAsync(station, ct);
            if (await Sessions(station).AnyAsync(x => x.Status != AcbSessionStatus.Cancelled, ct)) return await StateAsync(station, ct);
            await RequireEditableAsync(station, ct);
            // A definitively cancelled attempt can receive a new key; uncertain attempts stay recoverable.
            if (station.CheckoutKey != null && await Sessions(station).AnyAsync(x => x.Status == AcbSessionStatus.Cancelled && db.PosPaymentQrRequests.Any(q => q.Id == x.QrRequestId && q.ClientRequestId == station.CheckoutKey), ct)) station.CheckoutKey = null;
            station.CheckoutKey ??= Guid.NewGuid();
            await db.SaveChangesAsync(ct);
            // ACB service durably saves correlation before the network call. Retrying reuses this same key.
            var qr = await bank.TryCreateAsync(station.OrderId.Value, new CreatePOSPaymentQrRequest { ClientRequestId = station.CheckoutKey }, ct);
            if (qr?.AutomaticConfirmation != true) throw new ConflictAppException("Quầy chưa sẵn sàng nhận thanh toán QR tự động. Vui lòng gọi nhân viên.");
            station.Revision++; station.LastCommandId = command.CommandId; station.LastCommandHash = hash;
            await db.SaveChangesAsync(ct);
            return await StateAsync(station, ct);
        }
        if (command.Action == "cancel-payment")
        {
            await RequireEditableAsync(station, ct);
            try
            {
                // Bank evidence is checked under the existing ACB order lock. Never trust a customer saying they have not paid.
                var sessions = await Sessions(station).Where(x => x.Status != AcbSessionStatus.Cancelled).OrderBy(x => x.Id).ToListAsync(ct);
                foreach (var session in sessions)
                    if (!await bank.CancelAsync(session.QrRequestId, ct)) throw new InvalidOperationException("Chưa xác nhận hủy QR.");
            }
            catch (Exception error) when (error is InvalidOperationException or AppException or HttpRequestException or JsonException ||
                (error is OperationCanceledException && !ct.IsCancellationRequested))
            {
                station.HelpRequestedAtUtc ??= DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
                throw new ConflictAppException("Chưa thể hủy thanh toán: giao dịch đã nhận tiền hoặc cần kiểm tra với ngân hàng. Vui lòng không chuyển thêm tiền và chờ nhân viên hỗ trợ.");
            }
            await using var cancellation = await db.Database.BeginTransactionAsync(ct);
            await CancelCartAsync(station, ct);
            station.LastCommandId = command.CommandId; station.LastCommandHash = hash;
            await db.SaveChangesAsync(ct); await cancellation.CommitAsync(ct);
            return await StateAsync(station, ct);
        }
        var pending = await Sessions(station).AnyAsync(x => x.Status != AcbSessionStatus.Cancelled, ct);
        if (pending && command.Action is not ("help" or "finish"))
            throw new ConflictAppException("Đang xử lý thanh toán. Không thể thay đổi giỏ hàng.");
        if (command.Action == "finish" && station.OrderId != null && !await EnsureCompletedInvoiceRouteAsync(station, ct))
            throw new ConflictAppException("Giao dịch chưa hoàn tất. Vui lòng chờ xác nhận thanh toán.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        switch (command.Action)
        {
            case "start":
                if (station.OrderId == null)
                {
                    if ((await bank.SettingsAsync(ct))?.Enabled != true) throw new ConflictAppException("Thanh toán QR tự động chưa được bật. Vui lòng mua hàng tại quầy thu ngân.");
                    var shift = await shifts.GetCurrentOpenAsync(ct);
                    if (shift == null) await shifts.OpenAsync(new OpenShiftRequest { WarehouseId = station.WarehouseId, OpeningCash = 0, Note = "Phiên quầy tự phục vụ" }, ct);
                    station.OrderId = await pos.CreateDraftAsync(note: "Đơn tại quầy tự phục vụ", ct: ct);
                }
                break;
            case "scan":
            case "add":
                await RequireEditableAsync(station, ct);
                if (command.Quantity <= 0 || command.Quantity > 999) throw new ValidationAppException("Số lượng không hợp lệ.");
                if (command.Action == "scan") {
                    if (string.IsNullOrWhiteSpace(command.Barcode)) throw new ValidationAppException("Vui lòng quét mã sản phẩm.");
                    await pos.AddItemByBarcodeAsync(station.OrderId!.Value, command.Barcode.Trim(), command.Quantity, ct);
                } else await pos.AddItemAsync(station.OrderId!.Value, command.VariantId, command.UnitId, command.Quantity, ct);
                if (await db.OrderLines.AnyAsync(x => x.StoreId == Store && x.OrderId == station.OrderId && !x.IsDeleted && !x.Variant.HasInputInvoice, ct))
                    throw new ValidationAppException("Sản phẩm hoặc quà tặng này chưa hỗ trợ thanh toán QR tự động. Vui lòng mua tại quầy thu ngân.");
                if (await db.OrderLines.CountAsync(x => x.StoreId == Store && x.OrderId == station.OrderId && !x.IsDeleted && !x.IsPromotionGift, ct) > 100 ||
                    await db.OrderLines.AnyAsync(x => x.StoreId == Store && x.OrderId == station.OrderId && !x.IsDeleted && !x.IsPromotionGift && x.Quantity > 999, ct))
                    throw new ValidationAppException("Giỏ tối đa 100 mặt hàng, mỗi dòng tối đa 999 sản phẩm. Vui lòng nhờ nhân viên hỗ trợ.");
                break;
            case "quantity":
                await RequireEditableAsync(station, ct);
                if (!await db.OrderLines.AnyAsync(x => x.Id == command.LineId && x.OrderId == station.OrderId && x.StoreId == Store && !x.IsDeleted && !x.IsPromotionGift, ct))
                    throw new ValidationAppException("Không tìm thấy sản phẩm trong giỏ của bạn.");
                if (command.Quantity == 0) await pos.RemoveLineAsync(command.LineId, ct);
                else await pos.UpdateLineQtyAsync(command.LineId, command.Quantity, ct);
                break;
            case "cancel": await CancelCartAsync(station, ct); break;
            case "finish":
                Reset(station); break;
            case "help": station.HelpRequestedAtUtc = DateTime.UtcNow; break;
            default: throw new ValidationAppException("Thao tác không được hỗ trợ.");
        }
        station.CartTouchedAtUtc = DateTime.UtcNow;
        station.Revision++; station.LastCommandId = command.CommandId; station.LastCommandHash = hash;
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return await StateAsync(station, ct);
    }
    private async Task<bool> PreparePaymentRetryAsync(KioskStation station, CancellationToken ct)
    {
        var session = await Sessions(station).OrderByDescending(x => x.Id).FirstOrDefaultAsync(ct)
            ?? throw new ConflictAppException("Chưa có lần tạo QR cần kiểm tra.");
        // A callback may have confirmed this attempt while the customer was opening the retry dialog.
        if (session.Status is AcbSessionStatus.Received or AcbSessionStatus.Completed or AcbSessionStatus.ReviewRequired)
        {
            await PollAsync(station, ct);
            return false;
        }
        if (session.Status is not (AcbSessionStatus.Creating or AcbSessionStatus.Cancelled) ||
            await db.PosPaymentQrRequests.AnyAsync(x => x.Id == session.QrRequestId && x.StoreId == Store && x.QrDataUrl != null && x.QrDataUrl != "", ct))
            throw new ConflictAppException("QR đã được tạo. Vui lòng kiểm tra thanh toán hiện tại trước khi thao tác tiếp.");
        await RequireEditableAsync(station, ct);
        try
        {
            // The same ACB cancellation policy as POS must prove the old attempt can be released.
            // Never recreate from an error code, timeout or an empty screen alone.
            if (!await bank.CancelAsync(session.QrRequestId, ct))
                throw new InvalidOperationException("Chưa xác nhận hủy lần tạo QR trước.");
        }
        catch (Exception error) when (error is InvalidOperationException or AppException or HttpRequestException or JsonException ||
            (error is OperationCanceledException && !ct.IsCancellationRequested))
        {
            await PollAsync(station, ct);
            if (session.Status is AcbSessionStatus.Received or AcbSessionStatus.Completed or AcbSessionStatus.ReviewRequired)
                return false;
            station.HelpRequestedAtUtc ??= DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            throw new ConflictAppException("Chưa thể tạo lại QR vì chưa xác minh được lần trước. Giỏ hàng được giữ nguyên; vui lòng gọi nhân viên hỗ trợ và không chuyển thêm tiền.");
        }
        // Keep this order and its cart; only replace the safely cancelled bank attempt.
        station.CheckoutKey = null;
        return true;
    }
    private async Task<bool> EnsureCompletedInvoiceRouteAsync(KioskStation station, CancellationToken ct)
    {
        if (station.OrderId == null) return false;
        var order = await db.Orders.AsNoTracking()
            .Where(x => x.StoreId == Store && x.Id == station.OrderId && x.POSShift.TerminalId == station.TerminalId && x.Status == OrderStatus.Completed)
            .Select(x => new { x.Id, x.InvoiceIssuanceRoute }).SingleOrDefaultAsync(ct);
        if (order == null) return false;
        if (order.InvoiceIssuanceRoute != InvoiceIssuanceRoute.Unselected) return true;

        // Same route as the POS "Khách không lấy hóa đơn" choice; retain normal audit and idempotency rules.
        var result = await invoiceRoutes.SetInitialRouteAsync(order.Id, InvoiceIssuanceRoute.Automatic, ct);
        if (!result.IsSuccess)
        {
            station.HelpRequestedAtUtc ??= DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            throw new ConflictAppException("Đơn đã thanh toán nhưng chưa lưu được lựa chọn hóa đơn. Vui lòng gọi nhân viên hỗ trợ.");
        }
        return true;
    }
    private async Task RequireEditableAsync(KioskStation station, CancellationToken ct)
    {
        if (!await db.Orders.AnyAsync(x => x.StoreId == Store && x.Id == station.OrderId && x.Status == OrderStatus.Draft && x.POSShift.TerminalId == station.TerminalId, ct))
            throw new ConflictAppException("Vui lòng bắt đầu phiên mua hàng mới.");
    }
    private async Task CancelCartAsync(KioskStation station, CancellationToken ct)
    {
        if (station.OrderId is int id && await db.Orders.AnyAsync(x => x.StoreId == Store && x.Id == id && x.Status == OrderStatus.Draft, ct))
            await pos.CancelAsync(id, "Kết thúc phiên tự phục vụ chưa thanh toán", ct);
        Reset(station);
    }
    public static void Reset(KioskStation station)
    {
        station.OrderId = null; station.CheckoutKey = null; station.SessionKey = Guid.NewGuid();
        station.CustomerId = null; station.CustomerExpiresAtUtc = null; station.CompletedAtUtc = null;
        station.CartTouchedAtUtc = null; station.Revision++;
    }
}
