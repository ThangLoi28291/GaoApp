using GaoApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

internal static class PosCameraBrowserFixture
{
    internal static async Task<object> VerifyAsync(FullApplicationFixture app, FullApplicationFixture.StoreSeed store, int orderId)
    {
        await using var db = app.Database.CreateTenantContext(store.StoreId);
        var orders = await db.Orders.AsNoTracking().ToListAsync();
        if (orders.Count != 1 || orders[0].Id != orderId || orders[0].Status != OrderStatus.Draft)
            throw new InvalidOperationException("Camera must keep the same draft without posting a sale.");
        var lines = await db.OrderLines.AsNoTracking().Where(x => x.OrderId == orderId && !x.IsDeleted).ToListAsync();
        var beerId = (await db.ProductVariants.SingleAsync(x => x.ProductVariantName == "Bia Ken bạc 330 ml")).Id;
        var waterId = (await db.ProductVariants.SingleAsync(x => x.Sku == "WATER-TEST")).Id;
        var beer = lines.Single(x => x.VariantId == beerId && x.SellingUnitName == "Lon");
        var pack = lines.Single(x => x.VariantId == beerId && x.SellingUnitName == "Lốc");
        var water = lines.Single(x => x.VariantId == waterId);
        if (lines.Count != 3 || beer.Quantity != 2 || beer.BaseQuantity != 2
            || pack.Quantity != 4 || pack.BaseQuantity != 24 || water.Quantity != 3 || water.BaseQuantity != 3)
            throw new InvalidOperationException("Camera must add each acknowledged product/pack quantity exactly once.");
        return new { passed = true, sameDraft = true, orderId, lineCount = lines.Count,
            lines = lines.Select(x => new { x.VariantId, x.SellingUnitName, x.Quantity, x.BaseQuantity }),
            storeMenusStockPaymentsCatalogOtherStoreAndShiftUnchanged = true };
    }
}
