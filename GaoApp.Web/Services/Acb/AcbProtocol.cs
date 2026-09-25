using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GaoApp.Domain.Entities;
using Microsoft.AspNetCore.DataProtection;

namespace GaoApp.Web.Services.Acb;

public sealed class AcbProtocol(HttpClient http, IDataProtectionProvider protection)
{
    public string Protect(int storeId, string value) => Protector(storeId).Protect(value);
    public string Unprotect(int storeId, string value) => Protector(storeId).Unprotect(value);
    private IDataProtector Protector(int storeId) => protection.CreateProtector("GaoApp.Acb", storeId.ToString(CultureInfo.InvariantCulture));

    public static void ValidateUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
            !uri.IsDefaultPort || !string.IsNullOrEmpty(uri.UserInfo) ||
            uri.Host is not ("sandbox.acb.com.vn" or "openapi.acb.com.vn" or "openapi-iam.acb.com.vn"))
            throw new InvalidOperationException("Endpoint phải là địa chỉ HTTPS chính thức của ACB.");
    }

    public bool VerifyKey(StoreAcbSettings settings, string key)
    {
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(settings.CallbackApiKeyProtected)) return false;
        var expected = SHA256.HashData(Encoding.UTF8.GetBytes(Unprotect(settings.StoreId, settings.CallbackApiKeyProtected)));
        return CryptographicOperations.FixedTimeEquals(expected, SHA256.HashData(Encoding.UTF8.GetBytes(key)));
    }

    public async Task<AcbConnectionCheckResult> CheckConnectionAsync(StoreAcbSettings settings, CancellationToken ct)
    {
        var environment = ConnectionEnvironment(settings);
        _ = await GetAccessTokenAsync(settings, ct);
        // The token stays on the server. This checks authentication, not payment or callback delivery.
        return new(environment, true);
    }

    private static string ConnectionEnvironment(StoreAcbSettings settings)
    {
        var environment = ValidateEnvironment(settings.TokenEndpoint, settings.ApiBaseUrl, settings.QrEndpoint);
        if (string.IsNullOrWhiteSpace(settings.ClientId) || string.IsNullOrWhiteSpace(settings.ClientSecretProtected))
            throw new InvalidOperationException("Hãy lưu ClientId và Client secret trước khi kiểm tra kết nối.");
        return environment;
    }

    public static string ValidateEnvironment(string tokenEndpoint, string apiBaseUrl, string qrEndpoint)
    {
        var urls = new[] { tokenEndpoint, apiBaseUrl, qrEndpoint };
        foreach (var url in urls) ValidateUrl(url);
        var sandbox = urls.Select(url => new Uri(url).Host == "sandbox.acb.com.vn").Distinct().ToArray();
        if (sandbox.Length != 1) throw new InvalidOperationException("TokenEndpoint, ApiBaseUrl và QrEndpoint phải cùng môi trường Sandbox hoặc Production.");
        return sandbox[0] ? "Sandbox" : "Production";
    }

    private async Task<string> GetAccessTokenAsync(StoreAcbSettings s, CancellationToken ct)
    {
        using var tokenRequest = new HttpRequestMessage(HttpMethod.Post, s.TokenEndpoint);
        tokenRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        tokenRequest.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials", ["scope"] = s.TokenScope,
            ["client_id"] = s.ClientId, ["client_secret"] = Unprotect(s.StoreId, s.ClientSecretProtected)
        });
        using var tokenResponse = await SendAsync(tokenRequest, ct);
        if (!tokenResponse.IsSuccessStatusCode)
            throw AcbApiException.TokenFailure((int)tokenResponse.StatusCode, await tokenResponse.Content.ReadAsStringAsync(ct));
        using var tokenJson = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync(ct));
        var token = Text(tokenJson.RootElement, "access_token");
        if (string.IsNullOrWhiteSpace(token)) throw new AcbApiException("ACB phản hồi thành công nhưng không trả access token.");
        return token;
    }

    public async Task<JsonElement> CallAsync(StoreAcbSettings s, HttpMethod method, string url, object? parameters, CancellationToken ct)
    {
        _ = ConnectionEnvironment(s);
        ValidateUrl(url);
        string token;
        try { token = await GetAccessTokenAsync(s, ct); }
        catch (AcbApiException error) { error.BusinessRequestNotSent = true; throw; }
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        foreach (var (name, value) in new Dictionary<string, string>
        {
            ["X-Client-ID"] = s.ClientId, ["X-Service"] = s.XService,
            ["X-Provider-ID"] = s.XProviderId, ["X-Owner-Number"] = s.XOwnerNumber,
            ["X-Owner-Type"] = s.XOwnerType, ["X-Request-ID"] = Guid.NewGuid().ToString()
        }) request.Headers.Add(name, value);
        if (parameters != null) request.Content = JsonContent.Create(new
        {
            requestTrace = Guid.NewGuid().ToString(), requestDateTime = FormatDateTime(DateTimeOffset.UtcNow),
            requestParameters = parameters
        });
        using var response = await SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) throw new AcbApiException($"ACB chưa xác nhận yêu cầu (HTTP {(int)response.StatusCode}). Hãy tra cứu lại trước khi thử tiếp.");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var code = Text(Path(json.RootElement, "responseStatus"), "responseCode");
        if (code != "00000000")
        {
            var safeCode = code.Length == 8 && code.All(char.IsAsciiDigit) ? $" (mã ACB {code})" : "";
            throw new AcbApiException($"ACB chưa xác nhận yêu cầu thành công{safeCode}. Hãy kiểm tra trạng thái giao dịch.")
            { ProviderResponseCode = safeCode.Length > 0 ? code : null };
        }
        return json.RootElement.Clone();
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        try { return await http.SendAsync(request, ct); }
        catch (HttpRequestException error) { throw AcbApiException.NetworkFailure(error); }
    }

    public static string FormatDateTime(DateTimeOffset value)
    {
        var formatted = value.ToString("yyyy-MM-dd'T'HH:mm:ss.fffzzz", CultureInfo.InvariantCulture);
        return formatted.Remove(formatted.Length - 3, 1); // ACB's documented +0700 / +0000 offset.
    }

    public static JsonElement Path(JsonElement value, params string[] names)
    {
        foreach (var name in names)
            if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(name, out value)) return default;
        return value;
    }
    public static string Text(JsonElement value, string name) => Path(value, name).ToString();
    public static decimal Money(JsonElement value, string name) => decimal.TryParse(Text(value, name), NumberStyles.Number, CultureInfo.InvariantCulture, out var n) ? n : 0;
    public static IEnumerable<JsonElement> Items(JsonElement value) => value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().ToArray() : [];
    public static bool IsPaid(string status) => status.ToUpperInvariant() is "COMPLETED" or "SUCCESS" or "PAID" or "APPROVED";
}

public sealed record AcbConnectionCheckResult(string Environment, bool AuthenticationSucceeded);
