using System.Diagnostics;
using System.Diagnostics.Metrics;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Common.Options;

namespace GaoApp.Application.Services.Reports;

/// <summary>One admission limit per web process, shared by profit and cost-adjustment reads.</summary>
public sealed class ProfitReportExecutionGate : IDisposable
{
    public static ProfitReportExecutionGate Default { get; } = new(new ProfitReportLimits());
    private static readonly Meter Meter = new("GaoApp.Reports");
    private static readonly Histogram<double> Duration = Meter.CreateHistogram<double>("profit.duration", "ms");
    private static readonly Counter<long> Rejected = Meter.CreateCounter<long>("profit.rejected");
    private readonly ProfitReportLimits limits;
    private readonly SemaphoreSlim slots;
    private int admitted;
    public ProfitReportExecutionGate(ProfitReportLimits limits)
    {
        if (!limits.IsValid()) throw new ArgumentException("Invalid profit report limits.", nameof(limits));
        this.limits = limits;
        slots = new(limits.MaxConcurrent, limits.MaxConcurrent);
    }

    public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (Interlocked.Increment(ref admitted) > limits.MaxConcurrent + limits.MaxQueued)
        {
            Interlocked.Decrement(ref admitted); Rejected.Add(1);
            throw new ReportBusyException();
        }
        var entered = false;
        var started = Stopwatch.GetTimestamp();
        try
        {
            entered = await slots.WaitAsync(TimeSpan.FromMilliseconds(limits.QueueTimeoutMilliseconds), ct);
            if (!entered) { Rejected.Add(1); throw new ReportBusyException(); }
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(limits.ExecutionTimeoutSeconds));
            try { return await operation(timeout.Token); }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                Rejected.Add(1);
                throw new ReportBusyException("Báo cáo vượt thời gian xử lý. Vui lòng chọn khoảng ngày ngắn hơn và thử lại.");
            }
        }
        finally
        {
            if (entered) slots.Release();
            Interlocked.Decrement(ref admitted);
            Duration.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
    }
    public void Dispose() => slots.Dispose();
}
