using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Security;

public static class ResourceProtectionResponse
{
    public static ObjectResult Result(HttpContext context, ResourceLease lease, Guid requestId = default)
    {
        context.Response.Headers.RetryAfter = lease.RetryAfterSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        context.Response.Headers.CacheControl = "no-store";
        var status = lease.Reason == "capacity" ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status429TooManyRequests;
        return new(new { contractVersion = "1.0", requestId, code = "resource_busy", errorCode = "RESOURCE_BUSY",
            message = $"Hệ thống đang bận. Vui lòng thử lại sau {lease.RetryAfterSeconds} giây.",
            retryAfterSeconds = lease.RetryAfterSeconds }) { StatusCode = status };
    }
    public static async Task WriteAsync(HttpContext context, ResourceLease lease)
    {
        var result = Result(context, lease);
        context.Response.StatusCode = result.StatusCode!.Value;
        await context.Response.WriteAsJsonAsync(result.Value, context.RequestAborted);
    }
}
