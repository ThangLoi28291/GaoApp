using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Web.Services.Acb;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Payments;

public sealed partial class AcbPaymentTests
{
    [Fact]
    public async Task Reopen_returns_the_exact_saved_QR_without_bank_calls_or_payment_changes()
    {
        await using var f = await Fixture.Create();
        var original = await f.Service.TryCreateAsync(100, default);
        var tokenCalls = f.Bank.TokenCalls;
        f.Bank.FailRetrieve = true; // Reopening remains available when ACB is unavailable.
        var history = await f.Service.QrHistoryAsync(100, default);
        var reopened = await f.Service.ReopenQrAsync(100, original!.Id, default);
        Assert.Equal(original.Id, history.LatestQrId);
        Assert.Equal(original.Id, reopened.Qr.Id);
        Assert.Equal(original.RequestCode, reopened.Qr.RequestCode);
        Assert.Equal(original.QrDataUrl, reopened.Qr.QrDataUrl);
        Assert.Equal(original.AccountNumber, reopened.Qr.AccountNumber);
        Assert.Equal(70000m, reopened.Qr.Amount);
        Assert.Equal(tokenCalls, f.Bank.TokenCalls);
        Assert.Equal(0, f.Bank.RetrieveCalls);
        Assert.Single(f.Bank.Orders);
        Assert.Single(f.Order.Payments);
        Assert.Equal(0, f.FinalizeCount);
    }

    [Fact]
    public async Task History_preserves_all_attempts_and_reopens_the_selected_QR_after_reload()
    {
        await using var f = await Fixture.Create();
        var first = await f.Service.TryCreateAsync(100, default);
        await f.Service.CancelAsync(first!.Id, default);
        var second = await f.Service.TryCreateAsync(100, default);
        f.Db.ChangeTracker.Clear();
        var history = await f.Service.QrHistoryAsync(100, default);
        Assert.Equal(new[] { second!.Id, first.Id }, history.Items.Select(x => x.QrId));
        Assert.Equal(second.Id, history.LatestQrId);
        Assert.True(history.Items[0].CanReopen);
        Assert.Equal("Cancelled", history.Items[1].Status);
        Assert.True(history.Items[1].CanReopen);
        Assert.True((await f.Service.ReopenQrAsync(100, first.Id, default)).ReadOnly);
        Assert.Equal(second.Id, (await f.Service.ReopenQrAsync(100, second.Id, default)).Qr.Id);
        Assert.Equal(2, f.Bank.Orders.Count);
    }

    [Fact]
    public async Task Reopen_review_QR_exposes_check_action_without_cancelling_or_creating_again()
    {
        await using var f = await Fixture.Create();
        var session = await LegacyReview(f);
        var result = await f.Service.ReopenQrAsync(100, session.QrRequestId, default);
        Assert.Equal("ReviewRequired", result.Status);
        Assert.False(result.CanCancel);
        Assert.True(result.Qr.AutomaticConfirmation);
        Assert.Contains("Kiểm tra ngay", result.Message);
        Assert.Contains("không chuyển thêm tiền", result.Message);
        Assert.Equal(AcbSessionStatus.ReviewRequired, session.Status);
        Assert.Null(session.PaymentId);
        Assert.Equal(0, f.Bank.RetrieveCalls);
    }

    [Fact]
    public async Task Completed_QR_is_history_only_and_cannot_reopen_as_a_new_transfer()
    {
        await using var f = await Fixture.Create();
        var qr = await f.Service.TryCreateAsync(100, default);
        f.Bank.Pay(f.Bank.Orders.Single().Key, 70000m);
        await f.Callback();
        await f.Service.CompleteAsync(qr!.Id, default);
        var history = await f.Service.QrHistoryAsync(100, default);
        Assert.Equal(qr.Id, history.LatestQrId);
        Assert.Equal("Completed", history.Items.Single().Status);
        Assert.True(history.Items.Single().CanReopen);
        var saved = await f.Service.ReopenQrAsync(100, qr.Id, default);
        Assert.True(saved.ReadOnly);
        Assert.False(saved.CanCancel);
        Assert.Equal(1, f.FinalizeCount);
    }

    [Theory]
    [InlineData("store")]
    [InlineData("terminal")]
    [InlineData("shift")]
    [InlineData("order")]
    public async Task Saved_QR_is_bound_to_the_original_store_order_and_terminal(string boundary)
    {
        await using var f = await Fixture.Create();
        var qr = await f.Service.TryCreateAsync(100, default);
        var orderId = 100;
        switch (boundary)
        {
            case "store": f.Tenant.SetStore(2, "other"); f.Db.ChangeTracker.Clear(); break;
            case "terminal": f.Runtime.TerminalId = 4; break;
            case "shift": f.Order.POSShift.Status = POSShiftStatus.Closed; await f.Db.SaveChangesAsync(); break;
            case "order": orderId = 101; break;
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.ReopenQrAsync(orderId, qr!.Id, default));
        Assert.Equal(0, f.Bank.RetrieveCalls);
        Assert.Equal(0, f.FinalizeCount);
    }

    [Fact]
    public async Task Manual_QR_history_keeps_distinct_amounts_and_selected_image()
    {
        await using var f = await Fixture.Create();
        var first = new PosPaymentQrRequest { StoreId = 1, OrderId = 100, BankAccountId = 1,
            RequestCode = "MANUAL-FIRST", Amount = 10000m, QrDataUrl = "data:image/png;base64,Zmlyc3Q=",
            Status = PosPaymentQrStatus.Pending, ExpireAtUtc = DateTime.UtcNow.AddMinutes(10) };
        var second = new PosPaymentQrRequest { StoreId = 1, OrderId = 100, BankAccountId = 1,
            RequestCode = "MANUAL-SECOND", Amount = 20000m, QrDataUrl = "data:image/png;base64,c2Vjb25k",
            Status = PosPaymentQrStatus.Pending, ExpireAtUtc = DateTime.UtcNow.AddMinutes(10) };
        f.Db.AddRange(first, second); await f.Db.SaveChangesAsync();
        var history = await f.Service.QrHistoryAsync(100, default);
        Assert.Equal(2, history.Items.Count);
        Assert.Equal(second.Id, history.LatestQrId);
        var reopened = await f.Service.ReopenQrAsync(100, first.Id, default);
        Assert.False(reopened.Qr.AutomaticConfirmation);
        Assert.Equal(10000m, reopened.Qr.Amount);
        Assert.Equal(first.QrDataUrl, reopened.Qr.QrDataUrl);
        Assert.Empty(f.Bank.Orders);
        Assert.Single(f.Order.Payments);
    }

    [Fact]
    public async Task History_for_order_without_QRs_is_empty_and_does_not_create_one()
    {
        await using var f = await Fixture.Create();
        var history = await f.Service.QrHistoryAsync(100, default);
        Assert.Empty(history.Items);
        Assert.Null(history.LatestQrId);
        Assert.Empty(f.Bank.Orders);
        Assert.Equal(0, await f.Db.PosPaymentQrRequests.CountAsync());
    }
}
