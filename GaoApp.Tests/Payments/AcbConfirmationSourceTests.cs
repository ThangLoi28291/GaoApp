using System.Text.Json;
using GaoApp.Application.DTOs.POS;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Web.Services.Acb;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Payments;

public sealed partial class AcbPaymentTests
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    private static async Task<int?> VerifyVia(Fixture f, int qrId, string reference, AcbConfirmationSource source)
    {
        switch (source)
        {
            case AcbConfirmationSource.Callback:
            case AcbConfirmationSource.DailyCallback:
                var payload = ListNotification(reference, code: source == AcbConfirmationSource.Callback ? "TRANSACTION_UPDATE" : "TRANSACTION_HISTORY");
                var receipt = await f.Inbox.AcceptAsync(payload, default);
                await f.Inbox.ProcessAsync(receipt, default);
                return receipt;
            case AcbConfirmationSource.InvoiceLookup:
                await f.Service.LookupAsync(100, true, default); break;
            case AcbConfirmationSource.CancellationCheck:
                await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.CancelAsync(qrId, default)); break;
            default:
                await f.Service.StatusAsync(qrId, true, default, source == AcbConfirmationSource.ManualCheck); break;
        }
        return null;
    }

    [Theory]
    [InlineData(AcbConfirmationSource.ScheduledCheck)]
    [InlineData(AcbConfirmationSource.ManualCheck)]
    [InlineData(AcbConfirmationSource.Callback)]
    [InlineData(AcbConfirmationSource.DailyCallback)]
    [InlineData(AcbConfirmationSource.InvoiceLookup)]
    [InlineData(AcbConfirmationSource.CancellationCheck)]
    public async Task Confirmation_source_is_saved_with_first_valid_bank_evidence_and_exposed_on_invoice(AcbConfirmationSource source)
    {
        await using var f = await Fixture.Create();
        f.Db.Set<User>().Add(new User { Id = 7, UserName = "cashier-test", FullName = "Thu ngân thử nghiệm", PasswordHash = "test-only-hash" });
        await f.Db.SaveChangesAsync();
        var qr = await f.Service.TryCreateAsync(100, default);
        f.Bank.Pay(qr!.RequestCode, 70000);
        var start = DateTime.UtcNow;
        var receipt = await VerifyVia(f, qr.Id, qr.RequestCode, source);
        var session = await f.Db.Set<AcbQrSession>().SingleAsync();
        Assert.Equal(source, session.ConfirmationSource);
        Assert.InRange(session.ConfirmedAtUtc!.Value, start, DateTime.UtcNow);
        Assert.Equal(receipt, session.ConfirmationCallbackReceiptId);
        var human = source is AcbConfirmationSource.ManualCheck or AcbConfirmationSource.InvoiceLookup or AcbConfirmationSource.CancellationCheck;
        Assert.Equal(human ? 7 : (int?)null, session.ConfirmedByUserId);
        Assert.Null(session.PaymentId); // Discovery and posting are separate steps.
        var before = JsonSerializer.SerializeToElement(await f.Service.LookupAsync(100, false, default), WebJson);
        Assert.False(before.GetProperty("sessions")[0].GetProperty("confirmation").GetProperty("recorded").GetBoolean());
        await f.Service.CompleteAsync(qr.Id, default);
        f.Db.ChangeTracker.Clear();
        var invoice = JsonSerializer.SerializeToElement(await f.Service.LookupAsync(100, false, default), WebJson);
        var confirmation = invoice.GetProperty("payments")[0].GetProperty("confirmation");
        Assert.Equal(source.ToString(), confirmation.GetProperty("code").GetString());
        Assert.True(confirmation.GetProperty("recorded").GetBoolean());
        Assert.NotEqual(JsonValueKind.Null, confirmation.GetProperty("confirmedAtUtc").ValueKind);
        if (human) Assert.Equal("Thu ngân thử nghiệm", confirmation.GetProperty("userName").GetString());
    }

    [Theory]
    [InlineData(AcbConfirmationSource.ScheduledCheck, AcbConfirmationSource.Callback)]
    [InlineData(AcbConfirmationSource.ManualCheck, AcbConfirmationSource.DailyCallback)]
    [InlineData(AcbConfirmationSource.Callback, AcbConfirmationSource.ManualCheck)]
    [InlineData(AcbConfirmationSource.DailyCallback, AcbConfirmationSource.Callback)]
    [InlineData(AcbConfirmationSource.ManualCheck, AcbConfirmationSource.ScheduledCheck)]
    public async Task Later_verification_or_completion_never_overwrites_the_winning_source(AcbConfirmationSource first, AcbConfirmationSource later)
    {
        await using var f = await Fixture.Create();
        var qr = await f.Service.TryCreateAsync(100, default);
        f.Bank.Pay(qr!.RequestCode, 70000);
        await VerifyVia(f, qr.Id, qr.RequestCode, first);
        var session = await f.Db.Set<AcbQrSession>().SingleAsync();
        var original = (session.ConfirmationSource, session.ConfirmedAtUtc, session.ConfirmedByUserId, session.ConfirmationCallbackReceiptId);
        await VerifyVia(f, qr.Id, qr.RequestCode, later);
        await f.Service.CompleteAsync(qr.Id, default);
        await f.Service.LookupAsync(100, true, default);
        await f.Service.CompleteAsync(qr.Id, default);
        Assert.Equal(original, (session.ConfirmationSource, session.ConfirmedAtUtc, session.ConfirmedByUserId, session.ConfirmationCallbackReceiptId));
        Assert.Single(f.Order.Payments, p => p.Provider == "ACB");
        Assert.Equal(1, f.FinalizeCount);
    }

    [Theory]
    [InlineData("unpaid")]
    [InlineData("bank-failure")]
    [InlineData("amount-mismatch")]
    public async Task Unsuccessful_check_does_not_claim_a_confirmation_source(string scenario)
    {
        await using var f = await Fixture.Create();
        var qr = await f.Service.TryCreateAsync(100, default);
        if (scenario == "bank-failure")
        {
            f.Bank.FailRetrieve = true;
            await Assert.ThrowsAsync<AcbApiException>(() => f.Service.StatusAsync(qr!.Id, true, default, true));
        }
        else
        {
            if (scenario == "amount-mismatch") f.Bank.Pay(qr!.RequestCode, 60000);
            await f.Service.StatusAsync(qr!.Id, true, default, true);
        }
        var session = await f.Db.Set<AcbQrSession>().SingleAsync();
        Assert.Null(session.ConfirmationSource); Assert.Null(session.ConfirmedAtUtc);
        Assert.Null(session.ConfirmedByUserId); Assert.Null(session.ConfirmationCallbackReceiptId);
    }

    [Fact]
    public async Task Callback_arriving_before_bank_evidence_does_not_steal_a_later_successful_manual_confirmation()
    {
        await using var f = await Fixture.Create();
        var qr = await f.Service.TryCreateAsync(100, default);
        await VerifyVia(f, qr!.Id, qr.RequestCode, AcbConfirmationSource.Callback);
        var session = await f.Db.Set<AcbQrSession>().SingleAsync();
        Assert.Null(session.ConfirmationSource);
        session.LastRetrievedAtUtc = DateTime.UtcNow.AddSeconds(-10); await f.Db.SaveChangesAsync();
        f.Bank.Pay(qr.RequestCode, 70000);
        await VerifyVia(f, qr.Id, qr.RequestCode, AcbConfirmationSource.ManualCheck);
        await VerifyVia(f, qr.Id, qr.RequestCode, AcbConfirmationSource.Callback);
        Assert.Equal(AcbConfirmationSource.ManualCheck, session.ConfirmationSource);
        Assert.Null(session.ConfirmationCallbackReceiptId);
    }

    [Fact]
    public async Task Historical_completed_payment_remains_unknown_after_new_lookup_and_callback()
    {
        await using var f = await Fixture.Create();
        var qr = await f.Service.TryCreateAsync(100, default);
        f.Bank.Pay(qr!.RequestCode, 70000); await f.Callback(); await f.Service.CompleteAsync(qr.Id, default);
        var session = await f.Db.Set<AcbQrSession>().SingleAsync();
        session.ConfirmationSource = null; session.ConfirmedAtUtc = null;
        session.ConfirmedByUserId = null; session.ConfirmationCallbackReceiptId = null;
        await f.Db.SaveChangesAsync();
        await VerifyVia(f, qr.Id, qr.RequestCode, AcbConfirmationSource.Callback);
        var data = JsonSerializer.SerializeToElement(await f.Service.LookupAsync(100, true, default), WebJson);
        var audit = data.GetProperty("payments")[0].GetProperty("confirmation");
        Assert.Equal("Unknown", audit.GetProperty("code").GetString());
        Assert.Equal("Chưa lưu nguồn xác nhận", audit.GetProperty("label").GetString());
        Assert.Equal(JsonValueKind.Null, audit.GetProperty("confirmedAtUtc").ValueKind);
    }

    [Fact]
    public async Task Installments_keep_their_own_sources_on_the_same_invoice()
    {
        await using var f = await Fixture.Create();
        var first = await f.Service.TryCreateAsync(100, Installment(20000), default);
        var second = await f.Service.TryCreateAsync(100, Installment(50000), default);
        f.Bank.Pay(first!.RequestCode, 20000); f.Bank.Pay(second!.RequestCode, 50000);
        await VerifyVia(f, first.Id, first.RequestCode, AcbConfirmationSource.ManualCheck);
        await f.Service.CompleteAsync(first.Id, default);
        await VerifyVia(f, second.Id, second.RequestCode, AcbConfirmationSource.ScheduledCheck);
        await f.Service.CompleteAsync(second.Id, default);
        var data = JsonSerializer.SerializeToElement(await f.Service.LookupAsync(100, false, default), WebJson);
        var byReference = data.GetProperty("payments").EnumerateArray().ToDictionary(x => x.GetProperty("referenceCode").GetString()!, x => x.GetProperty("confirmation").GetProperty("code").GetString());
        Assert.Equal("ManualCheck", byReference[first.RequestCode]); Assert.Equal("ScheduledCheck", byReference[second.RequestCode]);
    }

    [Fact]
    public async Task Recovering_a_legacy_QR_records_recovery_lookup_as_the_first_successful_source()
    {
        await using var f = await Fixture.Create();
        var session = await LegacyReview(f);
        var originalId = session.QrRequestId;
        await f.Service.TryCreateAsync(100, default);
        Assert.Equal(originalId, session.QrRequestId);
        Assert.Equal(AcbConfirmationSource.QrRecoveryCheck, session.ConfirmationSource);
        Assert.Equal(7, session.ConfirmedByUserId);
        Assert.NotNull(session.ConfirmedAtUtc);
        Assert.Null(session.ConfirmationCallbackReceiptId);
    }

    [Fact]
    public async Task Manual_QR_shows_cashier_confirmation_and_never_claims_bank_callback()
    {
        await using var f = await Fixture.Create();
        var service = await ManualService(f);
        var qr = await f.Service.CreateQrAsync(new OrderDraftDto { OrderId = 100 }, Installment(20000), service, default);
        await f.Service.ConfirmManualQrAsync(qr.Id, default);
        var data = JsonSerializer.SerializeToElement(await f.Service.LookupAsync(100, false, default), WebJson);
        var audit = data.GetProperty("payments")[0].GetProperty("confirmation");
        Assert.Equal("ManualQr", audit.GetProperty("code").GetString());
        Assert.Equal(7, audit.GetProperty("userId").GetInt32());
        Assert.Equal(JsonValueKind.Null, audit.GetProperty("callbackReceiptId").ValueKind);
        Assert.Equal("ManualQr", data.GetProperty("otherQrs")[0].GetProperty("confirmation").GetProperty("code").GetString());
    }
}
