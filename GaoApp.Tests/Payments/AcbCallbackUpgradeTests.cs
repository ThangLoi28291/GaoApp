using System.Text.Json;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Web.Services.Acb;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GaoApp.Tests.Payments;

public sealed partial class AcbPaymentTests
{
    private sealed class CallbackRecordingLogger : ILogger<AcbCallbackDiagnostics>
    {
        public List<string> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Assert.Null(exception); // Raw exceptions may contain bank responses or connection strings.
            Entries.Add(formatter(state, exception));
        }
    }

    [Fact]
    public async Task Malformed_bank_response_keeps_callback_queued_and_logs_safe_correlation()
    {
        await using var f = await Fixture.Create();
        await f.Service.TryCreateAsync(100, default);
        var bankId = f.Bank.Orders.Single().Key;
        var receiptId = await f.Inbox.AcceptAsync(Notification(bankId), default);
        f.Bank.MalformedRetrieve = true;
        await f.Inbox.ProcessAsync(receiptId, default);
        var receipt = await f.Db.Set<AcbCallbackReceipt>().SingleAsync();
        Assert.Null(receipt.ProcessedAtUtc);
        Assert.Equal("BANK_RESPONSE_INVALID_JSON", receipt.LastErrorCode);
        Assert.Contains(f.CallbackLogs.Entries, x => x.Contains("ReceiptId=" + receiptId) && x.Contains("BANK_RESPONSE_INVALID_JSON"));
        Assert.DoesNotContain(f.CallbackLogs.Entries, x => x.Contains("synthetic-secret") || x.Contains("test-key"));
        receipt.NextAttemptAtUtc = DateTime.UtcNow.AddSeconds(-1);
        await f.Db.SaveChangesAsync();
        f.Bank.MalformedRetrieve = false; f.Bank.Pay(bankId, 70000);
        await f.Inbox.ProcessAsync(receiptId, default);
        Assert.NotNull(receipt.ProcessedAtUtc);
        Assert.Equal(2, receipt.Attempts);
        Assert.Equal(0, f.FinalizeCount);
    }

    [Fact]
    public async Task Manual_check_without_callback_reads_bank_and_completes_original_order_once()
    {
        await using var f = await Fixture.Create();
        var qr = await f.Service.TryCreateAsync(100, default);
        var session = await f.Db.Set<AcbQrSession>().SingleAsync();
        session.LastRetrievedAtUtc = DateTime.UtcNow.AddSeconds(-2);
        await f.Db.SaveChangesAsync();
        f.Bank.Pay(session.ProviderOrderId, 70000);
        var status = JsonSerializer.SerializeToElement(await f.Service.StatusAsync(qr!.Id, true, default, manual: true));
        Assert.True(status.GetProperty("bankQueried").GetBoolean());
        Assert.Equal("Received", status.GetProperty("status").GetString());
        Assert.Empty(await f.Db.Set<AcbCallbackReceipt>().ToListAsync());
        Assert.Equal(0, f.FinalizeCount);
        await f.Service.CompleteAsync(qr.Id, default);
        var repeated = JsonSerializer.SerializeToElement(await f.Service.CompleteAsync(qr.Id, default));
        Assert.Equal(1, f.FinalizeCount);
        Assert.Equal(JsonValueKind.Null, repeated.GetProperty("printUrl").ValueKind);
        Assert.Single(f.Order.Payments, p => p.Provider == "ACB");
        Assert.Equal(30000, f.Order.Payments.Single(p => p.Method == PaymentMethod.Cash).Amount);
    }

    [Fact]
    public async Task Manual_check_without_money_keeps_draft_and_lookup_reports_history_without_finalizing()
    {
        await using var f = await Fixture.Create();
        var qr = await f.Service.TryCreateAsync(100, default);
        var status = JsonSerializer.SerializeToElement(await f.Service.StatusAsync(qr!.Id, true, default, manual: true));
        Assert.Equal("Pending", status.GetProperty("status").GetString());
        Assert.True(status.GetProperty("bankQueried").GetBoolean());
        var lookup = JsonSerializer.SerializeToElement(await f.Service.LookupAsync(100, false, default));
        Assert.Equal(3, lookup.GetProperty("sessions")[0].GetProperty("TerminalId").GetInt32());
        Assert.Equal(10, lookup.GetProperty("sessions")[0].GetProperty("ShiftId").GetInt32());
        Assert.Empty(lookup.GetProperty("sessions")[0].GetProperty("transactions").EnumerateArray());
        Assert.Equal(OrderStatus.Draft, f.Order.Status);
        Assert.Single(f.Order.Payments);
        Assert.Equal(0, f.FinalizeCount);
    }
}
