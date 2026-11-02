using GaoApp.Application.Common.Security;
using GaoApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

// Only used with FullApplicationFixture's disposable SQL and disabled delivery outbox.
internal static class PosSessionMobileBrowserFixture
{
    internal static Task<PosCheckoutMobileBrowserFixture.Seed> PrepareAsync(FullApplicationFixture app,
        FullApplicationFixture.StoreSeed store, FullApplicationFixture.Account account)
        => PosCheckoutMobileBrowserFixture.PrepareAsync(app, store, account);
    internal static Task<string> FingerprintAsync(FullApplicationFixture app, FullApplicationFixture.StoreSeed store)
        => PosCheckoutMobileBrowserFixture.FingerprintAsync(app, store);
    internal static async Task<object> VerifyAsync(FullApplicationFixture app, FullApplicationFixture.StoreSeed store, int orderId)
    {
        await using var db = app.Database.CreateTenantContext(store.StoreId);
        var deliveries = await db.DeliveryOrders.AsNoTracking().ToListAsync();
        var revisions = await db.DeliveryRevisions.AsNoTracking().ToListAsync();
        var stock = await db.InventoryBalances.SingleAsync(x => x.WarehouseId == store.WarehouseId && x.ProductVariantId == store.VariantId);
        var auditCount = await db.POSAuditLogs.CountAsync(x => x.Action == "DELIVERY_CREATED");
        var paymentCount = await db.OrderPayments.CountAsync();
        var completedCount = await db.Orders.CountAsync(x => x.Status == OrderStatus.Completed);
        if (deliveries.Count != 1 || revisions.Count != 1 || auditCount != 1 || paymentCount != 0 || completedCount != 0 || stock.OnHandQty != 100)
            throw new InvalidOperationException("Phone delivery retry must create one transfer, never a payment or inventory movement.");
        return new { passed = true, orderId, deliveryCount = deliveries.Count, revisionCount = revisions.Count, auditCount,
            paymentCount, completedCount, stock.OnHandQty, sameRequestReplayedOnce = true,
            configuredMenusStoreCustomerPreferencesCatalogAndOtherStoreUnchanged = true };
    }
}
