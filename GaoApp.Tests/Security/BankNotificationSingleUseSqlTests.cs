using System.Net;
using System.Net.Http.Json;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Web.Services.Acb;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

[Collection("SqlServerConcurrency")]
public sealed class BankNotificationSingleUseSqlTests
{
    [Fact]
    public async Task Notification_SQL_concurrent_confirmations_of_duplicate_bank_evidence_record_only_one_order_payment()
    {
        await using var app = await FullApplicationFixture.StartAsync(); var store = app.Stores[0];
        using var cashier = await app.LoginAsync(await app.AddAccountAsync(store, "*"));
        await cashier.JsonAsync(HttpMethod.Post, "/admin/pos/shift/open", new { openingCash = 0, warehouseId = store.WarehouseId });
        int receiptId, aliasId, firstWaitId, nextWaitId, firstOrderId, nextOrderId;
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var shift = await db.POSShifts.SingleAsync(x => x.TerminalId == store.TerminalId && x.Status == POSShiftStatus.Open);
            var variant = await db.ProductVariants.SingleAsync(x => x.Id == store.VariantId);
            Order NewOrder() => new() { StoreId = store.StoreId, POSShiftId = shift.Id, GrandTotal = 100, Subtotal = 100,
                Lines = [new OrderLine { StoreId = store.StoreId, VariantId = variant.Id, Variant = variant,
                    Quantity = 1, UnitPrice = 100, LineTotal = 100 }] };
            var first = NewOrder(); var next = NewOrder(); db.AddRange(first, next);
            var bank = new StoreBankAccount { StoreId = store.StoreId, BankCode = "ACB", BankName = "ACB", AccountNumber = "TEST-SINGLE-USE",
                AccountName = "TEST", ConfirmMode = BankQrConfirmMode.Manual, QrRenderMode = BankQrRenderMode.LocalEmvQr, IsActive = true };
            db.Add(bank); await db.SaveChangesAsync();
            var fingerprint = BankNotificationParser.AccountFingerprint(bank);
            var device = new BankNotificationDevice { StoreId = store.StoreId, BankAccountId = bank.Id, BankAccountFingerprint = fingerprint,
                Name = "Test phone", TokenHash = new string('A', 64) };
            db.Add(device); await db.SaveChangesAsync();
            PosPaymentQrRequest Qr(Order order) => new() { StoreId = store.StoreId, OrderId = order.Id, BankAccountId = bank.Id,
                Amount = 20, RequestCode = $"QR-{order.Id}-{Guid.NewGuid():N}", ConfirmMode = BankQrConfirmMode.Manual,
                QrRenderMode = BankQrRenderMode.LocalEmvQr, ExpireAtUtc = DateTime.UtcNow.AddMinutes(10) };
            var one = Qr(first); var two = Qr(next); db.AddRange(one, two); await db.SaveChangesAsync();
            BankNotificationWait Wait(Order order, PosPaymentQrRequest qr) => new() { StoreId = store.StoreId, QrId = qr.Id,
                OrderId = order.Id, BankAccountId = bank.Id, BankAccountFingerprint = fingerprint, TerminalId = store.TerminalId,
                ShiftId = shift.Id, Amount = 20, CartFingerprint = AcbPaymentPolicy.Fingerprint(order), ExpiresAtUtc = qr.ExpireAtUtc };
            var w1 = Wait(first, one); var w2 = Wait(next, two); db.AddRange(w1, w2); await db.SaveChangesAsync();
            var raw = "+20 VND từ KHACH THU tại ACB Chuyen tien GD SINGLEUSE12345 MGD: 31045";
            BankNotificationReceipt Receipt(string content, string code, char key) => new() { StoreId = store.StoreId, DeviceId = device.Id,
                BankAccountId = bank.Id, Amount = 20, Content = content, TransactionCode = code, EvidenceKey = new string(key, 64),
                PayloadHash = BankNotificationParser.Hash(content), PostedAtUtc = DateTime.UtcNow };
            var receipt = Receipt(raw, "GD:SINGLEUSE12345", 'B');
            var alias = Receipt(raw + " FT998877665544", "FT998877665544", 'C');
            db.AddRange(receipt, alias); await db.SaveChangesAsync();
            receiptId = receipt.Id; aliasId = alias.Id; firstWaitId = w1.Id; nextWaitId = w2.Id;
            firstOrderId = first.Id; nextOrderId = next.Id;
        }
        // Distinct HTTP requests use distinct EF contexts and real SQL session locks.
        var responses = await Task.WhenAll(
            cashier.Http.PostAsJsonAsync("/admin/pos/bank-notifications/confirm", new { receiptId, waitId = firstWaitId, automatic = false }),
            cashier.Http.PostAsJsonAsync("/admin/pos/bank-notifications/confirm", new { receiptId = aliasId, waitId = nextWaitId, automatic = false }));
        try
        {
            var details = await Task.WhenAll(responses.Select(x => x.Content.ReadAsStringAsync()));
            Assert.True(responses.Count(x => x.StatusCode == HttpStatusCode.OK) == 1, string.Join(" | ", details));
            Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict);
            await using var db = app.Database.CreateTenantContext(store.StoreId);
            var payment = Assert.Single(await db.OrderPayments.Where(x => x.OrderId == firstOrderId || x.OrderId == nextOrderId).ToListAsync());
            Assert.Equal(20, payment.Amount);
            var used = Assert.Single(await db.Set<BankNotificationReceipt>().Where(x => x.PaymentId != null).ToListAsync());
            Assert.Equal(payment.Id, used.PaymentId);
            var pending = await cashier.JsonAsync(HttpMethod.Get, "/admin/pos/bank-notifications/pending");
            Assert.Empty(pending.EnumerateArray());
            // Retrying the original successful command returns its result without adding money.
            var index = Array.FindIndex(responses, x => x.StatusCode == HttpStatusCode.OK);
            await cashier.JsonAsync(HttpMethod.Post, "/admin/pos/bank-notifications/confirm", new {
                receiptId = index == 0 ? receiptId : aliasId, waitId = index == 0 ? firstWaitId : nextWaitId, automatic = false });
            Assert.Single(await db.OrderPayments.Where(x => x.OrderId == firstOrderId || x.OrderId == nextOrderId).ToListAsync());
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }
}
