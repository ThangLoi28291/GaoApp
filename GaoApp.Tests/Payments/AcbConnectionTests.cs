using System.Text.Json;
using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Payments;

public sealed partial class AcbPaymentTests
{
    [Fact]
    public async Task Connection_check_only_authenticates_and_does_not_return_token_or_create_bank_orders()
    {
        await using var f = await Fixture.Create();
        var settings = await f.Service.SettingsAsync(default);
        var result = await f.Protocol.CheckConnectionAsync(settings!, default);
        Assert.True(result.AuthenticationSucceeded);
        Assert.Equal("Sandbox", result.Environment);
        Assert.Equal(1, f.Bank.TokenCalls);
        Assert.Equal(0, f.Bank.RetrieveCalls);
        Assert.Equal(0, f.Bank.CancelCalls);
        Assert.Empty(f.Bank.Orders);
        Assert.Empty(await f.Db.Set<AcbQrSession>().ToListAsync());
        Assert.DoesNotContain("test-token", JsonSerializer.Serialize(result));
    }

    [Fact]
    public async Task Connection_check_rejects_mixed_environments_before_transmitting_credentials()
    {
        await using var f = await Fixture.Create();
        var settings = (await f.Service.SettingsAsync(default))!;
        settings.ApiBaseUrl = "https://openapi.acb.com.vn";
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Protocol.CheckConnectionAsync(settings, default));
        Assert.Equal(0, f.Bank.TokenCalls);
    }

    [Fact]
    public async Task Connection_check_rejects_missing_saved_secret_before_network_request()
    {
        await using var f = await Fixture.Create();
        var settings = (await f.Service.SettingsAsync(default))!;
        settings.ClientSecretProtected = "";
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Protocol.CheckConnectionAsync(settings, default));
        Assert.Equal(0, f.Bank.TokenCalls);
    }

    [Fact]
    public async Task Connection_failure_reports_status_without_exposing_bank_response_body()
    {
        await using var f = await Fixture.Create();
        f.Bank.RejectToken = true;
        var settings = (await f.Service.SettingsAsync(default))!;
        var error = await Assert.ThrowsAsync<GaoApp.Web.Services.Acb.AcbApiException>(() => f.Protocol.CheckConnectionAsync(settings, default));
        Assert.Contains("401", error.Message);
        Assert.DoesNotContain("test-sensitive-bank-error", error.Message);
        Assert.Empty(f.Bank.Orders);
    }
}
