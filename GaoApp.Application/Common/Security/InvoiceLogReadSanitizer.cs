using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Common.Security;

/// <summary>
/// Read-only protection for legacy provider logs. Free text is not a trusted
/// diagnostic schema: retain only validated diagnostic metadata, never payloads.
/// </summary>
public static partial class InvoiceLogReadSanitizer
{
    public const string Redacted = "[REDACTED]";
    public const int MaxBodyCharacters = 65536;
    private static readonly HashSet<string> Codes = new(StringComparer.OrdinalIgnoreCase)
    {
        "TIMEOUT", "HTTP_ERROR", "NETWORK_ERROR", "AUTH_FAILED", "INVALID_RESPONSE",
        "CANCELLED", "CANCELED", "CONFIG_INVALID", "INVOICE_NOT_FOUND",
        // Public project-defined diagnostic codes only. Never allow arbitrary
        // values just because they start with a provider namespace.
        "Viettel.AuthModeUnsupported", "Viettel.BaseUrlRequired", "Viettel.BasicAuthAmbiguousResponse",
        "Viettel.BasicAuthBusinessFailed", "Viettel.BasicAuthConflictingResponse", "Viettel.BasicAuthHttpFailed",
        "Viettel.BasicAuthInvalidResponse", "Viettel.BasicAuthTimeout", "Viettel.BasicAuthTransportFailed",
        "Viettel.BuyerEmailRequired", "Viettel.ConnectionNoData", "Viettel.DateInvalid",
        "Viettel.DateRangeTooLong", "Viettel.DownloadException", "Viettel.DownloadFileConflictingResponse",
        "Viettel.DownloadFileFailed", "Viettel.DownloadTimeout", "Viettel.FileBusinessError",
        "Viettel.FileInvalidResponse", "Viettel.GetInvoicesAmbiguousResponse", "Viettel.GetInvoicesBusinessFailed",
        "Viettel.GetInvoicesConflictingResponse", "Viettel.GetInvoicesException", "Viettel.GetInvoicesFailed",
        "Viettel.GetInvoicesInvalidResponse", "Viettel.GetInvoicesTimeout", "Viettel.InvoiceHeadIdInvalid",
        "Viettel.InvoiceNotFoundOnGetInvoices", "Viettel.IssueAmbiguousResponse", "Viettel.IssueBusinessFailed",
        "Viettel.IssueConflictingResponse", "Viettel.IssueDateInvalid", "Viettel.IssueFailed",
        "Viettel.IssueInvalidResponse", "Viettel.IssueTimeout", "Viettel.IssueTransportFailed",
        "Viettel.LookupAmbiguousResponse", "Viettel.LookupBusinessFailed", "Viettel.LookupConflictingResponse",
        "Viettel.LookupInvalidResponse", "Viettel.LookupTimeout", "Viettel.LookupTransportFailed",
        "Viettel.NoFileToBytes", "Viettel.PasswordRequired", "Viettel.PreviewBusinessError",
        "Viettel.PreviewConflictingResponse", "Viettel.PreviewException", "Viettel.PreviewFailed",
        "Viettel.PreviewInvalidResponse", "Viettel.PreviewNoFile", "Viettel.PreviewTimeout",
        "Viettel.PreviewUnknownResponse", "Viettel.RequiredMissing", "Viettel.ResolveIssueDateFailed",
        "Viettel.SendEmailAmbiguousResponse", "Viettel.SendEmailBusinessFailed", "Viettel.SendEmailConflictingResponse",
        "Viettel.SendEmailException", "Viettel.SendEmailHttpFailed", "Viettel.SendEmailInvalidResponse",
        "Viettel.SendEmailTimeout", "Viettel.SupplierTaxCodeRequired", "Viettel.TokenLoginBusinessFailed",
        "Viettel.TokenLoginConflictingResponse", "Viettel.TokenLoginHttpFailed", "Viettel.TokenLoginInvalidResponse",
        "Viettel.TokenLoginTimeout", "Viettel.TokenLoginTransportFailed", "Viettel.TokenMissing",
        "Viettel.TransactionUuidRequired", "Viettel.UnknownFileResponse", "Viettel.UsernameRequired",
    };
    private static readonly string[] ExceptionNames =
        ["HttpRequestException", "JsonException", "TaskCanceledException", "OperationCanceledException", "SocketException"];

    public static string? Code(string? value)
    {
        if (value is null) return null;
        if (value.Length > 64) return Redacted;
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        if (Codes.TryGetValue(value, out var knownCode)) return knownCode;
        if (value.Length <= 8 && value.All(char.IsAsciiDigit)) return value;
        if (value.Length == 8 && value.StartsWith("HTTP_", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(value.AsSpan(5), NumberStyles.None, CultureInfo.InvariantCulture, out var status) && status is >= 100 and <= 599)
            return "HTTP_" + status.ToString(CultureInfo.InvariantCulture);
        return Redacted;
    }

    public static string? Error(string? value)
    {
        if (value is null) return null;
        if (value.Length > 16384) return Redacted;
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var markers = new SortedSet<string>(StringComparer.Ordinal);
        try
        {
            // Do not derive even diagnostic markers from a credential value
            // such as "password=timeout HTTP 500".
            var credential = CredentialMarker().Match(value);
            var prefixEnd = credential.Success ? credential.Index : value.Length;
            var structured = value.IndexOfAny(['{', '[', '<']);
            if (structured >= 0) prefixEnd = Math.Min(prefixEnd, structured);
            foreach (var scheme in new[] { "http://", "https://" })
            {
                var uriStart = value.IndexOf(scheme, StringComparison.OrdinalIgnoreCase);
                if (uriStart >= 0) prefixEnd = Math.Min(prefixEnd, uriStart);
            }
            var diagnosticPrefix = value[..prefixEnd];
            foreach (Match match in HttpStatus().Matches(diagnosticPrefix)) markers.Add("HTTP " + match.Groups[1].Value);
            if (diagnosticPrefix.Contains("timeout", StringComparison.OrdinalIgnoreCase)) markers.Add("timeout");
            foreach (var name in ExceptionNames)
                if (diagnosticPrefix.Contains(name, StringComparison.Ordinal)) markers.Add(name);
        }
        catch (RegexMatchTimeoutException) { return Redacted; }
        return markers.Count == 0 ? Redacted : string.Join("; ", markers) + "; " + Redacted;
    }

    public static string? Url(string? value)
    {
        if (value is null) return null;
        if (value.Length > 4096) return Redacted;
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") || string.IsNullOrEmpty(uri.Host)) return Redacted;
        // SchemeAndServer excludes user-info. Do not return any query, fragment
        // or path segment: historical file routes can embed bearer credentials.
        return uri.GetComponents(UriComponents.SchemeAndServer, UriFormat.UriEscaped) + "/" + Redacted;
    }

    public static string? Body(string? value)
    {
        if (value is null) return null;
        if (value.Length > MaxBodyCharacters) return Redacted;
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        try
        {
            using var document = JsonDocument.Parse(value, new JsonDocumentOptions { MaxDepth = 32 });
            if (document.RootElement.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array)) return Redacted;
            var diagnostics = new SortedDictionary<string, object>(StringComparer.Ordinal) { ["redacted"] = true };
            Collect(document.RootElement, diagnostics);
            return JsonSerializer.Serialize(diagnostics);
        }
        catch (JsonException) { return Redacted; }
        catch (ArgumentException) { return Redacted; }
    }

    private static void Collect(JsonElement element, IDictionary<string, object> result)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray()) Collect(child, result);
            return;
        }
        if (element.ValueKind != JsonValueKind.Object) return;
        foreach (var property in element.EnumerateObject())
        {
            if (property.Name.Length > 64 || AuditSensitiveData.IsSensitive(property.Name)) continue;
            var key = string.Concat(property.Name.Where(char.IsLetterOrDigit)).ToLowerInvariant();
            if (key is "pass" or "pwd" or "passwd" or "setcookie" or "proxyauthorization") continue;
            var text = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : null;
            switch (key)
            {
                case "statuscode" or "httpstatus":
                    if (TryNumber(property.Value, out var status) && status is >= 100 and <= 599)
                        result.TryAdd("statusCode", status);
                    break;
                case "errorcode" or "code":
                    var code = Code(text ?? (property.Value.ValueKind == JsonValueKind.Number ? property.Value.GetRawText() : null));
                    if (!string.IsNullOrWhiteSpace(code) && code != Redacted) result.TryAdd("errorCode", code);
                    break;
                case "traceid" or "requestid" or "correlationid" or "transactionuuid":
                    if (text is { Length: <= 64 } && Guid.TryParse(text, out var id))
                        result.TryAdd(key switch { "traceid" => "traceId", "requestid" => "requestId", "correlationid" => "correlationId", _ => "transactionUuid" }, id.ToString("D"));
                    break;
                case "operation":
                    if (text is { Length: <= 64 } && Enum.TryParse<InvoiceIntegrationActionType>(text, true, out var action) && Enum.IsDefined(action))
                        result.TryAdd("operation", action.ToString());
                    else if (text is "LookupByTransactionUuid" or "DownloadOfficialFile" or "PreviewInvoice")
                        result.TryAdd("operation", text);
                    break;
                case "responselength" or "filesize" or "filelength" or "durationms":
                    if (TryNumber(property.Value, out var length) && length >= 0)
                        result.TryAdd(key switch { "responselength" => "responseLength", "filesize" => "fileSize", "filelength" => "fileLength", _ => "durationMs" }, length);
                    break;
                case "contenttype":
                    if (text is "application/json" or "application/xml" or "application/pdf" or "application/zip" or "text/plain")
                        result.TryAdd("contentType", text);
                    break;
                case "filekind":
                    if (text is "pdf" or "xml" or "zip") result.TryAdd("fileKind", text);
                    break;
            }
            // Unknown nested containers may contain useful status/trace metadata;
            // arbitrary keys and string values never become output properties.
            Collect(property.Value, result);
        }
    }

    private static bool TryNumber(JsonElement value, out long number)
    {
        number = 0;
        return value.ValueKind == JsonValueKind.Number ? value.TryGetInt64(out number) :
            value.ValueKind == JsonValueKind.String && value.GetString() is { Length: <= 20 } text &&
            long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out number);
    }

    [GeneratedRegex(@"\bHTTP[\s_:=-]*([1-5][0-9]{2})\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 100)]
    private static partial Regex HttpStatus();

    [GeneratedRegex(@"\b(?:[a-z0-9_]*(?:password|passwd|pwd|secret|token|credential|authorization|cookie|bearer|basic)[a-z0-9_]*|pass|private[\s_-]*key|api[\s_-]*key|connection[\s_-]*string)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 100)]
    private static partial Regex CredentialMarker();
}
