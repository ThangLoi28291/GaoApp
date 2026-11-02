using GaoApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

// Disposable GUID database only; this probe never uses the configured store database.
internal static class PosEditorSingleOwnerBrowserFixture
{
    internal static Task<PosCheckoutMobileBrowserFixture.Seed> PrepareAsync(FullApplicationFixture app,
        FullApplicationFixture.StoreSeed store, FullApplicationFixture.Account account)
        => PosCheckoutMobileBrowserFixture.PrepareAsync(app, store, account);
    internal static Task<string> FingerprintAsync(FullApplicationFixture app, FullApplicationFixture.StoreSeed store)
        => PosCheckoutMobileBrowserFixture.FingerprintAsync(app, store);
    internal static async Task<object> VerifyAsync(FullApplicationFixture app, FullApplicationFixture.StoreSeed store, int orderId)
    {
        await using var db = app.Database.CreateTenantContext(store.StoreId);
        var order = await db.Orders.Include(x => x.Payments).SingleAsync(x => x.Id == orderId);
        var lines = await db.OrderLines.Where(x => x.OrderId == orderId && !x.IsDeleted).ToListAsync();
        var payments = order.Payments.Where(x => !x.IsDeleted).ToList();
        var stock = await db.InventoryBalances.SingleAsync(x => x.WarehouseId == store.WarehouseId && x.ProductVariantId == store.VariantId);
        var shift = await db.POSShifts.SingleAsync(x => x.Id == order.POSShiftId);
        if (order.Status != OrderStatus.Completed || order.Note != "UI06f — ghi chú dùng chung, giữ nguyên khi đổi màn hình."
            || order.Subtotal != 131000 || order.OrderDiscount != 1000 || order.DiscountTotal != 3000 || order.GrandTotal != 128000
            || order.PaidTotal != 128000 || order.BalanceDue != 0 || order.ChangeDue != 0 || lines.Count != 1 || lines[0].LineDiscount != 2000
            || payments.Count != 1 || payments[0].Amount != 128000 || payments[0].Method != PaymentMethod.Cash || !payments[0].ClientRequestId.HasValue
            || stock.OnHandQty != 99 || shift.CashSalesTotal != 128000 || shift.NonCashSalesTotal != 0)
            throw new InvalidOperationException("Shared editors and repeated input must persist the intended discounts once and collect one cash payment.");
        return new { passed = true, orderId, order.Note, order.Subtotal, order.OrderDiscount, order.DiscountTotal, order.GrandTotal,
            order.PaidTotal, order.BalanceDue, order.ChangeDue, stock.OnHandQty, shift.CashSalesTotal,
            payments = payments.Select(x => new { x.Id, x.Amount, x.ClientRequestId }),
            lines = lines.Select(x => new { x.Id, x.Quantity, x.LineDiscount }),
            configuredMenusStoreCustomerPreferencesCatalogAndOtherStoreUnchanged = true };
    }
}
