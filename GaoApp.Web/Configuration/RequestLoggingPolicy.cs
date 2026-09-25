using Serilog.Events;

namespace GaoApp.Web.Configuration;

public static class RequestLoggingPolicy
{
    public static LogEventLevel GetLevel(HttpContext context, double elapsedMilliseconds, Exception? exception)
    {
        if (exception is not null || context.Response.StatusCode >= 500)
            return LogEventLevel.Error;
        if (context.Response.StatusCode >= 400)
            return LogEventLevel.Warning;

        // Keep frequent, healthy POS polls available at Debug without flooding normal logs.
        // Slow polls and unsuccessful responses remain visible for diagnosis.
        if (HttpMethods.IsGet(context.Request.Method)
            && context.Response.StatusCode is >= 200 and < 300
            && elapsedMilliseconds < 500
            && (context.Request.Path.Equals("/admin/pos/offline/status", StringComparison.OrdinalIgnoreCase)
                || context.Request.Path.Equals("/admin/acb/payments/terminal-pending", StringComparison.OrdinalIgnoreCase)))
            return LogEventLevel.Debug;

        return LogEventLevel.Information;
    }
}
