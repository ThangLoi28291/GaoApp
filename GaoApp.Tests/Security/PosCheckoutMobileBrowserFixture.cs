using System.Text.Json;
using GaoApp.Application.Common.Security;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

// Only used with FullApplicationFixture's disposable GUID database.
internal static class PosCheckoutMobileBrowserFixture
{
    internal sealed record Seed(int OrderId, FullApplicationFixture.Account Limited,
        FullApplicationFixture.Account Denied, FullApplicationFixture.Account Other);

    internal static async Task<Seed> PrepareAsync(FullApplicationFixture app, FullApplicationFixture.StoreSeed store,
        FullApplicationFixture.Account account)
    {
        await using var db = app.Database.CreateTenantContext(store.StoreId);
        var variant = await db.ProductVariants.Include(x => x.Product).SingleAsync(x => x.Id == store.VariantId);
        variant.ProductVariantName = "Sản phẩm kiểm tra thanh toán";
        variant.Price = variant.Product.BasePrice = 131000;
        var customer = new Customer { StoreId = store.StoreId, Name = "Khách kiểm tra thanh toán", Code = "CHECKOUT-MOBILE", AskBeforePrintingReceipt = true };
        db.Add(customer); await db.SaveChangesAsync();
        using var client = await app.LoginAsync(account);
        var draft = await client.JsonAsync(HttpMethod.Post, "/admin/pos/cart/current/new", new { });
        var orderId = draft.GetProperty("orderId").GetInt32();
        await client.JsonAsync(HttpMethod.Post, $"/admin/pos/{orderId}/items?variantId={store.VariantId}&qty=1");
        await client.JsonAsync(HttpMethod.Post, $"/admin/pos/cart/current/customer/{customer.Id}", new { repriceExistingLines = true });
        return new(orderId,
            await app.AddAccountAsync(store, PermissionCodes.Pos.Order.View, PermissionCodes.Pos.Order.Create),
            await app.AddAccountAsync(store, PermissionCodes.Admin.DashboardView),
            await app.AddAccountAsync(app.Stores[1], "*"));
    }

    internal static async Task<string> FingerprintAsync(FullApplicationFixture app, FullApplicationFixture.StoreSeed store)
    {
        await using var db = app.Database.CreateTenantContext(store.StoreId);
        return JsonSerializer.Serialize(new {
            menus = await db.AdminMenuItems.IgnoreQueryFilters().AsNoTracking().OrderBy(x => x.Id)
                .Select(x => new { x.Id, x.StoreId, x.ParentId, x.Title, x.Url, x.PermissionCode, x.IsActive, x.SortOrder, x.RowVersion }).ToListAsync(),
            stores = await db.Stores.IgnoreQueryFilters().AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.Name, x.IsActive, x.RowVersion }).ToListAsync(),
            terminals = await db.POSTerminals.IgnoreQueryFilters().AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.StoreId, x.Code, x.Name, x.RowVersion }).ToListAsync(),
            prices = await db.ProductVariants.IgnoreQueryFilters().AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.Price, x.RowVersion }).ToListAsync(),
            customers = await db.Customers.IgnoreQueryFilters().AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.StoreId, x.PriceTier, x.AskBeforePrintingReceipt, x.RowVersion }).ToListAsync(),
            otherStock = await db.InventoryBalances.IgnoreQueryFilters().AsNoTracking().Where(x => x.StoreId != store.StoreId).OrderBy(x => x.Id).Select(x => new { x.Id, x.OnHandQty, x.RowVersion }).ToListAsync(),
            otherOrders = await db.Orders.IgnoreQueryFilters().AsNoTracking().Where(x => x.StoreId != store.StoreId).OrderBy(x => x.Id).Select(x => new { x.Id, x.Status, x.RowVersion }).ToListAsync()
        });
    }

    internal static async Task<object> VerifyAsync(FullApplicationFixture app, FullApplicationFixture.StoreSeed store, int orderId)
    {
        await using var db = app.Database.CreateTenantContext(store.StoreId);
        var order = await db.Orders.Include(x => x.Payments).SingleAsync(x => x.Id == orderId);
        var payments = order.Payments.Where(x => !x.IsDeleted).OrderBy(x => x.Id).ToList();
        var stock = await db.InventoryBalances.SingleAsync(x => x.WarehouseId == store.WarehouseId && x.ProductVariantId == store.VariantId);
        var shift = await db.POSShifts.SingleAsync(x => x.Id == order.POSShiftId);
        if (order.Status != OrderStatus.Completed || order.GrandTotal != 131000 || order.PaidTotal != 151000
            || order.ChangeDue != 20000 || order.BalanceDue != 0 || stock.OnHandQty != 99
            || payments.Count != 3 || payments.Count(x => x.Method == PaymentMethod.Card && x.Amount == 31000) != 1
            || payments.Count(x => x.Method == PaymentMethod.Cash && x.Amount == 20000) != 1
            || payments.Count(x => x.Method == PaymentMethod.Cash && x.Amount == 100000) != 1
            || payments.Any(x => !x.ClientRequestId.HasValue) || payments.Select(x => x.ClientRequestId).Distinct().Count() != 3
            || shift.CashSalesTotal != 100000 || shift.NonCashSalesTotal != 31000)
            throw new InvalidOperationException("Payment UI must collect each intended installment once and post the sale exactly once.");
        return new { passed = true, orderId, order.Status, order.GrandTotal, order.PaidTotal, order.ChangeDue, order.BalanceDue,
            stock.OnHandQty, shift.CashSalesTotal, shift.NonCashSalesTotal,
            payments = payments.Select(x => new { x.Id, x.Amount, x.Method, x.ClientRequestId, x.ReferenceCode }),
            configuredMenusStoreCustomerPreferencesCatalogAndOtherStoreUnchanged = true };
    }
}
