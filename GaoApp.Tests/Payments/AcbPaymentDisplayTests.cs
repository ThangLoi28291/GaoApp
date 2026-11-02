using System.Text.Json;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Services.POSPaymentQrs;
using GaoApp.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Payments;

public sealed partial class AcbPaymentTests
{
    [Fact]
    public async Task Customer_display_uses_saved_provider_QR_and_virtual_account_and_requires_committed_payment_after_callback()
    {
        await using var f = await Fixture.Create();
        f.Order.POSShift.CurrentOrderId = f.Order.Id;
        await f.Db.SaveChangesAsync();
        var qr = await f.Service.TryCreateAsync(100, default);
        var display = new CustomerPaymentDisplayService(f.Db, new CustomerPaymentDisplayState(),
            new CustomerDepositDisplayState(), new LocalVietQrGenerator());
        await display.ApplyAsync(1, 3, "customer_payment_qr_created", new(100, qr!.Id, 999999, 0), default);
        var snapshot = await display.GetAsync(1, 3, default);
        var payload = JsonSerializer.SerializeToElement(snapshot.Payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal("qr", snapshot.Mode);
        Assert.Equal(qr.QrDataUrl, payload.GetProperty("qrDataUrl").GetString());
        Assert.Equal("TESTVA", payload.GetProperty("accountNumber").GetString());
        Assert.Equal(70000, payload.GetProperty("amount").GetDecimal());
        Assert.Equal("idle", (await display.GetAsync(1, 4, default)).Mode);
        Assert.Equal("idle", (await display.GetAsync(2, 3, default)).Mode);
        f.Bank.Pay(f.Bank.Orders.Single().Key, 70000);
        await f.Callback();
        Assert.Null((await display.GetAsync(1, 3, default)).Receipt);
        await f.Service.CompleteAsync(qr.Id, default);
        snapshot = await display.GetAsync(1, 3, default);
        Assert.NotNull(snapshot.Receipt);
        payload = JsonSerializer.SerializeToElement(snapshot.Receipt.Payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.True(payload.GetProperty("finalized").GetBoolean());
        Assert.Equal(0, payload.GetProperty("remainingAmount").GetDecimal());
        Assert.Contains("Callback ACB", payload.GetProperty("confirmationSource").GetString());
    }
}
