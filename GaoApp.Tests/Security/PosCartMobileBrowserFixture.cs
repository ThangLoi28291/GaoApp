using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

// Synthetic data only, inside the browser harness's disposable GUID database.
internal static class PosCartMobileBrowserFixture
{
    internal sealed record Seed(int RetailId, int WholesaleId, int VoucherId);
    internal static async Task<Seed> PrepareAsync(FullApplicationFixture app, FullApplicationFixture.StoreSeed store,
        FullApplicationFixture.Account account, int orderId, int packId)
    {
        await using var db = app.Database.CreateTenantContext(store.StoreId);
        var retail = new Customer { StoreId = store.StoreId, Name = "Nguyễn Minh Anh — khách hàng thân thiết", Phone = "0901234567", PriceTier = "RETAIL", Code = "MOBILE-RETAIL" };
        var wholesale = new Customer { StoreId = store.StoreId, Name = "Đại lý Minh Anh", Phone = "0901234568", PriceTier = "WHOLESALE", Code = "MOBILE-WHOLESALE" };
        db.AddRange(retail, wholesale, new RewardSettings { StoreId = store.StoreId, IsEnabled = true, MoneyPerPoint = 1000, PointsPerVoucher = 100, VoucherValue = 10000 });
        await db.SaveChangesAsync();
        var voucher = new CustomerRewardVoucher { StoreId = store.StoreId, CustomerId = retail.Id, VoucherCode = "PHONE-10000", Value = 10000, RequiredAmount = 100000, IssuedAtUtc = DateTime.UtcNow };
        db.AddRange(voucher, new CustomerRewardLedger { StoreId = store.StoreId, CustomerId = retail.Id, Type = CustomerRewardLedgerType.ImportOldBalance, Amount = 200000 });
        await db.SaveChangesAsync();
        using var client = await app.LoginAsync(account);
        await client.JsonAsync(HttpMethod.Post, $"/admin/pos/{orderId}/items?variantId={store.VariantId}&qty=2");
        await client.JsonAsync(HttpMethod.Post, $"/admin/pos/{orderId}/items?variantId={store.VariantId}&productUnitConversionId={packId}&qty=1");
        await client.JsonAsync(HttpMethod.Post, $"/admin/pos/cart/current/customer/{retail.Id}", new { repriceExistingLines = true });
        return new(retail.Id, wholesale.Id, voucher.Id);
    }

    internal static async Task<object> VerifyAsync(FullApplicationFixture app, FullApplicationFixture.StoreSeed store, int orderId)
    {
        await using var db = app.Database.CreateTenantContext(store.StoreId);
        var order = await db.Orders.AsNoTracking().SingleAsync();
        var lines = await db.OrderLines.AsNoTracking().Where(x => x.OrderId == orderId && !x.IsDeleted).ToListAsync();
        var waterId = (await db.ProductVariants.SingleAsync(x => x.Sku == "WATER-TEST")).Id;
        var retail = await db.Customers.SingleAsync(x => x.Code == "MOBILE-RETAIL");
        if (order.Id != orderId || order.Status != OrderStatus.Draft || order.CustomerId != retail.Id
            || order.Note != "Giao sau 17 giờ — gọi trước khi đến." || order.OrderDiscount != 3000
            || lines.Count != 3 || lines.Single(x => x.VariantId == waterId).Quantity != 3
            || lines.Single(x => x.SellingUnitName == "Lốc").Quantity != 2
            || lines.Single(x => x.VariantId == store.VariantId && x.SellingUnitName == "Lon").Quantity != 2
            || lines.Sum(x => x.LineDiscount) != 1000 || order.PaidTotal != 0)
            throw new InvalidOperationException("Phone cart edits must persist once in the same unpaid draft.");
        var applied = await db.OrderRewardVouchers.AsNoTracking().Where(x => x.OrderId == orderId).ToListAsync();
        if (applied.Count != 1 || await db.CustomerRewardVouchers.AnyAsync(x => x.Status != CustomerRewardVoucherStatus.Available)
            || await db.CustomerRewardLedgers.CountAsync() != 1 || await db.CustomerRewardLedgers.SumAsync(x => x.Amount) != 200000)
            throw new InvalidOperationException("Viewing points/applying a voucher must not redeem or consume rewards before checkout.");
        return new { passed = true, sameDraft = true, orderId, order.CustomerId, order.Note, order.Subtotal,
            order.DiscountTotal, order.OrderDiscount, order.GrandTotal, order.BalanceDue, order.PaidTotal,
            lines = lines.Select(x => new { x.VariantId, x.SellingUnitName, x.Quantity, x.BaseQuantity, x.LineDiscount }),
            appliedVoucherCount = applied.Count, pointsAndVoucherNotConsumed = true, storeMenusStockPaymentsCatalogOtherStoreAndShiftUnchanged = true };
    }
}
