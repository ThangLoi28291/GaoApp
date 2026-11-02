using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using GaoApp.Web.Services.Assistant;
using GaoApp.Web.Services.CustomerService;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;
using Microsoft.AspNetCore.DataProtection;

namespace GaoApp.Web.Services.SystemIntegrations;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SystemIntegrationSwitches(string Version = "initial", bool? ZaloEnabled = null,
    bool? AiEnabled = null, bool? CampaignsEnabled = null, DateTimeOffset? UpdatedAt = null, int? UpdatedBy = null,
    string? ProtectedConnections = null);

public sealed record SystemIntegrationState(SystemIntegrationSwitches Saved, bool ZaloEnabled, bool AiEnabled,
    bool CampaignsEnabled, int ConfiguredZaloConnections, SystemIntegrationConnectionsView? Connections = null);

// One version and atomic file replacement cover both switches and encrypted connections.
public sealed partial class SystemIntegrationSwitchStore(IConfiguration configuration, IWebHostEnvironment environment,
    TimeProvider clock, IOptionsMonitorCache<AssistantOptions> assistantCache,
    IOptionsMonitorCache<CustomerChannelOptions> channelCache,
    IOptionsMonitorCache<CustomerMarketingCampaignOptions> campaignCache, IDataProtectionProvider protection) : IDisposable
{
    private readonly IDataProtector protector = protection.CreateProtector("GaoApp.SystemIntegrationConnections.v1");
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly object watcherGate = new();
    private PhysicalFileProvider? watcher;
    private string? watchedFile;

    public IChangeToken GetChangeToken()
    {
        lock (watcherGate)
        {
            if (watcher is null)
            {
                var file = FileName();
                var parent = new DirectoryInfo(Path.GetDirectoryName(file)!);
                while (!parent.Exists && parent.Parent is not null) parent = parent.Parent;
                watcher = new PhysicalFileProvider(parent.FullName);
                watchedFile = Path.GetRelativePath(parent.FullName, file).Replace('\\', '/');
            }
            return watcher.Watch(watchedFile!);
        }
    }

    private AssistantOptions BaseAssistant() => configuration.GetSection(AssistantOptions.Section).Get<AssistantOptions>() ?? new();
    private CustomerChannelOptions BaseChannels() => configuration.GetSection(CustomerChannelOptions.Section).Get<CustomerChannelOptions>() ?? new();
    public int ConfiguredZaloConnections => ConnectionView(Read()).Zalo.Length;
    private string FileName()
    {
        var assistant = BaseAssistant();
        var directory = Path.GetFullPath(assistant.StoreSettingsDirectory, environment.ContentRootPath);
        if (!environment.IsDevelopment()) AssistantStorageSafety.Settings(assistant.StoreSettingsDirectory, assistant.StorageId, environment);
        var path = Path.Combine(directory, "system-integration-switches.json");
        AssistantStorageSafety.NoLinks(path);
        return path;
    }

    public SystemIntegrationSwitches Read()
    {
        var path = FileName();
        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            if (file.Length > 1048576) throw new JsonException();
            var saved = JsonSerializer.Deserialize<SystemIntegrationSwitches>(file, Json);
            if (saved is null || !Guid.TryParseExact(saved.Version, "N", out _) || saved.UpdatedBy is not > 0 ||
                saved.UpdatedAt is null)
                throw new JsonException();
            return saved;
        }
        catch (FileNotFoundException) { return new(); }
        catch (DirectoryNotFoundException) { return new(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        { throw new AssistantException(503, "integration_switches_unavailable", "Chưa đọc được công tắc tích hợp. Vui lòng thử lại."); }
    }

    public SystemIntegrationState State()
    {
        var saved = Read();
        var channels = BaseChannels();
        ApplyConnections(channels, Connections(saved));
        var configured = channels.Connections.Where(c => c.Provider == "zalomarketing").ToArray();
        return new(saved,
            saved.ZaloEnabled ?? (channels.Enabled && channels.WorkerEnabled && configured.Any(c => c.Enabled)),
            saved.AiEnabled ?? BaseAssistant().Enabled,
            saved.CampaignsEnabled ?? configuration.GetValue<bool>(CustomerMarketingCampaignOptions.Section + ":Enabled"),
            configured.Length, ConnectionView(saved));
    }

    public void Apply(AssistantOptions options)
    {
        var saved = Read();
        ApplyConnections(options, Connections(saved));
        if (saved.AiEnabled is bool enabled) options.Enabled = enabled;
    }

    public void Apply(CustomerChannelOptions options)
    {
        var saved = Read();
        ApplyConnections(options, Connections(saved));
        ApplyChannels(options, saved);
    }

    public void Apply(CustomerMarketingCampaignOptions options)
    {
        var saved = Read();
        if (saved.CampaignsEnabled is bool enabled) options.Enabled = enabled;
        if (saved.ZaloEnabled == false) options.Enabled = false;
    }

    public static void ApplyChannels(CustomerChannelOptions options, SystemIntegrationSwitches saved)
    {
        if (saved.ZaloEnabled is bool enabled)
        {
            var previouslyRunning = options.Enabled && options.WorkerEnabled;
            foreach (var connection in options.Connections)
            {
                if (connection.Provider == "zalomarketing")
                {
                    connection.Enabled = enabled;
                    if (!enabled) connection.LiveReplyPilot.Enabled = false;
                }
                else if (enabled && !previouslyRunning) connection.Enabled = false;
            }
            if (enabled) { options.Enabled = true; options.WorkerEnabled = true; }
        }
        if (saved.AiEnabled == false)
            foreach (var connection in options.Connections)
            { connection.AutoReply = false; connection.LiveReplyPilot.Enabled = false; }
        else if (saved.AiEnabled == true)
            foreach (var connection in options.Connections.Where(c => c.Provider == "zalomarketing")) connection.AutoReply = true;
    }

    public Task<SystemIntegrationSwitches> SaveAsync(bool zaloEnabled, bool aiEnabled, bool campaignsEnabled,
        string version, int userId, CancellationToken ct)
        => SaveDocumentAsync(version, userId, current =>
        {
            var next = current with { ZaloEnabled = zaloEnabled, AiEnabled = aiEnabled, CampaignsEnabled = campaignsEnabled };
            ValidateEnabling(next);
            return next;
        }, ct);

    private async Task<SystemIntegrationSwitches> SaveDocumentAsync(string version, int userId,
        Func<SystemIntegrationSwitches, SystemIntegrationSwitches> change, CancellationToken ct)
    {
        if (userId <= 0) throw AssistantException.Denied();
        await gate.WaitAsync(ct);
        string? temporary = null;
        FileStream? writeLock = null;
        try
        {
            var current = Read();
            AssistantStorageSafety.Available(Path.GetDirectoryName(FileName())!);
            if (!string.Equals(current.Version, version, StringComparison.Ordinal))
                throw new AssistantException(409, "integration_switches_conflict", "Cấu hình vừa được thay đổi. Tải lại trang trước khi lưu.");
            var proposed = change(current);
            var path = FileName();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            AssistantStorageSafety.NoLinks(path + ".lock");
            try { writeLock = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) { throw new AssistantException(409, "integration_switches_busy", "Cấu hình đang được cập nhật. Tải lại trang trước khi lưu."); }
            current = Read();
            if (!string.Equals(current.Version, version, StringComparison.Ordinal))
                throw new AssistantException(409, "integration_switches_conflict", "Cấu hình vừa được thay đổi. Tải lại trang trước khi lưu.");
            AssistantStorageSafety.Available(Path.GetDirectoryName(path)!);
            var next = proposed with { Version = Guid.NewGuid().ToString("N"), UpdatedAt = clock.GetUtcNow(), UpdatedBy = userId };
            var bytes = JsonSerializer.SerializeToUtf8Bytes(next, Json);
            if (bytes.Length > 1048576)
                throw new AssistantException(400, "integration_configuration_too_large", "Cấu hình kết nối vượt dung lượng cho phép.");
            temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await File.WriteAllBytesAsync(temporary, bytes, ct);
            File.Move(temporary, path, overwrite: true);
            temporary = null;
            assistantCache.TryRemove(Options.DefaultName);
            channelCache.TryRemove(Options.DefaultName);
            campaignCache.TryRemove(Options.DefaultName);
            return next;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { throw new AssistantException(503, "integration_switches_unavailable", "Chưa lưu được công tắc tích hợp. Vui lòng thử lại."); }
        finally
        {
            if (temporary is not null) { try { File.Delete(temporary); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { } }
            writeLock?.Dispose();
            gate.Release();
        }
    }

    private void ValidateEnabling(SystemIntegrationSwitches proposed)
    {
        var document = Connections(proposed);
        var channels = BaseChannels();
        ApplyConnections(channels, document);
        var zaloRunning = proposed.ZaloEnabled ?? (channels.Enabled && channels.WorkerEnabled && channels.Connections.Any(c => c.Provider == "zalomarketing" && c.Enabled));
        if (zaloRunning)
        {
            if (!channels.Connections.Any(c => c.Provider == "zalomarketing"))
                throw new AssistantException(400, "zalo_not_configured", "Chưa có kết nối Zalo Marketing được cấu hình trên máy chủ.");
            ApplyChannels(channels, proposed);
            try
            {
                if (!channels.Valid()) throw new InvalidOperationException();
                CustomerChannelCredentialSafety.Validate(channels, environment, configuration);
            }
            catch (Exception e) when (e is InvalidOperationException or IOException or UnauthorizedAccessException)
            { throw new AssistantException(400, "zalo_not_ready", "Chưa bật được Zalo Marketing. Hãy kiểm tra cấu hình kết nối và quyền đọc khóa bảo mật trên máy chủ."); }
        }
        if (proposed.AiEnabled ?? BaseAssistant().Enabled)
        {
            var assistant = BaseAssistant();
            ApplyConnections(assistant, document);
            try
            {
                if (!Uri.TryCreate(assistant.BaseUrl, UriKind.Absolute, out var origin) || origin.UserInfo != "" || origin.Query != "" || origin.Fragment != "" ||
                    origin.Scheme != "https" && !(environment.IsDevelopment() && origin.Scheme == "http" && origin.IsLoopback) ||
                    string.IsNullOrWhiteSpace(assistant.Issuer) || string.IsNullOrWhiteSpace(assistant.KeyId) ||
                    assistant.TokenLifetimeSeconds is < 1 or > 120) throw new InvalidOperationException();
                using var rsa = AssistantSigningKey.Load(assistant, environment);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or CryptographicException)
            { throw new AssistantException(400, "ai_not_ready", "Chưa bật được AI. Hãy kiểm tra địa chỉ dịch vụ và khóa kết nối đã cấu hình trên máy chủ."); }
        }
    }

    public void Dispose()
    {
        watcher?.Dispose();
        gate.Dispose();
    }
}

public sealed class SystemIntegrationChangeTokenSource<T>(SystemIntegrationSwitchStore switches) : IOptionsChangeTokenSource<T> where T : class
{
    public string Name => Options.DefaultName;
    public IChangeToken GetChangeToken() => switches.GetChangeToken();
}

public static class SystemIntegrationRegistration
{
    public static IServiceCollection AddSystemIntegrationControls(this IServiceCollection services)
    {
        services.AddDataProtection();
        services.AddSingleton<SystemIntegrationSwitchStore>();
        services.AddSingleton<IOptionsChangeTokenSource<AssistantOptions>, SystemIntegrationChangeTokenSource<AssistantOptions>>();
        services.AddSingleton<IOptionsChangeTokenSource<CustomerChannelOptions>, SystemIntegrationChangeTokenSource<CustomerChannelOptions>>();
        services.AddSingleton<IOptionsChangeTokenSource<CustomerMarketingCampaignOptions>, SystemIntegrationChangeTokenSource<CustomerMarketingCampaignOptions>>();
        services.AddOptions<AssistantOptions>().PostConfigure<SystemIntegrationSwitchStore>((options, switches) => switches.Apply(options));
        services.AddOptions<CustomerChannelOptions>().PostConfigure<SystemIntegrationSwitchStore>((options, switches) => switches.Apply(options));
        services.AddOptions<CustomerMarketingCampaignOptions>().PostConfigure<SystemIntegrationSwitchStore>((options, switches) => switches.Apply(options));
        return services;
    }
}
