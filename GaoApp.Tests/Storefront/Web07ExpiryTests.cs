using GaoApp.Domain.Entities;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using GaoApp.Application.Storefront;
using GaoApp.Domain.Enums;
using GaoApp.Domain.Storefront;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.WebUtilities;

namespace GaoApp.Tests.Storefront;

[Collection("StorefrontWeb07"), Trait("Category", "StorefrontWeb07")]
public sealed class Web07ExpiryTests(Web01Fixture fixture)
{
    [Fact]
    public async Task Genuine_late_bank_credit_after_verified_expiry_is_retained_as_one_obligation_without_reopening_holds_or_needing_any_browser_or_shift()
    {
        var session = await Web07BankSession.CreateAsync(fixture); var c = session.Case; await ExpireClockAsync(c);
        using var transport = new Web07PagedTransport(session.Transport, session.Reference, 45_000m);
        await session.ProcessAsync(transport);
        await session.CaptureDiagnosticsAsync("late-credit-initial-expiry", transport);
        int proofId;
        await using (var db = c.Context())
        {
            Assert.Equal(StorefrontOrderPhase.Cancelled, (await db.StorefrontOrders.SingleAsync()).Phase);
            proofId = Assert.Single(await db.Set<StorefrontQrClosureProof>().Where(x => x.StorefrontOrderId == c.OrderId).ToListAsync()).Id;
            Assert.Empty(await db.InventoryReservations.Where(x => x.StorefrontOrderId == c.OrderId && x.Status == InventoryReservationStatus.Active).ToListAsync());
        }
        c.Checkout.Clock.Utc = c.Checkout.Clock.Utc.AddMinutes(1); session.Transport.Pay(session.Reference, 45_000m);
        await session.ProcessAsync(transport); await session.ProcessAsync(transport);
        await using var check = c.Context();
        Assert.Equal(StorefrontOrderPhase.Cancelled, (await check.StorefrontOrders.SingleAsync()).Phase);
        Assert.Equal(proofId, Assert.Single(await check.Set<StorefrontQrClosureProof>().Where(x => x.StorefrontOrderId == c.OrderId).ToListAsync()).Id);
        Assert.Equal(45_000m, Assert.Single(await check.Set<BankVerifiedReceipt>().Where(x => x.OriginStoreId == c.StoreId).ToListAsync()).Amount);
        Assert.Equal(45_000m, Assert.Single(await check.Set<StorefrontRefundFunding>().Where(x => x.StorefrontOrderId == c.OrderId).ToListAsync()).Amount);
        Assert.Equal(45_000m, Assert.Single(await check.Set<StorefrontRefund>().Where(x => x.StorefrontOrderId == c.OrderId).ToListAsync()).Amount);
        Assert.Empty(await check.Set<StorefrontRefundEvidence>().Where(x => x.StorefrontOrderId == c.OrderId).ToListAsync());
        Assert.Empty(await check.InventoryReservations.Where(x => x.StorefrontOrderId == c.OrderId && x.Status == InventoryReservationStatus.Active).ToListAsync());
        Assert.Equal(100m, (await check.InventoryBalances.SingleAsync()).OnHandQty); Assert.Equal(0m, (await check.InventoryBalances.SingleAsync()).ReservedQty);
        Assert.Empty(await check.Orders.ToListAsync()); Assert.Empty(await check.POSShifts.ToListAsync()); Assert.Empty(await check.POSShiftCashTransactions.ToListAsync()); Assert.Empty(await check.TreasuryEntries.ToListAsync());
    }

    [Fact]
    public async Task Actual_worker_expires_only_after_complete_authenticated_retrieval_and_verified_cancellation_then_releases_only_own_hold()
    {
        var session = await Web07BankSession.CreateAsync(fixture); var c = session.Case;
        var siblingRequest = c.Checkout.Request(1m); siblingRequest.PaymentMethod = StorefrontPaymentMethod.Cod;
        var sibling = await c.Checkout.PlaceAsync(Web04Case.PlaceRequest(await c.Checkout.QuoteAsync(siblingRequest)));
        Assert.Equal(StorefrontPlacementStatus.Placed, sibling.Status);
        await ExpireClockAsync(c);
        using var transport = new Web07PagedTransport(session.Transport, session.Reference, 45_000m);
        await session.ProcessAsync(transport); await session.CaptureDiagnosticsAsync("explicit-expiry", transport);
        Assert.True(transport.PageOneCalls > 0); Assert.True(transport.CancellationCalls > 0);
        await using (var db = c.Context())
        {
            var parent = await db.StorefrontOrders.SingleAsync(x => x.Id == c.OrderId); Assert.Equal(StorefrontOrderPhase.Cancelled, parent.Phase);
            Assert.Empty(await db.InventoryReservations.Where(x => x.StorefrontOrderId == c.OrderId && x.Status == InventoryReservationStatus.Active).ToListAsync());
            Assert.Equal(1m, (await db.InventoryBalances.SingleAsync()).ReservedQty);
            var proof = Assert.Single(await db.Set<StorefrontQrClosureProof>().Where(x => x.StorefrontOrderId == c.OrderId).ToListAsync());
            Assert.Equal(session.IntentId, proof.PaymentIntentId); Assert.Equal(session.Reference, proof.ProviderReference); Assert.False(string.IsNullOrWhiteSpace(proof.ProtectedEvidence));
            Assert.Empty(await db.Set<BankVerifiedReceipt>().Where(x => x.OriginStoreId == c.StoreId).ToListAsync());
            Assert.Empty(await db.Set<StorefrontRefund>().Where(x => x.StorefrontOrderId == c.OrderId).ToListAsync());
            Assert.Empty(await db.Orders.ToListAsync()); Assert.Empty(await db.POSShifts.ToListAsync()); Assert.Empty(await db.POSShiftCashTransactions.ToListAsync());
        }
        await session.ProcessAsync(transport); await using var check = c.Context();
        Assert.Single(await check.Set<StorefrontQrClosureProof>().Where(x => x.StorefrontOrderId == c.OrderId).ToListAsync());
        Assert.Equal(1, session.Transport.InitiateCalls); Assert.Equal(100m, (await check.InventoryBalances.SingleAsync()).OnHandQty);
    }

    [Theory]
    [InlineData("retrieve-unavailable")] [InlineData("partial-pages")] [InlineData("cancel-response-unknown")] [InlineData("binding-mismatch")]
    public async Task Expiry_with_unclear_bank_result_never_claims_absence_or_releases_holds_but_explicit_guest_cancellation_remains_available(string failure)
    {
        var session = await Web07BankSession.CreateAsync(fixture); var c = session.Case; await ExpireClockAsync(c);
        session.Transport.FailRetrieve = failure == "retrieve-unavailable";
        session.Transport.TraceOverride = failure == "binding-mismatch" ? "UNRELATED-TRACE" : null;
        using var transport = new Web07PagedTransport(session.Transport, session.Reference, 45_000m)
        { FailSecondPage = failure == "partial-pages", FailCancellationResponse = failure == "cancel-response-unknown" };
        await session.ProcessAsync(transport);
        await using (var db = c.Context())
        {
            var parent = await db.StorefrontOrders.SingleAsync(x => x.Id == c.OrderId); Assert.NotEqual(StorefrontOrderPhase.Cancelled, parent.Phase);
            Assert.Equal(1m, await db.InventoryReservations.Where(x => x.StorefrontOrderId == c.OrderId && x.Status == InventoryReservationStatus.Active).SumAsync(x => x.ReservedQty));
            Assert.Empty(await db.Set<StorefrontQrClosureProof>().Where(x => x.StorefrontOrderId == c.OrderId).ToListAsync());
            Assert.Empty(await db.Set<BankVerifiedReceipt>().Where(x => x.OriginStoreId == c.StoreId).ToListAsync());
            var guest = await Web07Support.Cancellation(c, db, true).GetGuestAsync(c.Token); Assert.NotNull(guest); Assert.True(guest.CanSelfCancel);
        }
        var command = await c.CancelRequestAsync("Khách chủ động hủy; không coi kết quả ngân hàng chưa rõ là chứng cứ chưa nhận tiền");
        await using (var db = c.Context()) await Web07Support.Cancellation(c, db, true).CancelGuestAsync(command);
        await using var check = c.Context(); Assert.Equal(StorefrontOrderPhase.Cancelled, (await check.StorefrontOrders.SingleAsync()).Phase);
        Assert.Empty(await check.Set<StorefrontQrClosureProof>().Where(x => x.StorefrontOrderId == c.OrderId).ToListAsync());
        Assert.Empty(await check.InventoryReservations.Where(x => x.StorefrontOrderId == c.OrderId && x.Status == InventoryReservationStatus.Active).ToListAsync());
    }

    [Fact]
    public async Task Expired_order_with_genuine_prior_payment_cannot_be_auto_cancelled_or_reopened_from_a_terminal_state()
    {
        var session = await Web07BankSession.CreateAsync(fixture); var c = session.Case;
        var paidAt = c.Clock.GetUtcNow().AddSeconds(1); c.Checkout.Clock.Utc = paidAt.UtcDateTime;
        session.Transport.Pay(session.Reference, 45_000m, bankTime: paidAt.ToString("O", CultureInfo.InvariantCulture));
        await ExpireClockAsync(c); using var transport = new Web07PagedTransport(session.Transport, session.Reference, 45_000m);
        await session.ProcessAsync(transport);
        await using (var db = c.Context())
        {
            Assert.NotEqual(StorefrontOrderPhase.Cancelled, (await db.StorefrontOrders.SingleAsync()).Phase);
            Assert.Equal(45_000m, await db.Set<BankVerifiedReceipt>().Where(x => x.OriginStoreId == c.StoreId).SumAsync(x => x.Amount));
            Assert.Equal(1m, await db.InventoryReservations.Where(x => x.StorefrontOrderId == c.OrderId && x.Status == InventoryReservationStatus.Active).SumAsync(x => x.ReservedQty));
            Assert.Empty(await db.Set<StorefrontQrClosureProof>().Where(x => x.StorefrontOrderId == c.OrderId).ToListAsync());
        }
        await using (var db = c.Context()) await Web07Support.Cancellation(c, db, true).CancelGuestAsync(await c.CancelRequestAsync());
        await session.ProcessAsync(transport); await using var check = c.Context();
        Assert.Equal(StorefrontOrderPhase.Cancelled, (await check.StorefrontOrders.SingleAsync()).Phase);
        Assert.Equal(45_000m, Assert.Single(await check.Set<StorefrontRefundFunding>().Where(x => x.StorefrontOrderId == c.OrderId).ToListAsync()).Amount);
        Assert.Empty(await check.Orders.ToListAsync()); Assert.Empty(await check.OrderPayments.ToListAsync());
    }

    [Fact]
    public async Task Long_dormant_QR_retrieves_every_bounded_bank_window_before_any_absence_or_closure_decision_and_retains_old_paid_evidence()
    {
        var session = await Web07BankSession.CreateAsync(fixture); var c = session.Case;
        var paidAt = c.Clock.GetUtcNow().AddSeconds(1); session.Transport.Pay(session.Reference, 45_000m, bankTime: paidAt.ToString("O", CultureInfo.InvariantCulture));
        session.Transport.RespectQueryWindows = true; c.SynchronizeCommandClockWithActualLegacyTime = false; c.Checkout.Clock.Utc = paidAt.AddDays(92).UtcDateTime;
        using var transport = new Web07PagedTransport(session.Transport, session.Reference, 45_000m);
        // Each real tick is deliberately bounded to current + one historical
        // chunk. Resume normally; do not seed a cursor or all-history proof.
        for (var tick = 0; tick < 3; tick++)
        {
            await session.ProcessAsync(transport); await session.CaptureDiagnosticsAsync("dormant-tick-" + tick, transport);
            await using var current = c.Context();
            Assert.Equal(45_000m, await current.Set<BankVerifiedReceipt>().Where(x => x.OriginStoreId == c.StoreId).SumAsync(x => x.Amount));
            Assert.NotEqual(StorefrontOrderPhase.Cancelled, (await current.StorefrontOrders.SingleAsync()).Phase);
            Assert.Empty(await current.Set<StorefrontQrClosureProof>().Where(x => x.StorefrontOrderId == c.OrderId).ToListAsync());
            Assert.Equal(1m, await current.InventoryReservations.Where(x => x.StorefrontOrderId == c.OrderId && x.Status == InventoryReservationStatus.Active).SumAsync(x => x.ReservedQty));
        }
        await using var db = c.Context(); Assert.Equal(45_000m, await db.Set<BankVerifiedReceipt>().Where(x => x.OriginStoreId == c.StoreId).SumAsync(x => x.Amount));
        Assert.NotEqual(StorefrontOrderPhase.Cancelled, (await db.StorefrontOrders.SingleAsync()).Phase); Assert.Empty(await db.Set<StorefrontQrClosureProof>().Where(x => x.StorefrontOrderId == c.OrderId).ToListAsync());
        var windows = session.Transport.Requests.Select(x => System.Text.Json.JsonSerializer.SerializeToElement(x)).Where(x => x.TryGetProperty("FromDate", out _)).ToArray();
        Assert.True(windows.Length >= 4);
        Assert.All(windows, row => Assert.InRange((DateTime.ParseExact(row.GetProperty("ToDate").GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture) - DateTime.ParseExact(row.GetProperty("FromDate").GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture)).Days + 1, 1, 31));
        var covered = windows.Select(row => (From: DateTime.ParseExact(row.GetProperty("FromDate").GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture),
            To: DateTime.ParseExact(row.GetProperty("ToDate").GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture))).OrderBy(x => x.From).ThenBy(x => x.To).ToList();
        var expectedFrom = (await db.Set<StorefrontPaymentIntent>().SingleAsync()).CreatedAtUtc.AddHours(7).Date.AddDays(-1);
        var through = expectedFrom.AddDays(-1);
        foreach (var window in covered) { Assert.True(window.From <= through.AddDays(1), "Actual request windows must not skip any historical day."); if (window.To > through) through = window.To; }
        Assert.True(through >= c.Clock.GetUtcNow().ToOffset(TimeSpan.FromHours(7)).Date.AddDays(1));
        Assert.Equal(1, session.Transport.InitiateCalls); Assert.Empty(await db.POSShifts.ToListAsync());
    }

    private static async Task ExpireClockAsync(Web07Case c)
    {
        c.SynchronizeCommandClockWithActualLegacyTime = false;
        await using var db = c.Context(); c.Checkout.Clock.Utc = DateTime.SpecifyKind((await db.Set<StorefrontPaymentIntent>().SingleAsync()).ExpiresAtUtc, DateTimeKind.Utc).AddMinutes(1);
    }

    [Theory]
    [InlineData("PAID")] [InlineData("UNRECOGNIZED")]
    public async Task Historical_paid_or_unknown_empty_order_header_cannot_disappear_from_cumulative_expiry_absence_proof_on_the_next_scan(string historicalStatus)
    {
        var session = await Web07BankSession.CreateAsync(fixture); var c = session.Case;
        var siblingRequest = c.Checkout.Request(1m); siblingRequest.PaymentMethod = StorefrontPaymentMethod.Cod;
        var sibling = await c.Checkout.PlaceAsync(Web04Case.PlaceRequest(await c.Checkout.QuoteAsync(siblingRequest)));
        Assert.Equal(StorefrontPlacementStatus.Placed, sibling.Status);
        DateTime created; int globalReceipts; int globalConsumptions;
        await using (var db = c.Context())
        {
            created = (await db.Set<StorefrontPaymentIntent>().SingleAsync()).CreatedAtUtc;
            globalReceipts = await db.Set<BankVerifiedReceipt>().CountAsync(); globalConsumptions = await db.Set<BankReceiptConsumption>().CountAsync();
        }
        c.SynchronizeCommandClockWithActualLegacyTime = false;
        var originalDay = DateTime.SpecifyKind(created, DateTimeKind.Utc).AddHours(7).Date;
        using var transport = new HistoricalHeaderTransport(session.Transport, originalDay, historicalStatus, session.Reference);
        foreach (var age in new[] { 35, 36 })
        {
            c.Checkout.Clock.Utc = DateTime.SpecifyKind(created, DateTimeKind.Utc).AddDays(age);
            await session.ProcessAsync(transport);
            await using var db = c.Context();
            Assert.Equal(StorefrontOrderPhase.AwaitingPayment, (await db.StorefrontOrders.SingleAsync(x => x.Id == c.OrderId)).Phase);
            Assert.Equal(created, (await db.Set<StorefrontPaymentIntent>().SingleAsync()).CreatedAtUtc);
            Assert.Empty(await db.Set<StorefrontQrClosureProof>().Where(x => x.StorefrontOrderId == c.OrderId).ToListAsync());
            Assert.Equal(1m, await db.InventoryReservations.Where(x => x.StorefrontOrderId == c.OrderId && x.Status == InventoryReservationStatus.Active).SumAsync(x => x.ReservedQty));
            Assert.Equal(2m, (await db.InventoryBalances.SingleAsync()).ReservedQty);
            Assert.Equal(100m, (await db.InventoryBalances.SingleAsync()).OnHandQty);
            Assert.Equal(globalReceipts, await db.Set<BankVerifiedReceipt>().CountAsync()); Assert.Equal(globalConsumptions, await db.Set<BankReceiptConsumption>().CountAsync());
            Assert.Equal(0, transport.CancellationCalls);
        }
        Assert.Contains(transport.Ranges, x => x.From == originalDay.AddDays(-1) && x.To == originalDay.AddDays(5) && x.Uncertain);
        Assert.True(transport.Ranges.Count(x => x.From <= originalDay && x.To >= originalDay && x.Uncertain) >= 2,
            "A later real scan must revisit the unresolved historical day rather than treating coverage alone as absence.");
        transport.HistoricalStatus = "INIT"; transport.PermitVerifiedCancellation = true;
        c.Checkout.Clock.Utc = DateTime.SpecifyKind(created, DateTimeKind.Utc).AddDays(37);
        await session.ProcessAsync(transport);
        await using var check = c.Context();
        Assert.True(await check.Set<StorefrontBankScanCompletion>().CountAsync(x => x.PaymentIntentId == session.IntentId) >= 4);
        Assert.Equal(StorefrontOrderPhase.Cancelled, (await check.StorefrontOrders.SingleAsync(x => x.Id == c.OrderId)).Phase);
        Assert.Single(await check.Set<StorefrontQrClosureProof>().Where(x => x.StorefrontOrderId == c.OrderId).ToListAsync());
        Assert.Equal(1, transport.CancellationCalls);
        Assert.Empty(await check.InventoryReservations.Where(x => x.StorefrontOrderId == c.OrderId && x.Status == InventoryReservationStatus.Active).ToListAsync());
        Assert.Equal(1m, (await check.InventoryBalances.SingleAsync()).ReservedQty); Assert.Equal(100m, (await check.InventoryBalances.SingleAsync()).OnHandQty);
        Assert.Equal(1m, await check.InventoryReservations.Where(x => x.StorefrontOrderId != c.OrderId && x.Status == InventoryReservationStatus.Active).SumAsync(x => x.ReservedQty));
        Assert.Equal(globalReceipts, await check.Set<BankVerifiedReceipt>().CountAsync()); Assert.Equal(globalConsumptions, await check.Set<BankReceiptConsumption>().CountAsync());
        Assert.Empty(await check.Set<StorefrontRefundFunding>().Where(x => x.StorefrontOrderId == c.OrderId).ToListAsync());
        Assert.Equal(1, session.Transport.InitiateCalls);
    }

    // A provider-response boundary only: Day35 historical range contains an
    // ambiguous header with no transactions. The real durable scan chain must
    // revisit that history, then allow recovery from fresh INIT/empty proof.
    private sealed class HistoricalHeaderTransport(Web05BankTransport original, DateTime originalDay, string status, string reference) : HttpMessageHandler
    {
        private readonly HttpMessageInvoker inner = new(original, disposeHandler: false);
        internal readonly List<(DateTime From, DateTime To, bool Uncertain)> Ranges = [];
        internal int CancellationCalls;
        internal string HistoricalStatus = status;
        internal bool PermitVerifiedCancellation;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Method == HttpMethod.Delete)
            {
                CancellationCalls++; Assert.True(PermitVerifiedCancellation, "Historical bank uncertainty must prevent a cancellation request.");
                Assert.Equal("https", request.RequestUri!.Scheme); Assert.EndsWith("/cancellation", request.RequestUri.AbsolutePath);
                Assert.Equal("Bearer", request.Headers.Authorization?.Scheme); Assert.Equal("synthetic-web05-access-token", request.Headers.Authorization?.Parameter);
                using var body = System.Text.Json.JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)); var values = body.RootElement.GetProperty("requestParameters");
                Assert.Equal(reference, values.GetProperty("orderId").GetString()); Assert.Equal(original.Orders[reference].Trace, values.GetProperty("traceNumber").GetString());
                Assert.Equal(original.Orders[reference].Amount, values.GetProperty("amount").GetDecimal());
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { responseStatus = new { responseCode = "00000000" }, responseBody = new { status = "SUCCESS" } }) };
            }
            var response = await inner.SendAsync(request, ct);
            if (request.Method != HttpMethod.Get || !request.RequestUri!.AbsolutePath.EndsWith("/retrieve", StringComparison.Ordinal)) return response;
            var query = QueryHelpers.ParseQuery(request.RequestUri.Query);
            var from = DateTime.ParseExact(query["fromDate"].ToString(), "yyyy-MM-dd", CultureInfo.InvariantCulture);
            var to = DateTime.ParseExact(query["toDate"].ToString(), "yyyy-MM-dd", CultureInfo.InvariantCulture);
            var uncertain = from <= originalDay && to >= originalDay && HistoricalStatus != "INIT"; Ranges.Add((from, to, uncertain));
            var json = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct))!;
            foreach (var order in json["responseBody"]!["orders"]!.AsArray())
            {
                Assert.Empty(order!["transactionDetail"]!.AsArray()); order["status"] = uncertain ? HistoricalStatus : "INIT";
            }
            var result = new HttpResponseMessage(response.StatusCode) { Content = JsonContent.Create(json) }; response.Dispose(); return result;
        }
        protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
    }
}
