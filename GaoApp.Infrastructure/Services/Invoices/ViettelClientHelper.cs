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
               bytes[1] == 0x4B;
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

    public static byte[]? TryReadFileToBytes(JsonElement root)
    {
        if (!TryFindProperty(root, "fileToBytes", out var fileElement))
            return null;

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
                if (item.ValueKind == JsonValueKind.Number &&
                    item.TryGetInt32(out var number) &&
                    number >= 0 &&
                    number <= 255)
                {
                    bytes.Add((byte)number);
                }
            }

            return bytes.ToArray();
        }

        return null;
    }

    public static bool TryFindProperty(
        JsonElement element,
        string propertyName,
        out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in element.EnumerateObject())
            {
                if (string.Equals(prop.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    value = prop.Value;
                    return true;
                }

                if (TryFindProperty(prop.Value, propertyName, out value))
                    return true;
            }
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (TryFindProperty(item, propertyName, out value))
                    return true;
            }
        }

        value = default;
        return false;
    }

    public static string? FindStringProperty(
        JsonElement element,
        string propertyName)
    {
        if (!TryFindProperty(element, propertyName, out var value))
            return null;

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => null,
            _ => value.ToString()
        };
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

    public static bool IsViettelSuccessCode(string? errorCode)
    {
        return string.IsNullOrWhiteSpace(errorCode) ||
               errorCode.Equals("null", StringComparison.OrdinalIgnoreCase) ||
               errorCode.Equals("200", StringComparison.OrdinalIgnoreCase) ||
               errorCode.Equals("OK", StringComparison.OrdinalIgnoreCase);
    }
}