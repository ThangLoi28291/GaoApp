using GaoApp.Web.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GaoApp.Tests.Security;

public sealed class ResourceProtectionTests
{
    private static ResourceAdmission Create(ResourceProtectionOptions? options = null) => new(Options.Create(options ?? new()), NullLogger<ResourceAdmission>.Instance);
    [Fact]
    public void Ai_frequency_uses_verified_user_and_store_and_control_has_its_own_budget()
    {
        using var gate = Create();
        for (var user = 1; user <= 6; user++)
        {
            for (var i = 0; i < 10; i++) { using var lease = gate.Enter(ResourceGroup.AssistantWork, ResourceSubject.User(user), 1); Assert.True(lease.IsAcquired); }
            using var blocked = gate.Enter(ResourceGroup.AssistantWork, ResourceSubject.User(user), 1); Assert.False(blocked.IsAcquired);
        }
        using var store = gate.Enter(ResourceGroup.AssistantWork, ResourceSubject.User(7), 1); Assert.False(store.IsAcquired);
        using var other = gate.Enter(ResourceGroup.AssistantWork, ResourceSubject.User(7), 2); Assert.True(other.IsAcquired);
        using var control = gate.Enter(ResourceGroup.AssistantControl, ResourceSubject.User(1), 1); Assert.True(control.IsAcquired);
    }
    [Fact]
    public void Concurrency_is_atomic_and_double_dispose_releases_once()
    {
        using var gate = Create();
        var first = gate.Enter(ResourceGroup.AssistantWork, ResourceSubject.User(1), 1);
        var second = gate.Enter(ResourceGroup.AssistantWork, ResourceSubject.User(1), 1);
        using var denied = gate.Enter(ResourceGroup.AssistantWork, ResourceSubject.User(1), 1);
        Assert.False(denied.IsAcquired); Assert.Equal("concurrency", denied.Reason);
        first.Dispose(); first.Dispose();
        using var third = gate.Enter(ResourceGroup.AssistantWork, ResourceSubject.User(1), 1); Assert.True(third.IsAcquired);
        using var fourth = gate.Enter(ResourceGroup.AssistantWork, ResourceSubject.User(1), 1); Assert.False(fourth.IsAcquired);
        second.Dispose();
    }
    [Fact]
    public void Store_concurrency_rejections_do_not_consume_another_users_time_permits()
    {
        var options = new ResourceProtectionOptions(); options.Limits[ResourceGroup.AssistantWork].StoreConcurrency = 1;
        options.Limits[ResourceGroup.AssistantWork].CallerPerWindow = 1;
        using var gate = Create(options);
        using (var held = gate.Enter(ResourceGroup.AssistantWork, ResourceSubject.User(1), 1))
            for (var i = 0; i < 100; i++) { using var denied = gate.Enter(ResourceGroup.AssistantWork, ResourceSubject.User(2), 1); Assert.False(denied.IsAcquired); }
        using var allowed = gate.Enter(ResourceGroup.AssistantWork, ResourceSubject.User(2), 1); Assert.True(allowed.IsAcquired);
    }
    [Fact]
    public async Task Parallel_burst_never_exceeds_two_upstream_leases_per_user_and_releases_after_cancellation()
    {
        using var gate = Create(); using var canceled = new CancellationTokenSource();
        using var entered = new CountdownEvent(2); using var attempted = new CountdownEvent(20); var current = 0; var maximum = 0; var monitor=new object();
        var work = Enumerable.Range(0, 20).Select(_ => Task.Run(async () => {
            using var lease = gate.Enter(ResourceGroup.AssistantWork, ResourceSubject.User(1), 1);
            attempted.Signal();
            if (!lease.IsAcquired) return;
            var count = Interlocked.Increment(ref current); lock(monitor) maximum = Math.Max(maximum, count); entered.Signal();
            try { await Task.Delay(30000, canceled.Token); } catch (OperationCanceledException) { }
            finally { Interlocked.Decrement(ref current); }
        })).ToArray();
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5))); Assert.True(attempted.Wait(TimeSpan.FromSeconds(5))); canceled.Cancel(); await Task.WhenAll(work);
        Assert.Equal(2, maximum); Assert.Equal(0, current);
        using var after = gate.Enter(ResourceGroup.AssistantWork, ResourceSubject.User(1), 1); Assert.True(after.IsAcquired);
    }
    [Fact]
    public async Task Partition_capacity_is_bounded_and_only_idle_entries_are_evicted()
    {
        var options = new ResourceProtectionOptions { MaxPartitionsPerGroup = 16, WindowSeconds = 1, IdleSeconds = 2 };
        using var gate = Create(options);
        using var active = gate.Enter(ResourceGroup.HeavyIngress, ResourceSubject.User(1));
        for (var i = 2; i <= 15; i++) { using var lease = gate.Enter(ResourceGroup.HeavyIngress, ResourceSubject.User(i)); Assert.True(lease.IsAcquired); }
        using var full = gate.Enter(ResourceGroup.HeavyIngress, ResourceSubject.User(16)); Assert.False(full.IsAcquired); Assert.Equal("capacity", full.Reason);
        Assert.Equal(16, gate.PartitionCount(ResourceGroup.HeavyIngress));
        await Task.Delay(2200);
        using var next = gate.Enter(ResourceGroup.HeavyIngress, ResourceSubject.User(16)); Assert.True(next.IsAcquired);
        Assert.InRange(gate.PartitionCount(ResourceGroup.HeavyIngress), 2, 4);
    }
    [Fact]
    public async Task Frequency_replenishes_without_a_timer_or_queued_work()
    {
        var options = new ResourceProtectionOptions { WindowSeconds = 1, IdleSeconds = 2 };
        options.Limits[ResourceGroup.AssistantWork].CallerPerWindow = 1;
        using var gate = Create(options);
        using (var first = gate.Enter(ResourceGroup.AssistantWork, ResourceSubject.User(1), 1)) Assert.True(first.IsAcquired);
        using var blocked = gate.Enter(ResourceGroup.AssistantWork, ResourceSubject.User(1), 1); Assert.False(blocked.IsAcquired); Assert.InRange(blocked.RetryAfterSeconds, 1, 1);
        await Task.Delay(1100);
        using var again = gate.Enter(ResourceGroup.AssistantWork, ResourceSubject.User(1), 1); Assert.True(again.IsAcquired);
    }
    [Fact]
    public void Bank_accepted_traffic_cannot_be_rejected_by_legacy_enforcement_frequency_concurrency_or_capacity()
    {
        var options = new ResourceProtectionOptions(); options.Limits[ResourceGroup.BankAccepted].StorePerWindow = 1;
        using (var gate = Create(options)) for (var i = 0; i < 5; i++) { using var lease = gate.Enter(ResourceGroup.BankAccepted, storeId: 1); Assert.True(lease.IsAcquired); }
        options.Limits[ResourceGroup.BankAccepted].ObserveOnly = false;
        options.MaxPartitionsPerGroup = 16;
        options.Limits[ResourceGroup.BankAccepted].ProcessPerWindow = 1;
        options.Limits[ResourceGroup.BankAccepted].ProcessConcurrency = 1;
        using var legacy = Create(options);
        using (var one = legacy.Enter(ResourceGroup.BankAccepted, storeId: 1))
        {
            Assert.True(one.IsAcquired);
            using var concurrent = legacy.Enter(ResourceGroup.BankAccepted, storeId: 1);
            Assert.True(concurrent.IsAcquired); Assert.Equal("concurrency", concurrent.Reason);
        }
        using var frequency = legacy.Enter(ResourceGroup.BankAccepted, storeId: 1);
        Assert.True(frequency.IsAcquired); Assert.Equal("frequency", frequency.Reason);
        for (var store = 2; store <= 30; store++) { using var lease = legacy.Enter(ResourceGroup.BankAccepted, storeId: store); Assert.True(lease.IsAcquired); }
        using var capacity = legacy.Enter(ResourceGroup.BankAccepted, storeId: 31);
        Assert.True(capacity.IsAcquired); Assert.Equal("capacity", capacity.Reason);
    }
    [Fact]
    public void Process_budget_bounds_many_distinct_stores_without_sharing_control_limits()
    {
        var options=new ResourceProtectionOptions();options.Limits[ResourceGroup.AssistantWork].ProcessPerWindow=2;
        using var gate=Create(options);
        for(var i=1;i<=2;i++) {using var lease=gate.Enter(ResourceGroup.AssistantWork,ResourceSubject.User(i),i);Assert.True(lease.IsAcquired);}
        using var blocked=gate.Enter(ResourceGroup.AssistantWork,ResourceSubject.User(3),3);Assert.False(blocked.IsAcquired);
        using var cancel=gate.Enter(ResourceGroup.AssistantControl,ResourceSubject.User(3),3);Assert.True(cancel.IsAcquired);
    }
    [Fact]
    public void Invalid_options_fail_closed_and_raw_identifiers_are_not_accepted()
    {
        var options = new ResourceProtectionOptions(); options.MaxPartitionsPerGroup = 1;
        Assert.Throws<OptionsValidationException>(() => Create(options));
        options = new(); options.Limits[ResourceGroup.AssistantWork].ObserveOnly = true;
        Assert.Throws<OptionsValidationException>(() => Create(options));
        options = new(); options.Limits[ResourceGroup.AssistantWork].StorePerWindow = 0;
        Assert.Throws<OptionsValidationException>(() => Create(options));
        Assert.Throws<ArgumentOutOfRangeException>(() => ResourceSubject.User(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => ResourceSubject.Device(-1));
    }
}
