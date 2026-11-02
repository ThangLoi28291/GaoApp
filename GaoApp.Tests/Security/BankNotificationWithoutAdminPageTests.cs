using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Web.Services.Acb;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

[Collection("SqlServerConcurrency")]
public sealed class BankNotificationWithoutAdminPageTests
{
    [Fact]
    public async Task Phone_and_POS_record_known_and_cashier_reviewed_transfers_before_any_admin_page_is_opened()
    {
        await using var app = await FullApplicationFixture.StartBankNotificationAsync();
        var store = app.Stores[0];
        var account = await app.AddAccountAsync(store, "*");
        using var cashier = await app.LoginAsync(account);
        await cashier.JsonAsync(HttpMethod.Post, "/admin/pos/shift/open", new { openingCash = 0, warehouseId = store.WarehouseId });
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        int deviceId, bankId;
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var variant = await db.ProductVariants.SingleAsync(x => x.Id == store.VariantId);
            variant.Price = 10000;
            variant.ProductVariantName = "HANG THU QR LOCAL";
            var bank = new StoreBankAccount { StoreId = store.StoreId, BankCode = "ACB", BankName = "ACB LOCAL TEST",
                AccountNumber = "123456789", AccountName = "CUA HANG THU", VietQrBankBin = "970416",
                ConfirmMode = BankQrConfirmMode.Manual, QrRenderMode = BankQrRenderMode.LocalEmvQr, IsActive = true, IsDefault = true };
            db.Add(bank); await db.SaveChangesAsync(); bankId = bank.Id;
            var device = new BankNotificationDevice { StoreId = store.StoreId, BankAccountId = bank.Id,
                Name = "LOCAL TEST PHONE", TokenHash = BankNotificationParser.Hash(token),
                BankAccountFingerprint = BankNotificationParser.AccountFingerprint(bank) };
            db.Add(device); await db.SaveChangesAsync(); deviceId = device.Id;
        }
        using var phone = app.Anonymous(store);
        phone.Http.DefaultRequestHeaders.Add("X-GaoApp-Notification-Device", $"{deviceId}:{token}");
        // Exercise the real trusted-proxy middleware and phone authentication.
        // This test-only host trusts loopback; production security is unchanged.
        phone.Http.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");

        async Task<int> Prepare()
        {
            var draft = await cashier.JsonAsync(HttpMethod.Post, "/admin/pos/cart/current/new", new { });
            var id = draft.GetProperty("orderId").GetInt32();
            await cashier.JsonAsync(HttpMethod.Post, $"/admin/pos/{id}/items?variantId={store.VariantId}&qty=1", new { });
            return id;
        }
        async Task<JsonElement> Upload(bool known, PosPaymentQrRequest qr)
        {
            var name = known ? "NGUYEN THI TU DONG" : "TRAN VAN KHACH CHUYEN";
            var reference = known ? "LOCALKNOWN12345" : "LOCALREVIEW12345";
            var pos = known ? $"POS-{qr.OrderId}" : "Chuyen tien";
            var amount = qr.Amount.ToString("0", CultureInfo.InvariantCulture);
            var upload = new BankNotificationUpload("mobile.acb.com.vn",
                $"Cửa hàng nhận tiền thành công +{amount} VND từ {name} tại ACB - {pos} GD {reference} MGD: {(known ? 81001 : 81002)}",
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            return await phone.JsonAsync(HttpMethod.Post, "/api/bank-notifications", upload);
        }
        async Task<object> Status()
        {
            await using var db = app.Database.CreateTenantContext(store.StoreId);
            return new {
                orders = await db.Orders.OrderBy(x => x.Id).Select(x => new { x.Id, x.Status, x.GrandTotal, x.PaidTotal, x.PaymentStatus }).ToListAsync(),
                qrs = await db.PosPaymentQrRequests.OrderBy(x => x.Id).Select(x => new { x.Id, x.OrderId, x.Amount, x.Status, x.PaymentId, x.NotificationConfirmationSource }).ToListAsync(),
                receipts = await db.Set<BankNotificationReceipt>().OrderBy(x => x.Id).Select(x => new { x.Id, x.OrderId, x.Amount, x.SenderName, x.PaymentId, x.AppliedWaitId, x.ConfirmationSource }).ToListAsync(),
                payments = await db.OrderPayments.OrderBy(x => x.Id).Select(x => new { x.Id, x.OrderId, x.Amount, x.Method }).ToListAsync()
            };
        }

        if (Environment.GetEnvironmentVariable("GAO_BANK_UI_SESSION") == "1")
        {
            using (var script = await cashier.Http.GetAsync("/Admin/js/pos/pos.bank-notifications.js"))
                Assert.Equal(HttpStatusCode.OK, script.StatusCode);
            await Prepare();
            var folder = Path.Combine(FullApplicationFixture.SourceRoot(), "TestResults", "bank-notifications-local");
            Directory.CreateDirectory(folder);
            var sessionId = Guid.NewGuid().ToString("N");
            // Ephemeral test account only, isolated SQL and localhost. Never include the phone key.
            await WriteJson(Path.Combine(folder, "session.json"), new { sessionId,
                url = $"http://{store.Host}:{app.Address.Port}/admin/pos", userName = account.Name, password = account.Password,
                storeId = store.StoreId, variantId = store.VariantId, terminalId = store.TerminalId, bankId });
            try
            {
            var seen = new HashSet<string>();
            var until = DateTime.UtcNow.AddMinutes(25);
            while (DateTime.UtcNow < until)
            {
                var commandPath = Path.Combine(folder, "command.json");
                if (File.Exists(commandPath))
                {
                    JsonElement command;
                    try { command = JsonDocument.Parse(await File.ReadAllTextAsync(commandPath)).RootElement.Clone(); }
                    catch (JsonException) { await Task.Delay(200); continue; }
                    var id = command.GetProperty("id").GetString()!;
                    if (command.GetProperty("sessionId").GetString() == sessionId && seen.Add(id))
                    {
                        var action = command.GetProperty("action").GetString();
                        try
                        {
                            object? result = null;
                            if (action == "prepare") result = new { orderId = await Prepare() };
                            else if (action is "upload-known" or "upload-missing")
                            {
                                await using var db = app.Database.CreateTenantContext(store.StoreId);
                                var shift = await db.POSShifts.SingleAsync(x => x.TerminalId == store.TerminalId && x.Status == POSShiftStatus.Open);
                                var qr = await db.PosPaymentQrRequests.Where(x => x.OrderId == shift.CurrentOrderId && x.Status == PosPaymentQrStatus.Pending && x.PaymentId == null).OrderByDescending(x => x.Id).FirstAsync();
                                result = await Upload(action == "upload-known", qr);
                            }
                            else if (action == "status") result = await Status();
                            else if (action == "finish")
                            {
                                await VerifyCompleted(app, store.StoreId);
                                await WriteJson(Path.Combine(folder, "verification.json"), new { sessionId, success = true,
                                    verifiedAtUtc = DateTime.UtcNow, result = await Status() });
                                await WriteJson(Path.Combine(folder, "response.json"), new { sessionId, id, success = true, result = await Status() });
                                return;
                            }
                            else throw new InvalidOperationException("Unknown test command.");
                            await WriteJson(Path.Combine(folder, "response.json"), new { sessionId, id, success = true, result });
                        }
                        catch (Exception ex)
                        { await WriteJson(Path.Combine(folder, "response.json"), new { sessionId, id, success = false, error = ex.Message }); }
                    }
                }
                await Task.Delay(250);
            }
            throw new TimeoutException("Local POS browser verification did not finish.");
            }
            finally
            {
                var sessionPath = Path.Combine(folder, "session.json");
                if (File.Exists(sessionPath))
                {
                    var ownedSession = JsonDocument.Parse(await File.ReadAllTextAsync(sessionPath)).RootElement.GetProperty("sessionId").GetString();
                    if (ownedSession == sessionId) File.Delete(sessionPath);
                }
            }
        }

        var orderIds = new List<int>();
        foreach (var known in new[] { true, false })
        {
            var orderId = await Prepare(); orderIds.Add(orderId);
            var qrJson = await cashier.JsonAsync(HttpMethod.Post, "/admin/pos/cart/current/payment-qr", new { clientRequestId = Guid.NewGuid(), bankAccountId = bankId });
            PosPaymentQrRequest qr;
            await using (var db = app.Database.CreateTenantContext(store.StoreId))
                qr = await db.PosPaymentQrRequests.AsNoTracking().SingleAsync(x => x.Id == qrJson.GetProperty("id").GetInt32());
            var received = await Upload(known, qr);
            Assert.True(received.GetProperty("accepted").GetBoolean());
            var suggestions = await cashier.JsonAsync(HttpMethod.Get, "/admin/pos/bank-notifications/pending");
            var item = Assert.Single(suggestions.EnumerateArray());
            Assert.Equal(known, item.GetProperty("automatic").GetBoolean());
            Assert.Equal(10000, item.GetProperty("amount").GetDecimal());
            Assert.Equal(known ? "NGUYEN THI TU DONG" : "TRAN VAN KHACH CHUYEN", item.GetProperty("senderName").GetString());
            var decision = new { receiptId = item.GetProperty("receiptId").GetInt32(), waitId = item.GetProperty("waitId").GetInt32(), automatic = known };
            if (!known)
            {
                using var rejected = await cashier.Http.PostAsJsonAsync("/admin/pos/bank-notifications/confirm", new { decision.receiptId, decision.waitId, automatic = true });
                Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
                await using var db = app.Database.CreateTenantContext(store.StoreId);
                Assert.Empty(await db.OrderPayments.Where(x => x.OrderId == orderId).ToListAsync());
            }
            var confirmed = await cashier.JsonAsync(HttpMethod.Post, "/admin/pos/bank-notifications/confirm", decision);
            Assert.True(confirmed.GetProperty("finalized").GetBoolean());
            Assert.Equal(0, confirmed.GetProperty("remainingAmount").GetDecimal());
            var repeated = await cashier.JsonAsync(HttpMethod.Post, "/admin/pos/bank-notifications/confirm", decision);
            Assert.Equal(JsonValueKind.Null, repeated.GetProperty("printUrl").ValueKind);
            Assert.Equal(received.GetProperty("receiptId").GetInt32(), (await Upload(known, qr)).GetProperty("receiptId").GetInt32());
            Assert.Empty((await cashier.JsonAsync(HttpMethod.Get, "/admin/pos/bank-notifications/pending")).EnumerateArray());
        }
        await VerifyCompleted(app, store.StoreId);

        // This is deliberately the first bank admin request in the whole test.
        var html = WebUtility.HtmlDecode(await cashier.Http.GetStringAsync("/admin/bank-notifications"));
        var history = Regex.Match(html, "<tbody id=\"bankNotificationHistoryRows\"[^>]*>(.*?)</tbody>", RegexOptions.Singleline).Groups[1].Value;
        var waiting = Regex.Match(html, "<tbody id=\"bankNotificationWaitingRows\"[^>]*>(.*?)</tbody>", RegexOptions.Singleline).Groups[1].Value;
        Assert.Contains("NGUYEN THI TU DONG", history); Assert.Contains("TRAN VAN KHACH CHUYEN", history);
        Assert.Contains("Tự xác nhận theo POS", history); Assert.Contains("Thu ngân chọn Đúng", history);
        foreach (var orderId in orderIds) Assert.Single(Regex.Matches(history, $"<span>POS-{orderId}</span>"));
        Assert.Contains("Không có khoản chuyển khoản đang chờ.", waiting);
        Assert.DoesNotContain("data-bank-approval", waiting);
        await VerifyCompleted(app, store.StoreId); // Viewing history cannot record more money.
    }

    private static async Task VerifyCompleted(FullApplicationFixture app, int storeId)
    {
        await using var db = app.Database.CreateTenantContext(storeId);
        var receipts = await db.Set<BankNotificationReceipt>().OrderBy(x => x.Id).ToListAsync();
        Assert.Equal(2, receipts.Count);
        Assert.Equal(new[] { "NotificationAuto", "NotificationCashier" }, receipts.Select(x => x.ConfirmationSource));
        var payments = await db.OrderPayments.ToListAsync(); Assert.Equal(2, payments.Count);
        Assert.All(payments, x => { Assert.Equal(10000, x.Amount); Assert.Equal(PaymentMethod.BankTransfer, x.Method); });
        foreach (var receipt in receipts)
        {
            var payment = Assert.Single(payments, x => x.Id == receipt.PaymentId);
            var qr = await db.PosPaymentQrRequests.SingleAsync(x => x.NotificationReceiptId == receipt.Id);
            Assert.Equal(payment.Id, qr.PaymentId); Assert.NotNull(receipt.AppliedWaitId);
            var order = await db.Orders.SingleAsync(x => x.Id == payment.OrderId);
            Assert.Equal(OrderStatus.Completed, order.Status); Assert.Equal(PaymentStatus.Paid, order.PaymentStatus);
            Assert.Equal(10000, order.PaidTotal); Assert.Equal(10000, order.GrandTotal);
        }
    }

    private static Task WriteJson(string path, object value)
        => File.WriteAllTextAsync(path, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
}
