using System.Security.Cryptography;

namespace GaoApp.Web.Security;

public static class ContentSecurityPolicy
{
    private static readonly object NonceKey = new();

    public static string Nonce(HttpContext context)
    {
        if (context.Items.TryGetValue(NonceKey, out var value) && value is string nonce) return nonce;
        nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
        context.Items[NonceKey] = nonce;
        return nonce;
    }

    public static string Header(HttpContext context)
    {
        // Only fixed loopback printing endpoints are allowed. Never allow all HTTP(S)
        // connections, arbitrary local ports, or a script/CDN wildcard.
        var printing = "http://127.0.0.1:17880 http://127.0.0.1:17881 " +
            "wss://localhost:8181 wss://localhost:8282 wss://localhost:8383 wss://localhost:8484";
        var websocket = "";
        if (Uri.TryCreate($"{context.Request.Scheme}://{context.Request.Host}", UriKind.Absolute, out var origin) &&
            origin.Scheme is "http" or "https" && !origin.Authority.Any(c => char.IsWhiteSpace(c) || c is ';' or '\''))
            websocket = $" {(origin.Scheme == "https" ? "wss" : "ws")}://{origin.Authority}";
        return $"default-src 'self'; script-src 'nonce-{Nonce(context)}'; script-src-attr 'none'; " +
            "style-src 'self' 'unsafe-inline'; font-src 'self' data:; img-src 'self' data: blob: https:; " +
            $"connect-src 'self'{websocket} {printing}; " +
            "worker-src 'self' blob:; frame-src 'self' blob:; media-src 'self' blob: https:; " +
            "manifest-src 'self'; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'self'";
    }
}
