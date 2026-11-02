using GaoApp.Application.DTOs.Delivery;
using GaoApp.Domain.Delivery;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Services.Delivery;

// Reservation is not redemption. The immutable delivery source cart owns its voucher links.
public static class DeliveryVoucherReservationService
{
    public static async Task ReserveAsync(AppDbContext db, DeliveryOrder delivery, Order cart, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Voucher reservation requires the delivery transaction.");
        var links = await db.Set<OrderRewardVoucher>().AsNoTracking()
            .Where(x => x.StoreId == delivery.StoreId && x.OrderId == cart.Id).OrderBy(x => x.VoucherId).ToListAsync(ct);
        if (links.Select(x => x.VoucherId).Distinct().Count() != links.Count ||
            !links.Select(x => (x.Id, x.VoucherId, x.VoucherValue)).SequenceEqual(cart.RewardVouchers.OrderBy(x => x.VoucherId).Select(x => (x.Id, x.VoucherId, x.VoucherValue))))
            throw Error("Voucher của giỏ vừa thay đổi; tải lại trước khi tạo đơn giao.");
        if (cart.VoucherDiscountTotal > 0 && links.Count == 0) throw Error("Thiếu voucher nguồn của khoản giảm giá.");
        var effectiveValue = Math.Min(DeliveryValues.RoundMoney(links.Sum(x => x.VoucherValue)), cart.GrandTotal + cart.VoucherDiscountTotal);
        if (effectiveValue != cart.VoucherDiscountTotal) throw Error("Khoản giảm voucher không khớp các phiếu nguồn và tổng POS.");
        foreach (var link in links)
        {
            var expectedVersion = cart.RewardVouchers.Single(x => x.VoucherId == link.VoucherId).Voucher.RowVersion.ToArray();
            var voucher = await LockedVoucher(db, delivery.StoreId, link.VoucherId, ct);
            if (voucher is null || voucher.Status != CustomerRewardVoucherStatus.Available ||
                voucher.CustomerId != cart.CustomerId || voucher.Value != link.VoucherValue ||
                voucher.UsedOrderId.HasValue || voucher.UsedAtUtc.HasValue || !voucher.RowVersion.SequenceEqual(expectedVersion))
                throw Error("Voucher đã được sử dụng/giữ cho đơn khác hoặc không thuộc khách nguồn.");
            voucher.Status = CustomerRewardVoucherStatus.ReservedForDelivery;
        }
        await db.SaveChangesAsync(ct);
    }

    public static async Task ReleaseAsync(AppDbContext db, DeliveryOrder delivery, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null || delivery.State != DeliveryState.Cancelled)
            throw new InvalidOperationException("Voucher release requires an owned prehandover cancellation transaction.");
        var links = await db.Set<OrderRewardVoucher>().AsNoTracking()
            .Where(x => x.StoreId == delivery.StoreId && x.OrderId == delivery.SourceCartId).OrderBy(x => x.VoucherId).ToListAsync(ct);
        foreach (var link in links)
        {
            var voucher = await LockedVoucher(db, delivery.StoreId, link.VoucherId, ct);
            if (voucher is null || voucher.Status != CustomerRewardVoucherStatus.ReservedForDelivery ||
                voucher.CustomerId != delivery.CustomerId || voucher.UsedOrderId.HasValue || voucher.UsedAtUtc.HasValue)
                throw Error("Chứng cứ giữ voucher nguồn không khớp; không tự nhả voucher của đơn khác.");
            if (await db.DeliveryOrders.AsNoTracking().AnyAsync(x => x.StoreId == delivery.StoreId && x.Id != delivery.Id &&
                x.State != DeliveryState.Cancelled && db.Set<OrderRewardVoucher>().Any(v => v.StoreId == x.StoreId && v.OrderId == x.SourceCartId && v.VoucherId == link.VoucherId), ct))
                throw Error("Voucher còn được liên kết với đơn giao khác; cần đối chiếu quyền sở hữu.");
            voucher.Status = CustomerRewardVoucherStatus.Available;
        }
        await db.SaveChangesAsync(ct);
    }

    private static async Task<CustomerRewardVoucher?> LockedVoucher(AppDbContext db, int store, int id, CancellationToken ct)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT Id FROM CustomerRewardVouchers WITH (UPDLOCK,HOLDLOCK) WHERE StoreId={store} AND Id={id};", ct);
        var tracked = db.ChangeTracker.Entries<CustomerRewardVoucher>().SingleOrDefault(x => x.Entity.StoreId == store && x.Entity.Id == id);
        if (tracked is not null)
        {
            if (tracked.State != EntityState.Unchanged) throw Error("Voucher có thay đổi chưa lưu.");
            await tracked.ReloadAsync(ct);
            return tracked.State == EntityState.Detached ? null : tracked.Entity;
        }
        return await db.Set<CustomerRewardVoucher>().SingleOrDefaultAsync(x => x.StoreId == store && x.Id == id, ct);
    }
    private static DeliveryFoundationException Error(string message) => new(409, "VOUCHER_UNAVAILABLE", message);
}
