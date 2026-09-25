using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Common.Options;
using GaoApp.Application.Services.Reports;
using GaoApp.Web.Middlewares;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace GaoApp.Tests.Reports;

public sealed class ProfitReportExecutionGateTests
{
    [Fact]
    public async Task Overload_is_bounded_and_cancelled_queued_request_releases_capacity()
    {
        using var gate = new ProfitReportExecutionGate(new ProfitReportLimits { MaxConcurrent = 2, MaxQueued = 1 });
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var active = 0; var peak = 0;
        async Task<int> Work(CancellationToken ct)
        {
            var count = Interlocked.Increment(ref active); peak = Math.Max(peak, count);
            try { await release.Task.WaitAsync(ct); return 1; }
            finally { Interlocked.Decrement(ref active); }
        }
        var first = gate.ExecuteAsync(Work, default); var second = gate.ExecuteAsync(Work, default);
        using var cancel = new CancellationTokenSource();
        var queued = gate.ExecuteAsync(Work, cancel.Token);
        await Assert.ThrowsAsync<ReportBusyException>(() => gate.ExecuteAsync(Work, default));
        cancel.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued);
        var replacement = gate.ExecuteAsync(Work, default);
        release.SetResult(); await Task.WhenAll(first, second, replacement);
        Assert.Equal(2, peak);
        Assert.Equal(1, await gate.ExecuteAsync(Work, default));
    }

    [Fact]
    public async Task Timed_out_or_failed_work_does_not_leak_a_slot()
    {
        using var gate = new ProfitReportExecutionGate(new ProfitReportLimits { MaxConcurrent = 1, MaxQueued = 0, ExecutionTimeoutSeconds = 1 });
        await Assert.ThrowsAsync<ReportBusyException>(() => gate.ExecuteAsync(async ct => { await Task.Delay(Timeout.Infinite, ct); return 1; }, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => gate.ExecuteAsync<int>(_ => throw new InvalidOperationException("synthetic"), default));
        Assert.Equal(2, await gate.ExecuteAsync(_ => Task.FromResult(2), default));
    }

    [Fact]
    public async Task Busy_response_is_retryable_and_explains_why_report_was_rejected()
    {
        var context = new DefaultHttpContext(); context.Response.Body = new MemoryStream();
        var middleware = new GlobalExceptionMiddleware(_ => throw new ReportBusyException(), NullLogger<GlobalExceptionMiddleware>.Instance);
        await middleware.InvokeAsync(context);
        Assert.Equal(503, context.Response.StatusCode); Assert.Equal("5", context.Response.Headers.RetryAfter.ToString());
        context.Response.Body.Position = 0;
        Assert.Contains("REPORT_BUSY", await new StreamReader(context.Response.Body).ReadToEndAsync());
    }
}
