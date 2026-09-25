using System.Text.Json;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Repositories.Orders;

namespace GaoApp.Tests.Payments;

public sealed partial class AcbPaymentTests
{
    [Fact]
    public async Task Cash_and_card_only_orders_have_no_transfer_history_action()
    {
        await using var f = await Fixture.Create();
        f.Order.Payments.Add(new OrderPayment { StoreId = 1, Method = PaymentMethod.Card, Amount = 10000 });
        await f.Db.SaveChangesAsync();
        var ids = await new OrderRepository(f.Db).GetBankTransferOrderIdsAsync(new[] { 100 });
        Assert.Empty(ids);
    }

    [Fact]
    public async Task Manual_transfer_is_visible_but_never_reported_as_bank_verified()
    {
        await using var f = await Fixture.Create();
        f.Order.Payments.Add(new OrderPayment { StoreId = 1, Method = PaymentMethod.BankTransfer,
            Amount = 12000, Provider = "ACB", ReferenceCode = "MANUAL-TEST" });
        await f.Db.SaveChangesAsync();
        Assert.Contains(100, await new OrderRepository(f.Db).GetBankTransferOrderIdsAsync(new[] { 100 }));
        var data = JsonSerializer.SerializeToElement(await f.Service.LookupAsync(100, false, default));
        var payment = Assert.Single(data.GetProperty("payments").EnumerateArray());
        Assert.Equal(12000, payment.GetProperty("Amount").GetDecimal());
        Assert.False(payment.GetProperty("automatic").GetBoolean());
        Assert.Empty(data.GetProperty("sessions").EnumerateArray());
        Assert.Equal(0, f.FinalizeCount);
    }

    [Fact]
    public async Task Pending_and_cancelled_QR_keep_transfer_history_without_a_bank_payment()
    {
        await using var f = await Fixture.Create();
        var qr = await f.Service.TryCreateAsync(100, default);
        var repo = new OrderRepository(f.Db);
        Assert.Contains(100, await repo.GetBankTransferOrderIdsAsync(new[] { 100 }));
        Assert.True(await f.Service.CancelAsync(qr!.Id, default));
        Assert.Contains(100, await repo.GetBankTransferOrderIdsAsync(new[] { 100 }));
        var data = JsonSerializer.SerializeToElement(await f.Service.LookupAsync(100, false, default));
        Assert.Empty(data.GetProperty("payments").EnumerateArray());
        Assert.Empty(data.GetProperty("otherQrs").EnumerateArray());
        Assert.Equal("Cancelled", Assert.Single(data.GetProperty("sessions").EnumerateArray()).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Non_ACB_QR_has_its_own_history_without_bank_confirmation()
    {
        await using var f = await Fixture.Create();
        f.Db.PosPaymentQrRequests.Add(new PosPaymentQrRequest { StoreId = 1, OrderId = 100,
            BankAccountId = 1, Amount = 70000, RequestCode = "MANUAL-QR", Status = PosPaymentQrStatus.Cancelled });
        await f.Db.SaveChangesAsync();
        Assert.Contains(100, await new OrderRepository(f.Db).GetBankTransferOrderIdsAsync(new[] { 100 }));
        var data = JsonSerializer.SerializeToElement(await f.Service.LookupAsync(100, false, default));
        Assert.Empty(data.GetProperty("sessions").EnumerateArray());
        Assert.Empty(data.GetProperty("payments").EnumerateArray());
        Assert.Equal("MANUAL-QR", Assert.Single(data.GetProperty("otherQrs").EnumerateArray()).GetProperty("RequestCode").GetString());
    }

    [Fact]
    public async Task Deleted_transfer_does_not_make_a_cash_order_visible_in_lookup()
    {
        await using var f = await Fixture.Create();
        var payment = new OrderPayment { StoreId = 1, Method = PaymentMethod.BankTransfer, Amount = 12000 };
        f.Order.Payments.Add(payment);
        await f.Db.SaveChangesAsync();
        f.Db.OrderPayments.Remove(payment);
        await f.Db.SaveChangesAsync();
        Assert.Empty(await new OrderRepository(f.Db).GetBankTransferOrderIdsAsync(new[] { 100 }));
        var data = JsonSerializer.SerializeToElement(await f.Service.LookupAsync(100, false, default));
        Assert.Empty(data.GetProperty("payments").EnumerateArray());
    }

    [Fact]
    public async Task Transfer_flags_only_include_requested_orders_in_current_store()
    {
        await using var f = await Fixture.Create();
        await f.Service.TryCreateAsync(100, default);
        var repo = new OrderRepository(f.Db);
        Assert.Empty(await repo.GetBankTransferOrderIdsAsync(new[] { 999 }));
        Assert.Empty(await repo.GetBankTransferOrderIdsAsync(Array.Empty<int>()));
        f.Tenant.SetStore(2, "store2");
        Assert.Empty(await repo.GetBankTransferOrderIdsAsync(new[] { 100 }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.LookupAsync(100, false, default));
    }

    [Fact]
    public async Task Completed_QR_lookup_links_one_payment_to_bank_transactions_without_duplicating_manual_rows()
    {
        await using var f = await Fixture.Create();
        var qr = await f.Service.TryCreateAsync(100, default);
        f.Bank.Pay(f.Bank.Orders.Single().Key, 70000);
        await f.Callback();
        await f.Service.CompleteAsync(qr!.Id, default);
        var data = JsonSerializer.SerializeToElement(await f.Service.LookupAsync(100, true, default));
        Assert.True(Assert.Single(data.GetProperty("payments").EnumerateArray()).GetProperty("automatic").GetBoolean());
        Assert.Single(data.GetProperty("sessions")[0].GetProperty("transactions").EnumerateArray());
        Assert.Equal(1, f.FinalizeCount);
    }
}
