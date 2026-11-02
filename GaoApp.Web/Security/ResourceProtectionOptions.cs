namespace GaoApp.Web.Security;

public enum ResourceGroup
{
    AssistantIngress, AssistantSourceIngress, BankIngress, PhoneIngress, HeavyIngress, UploadIngress, OfflineIngress,
    AssistantWork, AssistantRead, AssistantControl, AssistantSource, BankAccepted, PhoneEvents, PhoneHeartbeat,
    HeavyRead, Upload, OfflineRead, OfflineControl, OfflineSync, CustomerIngress, CustomerWork, CustomerRead
}

public sealed class ResourceLimit
{
    public int CallerPerWindow { get; set; }
    public int StorePerWindow { get; set; }
    public int ProcessPerWindow { get; set; }
    public int CallerConcurrency { get; set; }
    public int StoreConcurrency { get; set; }
    public int ProcessConcurrency { get; set; }
    public bool ObserveOnly { get; set; }
    public static ResourceLimit Create(int caller, int store, int process, int callerConcurrent, int storeConcurrent, int processConcurrent)
        => new() { CallerPerWindow = caller, StorePerWindow = store, ProcessPerWindow = process,
            CallerConcurrency = callerConcurrent, StoreConcurrency = storeConcurrent, ProcessConcurrency = processConcurrent };
}

/// <summary>Per-process admission. Multi-instance aggregate quotas must be enforced by a shared gateway/counter.</summary>
public sealed class ResourceProtectionOptions
{
    public const string Section = "Security:ResourceProtection";
    public int WindowSeconds { get; set; } = 60;
    public int MaxPartitionsPerGroup { get; set; } = 2048;
    public int IdleSeconds { get; set; } = 600;
    public Dictionary<ResourceGroup, ResourceLimit> Limits { get; set; } = new()
    {
        [ResourceGroup.CustomerIngress] = ResourceLimit.Create(240, 0, 2400, 8, 0, 32),
        [ResourceGroup.CustomerWork] = ResourceLimit.Create(12, 120, 360, 2, 8, 16),
        [ResourceGroup.CustomerRead] = ResourceLimit.Create(120, 1200, 6000, 4, 24, 64),
        [ResourceGroup.AssistantIngress] = ResourceLimit.Create(1200, 0, 6000, 32, 0, 64),
        [ResourceGroup.AssistantSourceIngress] = ResourceLimit.Create(1800, 0, 6000, 32, 0, 64),
        // ACB ingress limits apply only to rejected authentication, never to valid bank intake.
        [ResourceGroup.BankIngress] = ResourceLimit.Create(1200, 0, 6000, 32, 0, 64),
        [ResourceGroup.PhoneIngress] = ResourceLimit.Create(1200, 0, 6000, 32, 0, 64),
        [ResourceGroup.HeavyIngress] = ResourceLimit.Create(2400, 0, 12000, 32, 0, 64),
        [ResourceGroup.UploadIngress] = ResourceLimit.Create(600, 0, 3000, 8, 0, 16),
        [ResourceGroup.OfflineIngress] = ResourceLimit.Create(6000, 0, 24000, 64, 0, 128),
        [ResourceGroup.AssistantWork] = ResourceLimit.Create(10, 60, 240, 2, 8, 16),
        [ResourceGroup.AssistantRead] = ResourceLimit.Create(120, 600, 2400, 4, 16, 32),
        [ResourceGroup.AssistantControl] = ResourceLimit.Create(120, 600, 2400, 4, 16, 32),
        [ResourceGroup.AssistantSource] = ResourceLimit.Create(120, 600, 2400, 4, 16, 32),
        // Observational thresholds only: the admission service always allows BankAccepted, even with old config overrides.
        [ResourceGroup.BankAccepted] = new() { StorePerWindow = 1200, ProcessPerWindow = 6000,
            StoreConcurrency = 8, ProcessConcurrency = 32, ObserveOnly = true },
        [ResourceGroup.PhoneEvents] = ResourceLimit.Create(120, 1200, 6000, 2, 8, 32),
        [ResourceGroup.PhoneHeartbeat] = ResourceLimit.Create(30, 300, 1800, 1, 4, 16),
        [ResourceGroup.HeavyRead] = ResourceLimit.Create(120, 600, 2400, 4, 16, 32),
        [ResourceGroup.Upload] = ResourceLimit.Create(60, 300, 1200, 2, 4, 8),
        // Bootstrap loads catalog, customers and promotions concurrently; allow that normal three-request burst.
        [ResourceGroup.OfflineRead] = ResourceLimit.Create(240, 2400, 12000, 4, 32, 96),
        [ResourceGroup.OfflineControl] = ResourceLimit.Create(120, 2400, 12000, 4, 32, 128),
        [ResourceGroup.OfflineSync] = ResourceLimit.Create(300, 3000, 18000, 1, 16, 64)
    };

    public bool IsValid() => WindowSeconds is >= 1 and <= 600 && IdleSeconds >= WindowSeconds * 2 && IdleSeconds <= 3600 &&
        MaxPartitionsPerGroup is >= 16 and <= 8192 && Limits is not null && Enum.GetValues<ResourceGroup>().All(g => Limits.TryGetValue(g, out var p) && p is not null &&
            p.CallerPerWindow is >= 0 and <= 100000 && p.StorePerWindow is >= 0 and <= 100000 && p.ProcessPerWindow is >= 1 and <= 100000 &&
            p.CallerConcurrency is >= 0 and <= 1024 && p.StoreConcurrency is >= 0 and <= 1024 && p.ProcessConcurrency is >= 1 and <= 1024 &&
            (!p.ObserveOnly || g == ResourceGroup.BankAccepted) &&
            (g == ResourceGroup.BankAccepted || p.CallerPerWindow > 0 && p.CallerConcurrency > 0) &&
            ((int)g <= (int)ResourceGroup.OfflineIngress || g == ResourceGroup.CustomerIngress || p.StorePerWindow > 0 && p.StoreConcurrency > 0));
}
