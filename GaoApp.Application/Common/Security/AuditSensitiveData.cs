using System.Text.Json;
using System.Text.Json.Nodes;

namespace GaoApp.Application.Common.Security;

/// <summary>Shared boundary for automatic snapshots, explicit writes and legacy audit reads.</summary>
public static class AuditSensitiveData
{
    public const string Redacted = "[REDACTED]";

    public static bool IsSensitive(string name)
    {
        var normalized = string.Concat(name.Where(char.IsLetterOrDigit)).ToLowerInvariant();
        return normalized.Contains("password") || normalized.Contains("secret") ||
            normalized.Contains("token") || normalized.Contains("credential") ||
            normalized.Contains("privatekey") || normalized.Contains("apikey") ||
            normalized.Contains("connectionstring") || normalized.Contains("sessionstamp") ||
            normalized.Contains("securitystamp") || normalized is "authorization" or "cookie";
    }

    public static string? SanitizeJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return json;
        try
        {
            var node = JsonNode.Parse(json);
            Sanitize(node);
            return node?.ToJsonString() ?? "null";
        }
        catch (JsonException)
        {
            // Never return malformed historical payloads unchanged.
            return JsonSerializer.Serialize(Redacted);
        }
    }

    private static void Sanitize(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            foreach (var name in obj.Select(x => x.Key).ToArray())
            {
                if (IsSensitive(name)) obj[name] = Redacted;
                else Sanitize(obj[name]);
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var child in array) Sanitize(child);
        }
    }
}
