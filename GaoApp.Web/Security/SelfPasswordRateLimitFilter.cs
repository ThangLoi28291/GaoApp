using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace GaoApp.Web.Security;

// Runs after authentication and antiforgery. Share the account's credential-attempt budget across stores/IPs.
public sealed class SelfPasswordRateLimitFilter(LoginAccountLimiter limiter) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (!int.TryParse(context.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) || userId <= 0)
        { context.Result = new UnauthorizedResult(); return; }
        if (!context.ModelState.IsValid) { await next(); return; }
        using var lease = limiter.Acquire("", userId);
        if (!lease.IsAcquired)
        {
            var seconds = lease.TryGetMetadata(MetadataName.RetryAfter, out var retry)
                ? Math.Max(1, (int)Math.Ceiling(retry.TotalSeconds)) : 60;
            context.HttpContext.Response.Headers.RetryAfter = seconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
            context.Result = new ObjectResult(new { message = $"Bạn đã thử quá nhiều lần. Vui lòng chờ {seconds} giây rồi thử lại." }) { StatusCode = 429 };
            return;
        }
        await next();
    }
}
