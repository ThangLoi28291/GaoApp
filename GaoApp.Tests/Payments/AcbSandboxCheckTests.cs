using System.Text.Json;
using GaoApp.Domain.Entities;
using GaoApp.Web.Services.Acb;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Payments;

public sealed partial class AcbPaymentTests
{
    private sealed class SandboxJournal : IAcbSandboxJournal
    {
        public List<string> Saved = [];
        public bool Fail;
        public Task SaveAsync(AcbSandboxCheckResult result, CancellationToken ct)
        {
            if (Fail) throw new IOException("Test journal unavailable");
            Saved.Add(JsonSerializer.Serialize(result));
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Sandbox_check_creates_retrieves_cancels_only_test_QR_without_modifying_POS()
    {
        await using var f = await Fixture.Create();
        var journal = new SandboxJournal();
        var checker = new AcbSandboxCheck(f.Protocol, journal);
        var result = await checker.RunAsync((await f.Service.SettingsAsync(default))!, 3, default);
        Assert.Equal(4, result.Steps.Count);
        Assert.All(result.Steps, step => Assert.True(step.Succeeded));
        Assert.True(result.CancellationConfirmed);
        Assert.True(f.Bank.Orders.Single().Value.Cancelled);
        Assert.Matches("^TS[0-9A-F]{11}$", result.ProviderOrderId);
        Assert.Equal(1000, f.Bank.Orders.Single().Value.Amount);
        Assert.Equal(1, f.Bank.RetrieveCalls);
        Assert.Equal(1, f.Bank.CancelCalls);
        Assert.Empty(await f.Db.Set<AcbQrSession>().ToListAsync());
        Assert.Single(f.Order.Payments); // Existing cash payment stays untouched.
        Assert.Equal(0, f.FinalizeCount);
        Assert.Equal(2, journal.Saved.Count);
        Assert.DoesNotContain("test-token", string.Join("", journal.Saved));
        Assert.DoesNotContain("test-secret", string.Join("", journal.Saved));
        Assert.DoesNotContain("data:image", string.Join("", journal.Saved));
    }

    [Fact]
    public async Task Sandbox_check_cannot_create_QR_on_production_even_with_valid_credentials()
    {
        await using var f = await Fixture.Create();
        var settings = (await f.Service.SettingsAsync(default))!;
        settings.TokenEndpoint = "https://openapi-iam.acb.com.vn/token";
        settings.ApiBaseUrl = "https://openapi.acb.com.vn";
        settings.QrEndpoint = "https://openapi.acb.com.vn/acb/open/payments/qr-payment/v1/initiate";
        var journal = new SandboxJournal();
        await Assert.ThrowsAsync<InvalidOperationException>(() => new AcbSandboxCheck(f.Protocol, journal).RunAsync(settings, 3, default));
        Assert.Equal(0, f.Bank.TokenCalls);
        Assert.Empty(f.Bank.Orders);
        Assert.Empty(journal.Saved);
    }

    [Fact]
    public async Task Lost_Sandbox_initiate_response_still_cancels_the_original_test_reference()
    {
        await using var f = await Fixture.Create();
        f.Bank.DropInitiateResponse = true;
        var journal = new SandboxJournal();
        var result = await new AcbSandboxCheck(f.Protocol, journal).RunAsync((await f.Service.SettingsAsync(default))!, 3, default);
        Assert.Contains(result.Steps, x => !x.Succeeded);
        Assert.True(result.CancellationConfirmed);
        Assert.Equal(result.ProviderOrderId, f.Bank.Orders.Single().Key);
        Assert.True(f.Bank.Orders.Single().Value.Cancelled);
        Assert.Equal(0, f.Bank.RetrieveCalls);
        Assert.Equal(1, f.Bank.CancelCalls);
    }

    [Fact]
    public async Task Sandbox_retrieve_failure_does_not_skip_QR_cleanup()
    {
        await using var f = await Fixture.Create();
        f.Bank.FailRetrieve = true;
        var result = await new AcbSandboxCheck(f.Protocol, new SandboxJournal()).RunAsync((await f.Service.SettingsAsync(default))!, 3, default);
        Assert.True(result.CancellationConfirmed);
        Assert.Contains(result.Steps, x => !x.Succeeded);
        Assert.True(f.Bank.Orders.Single().Value.Cancelled);
    }

    [Fact]
    public async Task Failed_Sandbox_cancellation_keeps_a_recoverable_reference_and_reports_failure()
    {
        await using var f = await Fixture.Create();
        f.Bank.FailCancel = true;
        var journal = new SandboxJournal();
        var result = await new AcbSandboxCheck(f.Protocol, journal).RunAsync((await f.Service.SettingsAsync(default))!, 3, default);
        Assert.False(result.CancellationConfirmed);
        Assert.False(result.Steps.Last().Succeeded);
        Assert.Contains(result.ProviderOrderId, journal.Saved.Last());
        Assert.Contains(result.TraceNumber, journal.Saved.Last());
    }

    [Fact]
    public async Task No_Sandbox_request_is_sent_if_the_recovery_reference_cannot_be_saved()
    {
        await using var f = await Fixture.Create();
        var settings = (await f.Service.SettingsAsync(default))!;
        await Assert.ThrowsAsync<IOException>(() => new AcbSandboxCheck(f.Protocol, new SandboxJournal { Fail = true })
            .RunAsync(settings, 3, default));
        Assert.Equal(0, f.Bank.TokenCalls);
        Assert.Empty(f.Bank.Orders);
    }
}
