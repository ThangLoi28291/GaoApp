using System.Text.Json;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Services.Banking;
using Microsoft.AspNetCore.DataProtection;

namespace GaoApp.Web.Services.Acb;

// Compatibility facade: POS retains its existing type, purpose, exceptions and permission flow.
public sealed class AcbProtocol(HttpClient http, IDataProtectionProvider protection)
{
    public AcbProtocolCore Core { get; } = new(http, protection);
    public string Protect(int storeId, string value) => Core.Protect(storeId, value);
    public string Unprotect(int storeId, string value) => Core.Unprotect(storeId, value);
    public bool VerifyKey(StoreAcbSettings settings, string key) => Core.VerifyKey(settings, key);
    public static void ValidateUrl(string value) => AcbProtocolCore.ValidateUrl(value);
    public static string ValidateEnvironment(string token, string api, string qr) => AcbProtocolCore.ValidateEnvironment(token, api, qr);
    public async Task<AcbConnectionCheckResult> CheckConnectionAsync(StoreAcbSettings settings, CancellationToken ct)
    {
        try { var value = await Core.CheckConnectionAsync(settings, ct); return new(value.Environment, value.AuthenticationSucceeded); }
        catch (AcbTransportException error) { throw Compatible(error); }
    }
    public async Task<JsonElement> CallAsync(StoreAcbSettings settings, HttpMethod method, string url, object? parameters, CancellationToken ct)
    {
        try { return await Core.CallAsync(settings, method, url, parameters, ct); }
        catch (AcbTransportException error) { throw Compatible(error); }
    }
    private static AcbApiException Compatible(AcbTransportException error) => new(error.Message)
    { BusinessRequestNotSent = error.BusinessRequestNotSent, ProviderResponseCode = error.ProviderResponseCode, ResponseFailureReason = error.ResponseFailureReason };
    public static string FormatDateTime(DateTimeOffset value) => AcbProtocolCore.FormatDateTime(value);
    public static JsonElement Path(JsonElement value, params string[] names) => AcbProtocolCore.Path(value, names);
    public static string Text(JsonElement value, string name) => AcbProtocolCore.Text(value, name);
    public static decimal Money(JsonElement value, string name) => AcbProtocolCore.Money(value, name);
    public static IEnumerable<JsonElement> Items(JsonElement value) => AcbProtocolCore.Items(value);
    public static bool IsPaid(string status) => AcbProtocolCore.IsPaid(status);
}
public sealed record AcbConnectionCheckResult(string Environment, bool AuthenticationSucceeded);
