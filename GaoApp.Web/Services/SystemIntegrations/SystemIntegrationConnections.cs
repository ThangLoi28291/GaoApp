using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GaoApp.Web.Services.Assistant;
using GaoApp.Web.Services.CustomerService;
using Microsoft.AspNetCore.DataProtection;

namespace GaoApp.Web.Services.SystemIntegrations;

public sealed class SystemIntegrationConnectionsForm
{
    public string Version { get; set; } = "";
    public string? AiBaseUrl { get; set; }
    public string? AiIssuer { get; set; }
    public string? AiKeyId { get; set; }
    public string? AiPrivateKeyPem { get; set; }
    public int AiTimeoutSeconds { get; set; } = 100;
    public int AiTokenLifetimeSeconds { get; set; } = 120;
    public List<SystemZaloConnectionForm> Zalo { get; set; } = [];
}

public sealed class SystemZaloConnectionForm
{
    public string? Id { get; set; }
    public int StoreId { get; set; }
    public string? BaseUrl { get; set; }
    public string? AccountId { get; set; }
    public string? AccessToken { get; set; }
}

public sealed record SystemZaloConnectionView(string Id, int StoreId, string BaseUrl, string AccountId, bool HasToken);
public sealed record SystemIntegrationConnectionsView(string AiBaseUrl, string AiIssuer, string AiKeyId,
    int AiTimeoutSeconds, int AiTokenLifetimeSeconds, bool HasAiKey, SystemZaloConnectionView[] Zalo);

// This entire document is encrypted with the host's persistent Data Protection keys.
// Secret fields never enter the administration view model.
internal sealed class SystemConnectionDocument
{
    public SystemAiConnection? Ai { get; set; }
    public List<SystemZaloConnection> Zalo { get; set; } = [];
}

internal sealed class SystemAiConnection
{
    public string BaseUrl { get; set; } = "";
    public string Issuer { get; set; } = "";
    public string KeyId { get; set; } = "";
    public string? PrivateKeyPem { get; set; }
    public int TimeoutSeconds { get; set; }
    public int TokenLifetimeSeconds { get; set; }
}

internal sealed class SystemZaloConnection
{
    public string Id { get; set; } = "";
    public int StoreId { get; set; }
    public string BaseUrl { get; set; } = "";
    public string AccountId { get; set; } = "";
    public string? AccessToken { get; set; }
}

public sealed partial class SystemIntegrationSwitchStore
{
    private SystemConnectionDocument Connections(SystemIntegrationSwitches saved)
    {
        if (saved.ProtectedConnections is null) return new();
        try
        {
            return JsonSerializer.Deserialize<SystemConnectionDocument>(protector.Unprotect(saved.ProtectedConnections), Json)
                ?? throw new JsonException();
        }
        catch (Exception e) when (e is CryptographicException or JsonException)
        { throw new AssistantException(503, "integration_connections_unavailable", "Chưa đọc được cấu hình kết nối đã mã hóa. Vui lòng kiểm tra khóa bảo vệ dữ liệu của Web."); }
    }

    private static void ApplyConnections(AssistantOptions options, SystemConnectionDocument document)
    {
        if (document.Ai is not { } ai) return;
        options.BaseUrl = ai.BaseUrl;
        options.Issuer = ai.Issuer;
        options.KeyId = ai.KeyId;
        options.TimeoutSeconds = ai.TimeoutSeconds;
        options.TokenLifetimeSeconds = ai.TokenLifetimeSeconds;
        options.ManagedPrivateKeyPem = ai.PrivateKeyPem;
    }

    private static void ApplyConnections(CustomerChannelOptions options, SystemConnectionDocument document)
    {
        foreach (var saved in document.Zalo)
        {
            var connection = options.Connections.SingleOrDefault(c => c.Id == saved.Id);
            if (connection is null)
            {
                connection = new() { Id = saved.Id, Provider = "zalomarketing" };
                options.Connections.Add(connection);
            }
            if (connection.Provider != "zalomarketing") throw AssistantException.Invalid();
            var oldBinding = connection.Binding;
            connection.StoreId = saved.StoreId;
            connection.BaseUrl = saved.BaseUrl;
            connection.AccountId = saved.AccountId;
            if (oldBinding != connection.Binding) connection.LiveReplyPilot = new();
            if (saved.AccessToken is not null)
            {
                connection.ManagedAccessToken = saved.AccessToken;
                // Existing transport guards also detect credential rotation before submission.
                connection.AccessTokenFile = "admin-managed:" + saved.Id + ":" +
                    Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(saved.AccessToken)));
            }
        }
    }

    private SystemIntegrationConnectionsView ConnectionView(SystemIntegrationSwitches saved)
    {
        var document = Connections(saved);
        var ai = BaseAssistant(); ApplyConnections(ai, document);
        var channels = BaseChannels(); ApplyConnections(channels, document);
        return new(ai.BaseUrl, ai.Issuer, ai.KeyId, ai.TimeoutSeconds, ai.TokenLifetimeSeconds,
            !string.IsNullOrWhiteSpace(ai.ManagedPrivateKeyPem) || !string.IsNullOrWhiteSpace(ai.PrivateKeyFile),
            channels.Connections.Where(c => c.Provider == "zalomarketing").Select(c => new SystemZaloConnectionView(
                c.Id, c.StoreId, c.BaseUrl, c.AccountId, !string.IsNullOrWhiteSpace(c.AccessTokenFile))).ToArray());
    }

    public Task<SystemIntegrationSwitches> SaveConnectionsAsync(SystemIntegrationConnectionsForm form, int userId, CancellationToken ct)
        => SaveDocumentAsync(form.Version, userId, current =>
        {
            form.AiBaseUrl ??= ""; form.AiIssuer ??= ""; form.AiKeyId ??= ""; form.AiPrivateKeyPem ??= "";
            var document = Connections(current);
            var channels = BaseChannels(); ApplyConnections(channels, document);
            if (form.Zalo is null || form.Zalo.Count > 50) throw AssistantException.Invalid();
            foreach (var input in form.Zalo)
            { input.Id ??= ""; input.BaseUrl ??= ""; input.AccountId ??= ""; input.AccessToken ??= ""; }
            var submitted = form.Zalo.Where(c => c.Id != "" || c.StoreId != 0 || c.BaseUrl != "" || c.AccountId != "" || c.AccessToken != "").ToArray();
            if (submitted.Select(c => c.Id).Distinct(StringComparer.Ordinal).Count() != submitted.Length)
                throw new AssistantException(400, "zalo_duplicate_id", "Mã kết nối Zalo bị trùng.");
            foreach (var input in submitted)
            {
                if (!System.Text.RegularExpressions.Regex.IsMatch(input.Id!, @"\A[a-z0-9][a-z0-9-]{2,47}\z") || input.StoreId <= 0)
                    throw new AssistantException(400, "zalo_identity_invalid", "Mã kết nối gồm 3–48 chữ thường, số hoặc dấu gạch ngang; hãy chọn cửa hàng.");
                var existing = channels.Connections.SingleOrDefault(c => c.Id == input.Id);
                if (existing is not null && existing.Provider != "zalomarketing")
                    throw new AssistantException(400, "zalo_id_in_use", "Mã kết nối đã được một kênh khác sử dụng.");
                var url = ValidOrigin(input.BaseUrl!, "Zalo Marketing");
                if (!Guid.TryParse(input.AccountId, out var account) || account == Guid.Empty)
                    throw new AssistantException(400, "zalo_sender_invalid", "Mã hồ sơ gửi Zalo phải là UUID hợp lệ.");
                var old = document.Zalo.SingleOrDefault(c => c.Id == input.Id);
                var token = string.IsNullOrWhiteSpace(input.AccessToken) ? old?.AccessToken : input.AccessToken.Trim();
                if (token is not null && (token.Length is < 16 or > 8192 || token.Any(c => c is < '!' or > '~')))
                    throw new AssistantException(400, "zalo_token_invalid", "Token Zalo cần 16–8192 ký tự ASCII, không có khoảng trắng hoặc ký tự điều khiển.");
                if (token is null && string.IsNullOrWhiteSpace(existing?.AccessTokenFile))
                    throw new AssistantException(400, "zalo_token_required", "Hãy nhập token cho kết nối Zalo mới.");
                document.Zalo.RemoveAll(c => c.Id == input.Id);
                document.Zalo.Add(new() { Id = input.Id!, StoreId = input.StoreId, BaseUrl = url,
                    AccountId = existing?.AccountId is { } original && Guid.TryParse(original, out var originalId) && originalId == account
                        ? original : account.ToString("D"), AccessToken = token });
            }
            if (!string.IsNullOrWhiteSpace(form.AiBaseUrl) || !string.IsNullOrWhiteSpace(form.AiIssuer) ||
                !string.IsNullOrWhiteSpace(form.AiKeyId) || !string.IsNullOrWhiteSpace(form.AiPrivateKeyPem))
            {
                var ai = new SystemAiConnection
                {
                    BaseUrl = ValidOrigin(form.AiBaseUrl, "AI"), Issuer = form.AiIssuer.Trim(), KeyId = form.AiKeyId.Trim(),
                    TimeoutSeconds = form.AiTimeoutSeconds, TokenLifetimeSeconds = form.AiTokenLifetimeSeconds,
                    PrivateKeyPem = string.IsNullOrWhiteSpace(form.AiPrivateKeyPem) ? document.Ai?.PrivateKeyPem : form.AiPrivateKeyPem.Trim()
                };
                if (ai.Issuer.Length is < 1 or > 200 || ai.KeyId.Length is < 1 or > 120 || ai.Issuer.Any(char.IsControl) || ai.KeyId.Any(char.IsControl) ||
                    ai.TimeoutSeconds is < 5 or > 100 || ai.TokenLifetimeSeconds is < 1 or > 120)
                    throw new AssistantException(400, "ai_configuration_invalid", "Hãy nhập issuer, mã khóa, thời gian chờ 5–100 giây và hạn token 1–120 giây hợp lệ.");
                var options = BaseAssistant();
                var check = new SystemConnectionDocument { Ai = ai }; ApplyConnections(options, check);
                try { using var key = AssistantSigningKey.Load(options, environment); }
                catch (Exception e) when (e is AssistantException or IOException or UnauthorizedAccessException or ArgumentException or CryptographicException)
                { throw new AssistantException(400, "ai_key_invalid", "Hãy nhập khóa riêng RSA PEM hợp lệ, tối thiểu 2048 bit, hoặc để trống để giữ khóa đã cấu hình."); }
                document.Ai = ai;
            }
            else if (document.Ai is not null)
                throw new AssistantException(400, "ai_configuration_required", "Hãy giữ đầy đủ địa chỉ, issuer và mã khóa của AI.");

            channels = BaseChannels(); ApplyConnections(channels, document);
            if (channels.Connections.Count > 50 || channels.Connections.GroupBy(c => c.Provider + ":" + c.AccountKey).Any(g => g.Count() > 1))
                throw new AssistantException(400, "zalo_duplicate_account", "Mỗi hồ sơ Zalo chỉ được cấu hình một lần; tối đa 50 kết nối.");
            var next = current with { ProtectedConnections = protector.Protect(JsonSerializer.Serialize(document, Json)) };
            ValidateEnabling(next);
            return next;
        }, ct);

    private string ValidOrigin(string value, string label)
    {
        if (value.Length > 2048 || !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) ||
            uri.UserInfo != "" || uri.Query != "" || uri.Fragment != "" ||
            uri.Scheme != "https" && !(environment.IsDevelopment() && uri.Scheme == "http" && uri.IsLoopback))
            throw new AssistantException(400, "integration_url_invalid", $"Địa chỉ dịch vụ {label} phải dùng HTTPS, không kèm tài khoản, query hoặc fragment.");
        return value.Trim();
    }
}
