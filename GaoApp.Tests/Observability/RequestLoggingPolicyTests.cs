using GaoApp.Web.Configuration;
using Microsoft.AspNetCore.Http;
using Serilog.Events;

namespace GaoApp.Tests.Observability;

public sealed class RequestLoggingPolicyTests
{
    [Theory]
    [InlineData("/admin/pos/offline/status", 200, 7, LogEventLevel.Debug)]
    [InlineData("/admin/acb/payments/terminal-pending", 200, 7, LogEventLevel.Debug)]
    [InlineData("/admin/pos/offline/status", 200, 500, LogEventLevel.Information)]
    [InlineData("/admin/acb/payments/terminal-pending", 200, 901, LogEventLevel.Information)]
    [InlineData("/admin/pos/offline/status", 401, 7, LogEventLevel.Warning)]
    [InlineData("/admin/acb/payments/terminal-pending", 403, 7, LogEventLevel.Warning)]
    [InlineData("/admin/pos/offline/status", 500, 7, LogEventLevel.Error)]
    [InlineData("/admin/pos/products/search", 200, 7, LogEventLevel.Information)]
    [InlineData("/admin/pos/screen", 200, 907, LogEventLevel.Information)]
    public void Frequent_successful_polls_are_quiet_but_failures_and_slow_requests_remain_visible(
        string path, int status, double elapsed, LogEventLevel expected)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Path = path;
        context.Response.StatusCode = status;
        Assert.Equal(expected, RequestLoggingPolicy.GetLevel(context, elapsed, null));
        Assert.Equal(LogEventLevel.Error, RequestLoggingPolicy.GetLevel(context, elapsed, new Exception("Test failure")));
    }
}
