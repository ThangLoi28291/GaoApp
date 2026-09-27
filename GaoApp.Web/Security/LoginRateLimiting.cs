using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using GaoApp.Web.Areas.Admin.ViewModels.Account;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace GaoApp.Web.Security;

public sealed class LoginRateLimitOptions
{
    public int IpAttemptsPerMinute { get; set; } = 120;
    public int AccountAttemptsPerMinute { get; set; } = 10;
}

public static class LoginRateLimiting
{
    public static IServiceCollection AddLoginRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<LoginRateLimitOptions>().Bind(configuration.GetSection("Security:LoginRateLimit"))
            .Validate(x => x.IpAttemptsPerMinute is >= 50 and <= 1000 && x.AccountAttemptsPerMinute is >= 3 and <= 20,
                "Login rate limits must allow 50-1000 IP attempts and 3-20 account attempts per minute.").ValidateOnStart();
        services.AddSingleton<LoginAccountLimiter>();
        services.AddScoped<LoginAccountRateLimitFilter>();
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = (context, _) =>
            {
                context.HttpContext.Response.Headers.RetryAfter = "60";
                return ValueTask.CompletedTask;
            };
            options.AddPolicy("login", context =>
            {
                var limits = context.RequestServices.GetRequiredService<IOptions<LoginRateLimitOptions>>().Value;
                return RateLimitPartition.GetSlidingWindowLimiter(
                    context.Connection.RemoteIpAddress?.MapToIPv6().ToString() ?? "unknown",
                    _ => Window(limits.IpAttemptsPerMinute));
            });
            options.AddPolicy(
    "invoice-buyer-self-service",
    context =>
    {
        var key =
            context.Connection.RemoteIpAddress?
                .MapToIPv6()
                .ToString()
            ?? "unknown";

        return RateLimitPartition
            .GetSlidingWindowLimiter(
                key,
                _ =>
                    new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = 60,
                        Window =
                            TimeSpan.FromMinutes(1),
                        SegmentsPerWindow = 6,
                        QueueLimit = 0,
                        AutoReplenishment = true
                    });
    });
        });
        return services;
    }

    internal static SlidingWindowRateLimiterOptions Window(int permits) => new()
    {
        PermitLimit = permits, Window = TimeSpan.FromMinutes(1), SegmentsPerWindow = 6,
        QueueLimit = 0, AutoReplenishment = true
    };
}

/// <summary>Account identity is global, matching the global Users table, independent of client IP/tenant.</summary>
public sealed class LoginAccountLimiter : IDisposable
{
    private readonly PartitionedRateLimiter<string> limiter;
    public LoginAccountLimiter(IOptions<LoginRateLimitOptions> options)
        => limiter = PartitionedRateLimiter.Create<string, string>(key =>
            RateLimitPartition.GetSlidingWindowLimiter(key, _ => LoginRateLimiting.Window(options.Value.AccountAttemptsPerMinute)));

    public RateLimitLease Acquire(string username, int? userId = null)
    {
        var key = userId.HasValue ? "user:" + userId.Value.ToString(CultureInfo.InvariantCulture)
            : "name:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(username.Trim().ToUpperInvariant())));
        return limiter.AttemptAcquire(key);
    }
    public void Dispose() => limiter.Dispose();
}

// Action filters run after MVC antiforgery authorization; never consume account quota for invalid CSRF.
public sealed class LoginAccountRateLimitFilter(LoginAccountLimiter limiter, AppDbContext db) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var vm = context.ActionArguments.Values.OfType<LoginVm>().FirstOrDefault();
        if (vm is null || !context.ModelState.IsValid || string.IsNullOrWhiteSpace(vm.UserName)) { await next(); return; }
        var username = vm.UserName.Trim();
        // Match AuthUserRepository's SQL comparison, including the database collation.
        // All spellings which SQL considers the same user must share one quota.
        var userId = await db.Users.AsNoTracking().Where(u => u.UserName == username && !u.IsDeleted)
            .Select(u => (int?)u.Id).FirstOrDefaultAsync(context.HttpContext.RequestAborted);
        using var lease = limiter.Acquire(username, userId);
        if (!lease.IsAcquired)
        {
            var seconds = lease.TryGetMetadata(MetadataName.RetryAfter, out var retry) ? Math.Max(1, (int)Math.Ceiling(retry.TotalSeconds)) : 60;
            context.HttpContext.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
            context.Result = new ObjectResult(new { message = "Quá nhiều lần đăng nhập tài khoản này. Vui lòng chờ rồi thử lại." }) { StatusCode = 429 };
            return;
        }
        await next();
    }
}
