using System.Text.Json;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Storefront;
using GaoApp.Domain.Storefront;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Services.Storefront;

internal static class StorefrontServiceSource
{
    internal static async Task<StorefrontServiceCollectionProof> ReadAsync(AppDbContext db, StorefrontOrder order, CancellationToken ct)
    {
        if (order.PaymentMethod == StorefrontPaymentMethod.Qr)
        {
            var intent = await db.Set<StorefrontPaymentIntent>().AsNoTracking().SingleAsync(x => x.StoreId == order.StoreId && x.StorefrontOrderId == order.Id, ct);
            var binding = JsonSerializer.Deserialize<StorefrontBankBinding>(intent.BindingSnapshotJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            if (binding.ServiceFee is not null) return binding.ServiceFee;
            var current = await Current(db, order.StoreId, ct);
            if (!binding.SourceLegalEntityIds.Contains(current.LegalEntityId)) throw new ConflictAppException("QR cũ chưa có bằng chứng quyền thu phí cho chủ thể dịch vụ này. Chưa ghi bán; cần đối soát nguồn thu.");
            return current;
        }
        return await Current(db, order.StoreId, ct);
    }
    private static async Task<StorefrontServiceCollectionProof> Current(AppDbContext db, int store, CancellationToken ct)
    {
        var setting = await db.Set<StorefrontServiceRevenueSetting>().AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == store, ct)
            ?? throw new ConflictAppException("Phí giao hàng cần cấu hình rõ chủ thể, thuế và tên dịch vụ tại /Admin/StorefrontFulfillment/ServiceSettings. Đơn đã giao chưa ghi bán.");
        var tax = await db.Taxes.AsNoTracking().SingleOrDefaultAsync(x => x.StoreId == store && x.Id == setting.TaxId && x.IsActive, ct);
        if (tax is null || !await db.LegalEntities.AnyAsync(x => x.StoreId == store && x.Id == setting.LegalEntityId && x.IsActive, ct))
            throw new ConflictAppException("Thuế/chủ thể dịch vụ không hoạt động; chưa ghi bán.");
        return new(setting.LegalEntityId, setting.TaxId, setting.Name, tax.Rate, setting.InvoiceRoute, Convert.ToBase64String(setting.RowVersion), 0, "");
    }
}
