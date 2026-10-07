namespace GaoApp.Web.Services.StoreMonitor;

public sealed record StoreActivityEvent(long Id, int UserId, string Person, string Module,
    string Text, string? Document, string? Terminal, DateTimeOffset OccurredAtUtc,
    string? Action = null, string? Detail = null, string? WorkKey = null);
public sealed record StoreActivityPerson(int UserId, string Person, string Module, string State,
    string? Terminal, DateTimeOffset SeenAtUtc, int Tabs,
    DateTimeOffset? OpenedAtUtc = null, DateTimeOffset? EditedAtUtc = null,
    string? WorkKey = null, string? Document = null);
public sealed record StoreActivitySnapshot(DateTimeOffset ServerTimeUtc, DateTimeOffset StartedAtUtc,
    IReadOnlyList<StoreActivityPerson> People, IReadOnlyList<StoreActivityEvent> Events);

/// <summary>
/// Bounded, transient wallboard state for one Web instance. Business documents and audit history
/// remain in SQL. No employee activity is inferred from an open draft or a progress timer.
/// </summary>
public sealed class StoreActivityRegistry(TimeProvider time)
{
    private sealed record PresenceEntry(int UserId, string Person, string Module, string? Terminal,
        DateTimeOffset SeenAtUtc, DateTimeOffset? EditedAtUtc, DateTimeOffset OpenedAtUtc,
        string? WorkKey, string? Document);
    private sealed class StoreState(DateTimeOffset now)
    {
        public DateTimeOffset StartedAtUtc { get; } = now;
        public DateTimeOffset TouchedAtUtc { get; set; } = now;
        public Dictionary<(int User, Guid Tab), PresenceEntry> People { get; } = [];
        public Queue<StoreActivityEvent> Events { get; } = [];
        public int Streams { get; set; }
    }
    private readonly object gate = new();
    private readonly Dictionary<int, StoreState> stores = [];
    private long nextId;
    public const int MaxEvents = 120;
    public const int MaxPeople = 200;
    public static readonly TimeSpan PresenceLifetime = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan EditingLifetime = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan HistoryLifetime = TimeSpan.FromHours(4);

    private StoreState? Store(int storeId, DateTimeOffset now)
    {
        if (storeId <= 0) return null;
        if (!stores.TryGetValue(storeId, out var state))
        {
            foreach (var expired in stores.Where(x => x.Value.Streams == 0 &&
                now - x.Value.TouchedAtUtc > HistoryLifetime).Select(x => x.Key).ToArray()) stores.Remove(expired);
            if (stores.Count >= 512) return null;
            stores[storeId] = state = new(now);
        }
        state.TouchedAtUtc = now;
        Prune(state, now);
        return state;
    }
    private static void Prune(StoreState state, DateTimeOffset now)
    {
        foreach (var key in state.People.Where(x => now - x.Value.SeenAtUtc > PresenceLifetime)
            .Select(x => x.Key).ToArray()) state.People.Remove(key);
        while (state.Events.TryPeek(out var item) && now - item.OccurredAtUtc > HistoryLifetime) state.Events.Dequeue();
    }
    public bool Presence(int storeId, int userId, Guid tab, string person, string module,
        string? terminal, string state, string? workKey = null, string? document = null)
    {
        if (userId <= 0 || tab == Guid.Empty || !StoreActivityCatalog.Modules.Contains(module) ||
            state is not ("viewing" or "editing" or "leave")) return false;
        lock (gate)
        {
            var now = time.GetUtcNow();
            var store = Store(storeId, now);
            if (store is null) return false;
            var key = (userId, tab);
            if (state == "leave") { store.People.Remove(key); return true; }
            store.People.TryGetValue(key, out var old);
            if (old is null && (store.People.Count >= MaxPeople || store.People.Keys.Count(x => x.User == userId) >= 8)) return false;
            var cleanKey = workKey is null ? null : Clean(workKey, 80);
            var samePage = old?.Module == module && old.Terminal == Clean(terminal, 50) && old.WorkKey == cleanKey;
            store.People[key] = new(userId, Clean(person, 100), module, Clean(terminal, 50), now,
                state == "editing" ? now : samePage ? old!.EditedAtUtc : null,
                samePage ? old!.OpenedAtUtc : now, cleanKey, document is null ? null : Clean(document, 70));
            return true;
        }
    }
    public void Record(int storeId, int userId, string person, string module, string text,
        string? document, string? terminal, string? action = null, string? detail = null, string? workKey = null)
    {
        if (userId <= 0 || !StoreActivityCatalog.Modules.Contains(module)) return;
        lock (gate)
        {
            var now = time.GetUtcNow();
            var store = Store(storeId, now);
            if (store is null) return;
            store.Events.Enqueue(new(++nextId, userId, Clean(person, 100), module, Clean(text, 160),
                Clean(document, 70), Clean(terminal, 50), now,
                action is null ? null : Clean(action, 80), detail is null ? null : Clean(detail, 240),
                workKey is null ? null : Clean(workKey, 80)));
            while (store.Events.Count > MaxEvents) store.Events.Dequeue();
        }
    }
    public StoreActivitySnapshot Snapshot(int storeId)
    {
        lock (gate)
        {
            var now = time.GetUtcNow();
            var store = Store(storeId, now);
            if (store is null) return new(now, now, [], []);
            var people = store.People.Values.GroupBy(x => (x.UserId, x.Module, x.Terminal, x.WorkKey)).Select(group =>
            {
                var current = group.OrderByDescending(x => x.EditedAtUtc is { } edited && now - edited <= EditingLifetime)
                    .ThenByDescending(x => x.SeenAtUtc).First();
                return new StoreActivityPerson(current.UserId, current.Person, current.Module,
                    current.EditedAtUtc is { } at && now - at <= EditingLifetime ? "editing" : "viewing",
                    current.Terminal, current.SeenAtUtc, group.Count(), group.Min(x => x.OpenedAtUtc), current.EditedAtUtc,
                    current.WorkKey, current.Document);
            }).OrderBy(x => x.Terminal).ThenBy(x => x.Person).ToArray();
            return new(now, store.StartedAtUtc, people, store.Events.Reverse().ToArray());
        }
    }
    public IDisposable? TryOpenStream(int storeId)
    {
        lock (gate)
        {
            var store = Store(storeId, time.GetUtcNow());
            if (store is null || store.Streams >= 8) return null;
            store.Streams++;
            return new StreamLease(() => { lock (gate) store.Streams--; });
        }
    }
    private sealed class StreamLease(Action close) : IDisposable
    {
        private Action? action = close;
        public void Dispose() => Interlocked.Exchange(ref action, null)?.Invoke();
    }
    private static string Clean(string? value, int max)
    {
        var clean = new string((value ?? "").Where(c => !char.IsControl(c)).ToArray()).Trim();
        return clean.Length <= max ? clean : clean[..max];
    }
}
