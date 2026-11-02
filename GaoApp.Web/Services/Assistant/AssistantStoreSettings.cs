using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using GaoApp.Application.Common.Security;
using Microsoft.Extensions.Options;

namespace GaoApp.Web.Services.Assistant;

public sealed record AssistantStoreSwitches([property: JsonRequired] int StoreId, [property: JsonRequired] bool Enabled = true, [property: JsonRequired] bool ChatEnabled = true,
    [property: JsonRequired] bool ReceivingEnabled = true, [property: JsonRequired] bool NormalizeEnabled = true, [property: JsonRequired] string Version = "initial",
    DateTimeOffset? UpdatedAt = null, int? UpdatedBy = null)
{
    public string[] Filter(IEnumerable<string> permissions) => !Enabled ? [] : permissions.Where(p =>
        (ChatEnabled || p is not (AssistantPermissions.Chat or AssistantPermissions.Retail or AssistantPermissions.Wholesale))
        && (ReceivingEnabled || p != AssistantPermissions.Receiving)
        && (NormalizeEnabled || p != AssistantPermissions.NormalizeName)).Order(StringComparer.Ordinal).ToArray();
}

// Store-specific switches remain available even when the assistant connector is offline.
public sealed class AssistantStoreSettings(IWebHostEnvironment environment, IOptionsMonitor<AssistantOptions> options,
    TimeProvider clock, ILogger<AssistantStoreSettings> logger)
{
    private readonly ConcurrentDictionary<int, SemaphoreSlim> gates = new();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) {
        WriteIndented = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    private string FileName(int storeId)
    {
        if (storeId <= 0) throw AssistantException.Denied();
        return AssistantStorageSafety.SettingsFile(environment, options.CurrentValue, $"store-{storeId}.json");
    }
    public async Task<AssistantStoreSwitches> ReadAsync(int storeId, CancellationToken ct)
    {
        var path = FileName(storeId);
        try
        {
            await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete,
                4096, FileOptions.Asynchronous);
            if (file.Length > 16384) throw new JsonException();
            var value = await JsonSerializer.DeserializeAsync<AssistantStoreSwitches>(file, Json, ct);
            if (value is null || value.StoreId != storeId || !Guid.TryParseExact(value.Version, "N", out _))
                throw new JsonException();
            return value;
        }
        catch (FileNotFoundException) { return new(storeId); }
        catch (DirectoryNotFoundException) { return new(storeId); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            logger.LogWarning("Assistant store settings unavailable for store {StoreId}: {Type}", storeId, ex.GetType().Name);
            throw new AssistantException(503, "assistant_settings_unavailable", "Chưa đọc được cấu hình AI. Vui lòng thử lại.");
        }
    }
    public async Task<AssistantStoreSwitches> SaveAsync(int storeId, int userId, AssistantStoreSwitches proposed,
        string expectedVersion, CancellationToken ct)
    {
        var path = FileName(storeId);
        var gate = gates.GetOrAdd(storeId, _ => new(1, 1));
        await gate.WaitAsync(ct);
        string? temporary = null;
        try
        {
            var current = await ReadAsync(storeId, ct);
            if (!string.Equals(current.Version, expectedVersion, StringComparison.Ordinal))
                throw new AssistantException(409, "assistant_settings_conflict", "Cấu hình đã được thay đổi. Tải lại trang trước khi lưu.");
            var next = proposed with { StoreId = storeId, Version = Guid.NewGuid().ToString("N"),
                UpdatedAt = clock.GetUtcNow(), UpdatedBy = userId };
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(next, Json), ct);
            File.Move(temporary, path, overwrite: true);
            temporary = null;
            return next;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning("Assistant store settings save failed for store {StoreId}: {Type}", storeId, ex.GetType().Name);
            throw new AssistantException(503, "assistant_settings_unavailable", "Chưa lưu được cấu hình AI. Vui lòng thử lại.");
        }
        finally
        {
            if (temporary is not null)
            {
                try { File.Delete(temporary); }
                catch (IOException error)
                {
                    logger.LogDebug("Assistant settings temporary cleanup failed for store {StoreId}: {Type}", storeId, error.GetType().Name);
                }
            }
            gate.Release();
        }
    }
}
