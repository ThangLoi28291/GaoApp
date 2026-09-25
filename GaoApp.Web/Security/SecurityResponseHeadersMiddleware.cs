namespace GaoApp.Web.Security;

public sealed class SecurityResponseHeadersMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "SAMEORIGIN";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            // Same-origin invoice frames still work. A full script CSP needs removal
            // of existing inline scripts; do not pretend this subset prevents all XSS.
            headers.ContentSecurityPolicy = "frame-ancestors 'self'; object-src 'none'; base-uri 'self'";
            if (context.User.Identity?.IsAuthenticated == true ||
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
