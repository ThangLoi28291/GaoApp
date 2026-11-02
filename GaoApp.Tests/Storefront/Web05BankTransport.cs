using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;

namespace GaoApp.Tests.Storefront;

internal sealed record Web05Transfer(string ExternalId, decimal Amount, string BankTime, string Status = "COMPLETED");
internal sealed class Web05BankOrder(string reference, string trace, decimal amount)
{
    internal string Reference { get; } = reference;
    internal string Trace { get; } = trace;
    internal decimal Amount { get; } = amount;
    internal List<Web05Transfer> Transfers { get; } = [];
}

// An HTTP transport double, not a payment-policy double. Production code still
// authenticates, builds/parses provider messages, persists evidence and allocates.
internal sealed class Web05BankTransport(string accountNumber, TimeProvider clock, bool pos = false) : HttpMessageHandler
{
    internal const string ImageData = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/lWQAAAAASUVORK5CYII=";
    internal ConcurrentDictionary<string, Web05BankOrder> Orders { get; } = new(StringComparer.Ordinal);
    internal ConcurrentQueue<object> Requests { get; } = new();
    internal int InitiateCalls; internal int RetrieveCalls; internal int TokenCalls;
    internal bool DropFirstInitiateResponse; internal bool FailRetrieve;
    internal bool RespectQueryWindows;
    internal string? BeneficiaryOverride; internal string? MerchantOverride; internal string? TerminalOverride; internal string? UserOverride;
    internal string? TraceOverride; internal decimal? ExpectedAmountOverride; internal string? ImageOverride;
    internal Func<Task>? AfterTrustedLookup;

    internal void Pay(string reference, decimal amount, string? externalId = null, string? bankTime = null)
    {
        var order = Orders[reference]; lock (order.Transfers)
            order.Transfers.Add(new(externalId ?? "W05-TX-" + Guid.NewGuid().ToString("N"), amount, bankTime ?? clock.GetUtcNow().UtcDateTime.ToString("O", System.Globalization.CultureInfo.InvariantCulture)));
    }
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Assert.NotNull(request.RequestUri);
        Assert.Equal("https", request.RequestUri.Scheme);
        Assert.Contains(request.RequestUri.Host, new[] { "sandbox.acb.com.vn", "openapi.acb.com.vn", "openapi-iam.acb.com.vn" });
        var path = request.RequestUri.AbsolutePath;
        if (path.EndsWith("/token", StringComparison.Ordinal))
        {
            Interlocked.Increment(ref TokenCalls); Requests.Enqueue(new { Method = request.Method.Method, Path = path });
            return Ok(new { access_token = "synthetic-web05-access-token" });
        }
        Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
        Assert.Equal("synthetic-web05-access-token", request.Headers.Authorization?.Parameter);
        if (request.Method == HttpMethod.Post && path.EndsWith("/initiate", StringComparison.Ordinal))
        {
            var call = Interlocked.Increment(ref InitiateCalls);
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken)); var values = json.RootElement.GetProperty("requestParameters");
            var reference = values.GetProperty("orderId").GetString()!; var trace = values.GetProperty("traceNumber").GetString()!; var amount = values.GetProperty("amount").GetDecimal();
            Assert.Matches(pos ? "^GA[0-9]{10}$" : "^W[0-9]{12}$", reference); Assert.InRange(amount, 1m, int.MaxValue);
            var order = Orders.GetOrAdd(reference, _ => new(reference, trace, amount)); Assert.Equal(order.Amount, amount); Assert.Equal(order.Trace, trace);
            Requests.Enqueue(new { Method = request.Method.Method, Path = path, Reference = reference, Amount = amount,
                Merchant = values.GetProperty("merchantId").GetString(), Terminal = values.GetProperty("terminalId").GetString(), User = values.GetProperty("userId").GetString() });
            if (DropFirstInitiateResponse && call == 1) throw new HttpRequestException("Synthetic response loss AFTER provider accepted same reference.");
            return Ok(new { responseStatus = new { responseCode = "00000000" }, responseBody = new { traceNumber = order.Trace, virtualAccount = accountNumber, qrDataUrl = ImageOverride ?? ImageData } });
        }
        if (request.Method == HttpMethod.Get && path.EndsWith("/retrieve", StringComparison.Ordinal))
        {
            Interlocked.Increment(ref RetrieveCalls); if (FailRetrieve) throw new HttpRequestException("Synthetic retrieve unavailable.");
            var query = QueryHelpers.ParseQuery(request.RequestUri.Query); var reference = query["orderId"].ToString();
            var from = DateTime.ParseExact(query["fromDate"].ToString(), "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            var to = DateTime.ParseExact(query["toDate"].ToString(), "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            Assert.InRange((to - from).Days + 1, 1, 31);
            Requests.Enqueue(new { Method = request.Method.Method, Path = path, Reference = reference, FromDate = from.ToString("yyyy-MM-dd"), ToDate = to.ToString("yyyy-MM-dd") });
            if (!Orders.TryGetValue(reference, out var order)) return Ok(new { responseStatus = new { responseCode = "00000000" }, responseBody = new { pagination = new { totalPages = 0 }, orders = Array.Empty<object>() } });
            Web05Transfer[] transfers; lock (order.Transfers) transfers = [.. order.Transfers];
            if (RespectQueryWindows) transfers = transfers.Where(x =>
            {
                var day = DateTimeOffset.Parse(x.BankTime, System.Globalization.CultureInfo.InvariantCulture).ToOffset(TimeSpan.FromHours(7)).Date;
                return day >= from && day <= to;
            }).ToArray();
            var payload = new { responseStatus = new { responseCode = "00000000" }, responseBody = new { pagination = new { totalPages = 1 }, orders = new[] { new {
                orderId = order.Reference, traceNumber = TraceOverride ?? order.Trace, amount = ExpectedAmountOverride ?? order.Amount,
                merchantId = MerchantOverride ?? "SYNTHETIC-W05", terminalId = TerminalOverride ?? "WEB05-REGISTERED", userId = UserOverride ?? "WEB05-USER",
                beneficiaryAccountNumber = BeneficiaryOverride ?? accountNumber, status = transfers.Length == 0 ? "INIT" : "COMPLETED",
                transactionDetail = transfers.Select(x => new { transactionNumber = x.ExternalId, transactionAmount = x.Amount, transactionStatus = x.Status,
                    transactionContent = "Synthetic WEB05 transfer", origPostDate = x.BankTime }).ToArray() } } } };
            // This runs after the fake bank has fixed its response, before the
            // production processor receives it: SQL mutation/race tests use it.
            if (AfterTrustedLookup is not null) await AfterTrustedLookup();
            return Ok(payload);
        }
        throw new InvalidOperationException("Unexpected WEB05 fake bank operation: " + request.Method + " " + path);
    }
    private static HttpResponseMessage Ok(object payload) => new(HttpStatusCode.OK) { Content = JsonContent.Create(payload) };
}
