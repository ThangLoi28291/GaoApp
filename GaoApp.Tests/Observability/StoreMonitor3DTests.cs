using GaoApp.Web.Services.StoreMonitor;
using GaoApp.Application.Common.Security;

namespace GaoApp.Tests.Observability;

public sealed class StoreMonitor3DTests
{
    [Fact]
    public void Monitor_view_requires_its_explicit_permission()
    {
        Assert.Equal(new[] { PermissionCodes.Admin.StoreMonitorView }, PermissionAliasMap.GetAcceptedCodes(PermissionCodes.Admin.StoreMonitorView));
    }
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.Parse("2026-10-09T10:00:00Z");
        public override DateTimeOffset GetUtcNow() => Now;
    }
    [Fact]
    public void Login_and_online_tabs_create_one_employee_and_expire_after_sixty_seconds()
    {
        var clock = new Clock(); var registry = new StoreActivityRegistry(clock);
        registry.Online(1, 7, Guid.Empty, "Lan"); registry.Online(1, 7, Guid.NewGuid(), "Lan");
        Assert.Single(registry.Snapshot(1).Employees!); Assert.Empty(registry.Snapshot(2).Employees!);
        Assert.Empty(registry.Snapshot(1).People);
        clock.Now += TimeSpan.FromSeconds(59); Assert.Single(registry.Snapshot(1).Employees!);
        clock.Now += TimeSpan.FromSeconds(2); Assert.Empty(registry.Snapshot(1).Employees!);
    }
    [Fact]
    public void Leaving_a_business_page_keeps_online_employee_until_logout()
    {
        var registry = new StoreActivityRegistry(new Clock()); var tab = Guid.NewGuid();
        registry.Online(1, 7, tab, "Lan"); registry.Presence(1, 7, tab, "Lan", "pos", "Q1", "editing");
        registry.Presence(1, 7, tab, "Lan", "pos", "Q1", "leave");
        Assert.Single(registry.Snapshot(1).Employees!); Assert.Empty(registry.Snapshot(1).People);
        registry.Logout(1, 7); Assert.Empty(registry.Snapshot(1).Employees!);
    }
    [Fact]
    public void Heartbeats_do_not_promote_an_old_job_above_a_later_interaction()
    {
        var clock = new Clock(); var registry = new StoreActivityRegistry(clock);
        var pos = Guid.NewGuid(); var receipt = Guid.NewGuid();
        registry.Presence(1, 7, pos, "Lan", "pos", "Q1", "editing");
        var original = clock.Now;
        clock.Now += TimeSpan.FromSeconds(5);
        registry.Presence(1, 7, receipt, "Lan", "receipt", null, "editing");
        clock.Now += TimeSpan.FromSeconds(2);
        registry.Presence(1, 7, pos, "Lan", "pos", "Q1", "editing", interaction:false);
        var people = registry.Snapshot(1).People;
        Assert.Equal(original, people.Single(x => x.Module == "pos").EditedAtUtc);
        Assert.Equal("receipt", people.OrderByDescending(x => x.EditedAtUtc).First().Module);
    }
    [Fact]
    public async Task Committed_reads_are_shared_within_one_second_and_isolated_by_store()
    {
        var clock = new Clock(); using var cache = new StoreMonitorReadCache(clock); var calls = 0;
        Task<StoreMonitorSavedData> Read()
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult(new StoreMonitorSavedData(new([], 0, 0, 0, 0, 0), []));
        }
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => cache.ReadAsync(1, Read, default)));
        Assert.Equal(1, calls);
        await cache.ReadAsync(2, Read, default); Assert.Equal(2, calls);
        clock.Now += TimeSpan.FromSeconds(1);
        await cache.ReadAsync(1, Read, default); Assert.Equal(3, calls);
    }
}
