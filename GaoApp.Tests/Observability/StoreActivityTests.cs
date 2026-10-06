using System.Security.Claims;
using GaoApp.Web.Services.StoreMonitor;
using Microsoft.AspNetCore.DataProtection;

namespace GaoApp.Tests.Observability;

public sealed class StoreActivityTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.Parse("2026-10-06T08:00:00Z");
        public override DateTimeOffset GetUtcNow() => Now;
    }
    [Fact]
    public void Open_pages_are_not_busy_and_editing_and_presence_expire_independently()
    {
        var clock = new Clock(); var registry = new StoreActivityRegistry(clock); var tab = Guid.NewGuid();
        registry.Presence(1, 7, tab, "Lan", "receipt", null, "viewing");
        Assert.Equal("viewing", Assert.Single(registry.Snapshot(1).People).State);
        registry.Presence(1, 7, tab, "Lan", "receipt", null, "editing");
        Assert.Equal("editing", Assert.Single(registry.Snapshot(1).People).State);
        clock.Now += StoreActivityRegistry.EditingLifetime + TimeSpan.FromSeconds(1);
        Assert.Equal("viewing", Assert.Single(registry.Snapshot(1).People).State);
        clock.Now += StoreActivityRegistry.PresenceLifetime;
        Assert.Empty(registry.Snapshot(1).People);
        Assert.Empty(registry.Snapshot(1).Events);
    }
    [Fact]
    public void Stores_are_isolated_tabs_of_the_same_workstation_are_grouped_and_leave_removes_only_its_tab()
    {
        var registry = new StoreActivityRegistry(new Clock()); var first = Guid.NewGuid(); var second = Guid.NewGuid();
        registry.Presence(1, 7, first, "Lan", "receipt", null, "editing");
        registry.Presence(1, 7, second, "Lan", "receipt", null, "viewing");
        registry.Record(1, 7, "Lan", "receipt", "vừa tạo phiếu nhập", "#10", null);
        var person = Assert.Single(registry.Snapshot(1).People);
        Assert.Equal(2, person.Tabs); Assert.Equal("receipt", person.Module);
        Assert.Empty(registry.Snapshot(2).People); Assert.Empty(registry.Snapshot(2).Events);
        registry.Presence(1, 7, first, "Lan", "receipt", null, "leave");
        Assert.Equal("receipt", Assert.Single(registry.Snapshot(1).People).Module);
        Assert.Equal("viewing", Assert.Single(registry.Snapshot(1).People).State);
    }
    [Fact]
    public void Same_account_at_three_counters_and_another_module_keeps_each_workstation_visible()
    {
        var registry = new StoreActivityRegistry(new Clock());
        foreach (var counter in new[] { "Q1", "Q2", "Q3" })
            registry.Presence(1, 7, Guid.NewGuid(), "Lan", "pos", counter, "editing");
        registry.Presence(1, 7, Guid.NewGuid(), "Lan", "receipt", "Q1", "viewing");
        var people = registry.Snapshot(1).People;
        Assert.Equal(4, people.Count);
        Assert.Equal(new[] { "Q1", "Q2", "Q3" }, people.Where(x => x.Module == "pos").Select(x => x.Terminal));
        Assert.All(people.Where(x => x.Module == "pos"), person => Assert.Equal("editing", person.State));
        Assert.Equal("viewing", Assert.Single(people.Where(x => x.Module == "receipt")).State);
    }
    [Fact]
    public void Workstation_opening_time_survives_heartbeats_and_recorded_details_do_not_create_presence()
    {
        var clock = new Clock(); var registry = new StoreActivityRegistry(clock); var tab = Guid.NewGuid();
        var opened = clock.Now;
        registry.Presence(1, 7, tab, "Lan", "pos", "Q1", "viewing");
        clock.Now += TimeSpan.FromSeconds(10);
        registry.Presence(1, 7, tab, "Lan", "pos", "Q1", "editing");
        var person = Assert.Single(registry.Snapshot(1).People);
        Assert.Equal(opened, person.OpenedAtUtc); Assert.Equal(clock.Now, person.EditedAtUtc);
        registry.Record(1, 7, "Lan", "label", "vừa gửi lệnh in tem", "Lệnh in #42", "Q1", "LabelPrinting.Print", "12 tem");
        var saved = Assert.Single(registry.Snapshot(1).Events);
        Assert.Equal(clock.Now, saved.OccurredAtUtc); Assert.Equal("12 tem", saved.Detail);
        Assert.Equal("LabelPrinting.Print", saved.Action);
        Assert.Single(registry.Snapshot(1).People); // A completed write is not proof that somebody is still editing.
    }
    [Theory]
    [InlineData("receipt")]
    [InlineData("warehouse")]
    [InlineData("label")]
    [InlineData("review")]
    public void Each_area_keeps_multiple_employees_and_two_documents_of_one_employee_independent(string module)
    {
        var registry = new StoreActivityRegistry(new Clock());
        var a = Guid.NewGuid(); var b = Guid.NewGuid(); var same = Guid.NewGuid();
        registry.Presence(1, 7, a, "Lan", module, "Q1", "editing", "receipt:42", "Phiếu #42");
        registry.Presence(1, 7, b, "Lan", module, "Q1", "viewing", "receipt:43", "Phiếu #43");
        registry.Presence(1, 7, same, "Lan", module, "Q1", "viewing", "receipt:42", "Phiếu #42");
        registry.Presence(1, 8, Guid.NewGuid(), "Nam", module, "Q1", "editing", "receipt:44", "Phiếu #44");
        var people = registry.Snapshot(1).People;
        Assert.Equal(3, people.Count);
        Assert.Equal(2, Assert.Single(people, x => x.WorkKey == "receipt:42").Tabs);
        Assert.Equal("viewing", Assert.Single(people, x => x.WorkKey == "receipt:43").State);
        registry.Presence(1, 7, a, "Lan", module, "Q1", "leave");
        registry.Presence(1, 7, same, "Lan", module, "Q1", "leave");
        Assert.Equal(2, registry.Snapshot(1).People.Count);
        Assert.Contains(registry.Snapshot(1).People, x => x.WorkKey == "receipt:43");
    }
    [Fact]
    public void Switching_a_tab_to_another_document_resets_its_clock_and_does_not_merge_document_families()
    {
        var clock = new Clock(); var registry = new StoreActivityRegistry(clock); var tab = Guid.NewGuid();
        registry.Presence(1, 7, tab, "Lan", "warehouse", "Q1", "editing", "count:42", "Kiểm kê #42");
        clock.Now += TimeSpan.FromSeconds(10);
        registry.Presence(1, 7, tab, "Lan", "warehouse", "Q1", "viewing", "transfer:42", "Chuyển kho #42");
        registry.Presence(1, 7, Guid.NewGuid(), "Lan", "warehouse", "Q1", "editing", "count:42", "Kiểm kê #42");
        var switched = Assert.Single(registry.Snapshot(1).People, x => x.WorkKey == "transfer:42");
        Assert.Equal(clock.Now, switched.OpenedAtUtc); Assert.Null(switched.EditedAtUtc);
        Assert.Equal("viewing", switched.State); Assert.Equal(2, registry.Snapshot(1).People.Count);
    }
    [Fact]
    public void History_and_streams_are_bounded_and_do_not_invent_busy_employees()
    {
        var registry = new StoreActivityRegistry(new Clock());
        for (var i = 0; i < 150; i++) registry.Record(1, 7, "Lan", "pos", "vừa cập nhật", "#" + i, null);
        var snapshot = registry.Snapshot(1);
        Assert.Empty(snapshot.People); Assert.Equal(StoreActivityRegistry.MaxEvents, snapshot.Events.Count);
        Assert.Equal("#149", snapshot.Events[0].Document);
        var leases = Enumerable.Range(0, 8).Select(_ => registry.TryOpenStream(1)).ToArray();
        Assert.All(leases, lease => Assert.NotNull(lease)); Assert.Null(registry.TryOpenStream(1));
        leases[0]!.Dispose(); leases[0]!.Dispose();
        using var replacement = registry.TryOpenStream(1); Assert.NotNull(replacement);
        foreach (var lease in leases) lease!.Dispose();
    }
    [Fact]
    public void Tickets_cannot_change_actor_store_module_or_outlive_the_authorized_page()
    {
        var clock = new Clock(); var tickets = new StoreActivityTicket(new EphemeralDataProtectionProvider(), clock);
        ClaimsPrincipal User(int id) => new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id.ToString())], "test"));
        var ticket = tickets.Issue(1, 7, "receipt", "receipt:42", "Phiếu nhập #42");
        Assert.Equal("receipt", tickets.Read(ticket, 1, User(7))!.Module);
        Assert.Equal("receipt:42", tickets.Read(ticket, 1, User(7))!.WorkKey);
        Assert.Equal("Phiếu nhập #42", tickets.Read(ticket, 1, User(7))!.Document);
        Assert.Null(tickets.Read(ticket, 2, User(7))); Assert.Null(tickets.Read(ticket, 1, User(8)));
        Assert.Null(tickets.Read("forged", 1, User(7)));
        clock.Now += TimeSpan.FromHours(13); Assert.Null(tickets.Read(ticket, 1, User(7)));
    }
}
