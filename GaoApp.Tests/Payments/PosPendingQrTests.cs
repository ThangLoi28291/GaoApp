using GaoApp.Application.DTOs.POS;
using GaoApp.Application.DTOs.POSPaymentQrs;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Web.Services.Acb;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Payments;

public sealed partial class AcbPaymentTests
{
    [Fact]
    public async Task Seven_manual_create_clicks_after_reload_keep_one_pending_QR_and_its_original_amount()
    {
        await using var f = await Fixture.Create();
        var manual = await ManualService(f);
        var draft = new OrderDraftDto { OrderId = 100 };
        var first = await f.Service.CreateQrAsync(draft, Installment(20000), manual, default);
        f.Db.ChangeTracker.Clear();
        for (var i = 0; i < 7; i++)
        {
            var conflict = await Assert.ThrowsAsync<PendingPaymentQrException>(() =>
                f.Service.CreateQrAsync(draft, Installment(30000 + i), manual, default));
            Assert.Equal(first.Id, conflict.QrId);
            Assert.Equal(first.QrDataUrl, conflict.SavedQr!.Qr.QrDataUrl);
            Assert.Equal(20000, conflict.SavedQr.Qr.Amount);
            Assert.True(conflict.SavedQr.CanCancel);
            Assert.False(conflict.SavedQr.ReadOnly);
        }
        Assert.Single(await f.Db.PosPaymentQrRequests.ToListAsync());
        Assert.Single(await f.Db.OrderPayments.ToListAsync());
        Assert.Empty(f.Bank.Orders);
    }

    [Fact]
    public async Task Cancelled_manual_QR_releases_creation_and_keeps_history()
    {
        await using var f = await Fixture.Create();
        var manual = await ManualService(f);
        var draft = new OrderDraftDto { OrderId = 100 };
        var first = await f.Service.CreateQrAsync(draft, Installment(20000), manual, default);
        await f.Service.CancelSavedQrAsync(first.Id, default);
        var next = await f.Service.CreateQrAsync(draft, Installment(30000), manual, default);
        Assert.NotEqual(first.Id, next.Id);
        Assert.Equal(30000, next.Amount);
        Assert.Equal(PosPaymentQrStatus.Cancelled, (await f.Db.PosPaymentQrRequests.SingleAsync(x => x.Id == first.Id)).Status);
        Assert.Equal(2, (await f.Service.QrHistoryAsync(100, default)).Items.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Dynamic_pending_or_received_but_unrecorded_QR_blocks_new_creation_without_bank_calls(bool received)
    {
        await using var f = await Fixture.Create();
        var first = (await f.Service.TryCreateAsync(100, Installment(20000), default))!;
        if (received) { f.Bank.Pay(first.RequestCode, 20000); await f.Callback(); }
        var tokenCalls = f.Bank.TokenCalls;
        var retrievals = f.Bank.RetrieveCalls;
        var conflict = await Assert.ThrowsAsync<PendingPaymentQrException>(() =>
            f.Service.TryCreateAsync(100, Installment(30000), default));
        Assert.Equal(first.Id, conflict.SavedQr!.Qr.Id);
        Assert.Equal(!received, conflict.SavedQr.CanCancel);
        Assert.True(conflict.SavedQr.Qr.AutomaticConfirmation);
        Assert.Equal(tokenCalls, f.Bank.TokenCalls);
        Assert.Equal(retrievals, f.Bank.RetrieveCalls);
        Assert.Single(f.Bank.Orders);
        Assert.Single(await f.Db.PosPaymentQrRequests.ToListAsync());
    }

    [Theory]
    [InlineData(PosPaymentQrStatus.Pending)]
    [InlineData(PosPaymentQrStatus.Expired)]
    [InlineData(PosPaymentQrStatus.Failed)]
    public async Task Expiry_does_not_silently_release_an_unpaid_manual_QR_and_cancellation_remains_available(PosPaymentQrStatus status)
    {
        await using var f = await Fixture.Create();
        var manual = await ManualService(f);
        var draft = new OrderDraftDto { OrderId = 100 };
        var first = await f.Service.CreateQrAsync(draft, Installment(20000), manual, default);
        var saved = await f.Db.PosPaymentQrRequests.SingleAsync();
        saved.Status = status; saved.ExpireAtUtc = DateTime.UtcNow.AddHours(-1);
        await f.Db.SaveChangesAsync();
        var conflict = await Assert.ThrowsAsync<PendingPaymentQrException>(() =>
            f.Service.CreateQrAsync(draft, Installment(30000), manual, default));
        Assert.True(conflict.SavedQr!.CanCancel);
        Assert.Equal(status != PosPaymentQrStatus.Pending, conflict.SavedQr.ReadOnly);
        await f.Service.CancelSavedQrAsync(first.Id, default);
        Assert.NotEqual(first.Id, (await f.Service.CreateQrAsync(draft, Installment(30000), manual, default)).Id);
    }

    [Fact]
    public async Task Switching_from_ACB_to_manual_bank_cannot_bypass_an_existing_pending_QR()
    {
        await using var f = await Fixture.Create();
        var first = (await f.Service.TryCreateAsync(100, Installment(20000), default))!;
        (await f.Db.Set<StoreAcbSettings>().SingleAsync()).Enabled = false;
        await f.Db.SaveChangesAsync();
        var manual = await ManualService(f);
        var conflict = await Assert.ThrowsAsync<PendingPaymentQrException>(() =>
            f.Service.CreateQrAsync(new OrderDraftDto { OrderId = 100 }, Installment(30000), manual, default));
        Assert.Equal(first.Id, conflict.QrId);
        Assert.Single(await f.Db.PosPaymentQrRequests.ToListAsync());
    }

    [Fact]
    public async Task Unknown_creation_with_no_image_blocks_new_key_and_points_to_transaction_history()
    {
        await using var f = await Fixture.Create();
        f.Bank.DropInitiateResponse = true;
        await Assert.ThrowsAsync<AcbApiException>(() => f.Service.TryCreateAsync(100, Installment(20000), default));
        f.Bank.DropInitiateResponse = false;
        var conflict = await Assert.ThrowsAsync<PendingPaymentQrException>(() =>
            f.Service.TryCreateAsync(100, Installment(30000), default));
        Assert.Null(conflict.SavedQr);
        Assert.Contains("lịch sử giao dịch", conflict.Message);
        Assert.Single(f.Bank.Orders);
        Assert.Single(await f.Db.PosPaymentQrRequests.ToListAsync());
    }

    [Fact]
    public async Task Old_unresolved_QR_still_blocks_after_a_newer_legacy_QR_was_cancelled()
    {
        await using var f = await Fixture.Create();
        var first = (await f.Service.TryCreateAsync(100, Installment(20000), default))!;
        var second = await SeedLegacyDynamicQrAsync(f, 30000);
        await f.Service.CancelSavedQrAsync(second.Id, default);
        var conflict = await Assert.ThrowsAsync<PendingPaymentQrException>(() =>
            f.Service.TryCreateAsync(100, Installment(40000), default));
        Assert.Equal(first.Id, conflict.QrId);
        Assert.Equal(2, f.Bank.Orders.Count);
    }

    // Preserve coverage for multiple bank requests left by older POS versions.
    // New creation must no longer be able to produce this state.
    private static async Task<POSPaymentQrDto> SeedLegacyDynamicQrAsync(Fixture f, decimal amount)
    {
        var qr = new PosPaymentQrRequest { StoreId = 1, OrderId = 100, BankAccountId = 1, Amount = amount,
            ClientRequestId = Guid.NewGuid(), QrRenderMode = BankQrRenderMode.ProviderApi, ConfirmMode = BankQrConfirmMode.Callback,
            Status = PosPaymentQrStatus.Pending, QrDataUrl = "data:image/png;base64,dGVzdA==", ExpireAtUtc = DateTime.UtcNow.AddMinutes(10) };
        f.Db.Add(qr); await f.Db.SaveChangesAsync();
        qr.RequestCode = $"GA{qr.Id:D10}";
        var session = new AcbQrSession { StoreId = 1, OrderId = 100, QrRequestId = qr.Id, ShiftId = 10, TerminalId = 3,
            Amount = amount, ProviderOrderId = qr.RequestCode, TraceNumber = Guid.NewGuid().ToString(),
            CartFingerprint = AcbPaymentPolicy.Fingerprint(f.Order), Status = AcbSessionStatus.Pending, VirtualAccount = "TESTVA" };
        f.Db.Add(session); await f.Db.SaveChangesAsync();
        f.Bank.Orders.Add(qr.RequestCode, new BankRecord { Amount = amount, Trace = session.TraceNumber });
        return (await f.Service.ReopenQrAsync(100, qr.Id, default)).Qr;
    }
}
