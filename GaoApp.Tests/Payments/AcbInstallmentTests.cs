using System.Text.Json;
using GaoApp.Application.DTOs.POS;
using GaoApp.Application.DTOs.POSPaymentQrs;
using GaoApp.Application.Interfaces.Services.POSPaymentQrs;
using GaoApp.Application.Services.POSPaymentQrs;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Repositories.POSPaymentQrs;
using GaoApp.Infrastructure.Repositories.StoreBankAccounts;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Payments;

public sealed partial class AcbPaymentTests
{
    private static CreatePOSPaymentQrRequest Installment(decimal? amount = null) => new() { Amount = amount, ClientRequestId = Guid.NewGuid() };

    [Fact]
    public async Task Separate_dynamic_QRs_record_partial_payments_and_finalize_and_print_only_once()
    {
        await using var f = await Fixture.Create();
        var one = await f.Service.TryCreateAsync(100, Installment(20000), default);
        f.Bank.Pay(one!.RequestCode, 20000);
        await f.Callback();
        var first = JsonSerializer.SerializeToElement(await f.Service.CompleteAsync(one.Id, default));
        Assert.False(first.GetProperty("finalized").GetBoolean());
        Assert.Equal(50000m, first.GetProperty("remainingAmount").GetDecimal());
        Assert.Equal(JsonValueKind.Null, first.GetProperty("printUrl").ValueKind);
        Assert.Equal(PaymentStatus.PartiallyPaid, f.Order.PaymentStatus);
        Assert.Equal(OrderStatus.Draft, f.Order.Status);
        Assert.Equal(0, f.FinalizeCount);
        await f.Service.CompleteAsync(one.Id, default);
        Assert.Equal(2, f.Order.Payments.Count);
        var two = await f.Service.TryCreateAsync(100, Installment(50000), default);
        Assert.NotEqual(one.Id, two!.Id);
        f.Bank.Pay(two.RequestCode, 50000);
        await f.Callback();
        var last = JsonSerializer.SerializeToElement(await f.Service.CompleteAsync(two.Id, default));
        Assert.True(last.GetProperty("finalized").GetBoolean());
        Assert.NotEqual(JsonValueKind.Null, last.GetProperty("printUrl").ValueKind);
        foreach (var id in new[] { one.Id, two.Id })
            Assert.Equal(JsonValueKind.Null, JsonSerializer.SerializeToElement(await f.Service.CompleteAsync(id, default)).GetProperty("printUrl").ValueKind);
        Assert.Equal(100000m, f.Order.PaidTotal);
        Assert.Equal(3, f.Order.Payments.Count);
        Assert.Equal(1, f.FinalizeCount);
    }

    [Fact]
    public async Task Create_key_retries_reuse_original_QR_but_another_key_creates_another_installment()
    {
        await using var f = await Fixture.Create();
        var request = Installment(20000);
        var one = await f.Service.TryCreateAsync(100, request, default);
        Assert.Equal(one!.Id, (await f.Service.TryCreateAsync(100, request, default))!.Id);
        f.Bank.Pay(one.RequestCode, 20000);
        await f.Callback(); await f.Service.CompleteAsync(one.Id, default);
        Assert.Equal(one.Id, (await f.Service.TryCreateAsync(100, request, default))!.Id);
        var two = await f.Service.TryCreateAsync(100, Installment(), default);
        Assert.Equal(50000, two!.Amount);
        Assert.NotEqual(one.Id, two.Id);
        request.Amount = 30000;
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.TryCreateAsync(100, request, default));
        Assert.Equal(2, f.Bank.Orders.Count);
    }

    [Theory]
    [InlineData(10000.5)]
    [InlineData(2147483648)]
    public async Task Dynamic_QR_rejects_fractional_amounts_and_bank_limit_overflow(decimal amount)
    {
        await using var f = await Fixture.Create();
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.TryCreateAsync(100, Installment(amount), default));
        Assert.Empty(f.Bank.Orders);
    }

    [Fact]
    public async Task Dynamic_QR_above_order_total_records_the_full_transfer_and_retries_once()
    {
        await using var f = await Fixture.Create();
        var request = Installment(125000);
        var qr = (await f.Service.TryCreateAsync(100, request, default))!;
        Assert.Equal(125000m, qr.Amount);
        Assert.Equal(qr.Id, (await f.Service.TryCreateAsync(100, request, default))!.Id);
        f.Bank.Pay(qr.RequestCode, 125000);
        await f.Callback();
        var result = JsonSerializer.SerializeToElement(await f.Service.CompleteAsync(qr.Id, default));
        Assert.True(result.GetProperty("finalized").GetBoolean());
        Assert.Equal(125000m, result.GetProperty("paidAmount").GetDecimal());
        await f.Service.CompleteAsync(qr.Id, default);
        Assert.Equal(125000m, f.Order.Payments.Single(x => x.Method == PaymentMethod.BankTransfer).Amount);
        Assert.Equal(155000m, f.Order.PaidTotal);
        Assert.Equal(55000m, f.Order.ChangeDue);
        Assert.Equal(0m, f.Order.BalanceDue);
        Assert.Equal(PaymentStatus.Paid, f.Order.PaymentStatus);
        Assert.Equal(1, f.FinalizeCount);
    }

    [Fact]
    public async Task Last_payment_cancels_unused_dynamic_QR_after_retrieval()
    {
        await using var f = await Fixture.Create();
        var unused = await f.Service.TryCreateAsync(100, Installment(70000), default);
        var paid = await SeedLegacyDynamicQrAsync(f, 70000);
        f.Bank.Pay(paid!.RequestCode, 70000);
        await f.Service.StatusAsync(paid.Id, true, default, true);
        await f.Service.CompleteAsync(paid.Id, default);
        Assert.True(f.Bank.Orders[unused!.RequestCode].Cancelled);
        Assert.Equal(1, f.Bank.CancelCalls);
        Assert.Equal(1, f.FinalizeCount);
    }

    [Fact]
    public async Task Another_received_QR_can_exceed_remaining_balance_and_preserves_all_money_and_evidence()
    {
        await using var f = await Fixture.Create();
        var one = await f.Service.TryCreateAsync(100, Installment(50000), default);
        var two = await SeedLegacyDynamicQrAsync(f, 70000);
        f.Bank.Pay(one!.RequestCode, 50000); f.Bank.Pay(two!.RequestCode, 70000);
        await f.Callback();
        await f.Service.CompleteAsync(one.Id, default);
        await f.Service.CompleteAsync(two.Id, default);
        await f.Service.CompleteAsync(two.Id, default);
        Assert.Equal(2, await f.Db.Set<AcbPaymentTransaction>().CountAsync());
        Assert.Equal(150000m, f.Order.PaidTotal);
        Assert.Equal(50000m, f.Order.ChangeDue);
        Assert.Equal(3, f.Order.Payments.Count);
        Assert.Equal(1, f.FinalizeCount);
        Assert.Equal(0, f.Bank.CancelCalls);
    }

    private static async Task<POSPaymentQrService> ManualService(Fixture f)
    {
        f.Order.Lines.Single().Variant.HasInputInvoice = false;
        var bank = await f.Db.StoreBankAccounts.SingleAsync();
        bank.IsDefault = true; bank.ConfirmMode = BankQrConfirmMode.Manual; bank.QrRenderMode = BankQrRenderMode.LocalEmvQr;
        await f.Db.SaveChangesAsync();
        return new(new StoreBankAccountRepository(f.Db), new POSPaymentQrRequestRepository(f.Db), new FakeQrGenerator());
    }

    [Fact]
    public async Task Cash_finalizes_after_partial_QR_without_a_second_print_from_ACB()
    {
        await using var f = await Fixture.Create();
        var qr = await f.Service.TryCreateAsync(100, Installment(20000), default);
        f.Bank.Pay(qr!.RequestCode, 20000);
        await f.Callback(); await f.Service.CompleteAsync(qr.Id, default);
        f.Order.Payments.Add(new OrderPayment { StoreId = 1, OrderId = 100, Amount = 50000, Method = PaymentMethod.Cash });
        f.Order.PaidTotal = 100000; f.Order.BalanceDue = 0; f.Order.Status = OrderStatus.Completed;
        await f.Db.SaveChangesAsync(); // Cash checkout has already finalized and printed.
        var result = JsonSerializer.SerializeToElement(await f.Service.CompleteAsync(qr.Id, default));
        Assert.True(result.GetProperty("finalized").GetBoolean());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("printUrl").ValueKind);
        Assert.Equal(0, f.FinalizeCount);
        Assert.Equal(AcbSessionStatus.Completed, (await f.Db.Set<AcbQrSession>().SingleAsync()).Status);
    }

    [Fact]
    public async Task Manual_QR_can_collect_more_than_remaining_after_another_QR_was_confirmed()
    {
        await using var f = await Fixture.Create();
        var service = await ManualService(f);
        var draft = new OrderDraftDto { OrderId = 100 };
        var one = await f.Service.CreateQrAsync(draft, Installment(50000), service, default);
        await f.Service.ConfirmManualQrAsync(one.Id, default);
        var two = await f.Service.CreateQrAsync(draft, Installment(50000), service, default);
        await f.Service.ConfirmManualQrAsync(two.Id, default);
        await f.Service.ConfirmManualQrAsync(two.Id, default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.CancelSavedQrAsync(one.Id, default));
        Assert.Equal(3, f.Order.Payments.Count); Assert.Equal(0m, f.Order.BalanceDue);
        Assert.Equal(130000m, f.Order.PaidTotal); Assert.Equal(30000m, f.Order.ChangeDue);
        Assert.Equal(1, f.FinalizeCount);
    }

    [Fact]
    public async Task Manual_QR_above_order_total_records_full_amount_after_reload_without_duplicates()
    {
        await using var f = await Fixture.Create();
        var service = await ManualService(f);
        var draft = new OrderDraftDto { OrderId = 100 };
        var request = Installment(125000);
        var qr = await f.Service.CreateQrAsync(draft, request, service, default);
        Assert.Equal(125000m, qr.Amount);
        Assert.Equal(qr.Id, (await f.Service.CreateQrAsync(draft, request, service, default)).Id);
        await f.Service.ConfirmManualQrAsync(qr.Id, default);
        f.Db.ChangeTracker.Clear();
        await f.Service.ConfirmManualQrAsync(qr.Id, default);
        var saved = await f.Db.Orders.Include(x => x.Payments).SingleAsync();
        Assert.Equal(125000m, saved.Payments.Single(x => x.Method == PaymentMethod.BankTransfer).Amount);
        Assert.Equal(155000m, saved.PaidTotal); Assert.Equal(55000m, saved.ChangeDue);
        Assert.Equal(0m, saved.BalanceDue); Assert.Equal(1, f.FinalizeCount);
    }

    [Theory]
    [InlineData(10000.5)]
    [InlineData(10000000000000000)]
    public async Task Manual_QR_still_rejects_fractional_and_storage_limit_amounts(decimal amount)
    {
        await using var f = await Fixture.Create();
        var service = await ManualService(f);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.CreateQrAsync(
            new OrderDraftDto { OrderId = 100 }, Installment(amount), service, default));
        Assert.Empty(await f.Db.PosPaymentQrRequests.ToListAsync());
    }
    private sealed class FakeQrGenerator : ILocalVietQrGenerator
    {
        public string BuildPayload(string bin, string account, decimal amount, string content) => $"{account}:{amount}:{content}";
        public string GeneratePngDataUrl(string payload) => "data:image/png;base64," + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(payload));
    }

    [Fact]
    public async Task Safe_token_failure_can_retry_with_same_browser_key_without_losing_cancelled_history()
    {
        await using var f = await Fixture.Create();
        var manual = new POSPaymentQrService(new StoreBankAccountRepository(f.Db), new POSPaymentQrRequestRepository(f.Db), new FakeQrGenerator());
        var request = Installment(20000);
        var draft = new OrderDraftDto { OrderId = 100 };
        f.Bank.RejectToken = true;
        await Assert.ThrowsAsync<GaoApp.Web.Services.Acb.AcbApiException>(() => f.Service.CreateQrAsync(draft, request, manual, default));
        Assert.Empty(f.Bank.Orders);
        f.Bank.RejectToken = false;
        var qr = await f.Service.CreateQrAsync(draft, request, manual, default);
        Assert.Equal(qr.Id, (await f.Service.CreateQrAsync(draft, request, manual, default)).Id);
        Assert.Single(f.Bank.Orders);
        Assert.Equal(2, await f.Db.PosPaymentQrRequests.CountAsync());
        Assert.Single(await f.Db.PosPaymentQrRequests.Where(x => x.Status == PosPaymentQrStatus.Cancelled).ToListAsync());
    }

    [Fact]
    public async Task Manual_QRs_use_default_account_allow_installments_and_confirm_idempotently_after_reload()
    {
        await using var f = await Fixture.Create();
        var manual = await ManualService(f);
        var request = Installment(20000); request.BankAccountId = 999; // POS must choose the default.
        var draft = new OrderDraftDto { OrderId = 100, BalanceDue = 70000 };
        var one = await f.Service.CreateQrAsync(draft, request, manual, default);
        Assert.Equal(1, one.BankAccountId); Assert.False(one.AutomaticConfirmation);
        Assert.Equal(one.Id, (await f.Service.CreateQrAsync(draft, request, manual, default)).Id);
        var partial = JsonSerializer.SerializeToElement(await f.Service.ConfirmManualQrAsync(one.Id, default));
        Assert.False(partial.GetProperty("finalized").GetBoolean());
        Assert.Equal(50000, partial.GetProperty("remainingAmount").GetDecimal());
        f.Db.ChangeTracker.Clear();
        await f.Service.ConfirmManualQrAsync(one.Id, default);
        Assert.Equal(2, await f.Db.OrderPayments.CountAsync());
        var two = await f.Service.CreateQrAsync(draft, Installment(), manual, default);
        Assert.Equal(50000, two.Amount); Assert.NotEqual(one.RequestCode, two.RequestCode);
        // Restore fixture reference used by its fake finalizer after reloading the DbContext.
        f.Order = await f.Db.Orders.Include(x => x.Payments).Include(x => x.POSShift).Include(x => x.Lines).ThenInclude(x => x.Variant).SingleAsync();
        var last = JsonSerializer.SerializeToElement(await f.Service.ConfirmManualQrAsync(two.Id, default));
        Assert.True(last.GetProperty("finalized").GetBoolean());
        Assert.NotEqual(JsonValueKind.Null, last.GetProperty("printUrl").ValueKind);
        Assert.Equal(JsonValueKind.Null, JsonSerializer.SerializeToElement(await f.Service.ConfirmManualQrAsync(one.Id, default)).GetProperty("printUrl").ValueKind);
        Assert.True((await f.Service.ReopenQrAsync(100, one.Id, default)).ReadOnly);
        Assert.Equal(3, await f.Db.OrderPayments.CountAsync()); Assert.Equal(1, f.FinalizeCount);
        Assert.Empty(f.Bank.Orders);
    }

    [Fact]
    public async Task Manual_confirm_rejects_wrong_terminal_cancelled_QR_and_dynamic_QR()
    {
        await using var f = await Fixture.Create();
        var dynamicQr = await f.Service.TryCreateAsync(100, Installment(20000), default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.ConfirmManualQrAsync(dynamicQr!.Id, default));
        await f.Service.CancelSavedQrAsync(dynamicQr!.Id, default);
        var manual = await ManualService(f);
        var qr = await f.Service.CreateQrAsync(new OrderDraftDto { OrderId = 100 }, Installment(10000), manual, default);
        f.Runtime.TerminalId = 4;
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.ConfirmManualQrAsync(qr.Id, default));
        f.Runtime.TerminalId = 3;
        await f.Service.CancelSavedQrAsync(qr.Id, default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.ConfirmManualQrAsync(qr.Id, default));
        Assert.Single(f.Order.Payments);
    }
}
