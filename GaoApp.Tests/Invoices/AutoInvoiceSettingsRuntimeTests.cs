using GaoApp.Application.DTOs.Invoices;
using GaoApp.Application.Services.Invoices;
using GaoApp.Domain.Enums;

namespace GaoApp.Tests.Invoices;

public sealed partial class AutoInvoiceServiceTests
{
    [Fact]
    public async Task Settings_controls_changed_threshold_is_used_by_the_next_worker_cycle()
    {
        var repo = CashRoutingRepository();
        repo.Candidates.Add(CashRoutingInvoice(701, 95_000m));
        var issue = new FakeIssueService();
        var admin = CashRoutingService(repo, issue);
        var request = SettingsControlsRequest(enabled: true, threshold: 100_000m);
        Assert.True((await admin.UpdateSettingsAsync(request)).IsSuccess);
        var saved = await admin.GetDashboardAsync(new() { Workspace = "settings" });
        Assert.Equal(100_000m, saved.Settings.SeparateAmountThreshold);
        Assert.Equal(250_000m, saved.Settings.GroupTargetAmount);
        Assert.True(Assert.Single(saved.Queue).IsGroupedConsumer);
        await CashRoutingService(repo, issue).RunOnceAsync();
        Assert.Equal(0, issue.Calls);

        request.SeparateAmountThreshold = 90_000m;
        Assert.True((await admin.UpdateSettingsAsync(request)).IsSuccess);
        // Same clock: saving must clear the previous interval so the next tick sees the new threshold.
        await CashRoutingService(repo, issue).RunOnceAsync();
        Assert.Equal(1, issue.Calls);
        Assert.Equal(AutoInvoiceOperationKind.Single, Assert.Single(repo.Operations).Kind);
    }

    [Fact]
    public async Task Settings_controls_admin_pause_and_resume_are_observed_by_host_worker_even_when_forced()
    {
        var repo = CashRoutingRepository();
        repo.Candidates.Add(CashRoutingInvoice(701, 95_000m));
        var issue = new FakeIssueService();
        var admin = CashRoutingService(repo, issue);
        AutoInvoiceService HostWorker() => new(repo, new SufficientStockRepository(), issue, new FoundLookupService(),
            new NoopUnitOfWork(), new StoreTenant(null), new TestCurrentUser(), new FixedTimeProvider(CashRoutingNow));

        Assert.False((await admin.ToggleEnabledAsync()).Value);
        Assert.False((await admin.GetDashboardAsync(new())).Settings.IsEnabled);
        await HostWorker().RunOnceAsync(force: true);
        Assert.Equal(0, issue.Calls);
        Assert.Empty(repo.Operations);
        Assert.True((await admin.GetDashboardAsync(new())).Worker.IsRunning);

        Assert.True((await admin.ToggleEnabledAsync()).Value);
        await HostWorker().RunOnceAsync();
        Assert.Equal(1, issue.Calls);
        Assert.Equal(AutoInvoiceOperationStatus.Succeeded, Assert.Single(repo.Operations).Status);
    }

    [Fact]
    public async Task Settings_controls_saving_while_disabled_keeps_issuance_paused()
    {
        var repo = CashRoutingRepository();
        repo.Candidates.Add(CashRoutingInvoice(701, 95_000m, "bank"));
        var issue = new FakeIssueService();
        var admin = CashRoutingService(repo, issue);
        Assert.True((await admin.UpdateSettingsAsync(SettingsControlsRequest(enabled: false, threshold: 120_000m))).IsSuccess);
        var dashboard = await admin.GetDashboardAsync(new());
        Assert.False(dashboard.Settings.IsEnabled);
        Assert.Equal(120_000m, dashboard.Settings.SeparateAmountThreshold);
        await CashRoutingService(repo, issue).RunOnceAsync(force: true);
        Assert.Equal(0, issue.Calls);
    }

    private static UpdateAutoInvoiceSettingsRequest SettingsControlsRequest(bool enabled, decimal threshold) => new()
    {
        IsEnabled = enabled, MinimumAgeMinutes = 1, SeparateAmountThreshold = threshold,
        GroupTargetAmount = 250_000m, SendIntervalSeconds = 45, ClosingTimeLocal = new TimeSpan(23, 0, 0),
        TimeZoneId = TimeZoneInfo.Utc.Id, ScopeMode = AutoInvoiceScopeMode.Today, IssueOldDayRemainder = false
    };
}
