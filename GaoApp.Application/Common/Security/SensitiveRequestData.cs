using System.Text.RegularExpressions;

namespace GaoApp.Application.Common.Security;

/// <summary>Invoice URLs contain bearer credentials. Keep the route useful without persisting the credential.</summary>
public static partial class SensitiveRequestData
{
    public static string Redact(string? value, string? requestToken = null)
    {
        var safe = QueryToken().Replace(InvoiceUrl().Replace(value ?? string.Empty, "$1[redacted]"), "$1[redacted]");
        return string.IsNullOrEmpty(requestToken) ? safe :
            safe.Replace(requestToken, "[redacted]", StringComparison.OrdinalIgnoreCase);
    }

    public static string? InvoiceToken(string? path, string? query = null)
    {
        var match = InvoiceUrl().Match(path ?? string.Empty);
        var token = match.Success ? match.Groups[2].Value : null;
        if (token is { Length: 64 } && token.All(Uri.IsHexDigit)) return token;
        if (path is null || !(path.Equals("/invoice-document", StringComparison.OrdinalIgnoreCase) || path.StartsWith("/invoice-document/", StringComparison.OrdinalIgnoreCase))) return null;
        var queryMatch = QueryToken().Match(query ?? string.Empty);
        if (!queryMatch.Success) return null;
        var documentToken = Uri.UnescapeDataString(queryMatch.Groups[2].Value);
        return documentToken.Length is >= 32 and <= 8192 && documentToken.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_') ? documentToken : null;
    }

    [GeneratedRegex("(/invoice-info/)([^/?#\\s\\\"'<>]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex InvoiceUrl();

    [GeneratedRegex("([?&]token=)([^&#\\s\\\"'<>]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex QueryToken();
}
