using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GaoApp.Application.Common;
using GaoApp.Application.Common.Options;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using GaoApp.Web.Controllers;
using GaoApp.Web.Middlewares;
using GaoApp.Web.Services.Acb;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GaoApp.Tests.Payments;

public sealed partial class AcbPaymentTests
{
    private static JsonElement Notification(string orderId, string? requestId = null, string status = "COMPLETED", decimal amount = 70000,
        int page = 1, int totalPages = 1, string merchant = "TEST", string code = "TRANSACTION_UPDATE") => JsonSerializer.SerializeToElement(new
    {
        requestTrace = "test-request-trace", requestDateTime = "2026-09-08T10:00:00.000+0700",
        requestParameters = new
        {
            masterMeta = new { clientId = "00000000-0000-0000-0000-000000000001", clientRequestId = requestId ?? Guid.NewGuid().ToString(), checksum = "test-checksum-not-used-as-monetary-evidence" },
            request = new
            {
                requestMeta = new { requestType = "NOTIFICATION", requestCode = code },
                requestParams = new
                {
                    transactions = new[] { new { transactionStatus = status, effectiveDate = "2026-09-08", amount,
                        transactionEntityAttribute = new { custom1 = merchant, custom2 = "3", custom4 = orderId, beneficiaryAccountNumber = "TESTVA" } } },
                    pagination = new { page, pageSize = 100, totalPage = totalPages }
                }
            }
        }
    });

    [Theory]
    [InlineData("/Admin/api-callback", "x-api-key", "TRANSACTION_UPDATE", 1)]
    [InlineData("/Admin/api-callback", "x-api-key", "TRANSACTION_HISTORY", 1)]
    [InlineData("/Admin/api-callback", "x-api-key", "TRANSACTION_HISTORY", 1000)]
    [InlineData("/api/acb/webhook", "x-api-key", "TRANSACTION_UPDATE", 1)]
    [InlineData("/Admin/api-callback", "Authorization", "TRANSACTION_UPDATE", 1)]
    public async Task Registered_callback_route_authenticates_store_and_acknowledges_before_bank_query(string route, string headerName, string code, int itemCount)
    {
        await using var f = await Fixture.Create();
        await f.Service.TryCreateAsync(100, default);
        f.Db.Add(new Store { Id = 1, Name = "Main store", SubDomain = "www", SubDomainNormalized = "www", IsActive = true });
        await f.Db.SaveChangesAsync();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing", ContentRootPath = Path.GetTempPath() });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton<AppDbContext>(f.Db);
        builder.Services.AddSingleton<ITenantContextWriter>(f.Tenant);
        builder.Services.AddSingleton<ITenantContext>(f.Tenant);
        builder.Services.AddSingleton(f.Diagnostics);
        builder.Services.AddSingleton(f.Service);
        builder.Services.AddSingleton(f.Protocol);
        builder.Services.AddSingleton(f.Inbox);
        builder.Services.Configure<TenantOptions>(o => { o.RootDomain = "gaomart.com.vn"; o.AdminSubdomain = "admin"; });
        builder.Services.AddControllers().AddApplicationPart(typeof(AcbWebhookController).Assembly);
        await using var app = builder.Build();
        app.UseMiddleware<AcbCallbackDiagnosticsMiddleware>();
        app.UseMiddleware<TenantResolutionMiddleware>();
        app.MapControllers();
        await app.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        client.DefaultRequestHeaders.Host = "www.gaomart.com.vn";
        var body = code == "TRANSACTION_HISTORY" ? ListNotification(f.Bank.Orders.Single().Key) : Notification(f.Bank.Orders.Single().Key);
        if (itemCount > 1)
        {
            var node = System.Text.Json.Nodes.JsonNode.Parse(body.GetRawText())!;
            var parameters = node["requestParameters"]!["request"]!["requestParams"]!;
            var template = parameters["transactions"]![0]!.DeepClone();
            template["transactionContent"] = new string('x', 512);
            parameters["transactions"] = new System.Text.Json.Nodes.JsonArray(Enumerable.Range(0, itemCount).Select(_ => template.DeepClone()).ToArray());
            parameters["pagination"]!["pageSize"] = itemCount;
            body = JsonSerializer.SerializeToElement(node);
        }
        f.Bank.FailRetrieve = true;
        using var forbidden = await client.PostAsJsonAsync(route, body);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Empty(await f.Db.Set<AcbCallbackReceipt>().ToListAsync());
        client.DefaultRequestHeaders.TryAddWithoutValidation(headerName, "test-key");
        using var accepted = await client.PostAsJsonAsync(route, body);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.True(accepted.Headers.Contains("X-Acb-Diagnostic-Id"));
        Assert.Contains(f.CallbackLogs.Entries, x => x.Contains("ACCEPTED") && x.Contains("ReceiptId=1"));
        Assert.DoesNotContain(f.CallbackLogs.Entries, x => x.Contains("test-key"));
        var ack = await accepted.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("00000000", AcbProtocol.Text(AcbProtocol.Path(ack, "responseStatus"), "responseCode"));
        Assert.Equal("test-request-trace", AcbProtocol.Text(ack, "requestTrace"));
        Assert.Equal(0, f.Bank.RetrieveCalls);
        Assert.Single(await f.Db.Set<AcbCallbackReceipt>().ToListAsync());
        Assert.Equal(itemCount, await f.Db.Set<AcbQrNotificationItem>().CountAsync());
        using var duplicate = await client.PostAsJsonAsync(route, body);
        Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode);
        var duplicateAck = await duplicate.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(AcbProtocol.Text(AcbProtocol.Path(ack, "responseBody"), "referenceCode"),
            AcbProtocol.Text(AcbProtocol.Path(duplicateAck, "responseBody"), "referenceCode"));
        using var invalid = await client.PostAsJsonAsync(route, Notification(f.Bank.Orders.Single().Key, code: "DAILY_REPORT"));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using var malformed = await client.PostAsync(route, new StringContent("{broken-json", System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
        var otherHeader = headerName == "x-api-key" ? "Authorization" : "x-api-key";
        client.DefaultRequestHeaders.TryAddWithoutValidation(otherHeader, "synthetic-secret-conflict");
        using var conflictingAuth = await client.PostAsJsonAsync(route, body);
        Assert.Equal(HttpStatusCode.Forbidden, conflictingAuth.StatusCode);
        client.DefaultRequestHeaders.Remove(otherHeader);
        f.Db.FailReceiptWrites = true;
        using var failedSave = await client.PostAsJsonAsync(route, Notification(f.Bank.Orders.Single().Key, Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, failedSave.StatusCode);
        Assert.Contains(f.CallbackLogs.Entries, x => x.Contains("INBOX_SAVE_FAILED"));
        f.Db.FailReceiptWrites = false; f.Db.ChangeTracker.Clear();
        Assert.Single(await f.Db.Set<AcbCallbackReceipt>().ToListAsync());
        Assert.DoesNotContain(f.CallbackLogs.Entries, x => x.Contains("synthetic-secret"));
        client.DefaultRequestHeaders.Host = "other.gaomart.com.vn";
        using var otherStore = await client.PostAsJsonAsync(route, body);
        Assert.Equal(HttpStatusCode.NotFound, otherStore.StatusCode);
        Assert.Equal(0, f.Bank.RetrieveCalls);
        await app.StopAsync();
    }

    [Fact]
    public async Task Durable_callback_retries_bank_outage_without_POS_and_can_resume_from_saved_data()
    {
        await using var f = await Fixture.Create();
        await f.Service.TryCreateAsync(100, default);
        var orderId = f.Bank.Orders.Single().Key;
        var id = await f.Inbox.AcceptAsync(Notification(orderId), default);
        f.Runtime.TerminalId = null; // Server processing does not need a till browser or its countdown.
        f.Bank.FailRetrieve = true;
        await f.Inbox.ProcessAsync(id, default);
        var receipt = await f.Db.Set<AcbCallbackReceipt>().SingleAsync();
        Assert.Null(receipt.ProcessedAtUtc);
        Assert.Equal("ACB_REQUEST_FAILED", receipt.LastErrorCode);
        Assert.True(receipt.NextAttemptAtUtc > DateTime.UtcNow);
        receipt.NextAttemptAtUtc = DateTime.UtcNow.AddSeconds(-1);
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        f.Bank.FailRetrieve = false;
        f.Bank.Pay(orderId, 70000);
        var resumedInbox = new AcbCallbackInbox(f.Db, new TestLocks(), f.Service, f.Signal, f.Diagnostics);
        await resumedInbox.ProcessAsync(id, default);
        receipt = await f.Db.Set<AcbCallbackReceipt>().SingleAsync();
        Assert.NotNull(receipt.ProcessedAtUtc);
        Assert.Equal(2, receipt.Attempts);
        Assert.Equal(AcbSessionStatus.Received, (await f.Db.Set<AcbQrSession>().SingleAsync()).Status);
        Assert.Contains((1, "3", 100), f.Notifications);
        Assert.Single(await f.Db.Set<OrderPayment>().ToListAsync()); // Cash only until the correct POS finalizes.
        Assert.Equal(0, f.FinalizeCount);
    }

    [Fact]
    public async Task Callback_before_retrieve_is_updated_stays_queued_until_bank_evidence_arrives()
    {
        await using var f = await Fixture.Create();
        await f.Service.TryCreateAsync(100, default);
        var bankId = f.Bank.Orders.Single().Key;
        var id = await f.Inbox.AcceptAsync(Notification(bankId), default);
        await f.Inbox.ProcessAsync(id, default);
        var receipt = await f.Db.Set<AcbCallbackReceipt>().SingleAsync();
        Assert.Equal("AWAITING_BANK_EVIDENCE", receipt.LastErrorCode);
        Assert.Null(receipt.ProcessedAtUtc);
        Assert.Equal(AcbSessionStatus.Pending, (await f.Db.Set<AcbQrSession>().SingleAsync()).Status);
        receipt.NextAttemptAtUtc = DateTime.UtcNow.AddSeconds(-1); await f.Db.SaveChangesAsync();
        f.Bank.Pay(bankId, 70000);
        await f.Inbox.ProcessAsync(id, default);
        Assert.NotNull(receipt.ProcessedAtUtc);
        Assert.Equal(AcbSessionStatus.Received, (await f.Db.Set<AcbQrSession>().SingleAsync()).Status);
    }

    [Fact]
    public async Task Conflicting_duplicate_is_rejected_and_original_evidence_is_unchanged()
    {
        await using var f = await Fixture.Create();
        var requestId = Guid.NewGuid().ToString();
        var payload = Notification("GA0000000001", requestId);
        var id = await f.Inbox.AcceptAsync(payload, default);
        Assert.Equal(id, await f.Inbox.AcceptAsync(payload, default));
        await Assert.ThrowsAsync<AcbCallbackConflictException>(() => f.Inbox.AcceptAsync(Notification("GA0000000001", requestId, amount: 1), default));
        Assert.Equal(payload.GetRawText(), (await f.Db.Set<AcbCallbackReceipt>().SingleAsync()).PayloadJson);
    }

    [Fact]
    public async Task Separate_callback_pages_are_saved_and_unknown_QR_is_retained_for_review()
    {
        await using var f = await Fixture.Create();
        var requestId = Guid.NewGuid().ToString();
        var id = await f.Inbox.AcceptAsync(Notification("unknown", requestId, page: 1, totalPages: 2), default);
        await f.Inbox.AcceptAsync(Notification("unknown", requestId, page: 2, totalPages: 2), default);
        Assert.Equal(2, await f.Db.Set<AcbCallbackReceipt>().CountAsync());
        await f.Inbox.ProcessAsync(id, default);
        var receipt = await f.Db.Set<AcbCallbackReceipt>().SingleAsync(x => x.Id == id);
        Assert.True(receipt.NeedsReview);
        Assert.Equal("UNMATCHED_QR", receipt.LastErrorCode);
        Assert.NotNull(receipt.ProcessedAtUtc);
    }

    [Fact]
    public async Task Tenant_cannot_process_another_stores_durable_callback()
    {
        await using var f = await Fixture.Create();
        var id = await f.Inbox.AcceptAsync(Notification("unknown"), default);
        f.Tenant.SetStore(2, "other");
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Inbox.ProcessAsync(id, default));
        Assert.Equal(0, f.Bank.RetrieveCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Bank_correction_blocks_auto_processing_without_reversing_cash_or_completed_sale(bool alreadyFinalized)
    {
        await using var f = await Fixture.Create();
        var qr = await f.Service.TryCreateAsync(100, default);
        var bankId = f.Bank.Orders.Single().Key;
        f.Bank.Pay(bankId, 70000); await f.Callback();
        if (alreadyFinalized) await f.Service.CompleteAsync(qr!.Id, default);
        var id = await f.Inbox.AcceptAsync(Notification(bankId, status: "ERRORCORRECTED"), default);
        await f.Inbox.ProcessAsync(id, default);
        var session = await f.Db.Set<AcbQrSession>().SingleAsync();
        Assert.Equal(AcbSessionStatus.ReviewRequired, session.Status);
        Assert.Contains("ERRORCORRECTED", session.ReviewReason);
        Assert.Equal(alreadyFinalized ? OrderStatus.Completed : OrderStatus.Draft, f.Order.Status);
        Assert.Equal(30000, f.Order.Payments.Single(x => x.Method == PaymentMethod.Cash).Amount);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.CompleteAsync(qr!.Id, default));
    }

    [Fact]
    public async Task Callback_merchant_mismatch_cannot_finalize_even_when_bank_reports_payment()
    {
        await using var f = await Fixture.Create();
        var qr = await f.Service.TryCreateAsync(100, default);
        var bankId = f.Bank.Orders.Single().Key; f.Bank.Pay(bankId, 70000);
        var id = await f.Inbox.AcceptAsync(Notification(bankId, merchant: "another-store"), default);
        await f.Inbox.ProcessAsync(id, default);
        Assert.Equal(AcbSessionStatus.ReviewRequired, (await f.Db.Set<AcbQrSession>().SingleAsync()).Status);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.CompleteAsync(qr!.Id, default));
        Assert.Equal(0, f.Bank.RetrieveCalls);
    }

    [Fact]
    public async Task QR_reference_and_date_time_follow_documented_ACB_limits()
    {
        await using var f = await Fixture.Create();
        await f.Service.TryCreateAsync(100, default);
        Assert.Matches("^GA[0-9]{10}$", f.Bank.Orders.Single().Key);
        Assert.Equal("2026-09-08T10:20:30.000+0700", AcbProtocol.FormatDateTime(new DateTimeOffset(2026, 9, 8, 10, 20, 30, TimeSpan.FromHours(7))));
    }
}
