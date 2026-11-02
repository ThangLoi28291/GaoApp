using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using ChatbotLocal.Integration;

namespace GaoApp.Web.Services.Assistant;

// Keys stay on the backend. The service validates only its configured local RSA key.
public sealed class AssistantJwt(IOptionsMonitor<AssistantOptions> options, IWebHostEnvironment environment, TimeProvider clock)
{
    private RSA Key(AssistantOptions config)
    {
        if (!config.Enabled) throw AssistantException.Disabled();
        if (string.IsNullOrWhiteSpace(config.Issuer) || string.IsNullOrWhiteSpace(config.KeyId)
            || string.IsNullOrWhiteSpace(config.PrivateKeyFile) && config.ManagedPrivateKeyPem is null || config.TokenLifetimeSeconds is < 1 or > 120)
            throw new AssistantException(503, "assistant_not_configured", "Chưa cấu hình kết nối trợ lý.");
        try { return AssistantSigningKey.Load(config, environment); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or CryptographicException)
        { throw new AssistantException(503, "assistant_not_configured", "Khóa kết nối trợ lý chưa hợp lệ."); }
    }
    public AssistantTokenPair Sign(AssistantIdentity identity)
    {
        if (identity.UserId <= 0 || identity.Permissions.Length is < 1 or > 30 || identity.DisplayName.Length is < 1 or > 120) throw AssistantException.Denied();
        var config = options.CurrentValue;
        using var rsa = Key(config);
        var now = clock.GetUtcNow().ToUnixTimeSeconds();
        string Create(string audience, string type, string party)
        {
            var payload = new Dictionary<string, object> {
                ["iss"] = config.Issuer, ["sub"] = identity.UserId.ToString(), ["display_name"] = identity.DisplayName,
                ["store_id"] = identity.Scope.StoreId, ["permissions"] = identity.Permissions,
                ["iat"] = now, ["nbf"] = now, ["exp"] = now + config.TokenLifetimeSeconds,
                ["jti"] = Guid.NewGuid().ToString(), ["aud"] = audience, ["azp"] = party };
            if (identity.Scope.WarehouseId is { } w) payload["warehouse_id"] = w;
            if (identity.Scope.LegalEntityId is { } l) payload["legal_entity_id"] = l;
            var header = Encode(JsonSerializer.SerializeToUtf8Bytes(new { alg = "RS256", typ = type, kid = config.KeyId }));
            var body = Encode(JsonSerializer.SerializeToUtf8Bytes(payload));
            var text = header + "." + body;
            return text + "." + Encode(rsa.SignData(Encoding.ASCII.GetBytes(text), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        }
        return new(Create("chatbotlocal-api", "gaoapp-assistant-context+jwt", "gaoapp-backend"),
            Create("gaoapp-assistant-data", "gaoapp-assistant-delegation+jwt", "chatbotlocal"));
    }
    public AssistantTokenPair SignCustomer(GaoApp.Web.Services.CustomerService.CustomerBridgeIdentity who, string? requestHash = null)
    {
        if(who.VisitorId==Guid.Empty||who.ConversationId==Guid.Empty||who.TurnId==Guid.Empty||who.StoreId<=0
            || requestHash is not null && (requestHash.Length != 43 || requestHash.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))))throw AssistantException.Denied();
        var config=options.CurrentValue;using var rsa=Key(config);var now=clock.GetUtcNow().ToUnixTimeSeconds();
        string Create(string aud,string typ,string azp) {
            var header=Encode(JsonSerializer.SerializeToUtf8Bytes(new{alg="RS256",typ,kid=config.KeyId}));
            var payload=Encode(JsonSerializer.SerializeToUtf8Bytes(new{iss=config.Issuer,sub=who.VisitorId.ToString("N"),display_name="Khách",store_id=who.StoreId.ToString(),
                conversation_id=who.ConversationId.ToString(),turn_id=who.TurnId.ToString(),permissions=new[]{"assistant.use","assistant.chat","catalog.read","quotes.retail.read"},iat=now,nbf=now,exp=now+config.TokenLifetimeSeconds,jti=Guid.NewGuid().ToString(),request_hash=requestHash,aud,azp}));
            var input=header+"."+payload;return input+"."+Encode(rsa.SignData(Encoding.ASCII.GetBytes(input),HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1));
        }
        return new(Create("chatbotlocal-customer-api","gaoapp-customer-context+jwt","gaoapp-customer-backend"),Create("gaoapp-customer-data","gaoapp-customer-delegation+jwt","chatbotlocal-customer"));
    }
    public GaoApp.Web.Services.CustomerService.CustomerBridgeIdentity ValidateCustomer(string token)
    {
        try {
            if(token.Length>16384)throw new FormatException();var parts=token.Split('.');if(parts.Length!=3)throw new FormatException();
            using var header=JsonDocument.Parse(Decode(parts[0]));using var body=JsonDocument.Parse(Decode(parts[1]));Unique(header.RootElement);Unique(body.RootElement);
            var h=header.RootElement;var b=body.RootElement;var config=options.CurrentValue;
            if(h.GetProperty("alg").GetString()!="RS256"||h.GetProperty("typ").GetString()!="gaoapp-customer-delegation+jwt"||h.GetProperty("kid").GetString()!=config.KeyId
                ||h.TryGetProperty("crit",out _)||h.TryGetProperty("jku",out _)||h.TryGetProperty("x5u",out _)||b.GetProperty("iss").GetString()!=config.Issuer
                ||b.GetProperty("aud").GetString()!="gaoapp-customer-data"||b.GetProperty("azp").GetString()!="chatbotlocal-customer")throw new FormatException();
            using var rsa=Key(config);if(!rsa.VerifyData(Encoding.ASCII.GetBytes(parts[0]+"."+parts[1]),Decode(parts[2]),HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1))throw new FormatException();
            var now=clock.GetUtcNow().ToUnixTimeSeconds();var iat=b.GetProperty("iat").GetInt64();var nbf=b.GetProperty("nbf").GetInt64();var exp=b.GetProperty("exp").GetInt64();
            if(iat<0||exp-iat is <=0 or >120||nbf<iat||nbf>=exp||iat>now+15||nbf>now+15||exp<=now-15)throw new FormatException();
            var permissions=b.GetProperty("permissions").EnumerateArray().Select(x=>x.GetString()).ToArray();
            if(permissions.Length!=4||!permissions.ToHashSet().SetEquals(new[]{"assistant.use","assistant.chat","catalog.read","quotes.retail.read"})
                ||!Guid.TryParse(b.GetProperty("jti").GetString(),out var jti)||jti==Guid.Empty||b.TryGetProperty("warehouse_id",out _)||b.TryGetProperty("legal_entity_id",out _))throw new FormatException();
            var visitor=Guid.ParseExact(b.GetProperty("sub").GetString()!,"N");var conversation=Guid.Parse(b.GetProperty("conversation_id").GetString()!);var turn=Guid.Parse(b.GetProperty("turn_id").GetString()!);
            var store=AssistantAccess.Id(b.GetProperty("store_id").GetString()!);if(visitor==Guid.Empty||conversation==Guid.Empty||turn==Guid.Empty)throw new FormatException();return new(visitor,store,conversation,turn);
        }catch(AssistantException){throw;}catch(Exception e)when(e is JsonException or FormatException or KeyNotFoundException or InvalidOperationException or ArgumentException or CryptographicException){throw new AssistantException(401,"customer_bridge_unauthenticated","Thông tin kết nối khách chưa hợp lệ.");}
    }
    public AssistantIdentity ValidateDelegation(string token)
    {
        try
        {
            if (token.Length > 16384) throw new FormatException();
            var parts = token.Split('.'); if (parts.Length != 3) throw new FormatException();
            using var header = JsonDocument.Parse(Decode(parts[0]));
            using var body = JsonDocument.Parse(Decode(parts[1]));
            Unique(header.RootElement); Unique(body.RootElement);
            var h = header.RootElement; var b = body.RootElement; var config = options.CurrentValue;
            if (h.GetProperty("alg").GetString() != "RS256" || h.GetProperty("typ").GetString() != "gaoapp-assistant-delegation+jwt"
                || h.GetProperty("kid").GetString() != config.KeyId || h.TryGetProperty("crit", out _)
                || h.TryGetProperty("jku", out _) || h.TryGetProperty("x5u", out _)
                || b.GetProperty("iss").GetString() != config.Issuer || b.GetProperty("aud").GetString() != "gaoapp-assistant-data"
                || b.GetProperty("azp").GetString() != "chatbotlocal") throw new FormatException();
            using var rsa = Key(config);
            if (!rsa.VerifyData(Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]), Decode(parts[2]), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)) throw new FormatException();
            var now = clock.GetUtcNow().ToUnixTimeSeconds(); var iat = b.GetProperty("iat").GetInt64();
            var nbf = b.GetProperty("nbf").GetInt64(); var exp = b.GetProperty("exp").GetInt64();
            if (iat < 0 || exp - iat is <= 0 or > 120 || nbf < iat || nbf >= exp || nbf > now + 15 || iat > now + 15 || exp <= now - 15) throw new FormatException();
            string Text(string name, int max = 64) { var value = b.GetProperty(name).GetString();
                return !string.IsNullOrWhiteSpace(value) && value.Length <= max && !value.Any(char.IsControl) ? value : throw new FormatException(); }
            var permissions = b.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()!).ToArray();
            if (permissions.Length is < 1 or > 30 || permissions.Any(p => string.IsNullOrWhiteSpace(p) || p.Length > 100)
                || permissions.Distinct(StringComparer.Ordinal).Count() != permissions.Length) throw new FormatException();
            if (!int.TryParse(Text("sub"), out var user) || user <= 0) throw new FormatException();
            _ = Text("jti", 120);
            return new(user, Text("display_name", 120), new(Text("store_id"),
                b.TryGetProperty("warehouse_id", out _) ? Text("warehouse_id") : null,
                b.TryGetProperty("legal_entity_id", out _) ? Text("legal_entity_id") : null), permissions);
        }
        catch (AssistantException ex) when (ex.Status == 400)
        { throw new AssistantException(401, "unauthenticated", "Thông tin kết nối không hợp lệ."); }
        catch (AssistantException) { throw; }
        catch (Exception ex) when (ex is JsonException or FormatException or KeyNotFoundException or InvalidOperationException or CryptographicException or ArgumentException)
        { throw new AssistantException(401, "unauthenticated", "Thông tin kết nối không hợp lệ hoặc đã hết hạn."); }
    }
    public static void Unique(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object) { var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in value.EnumerateObject()) { if (!seen.Add(p.Name)) throw AssistantException.Invalid(); Unique(p.Value); } }
        else if (value.ValueKind == JsonValueKind.Array) foreach (var item in value.EnumerateArray()) Unique(item);
    }
    private static string Encode(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static byte[] Decode(string value) => Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/') + new string('=', (4 - value.Length % 4) % 4));
}
