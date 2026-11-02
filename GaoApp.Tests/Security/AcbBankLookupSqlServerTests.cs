using System.Net;
using System.Net.Http.Json;
using GaoApp.Domain.Entities;
using GaoApp.Web.Services.Acb;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

[Collection("R1FinalDatabasePreflight")]
public sealed class AcbBankLookupSqlServerTests
{
    private sealed class Bank : HttpMessageHandler
    {
        public List<Uri> Queries = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            object body;
            if (request.RequestUri!.AbsolutePath.EndsWith("/token")) body = new { access_token = "isolated-test-token" };
            else
            {
                Assert.Equal(HttpMethod.Get, request.Method); Queries.Add(request.RequestUri);
                body = new { responseStatus = new { responseCode = "00000000" }, responseBody = new {
                    pagination = new { totalRows = 1, totalPages = 1 }, orders = new[] { new {
                        orderId = "BANK_ONLY", merchantId = "STORE_ONE", traceNumber = "fixture-trace", amount = 70000,
                        status = "COMPLETED", createdDatetime = "2026-10-12T12:00:00.000+0700",
                        transactionDetail = new[] { new { transactionNumber = 1234, transactionAmount = 70000,
                            transactionStatus = "COMPLETED", transactionContent = "isolated-bank-test", origPostDate = "2026-10-12" } }
                    } }
                } };
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(body) });
        }
    }

    [Fact]
    public async Task Bank_lookup_SQL_reads_real_receipts_by_store_and_real_page_renders_both_sources_and_protected_endpoint()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        var bank = new Bank();
        var protocol = new AcbProtocol(new HttpClient(bank), new EphemeralDataProtectionProvider());
        foreach (var target in app.Stores)
        {
            await using var db = app.Database.CreateTenantContext(target.StoreId);
            var account = new StoreBankAccount { StoreId = target.StoreId, BankCode = "ACB", BankName = "ACB",
                AccountNumber = "TEST" + target.StoreId, AccountName = "Isolated test", IsActive = true };
            db.Add(account); await db.SaveChangesAsync();
            db.Add(new StoreAcbSettings { StoreId = target.StoreId, BankAccountId = account.Id, Enabled = true,
                MerchantId = target.StoreId == store.StoreId ? "STORE_ONE" : "foreign-private-merchant",
                ClientId = "isolated-test-client", ClientSecretProtected = protocol.Protect(target.StoreId, "isolated-test-secret") });
            db.Add(new AcbCallbackReceipt { StoreId = target.StoreId, ClientRequestId = Guid.NewGuid().ToString(),
                RequestCode = "TRANSACTION_UPDATE", Page = 1, TotalPages = 1, PayloadJson = "{}",
                NextAttemptAtUtc = DateTime.UtcNow.AddDays(10), Items = [new() {
                    StoreId = target.StoreId, ProviderOrderId = "BANK_ONLY", RequestCode = "TRANSACTION_UPDATE",
                    Amount = 70000, BusinessDate = new(2026, 10, 12),
                    Content = target.StoreId == store.StoreId ? "own-callback-data" : "foreign-private-callback"
                }] });
            await db.SaveChangesAsync();
        }
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var result = await new AcbBankLookupService(db, protocol).ReadAsync(new(2026, 10, 12), new(2026, 10, 12), 1, default);
            Assert.Equal(1, Assert.Single(result.Orders).CallbackCount);
            Assert.Null(result.Orders[0].OrderId);
            Assert.Contains("merchantId=STORE_ONE", Assert.Single(bank.Queries).Query);
            Assert.Empty(await db.Orders.ToListAsync());
            Assert.Empty(await db.OrderPayments.ToListAsync());
            Assert.Empty(await db.Set<AcbPaymentTransaction>().ToListAsync());
            Assert.Equal(0, Assert.Single(await db.Set<AcbCallbackReceipt>().ToListAsync()).Attempts);
        }
        using var admin = await app.LoginAsync(await PosShiftAdministrationSqlServerTests.AddAdminAsync(app, store));
        using var page = await admin.Http.GetAsync("/admin/acb/reconciliation?from=2026-10-12&to=2026-10-12");
        var html = await page.Content.ReadAsStringAsync();
        Assert.True(page.IsSuccessStatusCode, html);
        var decoded = WebUtility.HtmlDecode(html);
        Assert.Contains("Tra cứu trực tiếp ACB", decoded);
        Assert.Contains("Callback đã nhận", decoded);
        Assert.Contains("Xem callback", decoded);
        Assert.Contains("Nguồn: API ngân hàng", decoded);
        Assert.Contains("Nguồn: thông báo ACB gửi về", decoded);
        Assert.Contains("action=\"/admin/acb/reconciliation/bank-lookup\"", html);
        Assert.Contains("__RequestVerificationToken", html);
        Assert.Contains("own-callback-data", html);
        Assert.DoesNotContain("foreign-private", html);
        var output = Path.Combine(FullApplicationFixture.SourceRoot(), "Logs", "acb-bank-lookup-20261012");
        Directory.CreateDirectory(output);
        await File.WriteAllTextAsync(Path.Combine(output, "reconciliation-page.html"), html);
        admin.Http.DefaultRequestHeaders.Remove("RequestVerificationToken");
        using var noToken = await admin.Http.PostAsync("/admin/acb/reconciliation/bank-lookup", new FormUrlEncodedContent(new Dictionary<string, string>()));
        Assert.Equal(HttpStatusCode.BadRequest, noToken.StatusCode);
        using var denied = await app.LoginAsync(await app.AddAccountAsync(store));
        using var deniedResponse = await denied.Http.PostAsync("/admin/acb/reconciliation/bank-lookup", new FormUrlEncodedContent(new Dictionary<string, string>()));
        Assert.Equal(HttpStatusCode.Forbidden, deniedResponse.StatusCode);
    }
}
