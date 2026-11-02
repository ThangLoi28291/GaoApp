using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GaoApp.Application.Common.Security;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Repositories.Invoices;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

[Collection("SqlServerConcurrency")]
public sealed class InvoiceBuyerSelfServiceSecurityTests
{
    private const string PrivateName = "PRIVATE-BUYER-ISSUE4";
    private const string PrivateEmail = "private-issue4@example.test";
    private const string PrivateCitizen = "012345678901";

    [Fact]
    public async Task Real_HTTP_old_expired_links_never_return_PII_and_receipt_reprint_still_works_without_QR()
    {
        await using var app = await FullApplicationFixture.StartInvoicePrivacyAsync();
        var store = app.Stores[0];
        var account = await app.AddAccountAsync(store, "*");
        using var employee = await app.LoginAsync(account);
        var orderId = await Seed(app, account);
        var link = await ReceiptLink(employee, orderId);
        var second = await ReceiptLink(employee, orderId);
        Assert.NotEqual(link, second);
        using var buyer = app.Anonymous(store);
        using (var active = await buyer.Http.GetAsync(link))
        {
            Assert.Equal(HttpStatusCode.OK, active.StatusCode);
            Protect(active);
            var html = await active.Content.ReadAsStringAsync();
            Assert.Contains(PrivateName, html);
            Assert.Contains(PrivateEmail, html);
            Assert.Contains("invoice-buyer-self-service.js", html);
        }
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            (await db.Orders.SingleAsync(x => x.Id == orderId)).CompletedAtUtc = DateTime.UtcNow.AddHours(-3);
            // Legacy data with a longer stored expiry must still respect completed-at + 2h.
            foreach (var row in await db.InvoiceBuyerSelfServiceRequests.ToListAsync()) row.ExpiresAtUtc = DateTime.UtcNow.AddDays(1);
            await db.SaveChangesAsync();
        }
        using (var expired = await buyer.Http.GetAsync(link))
        {
            Assert.Equal(HttpStatusCode.Gone, expired.StatusCode);
            Protect(expired); Empty(await expired.Content.ReadAsStringAsync());
        }
        var anti = await Antiforgery(buyer, employee, orderId, link, allowClosed: true);
        using (var post = await buyer.Http.PostAsync(link, Form(anti, "ATTACKER-POSTED-PII")))
        {
            Assert.Equal(HttpStatusCode.Gone, post.StatusCode);
            Protect(post);
            var html = await post.Content.ReadAsStringAsync(); Empty(html);
            Assert.DoesNotContain("ATTACKER-POSTED-PII", html);
        }
        var data = await employee.JsonAsync(HttpMethod.Get, $"/admin/pos/receipt/{orderId}?data=true");
        Assert.Equal(JsonValueKind.Null, data.GetProperty("invoiceBuyerSelfServiceUrl").ValueKind);
        using (var replace = await employee.Http.PostAsync($"/admin/pos/receipt/{orderId}/buyer-link/replace", new FormUrlEncodedContent([])))
            Assert.Equal(HttpStatusCode.Conflict, replace.StatusCode);
        await using var check = app.Database.CreateTenantContext(store.StoreId);
        Assert.Equal(2, await check.InvoiceBuyerSelfServiceRequests.CountAsync());
        Assert.All(await check.InvoiceHeads.ToListAsync(), head => Assert.Equal(PrivateName, head.BuyerName));
        Assert.Contains("/invoice-info/[redacted]", app.LogOutput);
        Assert.DoesNotContain(Token(link), app.LogOutput, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Token(second), app.LogOutput, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Real_HTTP_submit_updates_sibling_heads_and_replacement_revokes_all_copies_with_same_deadline()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var account = await app.AddAccountAsync(app.Stores[0], "*");
        using var employee = await app.LoginAsync(account);
        var orderId = await Seed(app, account, siblings: true);
        var first = await ReceiptLink(employee, orderId);
        var second = await ReceiptLink(employee, orderId);
        using var buyer = app.Anonymous(account.Store);
        var anti = await Antiforgery(buyer, employee, orderId, first);
        using (var update = await buyer.Http.PostAsync(first, Form(anti, "UPDATED-ISSUE4")))
            Assert.Equal(HttpStatusCode.Redirect, update.StatusCode);
        DateTime deadline;
        await using (var db = app.Database.CreateTenantContext(account.Store.StoreId))
        {
            var heads = await db.InvoiceHeads.ToListAsync(); Assert.Equal(2, heads.Count);
            Assert.All(heads, head => Assert.Equal("UPDATED-ISSUE4", head.BuyerName));
            Assert.NotNull((await db.InvoiceBuyerSelfServiceRequests.OrderBy(x => x.Id).FirstAsync()).LastSubmittedAtUtc);
            Assert.Empty(await db.InvoiceBuyerProfiles.ToListAsync());
            deadline = (await db.InvoiceBuyerSelfServiceRequests.FirstAsync()).ExpiresAtUtc;
        }
        var replacement = await employee.JsonAsync(HttpMethod.Post, $"/admin/pos/receipt/{orderId}/buyer-link/replace", new { });
        Assert.Equal(deadline, replacement.GetProperty("expiresAtUtc").GetDateTime());
        Assert.EndsWith("Z", replacement.GetProperty("expiresAtUtc").GetString());
        var newLink = PathOf(replacement.GetProperty("url").GetString()!);
        foreach (var old in new[] { first, second })
        {
            using var get = await buyer.Http.GetAsync(old); Assert.Equal(HttpStatusCode.NotFound, get.StatusCode); Protect(get); Empty(await get.Content.ReadAsStringAsync());
            using var post = await buyer.Http.PostAsync(old, Form(anti, "UNAUTHORIZED")); Assert.Equal(HttpStatusCode.NotFound, post.StatusCode);
        }
        using (var active = await buyer.Http.GetAsync(newLink))
        {
            Assert.Equal(HttpStatusCode.OK, active.StatusCode);
            Assert.Contains("UPDATED-ISSUE4", await active.Content.ReadAsStringAsync());
        }
        using var foreign = app.Anonymous(app.Stores[1]);
        using (var cross = await foreign.Http.GetAsync(newLink)) { Assert.Equal(HttpStatusCode.NotFound, cross.StatusCode); Empty(await cross.Content.ReadAsStringAsync()); }
        var foreignAnti = await Antiforgery(foreign, employee, orderId, newLink, allowClosed: true);
        using (var crossPost = await foreign.Http.PostAsync(newLink, Form(foreignAnti, "CROSS-STORE-UPDATE")))
            Assert.Equal(HttpStatusCode.NotFound, crossPost.StatusCode);
        using var anon = app.Anonymous(account.Store);
        using (var unauthorized = await anon.Http.PostAsync($"/admin/pos/receipt/{orderId}/buyer-link/replace", new FormUrlEncodedContent([])))
            Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        using var viewer = await app.LoginAsync(await app.AddAccountAsync(account.Store, PermissionCodes.Pos.Order.View));
        using (var denied = await viewer.Http.PostAsync($"/admin/pos/receipt/{orderId}/buyer-link/replace", new FormUrlEncodedContent([])))
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using (var noCsrf = app.Anonymous(account.Store))
        using (var rejected = await noCsrf.Http.PostAsync(newLink, Form("invalid", "ATTACKER")))
        { Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode); Protect(rejected); }
    }

    [Fact]
    public async Task Real_HTTP_cancelled_rerouted_locked_processing_and_issued_links_return_only_generic_notice()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var account = await app.AddAccountAsync(app.Stores[0], "*");
        using var employee = await app.LoginAsync(account);
        var orderId = await Seed(app, account);
        var link = await ReceiptLink(employee, orderId);
        using var buyer = app.Anonymous(account.Store);
        var anti = await Antiforgery(buyer, employee, orderId, link);
        foreach (var state in new[] { "cancelled", "route", "locked", "issuing", "issued" })
        {
            await using (var db = app.Database.CreateTenantContext(account.Store.StoreId))
            {
                var order = await db.Orders.SingleAsync(x => x.Id == orderId);
                order.Status = state == "cancelled" ? OrderStatus.Cancelled : OrderStatus.Completed;
                order.InvoiceIssuanceRoute = state == "route" ? InvoiceIssuanceRoute.Automatic : InvoiceIssuanceRoute.Manual;
                var head = await db.InvoiceHeads.SingleAsync();
                head.IsLocked = state == "locked";
                head.ProviderStatus = state == "issuing" ? InvoiceProviderStatus.Issuing : state == "issued" ? InvoiceProviderStatus.Issued : InvoiceProviderStatus.LocalDraft;
                await db.SaveChangesAsync();
            }
            using var get = await buyer.Http.GetAsync(link);
            Assert.Equal(HttpStatusCode.Conflict, get.StatusCode); Protect(get); Empty(await get.Content.ReadAsStringAsync());
            using var post = await buyer.Http.PostAsync(link, Form(anti, "UNAUTHORIZED"));
            Assert.Equal(HttpStatusCode.Conflict, post.StatusCode); Empty(await post.Content.ReadAsStringAsync());
        }
        await using var check = app.Database.CreateTenantContext(account.Store.StoreId);
        Assert.Equal(PrivateName, (await check.InvoiceHeads.SingleAsync()).BuyerName);
    }

    [Fact]
    public async Task Real_SQL_store_lock_prevents_submission_racing_issuance_or_revocation()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var account = await app.AddAccountAsync(app.Stores[0], "*");
        using var employee = await app.LoginAsync(account);
        var orderId = await Seed(app, account);
        var link = await ReceiptLink(employee, orderId);
        using var buyer = app.Anonymous(account.Store);
        var anti = await Antiforgery(buyer, employee, orderId, link);
        foreach (var revoke in new[] { false, true })
        {
            await using var db = app.Database.CreateTenantContext(account.Store.StoreId);
            await using var tx = await db.Database.BeginTransactionAsync();
            var repo = new InvoiceBuyerSelfServiceRepository(db);
            await repo.LockStoreAsync(account.Store.StoreId);
            var pending = buyer.Http.PostAsync(link, Form(anti, "RACING-UPDATE"));
            await Task.Delay(200);
            Assert.False(pending.IsCompleted, "Public update must wait for the issuance store lock.");
            (await db.InvoiceHeads.SingleAsync()).ProviderStatus = revoke ? InvoiceProviderStatus.LocalDraft : InvoiceProviderStatus.Issued;
            if (revoke) await repo.RevokeForOrderAsync(account.Store.StoreId, orderId, DateTime.UtcNow);
            await db.SaveChangesAsync(); await tx.CommitAsync();
            using var result = await pending;
            Assert.Equal(revoke ? HttpStatusCode.NotFound : HttpStatusCode.Conflict, result.StatusCode);
            Empty(await result.Content.ReadAsStringAsync());
        }
        await using var check = app.Database.CreateTenantContext(account.Store.StoreId);
        Assert.Equal(PrivateName, (await check.InvoiceHeads.SingleAsync()).BuyerName);
        Assert.Null((await check.InvoiceBuyerSelfServiceRequests.SingleAsync()).LastSubmittedAtUtc);
    }

    internal static async Task<int> Seed(FullApplicationFixture app, FullApplicationFixture.Account account, bool siblings = false)
    {
        var orderId = await InvoiceOrderDraftSqlServerTests.SeedOrder(app, account, "PRIVATE-ORDER-ISSUE4", InvoiceIssuanceRoute.Manual);
        await using var db = app.Database.CreateTenantContext(account.Store.StoreId);
        var legal = await db.LegalEntities.SingleAsync();
        void Add(int legalId) => db.InvoiceHeads.Add(new InvoiceHead { StoreId = account.Store.StoreId,
            OrderId = orderId, LegalEntityId = legalId, BuyerType = "Individual", BuyerName = PrivateName,
            BuyerEmail = PrivateEmail, BuyerCitizenId = PrivateCitizen, BuyerAddress = "PRIVATE-ADDRESS-ISSUE4", BuyerPhone = "0901234567",
            ProviderStatus = InvoiceProviderStatus.LocalDraft, GrandTotal = 10000 });
        Add(legal.Id);
        if (siblings)
        {
            var second = new LegalEntity { StoreId = account.Store.StoreId, Code = "ISSUE4-SECOND", Name = "Second entity", LegalName = "Second entity", IsActive = true, SalePriority = 2 };
            db.LegalEntities.Add(second); await db.SaveChangesAsync(); Add(second.Id);
        }
        await db.SaveChangesAsync();
        return orderId;
    }

    [Fact]
    public async Task Real_SQL_update_is_rolled_back_if_expiry_is_reached_during_buyer_save()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var account = await app.AddAccountAsync(app.Stores[0], "*");
        var orderId = await Seed(app, account);
        await using var db = app.Database.CreateTenantContext(account.Store.StoreId);
        var clock = new MutableClock(DateTime.UtcNow.AddHours(1));
        var deadline = (await db.Orders.SingleAsync(x => x.Id == orderId)).CompletedAtUtc!.Value.AddHours(2);
        var buyer = new ExpiringBuyer(db, clock, deadline);
        var service = new GaoApp.Application.Services.Invoices.InvoiceBuyerSelfServiceService(
            new InvoiceBuyerSelfServiceRepository(db), new GaoApp.Infrastructure.Repositories.Orders.OrderRepository(db),
            buyer, new GaoApp.Infrastructure.Data.AppUnitOfWork(db), new FixedTenant(account.Store.StoreId), clock);
        var link = await service.CreateLinkAsync(orderId);
        Assert.True(link.IsSuccess);
        var result = await service.SubmitAsync(link.Value.Token, new() { BuyerName = "TOO-LATE" });
        Assert.False(result.IsSuccess);
        Assert.Equal(1, buyer.Calls);
        await using var check = app.Database.CreateTenantContext(account.Store.StoreId);
        Assert.Equal(PrivateName, (await check.InvoiceHeads.SingleAsync()).BuyerName);
        Assert.Null((await check.InvoiceBuyerSelfServiceRequests.SingleAsync()).LastSubmittedAtUtc);
    }

    [Fact]
    public async Task Real_Chrome_public_form_expiry_back_navigation_and_link_replacement()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var account = await app.AddAccountAsync(app.Stores[0], "*");
        using var employee = await app.LoginAsync(account);
        var orderId = await Seed(app, account);
        var link = await ReceiptLink(employee, orderId);
        var root = FullApplicationFixture.SourceRoot();
        var start = new ProcessStartInfo("node") { WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.Environment["NODE_PATH"] = Path.Combine(root, "Logs/pos-offline-browser-deps/node_modules");
        start.ArgumentList.Add(Path.Combine(root, "GaoApp.Tests.Browser/invoice-buyer-self-service.browser.cjs"));
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new
        {
            baseUrl = $"http://{account.Store.Host}:{app.Address.Port}", user = account.Name, password = account.Password,
            terminalId = account.Store.TerminalId, orderId, link, evidenceRoot = Path.Combine(root, ".artifacts/security-issue4/browser")
        }));
        process.StandardInput.Close();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { if (!process.HasExited) process.Kill(true); throw; }
        Assert.True(process.ExitCode == 0, await output + await error);
        await using var verify = app.Database.CreateTenantContext(account.Store.StoreId);
        Assert.Equal("BROWSER-UPDATED-ISSUE4", (await verify.InvoiceHeads.SingleAsync()).BuyerName);
        Assert.Equal(1, await verify.InvoiceBuyerSelfServiceRequests.CountAsync(x => x.RevokedAtUtc == null));
    }

    private static async Task<string> ReceiptLink(FullApplicationFixture.Client employee, int orderId)
    {
        var data = await employee.JsonAsync(HttpMethod.Get, $"/admin/pos/receipt/{orderId}?data=true");
        return PathOf(data.GetProperty("invoiceBuyerSelfServiceUrl").GetString()!);
    }
    private static string PathOf(string url) => new Uri(url).PathAndQuery;
    private static string Token(string path) => path.Split('/').Last();
    private static FormUrlEncodedContent Form(string anti, string name) => new(new Dictionary<string, string>
    { ["__RequestVerificationToken"] = anti, ["BuyerType"] = "Individual", ["BuyerName"] = name,
      ["BuyerAddress"] = "Test address", ["BuyerCitizenId"] = PrivateCitizen });
    private static async Task<string> Antiforgery(FullApplicationFixture.Client buyer, FullApplicationFixture.Client employee,
        int orderId, string link, bool allowClosed = false)
    {
        // The public login page issues an anonymous antiforgery token without disclosing invoice data.
        var html = await buyer.Http.GetStringAsync(allowClosed ? "/admin/account/login" : link);
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success); return WebUtility.HtmlDecode(match.Groups[1].Value);
    }
    private static void Protect(HttpResponseMessage response)
    {
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.Contains("noindex", response.Headers.GetValues("X-Robots-Tag").Single());
    }
    private static void Empty(string html)
    {
        foreach (var secret in new[] { PrivateName, PrivateEmail, PrivateCitizen, "PRIVATE-ADDRESS-ISSUE4", "PRIVATE-ORDER-ISSUE4", "BuyerName", "BuyerCitizenId" })
            Assert.DoesNotContain(secret, html);
    }

    private sealed class FixedTenant(int storeId) : GaoApp.Application.Common.ITenantContext
    {
        public int? StoreId => storeId;
        public bool IsHostAdmin => false;
        public string? Subdomain => "test";
    }
    private sealed class MutableClock(DateTime now) : TimeProvider
    {
        public DateTime Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(Now, DateTimeKind.Utc));
    }
    private sealed class ExpiringBuyer(GaoApp.Infrastructure.Data.AppDbContext db, MutableClock clock, DateTime deadline)
        : GaoApp.Application.Interfaces.Services.Invoices.IInvoiceBuyerService
    {
        public int Calls { get; private set; }
        public Task<GaoApp.Application.Common.Results.Result<GaoApp.Application.DTOs.Invoices.InvoiceBuyerLookupDto>>
            LookupBuyerByTaxCodeAsync(int invoiceHeadId, string buyerType, string taxCode, CancellationToken ct = default)
            => throw new NotSupportedException();
        public async Task<GaoApp.Application.Common.Results.Result<GaoApp.Application.DTOs.Invoices.InvoiceHeadDto>>
            UpdateBuyerInfoAsync(GaoApp.Application.DTOs.Invoices.UpdateInvoiceBuyerInfoRequest request, CancellationToken ct = default)
        {
            Calls++;
            (await db.InvoiceHeads.SingleAsync(x => x.Id == request.InvoiceHeadId, ct)).BuyerName = request.BuyerName;
            await db.SaveChangesAsync(ct);
            clock.Now = deadline;
            return GaoApp.Application.Common.Results.Result<GaoApp.Application.DTOs.Invoices.InvoiceHeadDto>.Success(new());
        }
    }
}
