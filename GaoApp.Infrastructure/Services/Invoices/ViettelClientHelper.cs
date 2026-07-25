using System.Net.Http.Headers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GaoApp.Infrastructure.Services.Invoices;

internal static class ViettelClientHelper
{
    public static string NormalizeBaseUrl(string baseUrl)
    {
        return (baseUrl ?? string.Empty).Trim().TrimEnd('/');
    }

    public static AuthenticationHeaderValue BuildBasicAuthHeader(
        string username,
        string password)
    {
        var rawCredential = $"{username.Trim()}:{password}";
        var base64Credential = Convert.ToBase64String(
            Encoding.UTF8.GetBytes(rawCredential));

        return new AuthenticationHeaderValue("Basic", base64Credential);
    }

    public static string SerializeJson(object value)
    {
        return JsonSerializer.Serialize(
            value,
            new JsonSerializerOptions
            {
                WriteIndented = false,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });
    }

    public static ProviderJsonValidationFailure TryParseStrictProviderJson(
        string? json,
        out JsonDocument? document)
    {
        document = null;

        if (string.IsNullOrWhiteSpace(json))
            return ProviderJsonValidationFailure.MalformedJson;

        try
        {
            var parsedDocument = JsonDocument.Parse(json);

            if (!ValidateNoDuplicatePropertyNames(
                    parsedDocument.RootElement))
            {
                parsedDocument.Dispose();
                return ProviderJsonValidationFailure.DuplicatePropertyName;
            }

            document = parsedDocument;
            return ProviderJsonValidationFailure.None;
        }
        catch (JsonException)
        {
            return ProviderJsonValidationFailure.MalformedJson;
        }
    }

    public static bool IsPdfBytes(byte[] bytes)
    {
        return bytes.Length >= 4 &&
               bytes[0] == 0x25 &&
               bytes[1] == 0x50 &&
               bytes[2] == 0x44 &&
               bytes[3] == 0x46;
    }

    public static bool IsZipBytes(byte[] bytes)
    {
        return bytes.Length >= 4 &&
               bytes[0] == 0x50 &&
               bytes[1] == 0x4B &&
               ((bytes[2] == 0x03 && bytes[3] == 0x04) ||
                (bytes[2] == 0x05 && bytes[3] == 0x06) ||
                (bytes[2] == 0x07 && bytes[3] == 0x08));
    }

    public static string SafeReadText(byte[] bytes)
    {
        if (bytes == null || bytes.Length == 0)
            return string.Empty;

        try
        {
            return Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            return string.Empty;
        }
    }

    public static string BuildBinarySummary(
        byte[] bytes,
        string contentType,
        string fileKind)
    {
        return JsonSerializer.Serialize(new
        {
            contentType,
            fileKind,
            fileSize = bytes.Length,
            note = "Không lưu file bytes vào log để tránh DB phình lớn."
        });
    }

    public static string RedactedRequestSummary(string operation)
    {
        return JsonSerializer.Serialize(new
        {
            operation,
            redacted = true
        });
    }

    public static string? RedactedResponseSummary(
        string? responseBody,
        int? statusCode = null)
    {
        if (responseBody == null && !statusCode.HasValue)
            return null;

        return JsonSerializer.Serialize(new
        {
            statusCode,
            responseLength = responseBody?.Length ?? 0,
            redacted = true
        });
    }

    public static string ExceptionType(Exception exception) =>
        exception.GetType().Name;

    public static byte[]? TryGetDirectFileBytes(JsonElement container)
    {
        if (GetUniqueDirectProperty(
                container,
                "fileToBytes",
                out var fileElement) != DirectPropertyLookupResult.Found)
        {
            return null;
        }

        if (fileElement.ValueKind == JsonValueKind.String)
        {
            var value = fileElement.GetString();

            if (string.IsNullOrWhiteSpace(value))
                return null;

            try
            {
                return Convert.FromBase64String(value);
            }
            catch
            {
                return null;
            }
        }

        if (fileElement.ValueKind == JsonValueKind.Array)
        {
            var bytes = new List<byte>();

            foreach (var item in fileElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Number ||
                    !item.TryGetInt32(out var number) ||
                    number < 0 ||
                    number > 255)
                    return null;

                bytes.Add((byte)number);
            }

            return bytes.ToArray();
        }

        return null;
    }

    public static DirectPropertyLookupResult GetUniqueDirectProperty(
        JsonElement element,
        string propertyName,
        out JsonElement value)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            value = default;
            return DirectPropertyLookupResult.Missing;
        }

        var matchCount = 0;
        value = default;

        foreach (var property in element.EnumerateObject())
        {
            if (!string.Equals(
                    property.Name,
                    propertyName,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            matchCount++;

            if (matchCount == 1)
                value = property.Value;
        }

        return matchCount switch
        {
            0 => DirectPropertyLookupResult.Missing,
            1 => DirectPropertyLookupResult.Found,
            _ => DirectPropertyLookupResult.Duplicate
        };
    }

    public static IReadOnlyList<ProviderResponseContainer>
        GetDirectRecognizedContainers(
            JsonElement root,
            bool allowResultArray,
            bool allowDataArray,
            out bool hasInvalidEnvelope)
    {
        var containers = new List<ProviderResponseContainer>();
        hasInvalidEnvelope = false;

        if (root.ValueKind != JsonValueKind.Object)
        {
            hasInvalidEnvelope = true;
            return containers;
        }

        containers.Add(new ProviderResponseContainer("root", root));

        AddDirectEnvelope(
            containers,
            root,
            "result",
            allowResultArray,
            ref hasInvalidEnvelope);
        AddDirectEnvelope(
            containers,
            root,
            "data",
            allowDataArray,
            ref hasInvalidEnvelope);

        return containers;
    }

    public static string? GetDirectString(
        JsonElement element,
        string propertyName)
    {
        if (GetUniqueDirectProperty(
                element,
                propertyName,
                out var value) != DirectPropertyLookupResult.Found)
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.ToString(),
            _ => null
        };
    }

    public static string? GetFirstDirectString(
        JsonElement element,
        params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            var value = GetDirectString(element, propertyName);

            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
    }

    public static bool TryGetDirectBoolean(
        JsonElement element,
        string propertyName,
        out bool value)
    {
        if (GetUniqueDirectProperty(
                element,
                propertyName,
                out var property) != DirectPropertyLookupResult.Found)
        {
            value = default;
            return false;
        }

        if (property.ValueKind == JsonValueKind.True)
        {
            value = true;
            return true;
        }

        if (property.ValueKind == JsonValueKind.False)
        {
            value = false;
            return true;
        }

        if (property.ValueKind == JsonValueKind.String &&
            bool.TryParse(property.GetString(), out value))
        {
            return true;
        }

        value = default;
        return false;
    }

    public static bool HasDirectFailureMarker(
        JsonElement container,
        Func<string, bool> isAcceptedControlCode)
    {
        if (container.ValueKind != JsonValueKind.Object)
            return false;

        var status = GetDirectString(container, "status");

        if (IsExplicitViettelFailureStatus(status))
            return true;

        foreach (var markerName in new[] { "success", "isSuccess" })
        {
            var markerLookup = GetUniqueDirectProperty(
                container,
                markerName,
                out var marker);

            if (markerLookup == DirectPropertyLookupResult.Duplicate)
                return true;

            if (markerLookup == DirectPropertyLookupResult.Missing)
            {
                continue;
            }

            if (marker.ValueKind == JsonValueKind.Null)
                continue;

            if (!TryGetDirectBoolean(
                    container,
                    markerName,
                    out var markerValue) ||
                !markerValue)
            {
                return true;
            }
        }

        foreach (var codeName in new[] { "errorCode", "code" })
        {
            var codeLookup = GetUniqueDirectProperty(
                container,
                codeName,
                out var codeElement);

            if (codeLookup == DirectPropertyLookupResult.Duplicate)
                return true;

            if (codeLookup == DirectPropertyLookupResult.Missing)
            {
                continue;
            }

            if (codeElement.ValueKind == JsonValueKind.Null)
                continue;

            if (codeElement.ValueKind is not
                (JsonValueKind.String or JsonValueKind.Number))
            {
                return true;
            }

            var code = GetDirectString(container, codeName);

            if (string.IsNullOrWhiteSpace(code))
                continue;

            if (!isAcceptedControlCode(code))
                return true;
        }

        return HasMeaningfulDirectError(container, "error") ||
               HasMeaningfulDirectError(container, "errors");
    }

    public static string Trim(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        value = value.Trim();

        return value.Length <= maxLength
            ? value
            : value[..maxLength] + "...";
    }

    public static string? TrimNullable(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        value = value.Trim();

        return value.Length <= maxLength
            ? value
            : value[..maxLength] + "...";
    }

    public static bool IsExplicitViettelSuccessCode(string? errorCode)
    {
        return !string.IsNullOrWhiteSpace(errorCode) &&
               (errorCode.Equals("200", StringComparison.OrdinalIgnoreCase) ||
                errorCode.Equals("OK", StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsExplicitViettelFailureStatus(string? status)
    {
        return !string.IsNullOrWhiteSpace(status) &&
               (status.Equals("ERROR", StringComparison.OrdinalIgnoreCase) ||
                status.Equals("FAILED", StringComparison.OrdinalIgnoreCase) ||
                status.Equals("FAIL", StringComparison.OrdinalIgnoreCase));
    }

    private static void AddDirectEnvelope(
        ICollection<ProviderResponseContainer> containers,
        JsonElement root,
        string envelopeName,
        bool allowArray,
        ref bool hasInvalidEnvelope)
    {
        var envelopeLookup = GetUniqueDirectProperty(
            root,
            envelopeName,
            out var envelope);

        if (envelopeLookup == DirectPropertyLookupResult.Missing)
        {
            return;
        }

        if (envelopeLookup == DirectPropertyLookupResult.Duplicate)
        {
            hasInvalidEnvelope = true;
            return;
        }

        if (envelope.ValueKind == JsonValueKind.Null)
            return;

        if (envelope.ValueKind == JsonValueKind.Object ||
            (allowArray &&
             envelope.ValueKind == JsonValueKind.Array))
        {
            containers.Add(
                new ProviderResponseContainer(
                    envelopeName,
                    envelope));
            return;
        }

        hasInvalidEnvelope = true;
    }

    private static bool HasMeaningfulDirectError(
        JsonElement container,
        string propertyName)
    {
        var errorLookup = GetUniqueDirectProperty(
            container,
            propertyName,
            out var errorElement);

        if (errorLookup == DirectPropertyLookupResult.Duplicate)
            return true;

        if (errorLookup == DirectPropertyLookupResult.Missing ||
            errorElement.ValueKind == JsonValueKind.Null)
        {
            return false;
        }

        return errorElement.ValueKind switch
        {
            JsonValueKind.String =>
                !string.IsNullOrWhiteSpace(errorElement.GetString()),
            JsonValueKind.False => false,
            JsonValueKind.Array when
                propertyName.Equals(
                    "errors",
                    StringComparison.OrdinalIgnoreCase) =>
                errorElement.GetArrayLength() > 0,
            _ => true
        };
    }

    private static bool ValidateNoDuplicatePropertyNames(
        JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name) ||
                    !ValidateNoDuplicatePropertyNames(property.Value))
                {
                    return false;
                }
            }

            return true;
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (!ValidateNoDuplicatePropertyNames(item))
                    return false;
            }
        }

        return true;
    }
}

internal enum ProviderJsonValidationFailure
{
    None,
    MalformedJson,
    DuplicatePropertyName
}

internal enum DirectPropertyLookupResult
{
    Missing,
    Found,
    Duplicate
}

internal readonly record struct ProviderResponseContainer(
    string Name,
    JsonElement Element);
