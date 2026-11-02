using GaoApp.Domain.Entities;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Storefront;
using GaoApp.Domain.Storefront;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Storefront;

[Collection("StorefrontWeb07"), Trait("Category", "StorefrontWeb07")]
public sealed class Web07CoverageTests(Web01Fixture fixture)
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task An_incomplete_newer_retrieve_scan_blocks_date_only_refund_even_when_an_older_scan_completed_and_mutable_labels_change(bool priorComplete)
    {
        var session = await Web07BankSession.CreateAsync(fixture); var c = session.Case;
        session.Transport.Pay(session.Reference, 45_000m, bankTime: "unknown-bank-date");
        using var pages = new Web07PagedTransport(session.Transport, session.Reference, 45_000m) { FailSecondPage = !priorComplete };
        if (priorComplete)
        {
            await session.ProcessAsync(pages); Assert.True(pages.PageOneCalls > 0);
            pages.FailSecondPage = true;
        }
        await session.ProcessAsync(pages); Assert.True(pages.PageOneFailures > 0);
        int originalId; string originalProof; BankReceiptAssetConfidence originalConfidence;
        await using (var db = c.Context())
        {
            var actual = Assert.Single(await db.Set<BankVerifiedReceipt>().Where(x => x.OriginStoreId == c.StoreId).ToListAsync());
            originalId = actual.Id; originalProof = actual.ProtectedEvidence; originalConfidence = actual.AssetConfidence;
            Assert.Equal(45_000m, actual.Amount); Assert.Equal(BankTimePrecision.Unknown, actual.TimePrecision); Assert.Null(actual.OccurredAtUtc);
            Assert.Equal(BankReceiptAssetConfidence.CertifiedDistinct, originalConfidence);
            await db.Set<BankVerifiedReceipt>().Where(x => x.Id == originalId).ExecuteUpdateAsync(x => x.SetProperty(r => r.ReviewCode, "BANK_TIME_UNKNOWN"));
        }
        await using (var db = c.Context()) await Web07Support.Cancellation(c, db, true).CancelGuestAsync(await c.CancelRequestAsync());
        StorefrontRefundRead refund;
        await using (var db = c.Context())
        {
            refund = Assert.Single(await Web07Support.Refunds(c, db).GetAsync(c.OrderId));
            Assert.Equal(45_000m, refund.Amount); Assert.Equal(StorefrontRefundState.Requested, refund.State); Assert.Equal(0m, refund.ExecutedAmount);
            Assert.False(refund.CanApprove); Assert.False(refund.CanConfirmExecution); Assert.False(string.IsNullOrWhiteSpace(refund.BlockedReason));
            Assert.Equal(45_000m, await db.Set<StorefrontRefundFunding>().Where(x => x.RefundId == refund.Id).SumAsync(x => x.Amount));
        }
        var approval = await c.RefundDecisionAsync(refund.Id, "Không dùng trang đầu hoặc kết quả cũ để xác nhận chỉ thiếu ngày");
        var before = await c.SnapshotAsync();
        await using (var db = c.Context()) await Assert.ThrowsAsync<ConflictAppException>(() => Web07Support.Refunds(c, db).ApproveAsync(c.OrderId, approval));
        Assert.Equal(before, await c.SnapshotAsync());
        var debit = await Web07RecordedDebit.BankAsync(c, 45_000m);
        var execution = await c.ExecutionRequestAsync(refund.Id, debit.Source, c.Seed.LegalEntityId, c.Seed.BankAccountId, debit.ExternalReference, debit.ActualExecutionAt);
        before = await c.SnapshotAsync();
        await using (var db = c.Context()) await Assert.ThrowsAsync<ConflictAppException>(() => Web07Support.Refunds(c, db).ConfirmExecutionAsync(c.OrderId, execution));
        Assert.Equal(before, await c.SnapshotAsync());
        pages.FailSecondPage = false; await session.ProcessAsync(pages);
        await using (var db = c.Context())
        {
            var actual = Assert.Single(await db.Set<BankVerifiedReceipt>().Where(x => x.OriginStoreId == c.StoreId).ToListAsync());
            Assert.Equal(originalId, actual.Id); Assert.Equal(originalProof, actual.ProtectedEvidence); Assert.Equal(originalConfidence, actual.AssetConfidence);
            Assert.Equal(45_000m, actual.Amount); Assert.Equal(BankTimePrecision.Unknown, actual.TimePrecision); Assert.Null(actual.OccurredAtUtc); Assert.True(actual.NeedsReview);
            refund = Assert.Single(await Web07Support.Refunds(c, db).GetAsync(c.OrderId)); Assert.True(refund.CanApprove, refund.BlockedReason);
            Assert.Equal(0m, refund.ExecutedAmount); Assert.Equal(StorefrontRefundState.Requested, refund.State);
        }
        await using (var db = c.Context()) await Web07Support.Refunds(c, db).ApproveAsync(c.OrderId, await c.RefundDecisionAsync(refund.Id, "Đã có kết quả đầy đủ mới, nguyên trạng khoản nhận vẫn được bảo toàn"));
        // The earlier actual debit remains an existing outflow, not a new payout request.
        execution = await c.ExecutionRequestAsync(refund.Id, debit.Source, c.Seed.LegalEntityId, c.Seed.BankAccountId, debit.ExternalReference, debit.ActualExecutionAt);
        var flow = await Web07RefundExecutionTests.FlowAsync(c);
        await using (var db = c.Context()) await Web07Support.Refunds(c, db).ConfirmExecutionAsync(c.OrderId, execution);
        Assert.Equal(flow, await Web07RefundExecutionTests.FlowAsync(c));
        await using var check = c.Context(); Assert.Single(await check.Set<StorefrontRefundFunding>().Where(x => x.RefundId == refund.Id).ToListAsync());
        Assert.Equal(45_000m, await check.Set<StorefrontRefundEvidence>().Where(x => x.RefundId == refund.Id).SumAsync(x => x.Amount));
    }
}

// Controlled provider transport only. Page0 is the original real protocol
// response, page1 either completes that response or fails; no policy is stubbed.
internal sealed class Web07PagedTransport(Web05BankTransport original, string reference, decimal amount) : HttpMessageHandler
{
    private readonly HttpMessageInvoker inner = new(original, disposeHandler: false);
    internal bool FailSecondPage;
    internal bool FailCancellationResponse;
    internal int PageOneCalls;
    internal int PageOneFailures;
    internal int CancellationCalls;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var path = request.RequestUri!.AbsolutePath;
        Assert.Equal("https", request.RequestUri.Scheme);
        Assert.Contains(request.RequestUri.Host, new[] { "sandbox.acb.com.vn", "openapi.acb.com.vn", "openapi-iam.acb.com.vn" });
        if (!path.EndsWith("/token", StringComparison.Ordinal))
        { Assert.Equal("Bearer", request.Headers.Authorization?.Scheme); Assert.Equal("synthetic-web05-access-token", request.Headers.Authorization?.Parameter); }
        if (request.Method == HttpMethod.Delete && path.EndsWith("/cancellation", StringComparison.Ordinal))
        {
            Assert.Equal("https", request.RequestUri.Scheme);
            using var body = System.Text.Json.JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            var parameters = body.RootElement.GetProperty("requestParameters");
            Assert.Equal(reference, parameters.GetProperty("orderId").GetString()); Assert.Equal(amount, parameters.GetProperty("amount").GetDecimal());
            Assert.Equal(original.Orders[reference].Trace, parameters.GetProperty("traceNumber").GetString());
            CancellationCalls++;
            if (FailCancellationResponse) throw new HttpRequestException("WEB07 controlled cancellation response loss; not proof of closure");
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { responseStatus = new { responseCode = "00000000" }, responseBody = new { status = "SUCCESS" } }) };
        }
        if (request.Method != HttpMethod.Get || !path.EndsWith("/retrieve", StringComparison.Ordinal)) return await inner.SendAsync(request, ct);
        var query = QueryHelpers.ParseQuery(request.RequestUri.Query); var page = int.Parse(query["page"].ToString(), System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(reference, query["orderId"].ToString()); Assert.Equal(original.Orders[reference].Trace, query["traceNumber"].ToString());
        if (page > 0)
        {
            Assert.Equal(1, page); PageOneCalls++;
            if (FailSecondPage) { PageOneFailures++; throw new HttpRequestException("WEB07 controlled page1 timeout after durable page0 credit"); }
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { responseStatus = new { responseCode = "00000000" }, responseBody = new { pagination = new { totalPages = 2 }, orders = Array.Empty<object>() } }) };
        }
        using var response = await inner.SendAsync(request, ct); var json = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct))!;
        json["responseBody"]!["pagination"]!["totalPages"] = 2;
        return new(response.StatusCode) { Content = JsonContent.Create(json) };
    }
    protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
}
