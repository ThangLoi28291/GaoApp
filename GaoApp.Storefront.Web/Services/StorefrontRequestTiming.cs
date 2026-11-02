using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace GaoApp.Storefront.Web.Services;

// Optional per-request diagnostics only. No identifiers, configuration values,
// URLs, business data, query warming or cache entries are recorded.
public static class StorefrontRequestTiming
{
    private static readonly HashSet<string> Phases = ["db_context", "model", "host_gate", "store_read", "settings_read", "catalog", "view", "result",
        "media_read", "media_lookup", "media_header", "media_model", "image_transform", "image_read", "image_header", "image_cache", "image_decode", "image_scale", "image_encode"];
    public static bool IsEnabled(HttpContext? context) => context?.Items.ContainsKey(typeof(State)) == true;

    public static void Begin(HttpContext context, IConfiguration configuration, ILogger logger)
    {
        if (!bool.TryParse(configuration["StorefrontDiagnostics:RequestTimings"], out var enabled) || !enabled) return;
        var state = new State(); context.Items[typeof(State)] = state;
        context.Response.OnStarting(() =>
        {
            // Snapshot at headers; an active view span is partial here. The final
            // log below records completed pipeline/view durations.
            context.Response.Headers.Append("Server-Timing", string.Join(", ", state.Snapshot().Select(x => x.Key + ";dur=" + x.Value.ToString("0.###", CultureInfo.InvariantCulture))));
            return Task.CompletedTask;
        });
        context.Response.OnCompleted(() =>
        {
            logger.LogInformation("Storefront phase durations in milliseconds: {PhaseMilliseconds}", JsonSerializer.Serialize(state.Snapshot()));
            return Task.CompletedTask;
        });
    }

    public static IDisposable? Measure(HttpContext? context, string phase)
        => context is not null && Phases.Contains(phase) && context.Items.TryGetValue(typeof(State), out var value) && value is State state ? state.Start(phase) : null;

    public static async Task<T> TimeAsync<T>(HttpContext? context, string phase, Func<Task<T>> action)
    {
        using var span = Measure(context, phase);
        return await action();
    }

    public static void Complete(HttpContext context)
    {
        if (context.Items.TryGetValue(typeof(State), out var value) && value is State state) state.Complete();
    }

    private sealed class State
    {
        private readonly object gate = new();
        private readonly long started = Stopwatch.GetTimestamp();
        private long? completed;
        private readonly Dictionary<string, double> elapsed = new(StringComparer.Ordinal);
        private readonly List<Span> active = [];

        public IDisposable Start(string phase)
        {
            var span = new Span(this, phase, Stopwatch.GetTimestamp());
            lock (gate) active.Add(span);
            return span;
        }
        public void Complete() { lock (gate) completed ??= Stopwatch.GetTimestamp(); }
        public IReadOnlyDictionary<string, double> Snapshot()
        {
            lock (gate)
            {
                var now = Stopwatch.GetTimestamp();
                var result = new SortedDictionary<string, double>(elapsed, StringComparer.Ordinal)
                { ["pipeline"] = Stopwatch.GetElapsedTime(started, completed ?? now).TotalMilliseconds };
                foreach (var span in active) result[span.Phase] = result.GetValueOrDefault(span.Phase) + Stopwatch.GetElapsedTime(span.Started, now).TotalMilliseconds;
                foreach (var key in result.Keys.ToArray()) result[key] = Math.Round(result[key], 3);
                return result;
            }
        }
        private void End(Span span)
        {
            lock (gate)
            {
                if (!active.Remove(span)) return;
                elapsed[span.Phase] = elapsed.GetValueOrDefault(span.Phase) + Stopwatch.GetElapsedTime(span.Started).TotalMilliseconds;
            }
        }
        private sealed class Span(State owner, string phase, long startedAt) : IDisposable
        {
            public string Phase { get; } = phase;
            public long Started { get; } = startedAt;
            public void Dispose() => owner.End(this);
        }
    }
}

public sealed class StorefrontTimingFilter : IAsyncResultFilter
{
    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        using var span = StorefrontRequestTiming.Measure(context.HttpContext, context.Result is ViewResult or PartialViewResult ? "view" : "result");
        await next();
    }
}
