using System.Text.Json;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

// Local QR only, in FullApplicationFixture's disposable GUID database.
internal static class PosTransferMobileBrowserFixture
{
    internal sealed record Seed(int OrderId, int QrId, object Limited, object Denied, object Other);
    internal static async Task<Seed> PrepareAsync(FullApplicationFixture app, FullApplicationFixture.StoreSeed store,
        FullApplicationFixture.Account account)
    {
        var checkout = await PosCheckoutMobileBrowserFixture.PrepareAsync(app, store, account);
        await using var db = app.Database.CreateTenantContext(store.StoreId);
        db.Add(new StoreBankAccount { StoreId = store.StoreId, BankCode = "ACB", BankName = "Ngân hàng thử nghiệm",
            AccountNumber = "123456789", AccountName = "CUA HANG THU NGHIEM", VietQrBankBin = "970416",
            IsActive = true, IsDefault = true, QrRenderMode = BankQrRenderMode.LocalEmvQr, ConfirmMode = BankQrConfirmMode.Manual });
        await db.SaveChangesAsync();
        using var client = await app.LoginAsync(account);
        var qr = await client.JsonAsync(HttpMethod.Post, "/admin/pos/cart/current/payment-qr", new { clientRequestId = Guid.NewGuid(), amount = 31000 });
        var qrId = qr.GetProperty("id").GetInt32();
        var saved = await db.PosPaymentQrRequests.SingleAsync(x => x.Id == qrId);
        saved.CreatedAtUtc = DateTime.UtcNow.AddMinutes(-3); // Exercise the original server-timed manual takeover, without sleeping.
        await db.SaveChangesAsync();
        return new(checkout.OrderId, qrId, checkout.Limited, checkout.Denied, checkout.Other);
    }
    internal static Task<string> FingerprintAsync(FullApplicationFixture app, FullApplicationFixture.StoreSeed store)
        => PosCheckoutMobileBrowserFixture.FingerprintAsync(app, store);
    internal static async Task<object> VerifyAsync(FullApplicationFixture app, FullApplicationFixture.StoreSeed store, int orderId)
    {
        await using var db = app.Database.CreateTenantContext(store.StoreId);
        var order = await db.Orders.Include(x => x.Payments).SingleAsync(x => x.Id == orderId);
        var payments = order.Payments.Where(x => !x.IsDeleted).ToList();
        var qrs = await db.PosPaymentQrRequests.Where(x => x.OrderId == orderId).OrderBy(x => x.Id).ToListAsync();
        var stock = await db.InventoryBalances.SingleAsync(x => x.WarehouseId == store.WarehouseId && x.ProductVariantId == store.VariantId);
        if (order.Status != OrderStatus.Draft || order.GrandTotal != 131000 || order.PaidTotal != 31000 || order.BalanceDue != 100000
            || stock.OnHandQty != 100 || payments.Count != 1 || payments[0].Amount != 31000 || payments[0].Method != PaymentMethod.BankTransfer
            || qrs.Count != 3 || qrs[0].Status != PosPaymentQrStatus.ManualConfirmed || qrs[0].PaymentId != payments[0].Id
            || qrs[1].Status != PosPaymentQrStatus.Cancelled || qrs[1].PaymentId != null || qrs[2].Status != PosPaymentQrStatus.Pending || qrs[2].PaymentId != null)
            throw new InvalidOperationException("QR reopen/retry/cancel must not collect twice or turn an unpaid QR into money.");
        return new { passed = true, orderId, order.Status, order.GrandTotal, order.PaidTotal, order.BalanceDue, stock.OnHandQty,
            payments = payments.Select(x => new { x.Id, x.Amount, x.Method, x.ReferenceCode }),
            qrs = qrs.Select(x => new { x.Id, x.Amount, x.Status, x.ClientRequestId, x.PaymentId }),
            configuredMenusStoreCustomerPreferencesCatalogAndOtherStoreUnchanged = true };
    }
}
