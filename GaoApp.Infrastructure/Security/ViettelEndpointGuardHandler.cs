using Microsoft.Extensions.Configuration;

namespace GaoApp.Infrastructure.Security;

/// <summary>Request URLs come from store settings. Only host configuration may extend this allowlist.</summary>
public sealed class ViettelEndpointGuardHandler(IConfiguration configuration) : DelegatingHandler
{
    private static readonly string[] Defaults =
    [
        "https://api-vinvoice.viettel.vn", "https://api-sinvoice.viettel.vn",
        "https://demo-sinvoice.viettel.vn:8443"
    ];

    public static bool IsAllowed(Uri? uri, IEnumerable<string> origins)
    {
        if (uri is null || !uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttps ||
            uri.UserInfo.Length > 0 || uri.Fragment.Length > 0 || uri.HostNameType != UriHostNameType.Dns)
            return false;
        return origins.Any(origin => Uri.TryCreate(origin, UriKind.Absolute, out var allowed) &&
            allowed.Scheme == Uri.UriSchemeHttps && allowed.HostNameType == UriHostNameType.Dns &&
            allowed.UserInfo.Length == 0 && allowed.AbsolutePath == "/" && allowed.Query.Length == 0 && allowed.Fragment.Length == 0 &&
            string.Equals(allowed.IdnHost, uri.IdnHost, StringComparison.OrdinalIgnoreCase) && allowed.Port == uri.Port);
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var extra = configuration.GetSection("Security:Viettel:AdditionalAllowedOrigins").Get<string[]>() ?? [];
        if (!IsAllowed(request.RequestUri, Defaults.Concat(extra)))
            throw new HttpRequestException("Viettel destination is not an approved HTTPS origin. Check host integration configuration.");
        return base.SendAsync(request, ct);
    }
}
