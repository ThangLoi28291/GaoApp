using System.Net;
using System.Text.Json;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Web.Middlewares;
using GaoApp.Web.Services.Acb;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace GaoApp.Tests.Payments;

public sealed partial class AcbPaymentTests
{
    [Fact]
    public async Task Lost_key_before_initiate_returns_recovery_hint_and_does_not_leave_a_stuck_attempt()
    {
        await using var f = await Fixture.Create();
        var settings = (await f.Service.SettingsAsync(default))!;
        var oldProtocol = new AcbProtocol(new HttpClient(), new Microsoft.AspNetCore.DataProtection.EphemeralDataProtectionProvider());
        settings.ClientSecretProtected = oldProtocol.Protect(1, "synthetic-secret");
        await f.Db.SaveChangesAsync();
        var error = await Assert.ThrowsAsync<AcbApiException>(() => f.Service.TryCreateAsync(100, default));
        Assert.True(error.BusinessRequestNotSent);
        Assert.Contains("Client secret", error.Message);
        Assert.DoesNotContain("synthetic-secret", error.Message);
        Assert.Equal(0, f.Bank.TokenCalls);
        Assert.Empty(f.Bank.Orders);
        Assert.Equal(AcbSessionStatus.Cancelled, (await f.Db.Set<AcbQrSession>().SingleAsync()).Status);
        Assert.Equal(PosPaymentQrStatus.Cancelled, (await f.Db.PosPaymentQrRequests.SingleAsync()).Status);
        settings.ClientSecretProtected = f.Protocol.Protect(1, "test-secret");
        await f.Db.SaveChangesAsync();
        Assert.NotNull(await f.Service.TryCreateAsync(100, default));
        Assert.Single(f.Bank.Orders);
        Assert.Equal(30000m, Assert.Single(f.Order.Payments).Amount);
        Assert.Equal(0, f.FinalizeCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Token_failure_releases_only_local_attempt_and_retry_keeps_cash(bool networkFailure)
    {
        await using var f = await Fixture.Create();
        f.Bank.RejectToken = !networkFailure;
        f.Bank.TokenFailureBody = "{\"error\":\"invalid_client\",\"error_description\":\"synthetic-secret\"}";
        if (networkFailure) f.Bank.TokenNetworkError = new HttpRequestException("synthetic-secret");
        var error = await Assert.ThrowsAsync<AcbApiException>(() => f.Service.TryCreateAsync(100, default));
        Assert.True(error.BusinessRequestNotSent);
        Assert.Empty(f.Bank.Orders);
        var failed = await f.Db.Set<AcbQrSession>().SingleAsync();
        Assert.Equal(AcbSessionStatus.Cancelled, failed.Status);
        Assert.Equal(PosPaymentQrStatus.Cancelled, (await f.Db.PosPaymentQrRequests.SingleAsync()).Status);

        // POS receives useful diagnostics without returning the sensitive bank response.
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        await new GlobalExceptionMiddleware(_ => throw error, NullLogger<GlobalExceptionMiddleware>.Instance).InvokeAsync(context);
        Assert.Equal((int)HttpStatusCode.ServiceUnavailable, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        using var response = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal(error.Message, response.RootElement.GetProperty("message").GetString());
        Assert.DoesNotContain("synthetic-secret", response.RootElement.ToString());

        f.Bank.RejectToken = false; f.Bank.TokenNetworkError = null;
        var qr = await f.Service.TryCreateAsync(100, default);
        Assert.NotEqual(failed.QrRequestId, qr!.Id);
        Assert.Equal(70000m, qr.Amount);
        Assert.Single(f.Bank.Orders);
        Assert.Equal(30000m, Assert.Single(f.Order.Payments).Amount);
        Assert.Equal(0, f.FinalizeCount);
    }

    [Fact]
    public async Task Unconfirmed_create_can_be_cancelled_only_after_empty_retrieve_and_bank_not_found()
    {
        await using var f = await Fixture.Create();
        f.Bank.DropInitiateResponse = true;
        await Assert.ThrowsAsync<AcbApiException>(() => f.Service.TryCreateAsync(100, default));
        var session = await f.Db.Set<AcbQrSession>().SingleAsync();
        var bankRecord = f.Bank.Orders.Single();
        f.Bank.Orders.Clear(); // Models a persisted attempt for which no bank QR exists.
        Assert.True(await f.Service.CancelAsync(session.QrRequestId, default));
        Assert.Equal(AcbSessionStatus.Cancelled, session.Status);
        Assert.Contains("30020402", session.ReviewReason);
        Assert.Equal(30000m, Assert.Single(f.Order.Payments).Amount);
        Assert.Equal(0, f.FinalizeCount);

        // If money is reported later, retain it for review instead of silently completing the order.
        f.Bank.Orders.Add(bankRecord.Key, bankRecord.Value);
        f.Bank.Pay(bankRecord.Key, 70000m);
        await f.Callback();
        Assert.Equal(AcbSessionStatus.ReviewRequired, session.Status);
        Assert.Single(await f.Db.Set<AcbPaymentTransaction>().ToListAsync());
        Assert.Single(f.Order.Payments);
        Assert.Equal(0, f.FinalizeCount);
    }

    [Theory]
    [InlineData("bank-order-exists")]
    [InlineData("qr-already-shown")]
    [InlineData("retrieve-failed")]
    [InlineData("malformed-retrieve")]
    [InlineData("different-bank-error")]
    [InlineData("transaction-evidence")]
    public async Task Bank_not_found_does_not_release_ambiguous_or_confirmed_QR(string scenario)
    {
        await using var f = await Fixture.Create();
        f.Bank.DropInitiateResponse = scenario != "qr-already-shown";
        if (f.Bank.DropInitiateResponse)
            await Assert.ThrowsAsync<AcbApiException>(() => f.Service.TryCreateAsync(100, default));
        else await f.Service.TryCreateAsync(100, default);
        var session = await f.Db.Set<AcbQrSession>().SingleAsync();
        var originalStatus = session.Status;
        if (scenario != "bank-order-exists") f.Bank.Orders.Clear();
        f.Bank.CancellationCode = scenario == "different-bank-error" ? "30020500" : "30020402";
        f.Bank.FailRetrieve = scenario == "retrieve-failed";
        f.Bank.MissingRetrieveOrders = scenario == "malformed-retrieve";
        if (scenario == "transaction-evidence")
        {
            f.Db.Add(new AcbPaymentTransaction { StoreId = 1, SessionId = session.Id, TransactionNumber = "retained", Status = "INIT" });
            await f.Db.SaveChangesAsync();
        }
        await Assert.ThrowsAsync<AcbApiException>(() => f.Service.CancelAsync(session.QrRequestId, default));
        Assert.Equal(originalStatus, session.Status);
        Assert.Equal(PosPaymentQrStatus.Pending, (await f.Db.PosPaymentQrRequests.SingleAsync()).Status);
        Assert.Single(f.Order.Payments);
        Assert.Equal(0, f.FinalizeCount);
    }
}
