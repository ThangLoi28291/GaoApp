using System.Diagnostics;
using GaoApp.Application.Common;
using GaoApp.Web.Services.Acb;

namespace GaoApp.Web.Middlewares;

public sealed class AcbCallbackDiagnosticsMiddleware(RequestDelegate next, AcbCallbackDiagnostics diagnostics)
{
    public async Task InvokeAsync(HttpContext context, ITenantContext tenant)
    {
        if (!AcbCallbackEndpoint.IsCallbackPath(context.Request.Path))
        { await next(context); return; }
        var started = Stopwatch.StartNew();
        context.Response.OnStarting(() =>
        {
            // Tenant error responses may clear headers; retain correlation even for a missing/paused route.
            context.Response.Headers["X-Acb-Diagnostic-Id"] = context.TraceIdentifier;
            return Task.CompletedTask;
        });
        Exception? failure = null;
        try { await next(context); }
        catch (Exception error) { failure = error; throw; }
        finally
        {
            var status = failure != null ? 500 : context.Response.StatusCode;
            var outcome = context.Items["AcbCallbackOutcome"] as string ?? (status switch
            {
                400 => "INVALID_REQUEST", 401 or 403 => "AUTH_REJECTED", 404 => "ROUTE_OR_STORE_NOT_FOUND",
                405 => "METHOD_NOT_ALLOWED", 413 => "PAYLOAD_TOO_LARGE", 415 => "UNSUPPORTED_CONTENT_TYPE",
                >= 300 and < 400 => "HTTPS_REDIRECT", >= 500 => "CALLBACK_UNAVAILABLE", _ => "HTTP_COMPLETED"
            });
            diagnostics.Http(context.TraceIdentifier, tenant.StoreId, status, outcome,
                context.Items["AcbCallbackReceiptId"] as int?, started.ElapsedMilliseconds, failure ?? context.Items["AcbCallbackFailure"] as Exception,
                context.Request.Headers.ContainsKey("x-api-key") ? "x-api-key" : context.Request.Headers.ContainsKey("Authorization") ? "Authorization" : "missing",
                context.Request.Host.Host, context.Items[AcbCallbackEndpoint.RoutedStoreItem] as int?);
        }
    }
}
