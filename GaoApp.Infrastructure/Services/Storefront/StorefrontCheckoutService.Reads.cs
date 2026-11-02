using System.Text.Json;
using System.Text.RegularExpressions;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Storefront;
using GaoApp.Domain.Storefront;
using GaoApp.Infrastructure.Data;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Services.Storefront;

public sealed partial class StorefrontCheckoutService
{
    public async Task<StorefrontGuestOrder?> GetGuestOrderAsync(string token, CancellationToken ct = default)
    {
        if (!Regex.IsMatch(token ?? "", "^[A-Za-z0-9_-]{43}$")) return null;
        try { var bytes = WebEncoders.Base64UrlDecode(token!); if (bytes.Length != 32 || WebEncoders.Base64UrlEncode(bytes) != token) return null; }
        catch (FormatException) { return null; }
        if (!await db.Stores.AsNoTracking().AnyAsync(x => x.Id == Store && x.IsActive, ct)) return null;
        var hash = Hash(token!);
        var order = await db.StorefrontOrders.AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == Store && x.GuestTokenHash == hash, ct);
        return order is null ? null : await ProjectOrder(db, order, ct);
    }

    internal static async Task<StorefrontGuestOrder> ProjectOrder(AppDbContext db, StorefrontOrder order, CancellationToken ct)
    {
        // Snapshot reads deliberately do not call the live catalog or require a currently open shop.
        using var address = JsonDocument.Parse(order.AddressSnapshotJson);
        var recipient = address.RootElement.GetProperty("recipient").Deserialize<StorefrontRecipient>(Json)
            ?? throw new ValidationAppException("Bản chụp địa chỉ đơn chưa hợp lệ.");
        var lines = await db.Set<StorefrontOrderLine>().AsNoTracking().Where(x => x.StoreId == order.StoreId && x.StorefrontOrderId == order.Id).OrderBy(x => x.Id).ToListAsync(ct);
        var result = lines.Select(line =>
        {
            using var snapshot = JsonDocument.Parse(line.SnapshotJson);
            var pricing = snapshot.RootElement.GetProperty("pricing").Deserialize<StorefrontCheckoutLine>(Json);
            return new StorefrontGuestOrderLine(line.ItemName, line.UnitName, line.Quantity, line.UnitPrice, line.Discount,
                line.RoundingAdjustment, line.NetAmount, line.IsGift, pricing?.PromotionName, pricing?.VariantName);
        }).ToList();
        var works = await db.Set<StorefrontFulfillmentWork>().AsNoTracking().Where(x => x.StoreId == order.StoreId && x.StorefrontOrderId == order.Id).OrderBy(x => x.Id).ToListAsync(ct);
        var labels = new[] { "Đang chuẩn bị", "Đã soạn đủ", "Đang vận chuyển", "Đã giao thành công", "Đang nhận về do giao không thành công", "Cửa hàng đã nhận lại hàng", "Đang kiểm tra giao hàng" };
        var shipments = works.Select((x, i) => new StorefrontGuestShipment("Kiện hàng " + (i + 1),
            order.Phase == StorefrontOrderPhase.Cancelled && x.Stage is StorefrontFulfillmentStage.Picking or StorefrontFulfillmentStage.Picked
                ? "Đã dừng chuẩn bị do hủy đơn" : labels[(int)x.Stage],
            x.CourierUserId.HasValue ? "Nhân viên cửa hàng" : x.CarrierName, x.TrackingCode)).ToList();
        return new(order.Code, AsUtc(order.CreatedAtUtc), order.Phase, order.PaymentState, order.DeliveryState, order.RefundState,
            order.PaymentMethod, recipient, result, order.GoodsAmount, order.ShippingCharge, order.TotalDue, Convert.ToBase64String(order.RowVersion), shipments,
            await db.Set<StorefrontCustodyIncident>().AnyAsync(x => x.StoreId == order.StoreId && x.StorefrontOrderId == order.Id && x.ReconciledByEvidenceId == null, ct));
    }
}
