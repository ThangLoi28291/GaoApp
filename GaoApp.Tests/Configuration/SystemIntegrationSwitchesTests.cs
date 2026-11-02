using GaoApp.Web.Services.Assistant;
using GaoApp.Web.Services.CustomerService;
using GaoApp.Web.Services.SystemIntegrations;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace GaoApp.Tests.Configuration;

public sealed class SystemIntegrationSwitchesTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "GaoIntegrationControls", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Saved_switches_refresh_running_options_and_survive_a_new_service_provider()
    {
        using var provider = Services();
        var store = provider.GetRequiredService<SystemIntegrationSwitchStore>();
        var ai = provider.GetRequiredService<IOptionsMonitor<AssistantOptions>>();
        var channels = provider.GetRequiredService<IOptionsMonitor<CustomerChannelOptions>>();
        var campaigns = provider.GetRequiredService<IOptionsMonitor<CustomerMarketingCampaignOptions>>();
        Assert.True(ai.CurrentValue.Enabled);
        Assert.True(channels.CurrentValue.Connections[0].Enabled);
        Assert.True(campaigns.CurrentValue.Enabled);
        Assert.False(Directory.Exists(directory));

        var saved = await store.SaveAsync(false, false, false, "initial", 7, default);

        Assert.False(ai.CurrentValue.Enabled);
        Assert.False(channels.CurrentValue.Connections[0].Enabled);
        Assert.False(channels.CurrentValue.Connections[0].AutoReply);
        Assert.False(campaigns.CurrentValue.Enabled);
        Assert.Equal(7, saved.UpdatedBy);
        using var restarted = Services();
        Assert.False(restarted.GetRequiredService<IOptionsMonitor<AssistantOptions>>().CurrentValue.Enabled);
        Assert.Equal(saved.Version, restarted.GetRequiredService<SystemIntegrationSwitchStore>().Read().Version);
        var raw = File.ReadAllText(Path.Combine(directory, "system-integration-switches.json"));
        Assert.DoesNotContain("SYNTHETIC_CONFIG_TOKEN", raw);
        Assert.DoesNotContain("BaseUrl", raw);
        Assert.DoesNotContain("PrivateKeyFile", raw);
    }

    [Fact]
    public async Task Concurrent_hosts_cannot_both_replace_the_same_version()
    {
        using var first = Services();
        using var second = Services();
        async Task<bool> Save(ServiceProvider provider)
        {
            try
            {
                await provider.GetRequiredService<SystemIntegrationSwitchStore>().SaveAsync(false, false, false, "initial", 1, default);
                return true;
            }
            catch (AssistantException e) when (e.Status == 409) { return false; }
        }

        var results = await Task.WhenAll(Save(first), Save(second));

        Assert.Single(results, saved => saved);
        Assert.Single(results, saved => !saved);
    }

    [Fact]
    public async Task Another_running_host_observes_saved_switches_without_restarting()
    {
        using var first = Services();
        using var second = Services();
        var secondAi = second.GetRequiredService<IOptionsMonitor<AssistantOptions>>();
        Assert.True(secondAi.CurrentValue.Enabled);

        await first.GetRequiredService<SystemIntegrationSwitchStore>().SaveAsync(false, false, false, "initial", 1, default);

        var timeout = DateTime.UtcNow.AddSeconds(8);
        while (secondAi.CurrentValue.Enabled && DateTime.UtcNow < timeout) await Task.Delay(50);
        Assert.False(secondAi.CurrentValue.Enabled);
    }

    [Fact]
    public async Task Existing_switches_remain_readable_during_storage_maintenance_but_saves_are_blocked()
    {
        using var provider = Services();
        var store = provider.GetRequiredService<SystemIntegrationSwitchStore>();
        var saved = await store.SaveAsync(false, false, false, "initial", 1, default);
        File.WriteAllText(Path.Combine(directory, AssistantStorageSafety.Hold), "synthetic maintenance");

        Assert.Equal(saved.Version, store.Read().Version);
        Assert.False(store.State().AiEnabled);
        var error = await Assert.ThrowsAsync<AssistantException>(() => store.SaveAsync(false, false, true, saved.Version, 1, default));
        Assert.Equal("assistant_storage_maintenance", error.Code);
        Assert.Equal(saved.Version, store.Read().Version);
    }

    [Fact]
    public async Task Conflicting_save_cannot_overwrite_newer_switches()
    {
        using var provider = Services();
        var store = provider.GetRequiredService<SystemIntegrationSwitchStore>();
        var saved = await store.SaveAsync(false, false, false, "initial", 1, default);

        var error = await Assert.ThrowsAsync<AssistantException>(() => store.SaveAsync(false, false, true, "initial", 2, default));

        Assert.Equal(409, error.Status);
        Assert.Equal(saved.Version, store.Read().Version);
        Assert.False(store.State().CampaignsEnabled);
    }

    [Fact]
    public async Task Ai_can_be_enabled_from_UI_even_when_its_configured_master_switch_is_off()
    {
        Directory.CreateDirectory(directory);
        var key = Path.Combine(directory, "synthetic-key.pem");
        using (var rsa = RSA.Create(2048)) File.WriteAllText(key, rsa.ExportRSAPrivateKeyPem());
        using var provider = Services(new()
        {
            ["Assistant:Enabled"] = "false", ["Assistant:BaseUrl"] = "http://localhost:59999",
            ["Assistant:Issuer"] = "synthetic-tests", ["Assistant:KeyId"] = "synthetic-key", ["Assistant:PrivateKeyFile"] = key
        });
        var store = provider.GetRequiredService<SystemIntegrationSwitchStore>();

        await store.SaveAsync(false, true, false, "initial", 1, default);

        Assert.True(provider.GetRequiredService<IOptionsMonitor<AssistantOptions>>().CurrentValue.Enabled);
        Assert.True(provider.GetRequiredService<IOptionsMonitor<CustomerChannelOptions>>().CurrentValue.Connections[0].AutoReply);
    }

    [Fact]
    public async Task Enabling_unconfigured_Zalo_fails_without_persisting_or_changing_the_running_flags()
    {
        using var provider = Services(new() { ["CustomerChannels:Connections:0:Provider"] = "facebook" });
        var store = provider.GetRequiredService<SystemIntegrationSwitchStore>();

        var error = await Assert.ThrowsAsync<AssistantException>(() => store.SaveAsync(true, false, false, "initial", 1, default));

        Assert.Equal("zalo_not_configured", error.Code);
        Assert.Equal("initial", store.Read().Version);
        Assert.False(Directory.Exists(directory));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void Enabling_Zalo_does_not_start_previously_stopped_Facebook(bool master, bool worker)
    {
        var options = Channels(master, worker);

        SystemIntegrationSwitchStore.ApplyChannels(options, new(ZaloEnabled: true));

        Assert.True(options.Enabled);
        Assert.True(options.WorkerEnabled);
        Assert.True(options.Connections[0].Enabled);
        Assert.False(options.Connections[1].Enabled);
    }

    [Fact]
    public void Disabling_Zalo_keeps_an_already_running_Facebook_channel_unchanged()
    {
        var options = Channels(true, true);

        SystemIntegrationSwitchStore.ApplyChannels(options, new(ZaloEnabled: false));

        Assert.False(options.Connections[0].Enabled);
        Assert.True(options.Connections[1].Enabled);
        Assert.True(options.WorkerEnabled);
    }

    [Fact]
    public async Task Disabled_AI_blocks_customer_requests_and_pending_AI_reply_revalidation_before_any_network_access()
    {
        using var http = new HttpClient(new RejectNetwork());
        var options = new Monitor<AssistantOptions>(new() { Enabled = false });
        var bridge = new CustomerAiBridge(http, null!, options, new EnvironmentStub(), null!, null!, null!, TimeProvider.System);

        await Assert.ThrowsAsync<AssistantException>(() => bridge.Reply(null!, "test", "", default));
        Assert.False(await bridge.PolicyVersionCurrentAsync(1, "old-policy", default));
    }

    private ServiceProvider Services(Dictionary<string, string?>? extra = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["Assistant:StoreSettingsDirectory"] = directory, ["Assistant:Enabled"] = "true",
            ["CustomerChannels:Enabled"] = "true", ["CustomerChannels:WorkerEnabled"] = "true",
            ["CustomerChannels:Connections:0:Provider"] = "zalomarketing", ["CustomerChannels:Connections:0:Enabled"] = "true",
            ["CustomerChannels:Connections:0:AutoReply"] = "true", ["CustomerChannels:Connections:0:AccessTokenFile"] = "SYNTHETIC_CONFIG_TOKEN",
            ["CustomerMarketingCampaigns:Enabled"] = "true"
        };
        foreach (var pair in extra ?? new Dictionary<string, string?>()) values[pair.Key] = pair.Value;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IWebHostEnvironment>(new EnvironmentStub());
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        services.Configure<AssistantOptions>(configuration.GetSection(AssistantOptions.Section));
        services.Configure<CustomerChannelOptions>(configuration.GetSection(CustomerChannelOptions.Section));
        services.Configure<CustomerMarketingCampaignOptions>(configuration.GetSection(CustomerMarketingCampaignOptions.Section));
        services.AddSystemIntegrationControls();
        return services.BuildServiceProvider();
    }

    private static CustomerChannelOptions Channels(bool master, bool worker) => new()
    {
        Enabled = master, WorkerEnabled = worker,
        Connections = [new() { Provider = "zalomarketing", Enabled = false }, new() { Provider = "facebook", Enabled = true }]
    };

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void Stopping_Zalo_or_AI_keeps_effective_channel_validation_valid_with_a_configured_live_pilot(bool zalo, bool ai)
    {
        var connection = new CustomerChannelConnection { Id = "zalo-pilot", StoreId = 1, Provider = "zalomarketing",
            AccountId = Guid.NewGuid().ToString(), BaseUrl = "https://synthetic.example.test", AccessTokenFile = "unused-private-file",
            Enabled = true, AutoReply = true, LiveReplyPilot = new() { Enabled = true, ConversationId = Guid.NewGuid(),
                NativePeer = "12345", AfterIncomingSequence = 1, StartedAtUtc = DateTimeOffset.UtcNow } };
        var options = new CustomerChannelOptions { Connections = [connection] };
        Assert.True(options.Valid());

        SystemIntegrationSwitchStore.ApplyChannels(options, new(ZaloEnabled: zalo, AiEnabled: ai));

        Assert.True(options.Valid());
        Assert.False(connection.LiveReplyPilot.Enabled);
    }

    public void Dispose()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "GaoIntegrationControls")) + Path.DirectorySeparatorChar;
        var target = Path.GetFullPath(directory);
        if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !Guid.TryParseExact(Path.GetFileName(target), "N", out _))
            throw new InvalidOperationException("Unsafe test cleanup path.");
        if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
    }

    private sealed class Monitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;
        public T Get(string? name) => value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private sealed class RejectNetwork : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Network must not be used by integration control tests.");
    }

    private sealed class EnvironmentStub : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "GaoApp.Web";
        public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public string WebRootPath { get; set; } = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}
