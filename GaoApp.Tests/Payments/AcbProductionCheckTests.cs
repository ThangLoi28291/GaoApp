using System.Text.Json;
using GaoApp.Domain.Entities;
using GaoApp.Web.Services.Acb;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GaoApp.Tests.Payments;

public sealed partial class AcbPaymentTests
{
    private static void UseProduction(StoreAcbSettings settings)
    {
        settings.TokenEndpoint = "https://openapi-iam.acb.com.vn/acb/open/iam/id/v1/auth/realms/soba/protocol/openid-connect/token";
        settings.ApiBaseUrl = "https://openapi.acb.com.vn";
        settings.QrEndpoint = "https://openapi.acb.com.vn/acb/open/payments/qr-payment/v1/initiate";
    }

    [Fact]
    public async Task Explicit_production_diagnostic_creates_queries_cancels_fixed_amount_without_POS_mutation()
    {
        await using var f = await Fixture.Create();
        var settings = (await f.Service.SettingsAsync(default))!;
        UseProduction(settings);
        var journal = new SandboxJournal();
        var result = await new AcbSandboxCheck(f.Protocol, journal).RunProductionAsync(settings, 3, default);
        Assert.Equal("Production", result.Environment);
        Assert.Equal("acb-production-check", result.JournalFolder);
        Assert.Matches("^TP[0-9A-F]{11}$", result.ProviderOrderId);
        Assert.Equal(4, result.Steps.Count);
        Assert.All(result.Steps, step => Assert.True(step.Succeeded));
        Assert.Equal(1000, f.Bank.Orders.Single().Value.Amount);
        Assert.True(result.CancellationConfirmed);
        Assert.True(f.Bank.Orders.Single().Value.Cancelled);
        Assert.Equal(1, f.Bank.RetrieveCalls);
        Assert.Equal(1, f.Bank.CancelCalls);
        Assert.Single(f.Order.Payments);
        Assert.Empty(await f.Db.Set<AcbQrSession>().ToListAsync());
        Assert.Equal(0, f.FinalizeCount);
        Assert.DoesNotContain("test-secret", string.Join("", journal.Saved));
        Assert.DoesNotContain("test-token", string.Join("", journal.Saved));
    }

    [Theory]
    [InlineData("lost-create")]
    [InlineData("retrieve")]
    [InlineData("cancel")]
    public async Task Production_diagnostic_retains_reference_and_attempts_cleanup_when_a_bank_step_fails(string failure)
    {
        await using var f = await Fixture.Create();
        var settings = (await f.Service.SettingsAsync(default))!;
        UseProduction(settings);
        f.Bank.DropInitiateResponse = failure == "lost-create";
        f.Bank.FailRetrieve = failure == "retrieve";
        f.Bank.FailCancel = failure == "cancel";
        var journal = new SandboxJournal();
        var result = await new AcbSandboxCheck(f.Protocol, journal).RunProductionAsync(settings, 3, default);
        Assert.Contains(result.Steps, x => !x.Succeeded);
        Assert.Equal(failure != "cancel", result.CancellationConfirmed);
        Assert.Equal(failure != "cancel", f.Bank.Orders.Single().Value.Cancelled);
        Assert.Contains(result.ProviderOrderId, journal.Saved.Last());
        Assert.Contains(result.TraceNumber, journal.Saved.Last());
        Assert.Equal(0, f.FinalizeCount);
    }

    [Fact]
    public async Task Production_route_rejects_sandbox_before_any_request()
    {
        await using var f = await Fixture.Create();
        var journal = new SandboxJournal();
        var settings = (await f.Service.SettingsAsync(default))!;
        await Assert.ThrowsAsync<InvalidOperationException>(() => new AcbSandboxCheck(f.Protocol, journal).RunProductionAsync(settings, 3, default));
        Assert.Equal(0, f.Bank.TokenCalls);
        Assert.Empty(journal.Saved);
    }

    [Theory]
    [InlineData("invalid_client")]
    [InlineData("invalid_scope")]
    [InlineData("sensitive-unrecognized-error")]
    public async Task Token_failure_keeps_HTTP_and_allowlisted_OAuth_codes_in_diagnostic_without_echoing_secrets(string oauth)
    {
        await using var f = await Fixture.Create();
        var settings = (await f.Service.SettingsAsync(default))!;
        UseProduction(settings);
        f.Bank.RejectToken = true;
        f.Bank.TokenFailureBody = JsonSerializer.Serialize(new { error = oauth, error_description = "sensitive-response-body test-secret", access_token = "sensitive-token" });
        var journal = new SandboxJournal();
        var result = await new AcbSandboxCheck(f.Protocol, journal).RunProductionAsync(settings, 3, default);
        var step = Assert.Single(result.Steps);
        Assert.False(step.Succeeded);
        Assert.Contains("HTTP 401", step.Message);
        if (oauth.StartsWith("invalid_")) Assert.Contains(oauth, step.Message);
        else Assert.DoesNotContain(oauth, step.Message);
        Assert.DoesNotContain("sensitive", string.Join("", journal.Saved));
        Assert.DoesNotContain("test-secret", string.Join("", journal.Saved));
        Assert.Empty(f.Bank.Orders);
        Assert.Equal(0, f.Bank.CancelCalls);
    }

    [Theory]
    [InlineData(HttpRequestError.NameResolutionError)]
    [InlineData(HttpRequestError.SecureConnectionError)]
    [InlineData(HttpRequestError.ConnectionError)]
    public async Task Network_diagnostics_identify_the_connection_layer_without_exposing_exception_text(HttpRequestError kind)
    {
        await using var f = await Fixture.Create();
        var settings = (await f.Service.SettingsAsync(default))!;
        UseProduction(settings);
        f.Bank.TokenNetworkError = new HttpRequestException(kind, "sensitive exception data");
        var journal = new SandboxJournal();
        var result = await new AcbSandboxCheck(f.Protocol, journal).RunProductionAsync(settings, 3, default);
        var step = Assert.Single(result.Steps);
        Assert.False(step.Succeeded);
        Assert.Contains(kind.ToString(), step.Message);
        Assert.DoesNotContain("sensitive", string.Join("", journal.Saved));
        Assert.Empty(f.Bank.Orders);
    }

    [Fact]
    public async Task Mixed_production_and_sandbox_endpoints_cannot_be_saved()
    {
        await using var f = await Fixture.Create();
        await using var app = SettingsTestHost(f);
        using var scope = app.Services.CreateScope();
        var controller = SettingsController(f, scope.ServiceProvider);
        var form = new AcbSettingsForm { BankAccountId = 1,
            TokenEndpoint = "https://openapi-iam.acb.com.vn/acb/open/iam/id/v1/auth/realms/soba/protocol/openid-connect/token" };
        Assert.IsType<ViewResult>(await controller.Index(form, default));
        Assert.Contains(controller.ModelState[""]!.Errors, x => x.ErrorMessage.Contains("cùng môi trường"));
        Assert.Contains("sandbox.acb.com.vn", (await f.Service.SettingsAsync(default))!.TokenEndpoint);
        Assert.Equal(0, f.Bank.TokenCalls);
    }
}
