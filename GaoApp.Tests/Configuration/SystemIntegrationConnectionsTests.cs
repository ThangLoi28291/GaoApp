using System.Security.Cryptography;
using System.Text.Json;
using GaoApp.Web.Services.Assistant;
using GaoApp.Web.Services.CustomerService;
using GaoApp.Web.Services.SystemIntegrations;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;

namespace GaoApp.Tests.Configuration;

public sealed class SystemIntegrationConnectionsTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "GaoIntegrationConnections", Guid.NewGuid().ToString("N"));
    private readonly IDataProtectionProvider protection = new EphemeralDataProtectionProvider();
    private const string Token = "SYNTHETIC-ADMIN-TOKEN-DO-NOT-SEND";

    [Fact]
    public async Task Encrypted_UI_connections_override_running_options_and_survive_restart_without_secret_files()
    {
        using var first = Services();
        var store = first.GetRequiredService<SystemIntegrationSwitchStore>();
        using var rsa = RSA.Create(2048);
        var form = Form();
        form.AiBaseUrl = "https://synthetic-ai.example.test";
        form.AiIssuer = "synthetic-issuer";
        form.AiKeyId = "synthetic-key";
        form.AiPrivateKeyPem = rsa.ExportRSAPrivateKeyPem();
        // Populate caches before saving to exercise live invalidation.
        Assert.False(first.GetRequiredService<IOptionsMonitor<AssistantOptions>>().CurrentValue.Enabled);
        Assert.Empty(first.GetRequiredService<IOptionsMonitor<CustomerChannelOptions>>().CurrentValue.Connections);

        var saved = await store.SaveConnectionsAsync(form, 7, default);
        Assert.False(store.State().ZaloEnabled);
        Assert.False(store.State().AiEnabled);
        var raw = File.ReadAllText(Path.Combine(directory, "system-integration-switches.json"));
        Assert.DoesNotContain(Token, raw);
        Assert.DoesNotContain("PRIVATE KEY", raw);
        Assert.DoesNotContain("synthetic-ai.example.test", raw);
        Assert.DoesNotContain(Token, JsonSerializer.Serialize(store.State()));
        Assert.DoesNotContain("PRIVATE KEY", JsonSerializer.Serialize(store.State()));
        Assert.True(store.State().Connections!.HasAiKey);
        Assert.True(Assert.Single(store.State().Connections!.Zalo).HasToken);
        saved = await store.SaveAsync(true, true, false, saved.Version, 7, default);
        form.Version = saved.Version; form.AiPrivateKeyPem = ""; form.Zalo[0].AccessToken = "";
        await store.SaveConnectionsAsync(form, 7, default);

        using var restarted = Services();
        var options = restarted.GetRequiredService<IOptionsMonitor<AssistantOptions>>().CurrentValue;
        Assert.True(options.Enabled);
        Assert.Equal(form.AiBaseUrl, options.BaseUrl);
        Assert.Equal(form.AiIssuer, options.Issuer);
        Assert.Equal(form.AiKeyId, options.KeyId);
        var registry = new CustomerChannelRegistry(restarted.GetRequiredService<IOptionsMonitor<CustomerChannelOptions>>(), Environment(), new ConfigurationBuilder().Build());
        var connection = registry.Get("zalo-test");
        Assert.True(connection.Enabled);
        Assert.True(connection.AutoReply);
        Assert.Equal(Token, registry.Secret(connection.AccessTokenFile));
        CustomerChannelCredentialSafety.Validate(restarted.GetRequiredService<IOptionsMonitor<CustomerChannelOptions>>().CurrentValue, Environment());
        var jwt = new AssistantJwt(restarted.GetRequiredService<IOptionsMonitor<AssistantOptions>>(), Environment(), TimeProvider.System);
        var pair = jwt.SignCustomer(new(Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid()));
        Assert.Equal(3, pair.Context.Split('.').Length);
    }

    [Fact]
    public async Task Blank_secret_inputs_keep_saved_secrets_and_rotation_changes_the_transport_guard()
    {
        using var provider = Services();
        var store = provider.GetRequiredService<SystemIntegrationSwitchStore>();
        var saved = await store.SaveConnectionsAsync(Form(), 1, default);
        var registry = new CustomerChannelRegistry(provider.GetRequiredService<IOptionsMonitor<CustomerChannelOptions>>(), Environment());
        saved = await store.SaveAsync(true, false, false, saved.Version, 1, default);
        var old = registry.Get("zalo-test");
        var edit = Form(); edit.Version = saved.Version; edit.Zalo[0].AccessToken = "";
        saved = await store.SaveConnectionsAsync(edit, 2, default);
        Assert.Equal(Token, registry.Secret(registry.Get("zalo-test").AccessTokenFile));
        edit.Version = saved.Version; edit.Zalo[0].AccessToken = Token + "-ROTATED";
        await store.SaveConnectionsAsync(edit, 2, default);
        var current = registry.Get("zalo-test");
        Assert.NotEqual(old.AccessTokenFile, current.AccessTokenFile);
        Assert.Equal(old.Binding, current.Binding);
        Assert.Equal(Token + "-ROTATED", registry.Secret(current.AccessTokenFile));
        await Assert.ThrowsAsync<AssistantException>(() => Task.FromResult(registry.Secret(old.AccessTokenFile)));
    }

    [Theory]
    [InlineData("http://remote.example.test")]
    [InlineData("https://user:password@example.test")]
    [InlineData("https://example.test?token=secret")]
    [InlineData("file:///private/key")]
    public async Task Invalid_service_urls_are_rejected_without_saving(string url)
    {
        using var provider = Services();
        var store = provider.GetRequiredService<SystemIntegrationSwitchStore>();
        var form = Form(); form.Zalo[0].BaseUrl = url;
        var ex = await Assert.ThrowsAsync<AssistantException>(() => store.SaveConnectionsAsync(form, 1, default));
        Assert.Equal(400, ex.Status);
        Assert.Equal("initial", store.Read().Version);
        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public async Task Public_RSA_key_is_rejected_and_cannot_replace_working_configuration()
    {
        using var provider = Services();
        var store = provider.GetRequiredService<SystemIntegrationSwitchStore>();
        var saved = await store.SaveConnectionsAsync(Form(), 1, default);
        var form = Form(); form.Version = saved.Version;
        using var rsa = RSA.Create(2048);
        form.AiBaseUrl = "https://synthetic.example.test"; form.AiIssuer = "test"; form.AiKeyId = "test";
        form.AiPrivateKeyPem = rsa.ExportRSAPublicKeyPem();
        var ex = await Assert.ThrowsAsync<AssistantException>(() => store.SaveConnectionsAsync(form, 1, default));
        Assert.Equal("ai_key_invalid", ex.Code);
        Assert.Equal(saved.Version, store.Read().Version);
    }

    [Fact]
    public async Task Stale_connection_form_cannot_overwrite_switch_changes()
    {
        using var provider = Services();
        var store = provider.GetRequiredService<SystemIntegrationSwitchStore>();
        var saved = await store.SaveAsync(false, false, false, "initial", 1, default);
        var ex = await Assert.ThrowsAsync<AssistantException>(() => store.SaveConnectionsAsync(Form(), 1, default));
        Assert.Equal(409, ex.Status);
        Assert.Equal(saved.Version, store.Read().Version);
    }

    [Fact]
    public async Task Other_running_host_reloads_encrypted_connection_changes()
    {
        using var first = Services(); using var second = Services();
        var options = second.GetRequiredService<IOptionsMonitor<CustomerChannelOptions>>();
        Assert.Empty(options.CurrentValue.Connections);
        await first.GetRequiredService<SystemIntegrationSwitchStore>().SaveConnectionsAsync(Form(), 1, default);
        var deadline = DateTime.UtcNow.AddSeconds(8);
        while (options.CurrentValue.Connections.Count == 0 && DateTime.UtcNow < deadline) await Task.Delay(50);
        Assert.Equal("zalo-test", Assert.Single(options.CurrentValue.Connections).Id);
    }

    [Fact]
    public async Task Wrong_protection_key_cannot_read_saved_connection_secrets()
    {
        using var first = Services();
        await first.GetRequiredService<SystemIntegrationSwitchStore>().SaveConnectionsAsync(Form(), 1, default);
        using var wrong = Services(new EphemeralDataProtectionProvider());
        var ex = Assert.Throws<AssistantException>(() => wrong.GetRequiredService<SystemIntegrationSwitchStore>().State());
        Assert.Equal("integration_connections_unavailable", ex.Code);
        Assert.DoesNotContain(Token, ex.Message);
    }

    [Fact]
    public async Task Saving_unchanged_legacy_connection_keeps_its_binding_and_private_token_file()
    {
        Directory.CreateDirectory(directory);
        var tokenFile = Path.Combine(directory, "synthetic.token");
        File.WriteAllText(tokenFile, Token);
        var account = Guid.NewGuid().ToString().ToUpperInvariant();
        using var provider = Services(values: new()
        {
            ["CustomerChannels:CredentialDirectory"] = directory,
            ["CustomerChannels:Connections:0:Id"] = "zalo-test", ["CustomerChannels:Connections:0:StoreId"] = "1",
            ["CustomerChannels:Connections:0:Provider"] = "zalomarketing", ["CustomerChannels:Connections:0:Enabled"] = "true",
            ["CustomerChannels:Connections:0:BaseUrl"] = "https://synthetic-zalo.example.test/",
            ["CustomerChannels:Connections:0:AccountId"] = account, ["CustomerChannels:Connections:0:AccessTokenFile"] = tokenFile
        });
        var options = provider.GetRequiredService<IOptionsMonitor<CustomerChannelOptions>>();
        var before = Assert.Single(options.CurrentValue.Connections).Binding;
        var store = provider.GetRequiredService<SystemIntegrationSwitchStore>();
        var form = Form(); form.Zalo[0].BaseUrl = "https://synthetic-zalo.example.test/"; form.Zalo[0].AccountId = account;
        form.Zalo[0].AccessToken = "";
        var saved = await store.SaveConnectionsAsync(form, 1, default);
        await store.SaveAsync(true, false, false, saved.Version, 1, default);
        var registry = new CustomerChannelRegistry(options, Environment());

        Assert.Equal(before, registry.Get("zalo-test").Binding);
        Assert.Equal(tokenFile, registry.Get("zalo-test").AccessTokenFile);
        Assert.Equal(Token, registry.Secret(tokenFile));
        Assert.Equal(Token, File.ReadAllText(tokenFile));
    }

    private static SystemIntegrationConnectionsForm Form() => new()
    {
        Version = "initial", Zalo = [new() { Id = "zalo-test", StoreId = 1,
            BaseUrl = "https://synthetic-zalo.example.test", AccountId = "e87cc3c8-757c-4ffa-bfe1-b15c8721b509", AccessToken = Token }]
    };

    private ServiceProvider Services(IDataProtectionProvider? keys = null, Dictionary<string, string?>? values = null)
    {
        var defaults = new Dictionary<string, string?>
        {
            ["Assistant:StoreSettingsDirectory"] = directory, ["Assistant:Enabled"] = "false",
            ["CustomerChannels:Enabled"] = "false", ["CustomerChannels:WorkerEnabled"] = "false"
        };
        foreach (var item in values ?? []) defaults[item.Key] = item.Value;
        var config = new ConfigurationBuilder().AddInMemoryCollection(defaults).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config); services.AddSingleton<IWebHostEnvironment>(Environment());
        services.AddSingleton(TimeProvider.System); services.AddSingleton(keys ?? protection);
        services.Configure<AssistantOptions>(config.GetSection("Assistant"));
        services.Configure<CustomerChannelOptions>(config.GetSection("CustomerChannels"));
        services.AddSystemIntegrationControls();
        return services.BuildServiceProvider();
    }

    private static IWebHostEnvironment Environment() => new EnvironmentStub();
    public void Dispose()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "GaoIntegrationConnections")) + Path.DirectorySeparatorChar;
        var target = Path.GetFullPath(directory);
        if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !Guid.TryParseExact(Path.GetFileName(target), "N", out _))
            throw new InvalidOperationException("Unsafe test cleanup path.");
        if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
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
