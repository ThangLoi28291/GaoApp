namespace GaoApp.Web.Security;

public sealed class SecurityResponseHeadersMiddleware(RequestDelegate next, IConfiguration configuration)
{
    public Task InvokeAsync(HttpContext context)
    {
        ContentSecurityPolicy.Nonce(context);
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "SAMEORIGIN";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            if (context.Request.Path.Equals("/pos-offline-worker.js", StringComparison.OrdinalIgnoreCase))
            {
                // A worker has no HTML nonce. Its importScripts and cache fetches
                // are restricted to our origin, independently of page scripts.
                headers.ContentSecurityPolicy = "default-src 'none'; script-src 'self'; connect-src 'self'; object-src 'none'; base-uri 'none'";
            }
            else if (configuration.GetValue<bool>("Security:Csp:ReportOnly"))
            {
                // Explicit rollout mode. The default is enforcement; reports are
                // collected by browser tests, without a public URL/token log sink.
                headers.ContentSecurityPolicy = "frame-ancestors 'self'; object-src 'none'; base-uri 'self'";
                headers["Content-Security-Policy-Report-Only"] = ContentSecurityPolicy.Header(context);
            }
            else headers.ContentSecurityPolicy = ContentSecurityPolicy.Header(context);
            var officialDocument = context.Request.Path.StartsWithSegments("/invoice-document", StringComparison.OrdinalIgnoreCase);
            if (officialDocument)
            {
                headers.XFrameOptions = "DENY";
                headers.ContentSecurityPolicy = "default-src 'none'; base-uri 'none'; frame-ancestors 'none'; form-action 'none'; script-src 'none'; connect-src 'none'";
                headers.Remove("Content-Security-Policy-Report-Only");
            }
            var invoiceLink = officialDocument || context.Request.Path.StartsWithSegments("/invoice-info", StringComparison.OrdinalIgnoreCase);
            if (invoiceLink)
            {
                headers["Referrer-Policy"] = "no-referrer";
                headers["X-Robots-Tag"] = "noindex, nofollow, noarchive";
            }
            if (invoiceLink || context.User.Identity?.IsAuthenticated == true ||
                context.Request.Path.StartsWithSegments("/admin/account"))
            {
                headers.CacheControl = "no-store, no-cache";
                headers.Pragma = "no-cache";
                headers.Expires = "0";
            }
            return Task.CompletedTask;
        });
        return next(context);
    }
}
