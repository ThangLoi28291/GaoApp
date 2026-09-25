using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace GaoApp.Tests.Security;

internal sealed class PosWebSocketClient : IAsyncDisposable
{
    private readonly ClientWebSocket socket = new();
    private readonly CancellationTokenSource stopping = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> calls = new();
    private readonly TaskCompletionSource closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal ConcurrentQueue<JsonElement> Events { get; } = new();
    private Task reader = Task.CompletedTask;
    private int sequence;
    internal static async Task<PosWebSocketClient> ConnectAsync(FullApplicationFixture.Client client, int storeId, int terminalId)
    {
        var result = new PosWebSocketClient();
        try
        {
            result.socket.Options.Cookies = client.Cookies;
            result.socket.Options.SetRequestHeader("Host", client.Host);
            var uri = new UriBuilder(client.Http.BaseAddress!) { Scheme = "ws", Path = "/hubs/pos" }.Uri;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await result.socket.ConnectAsync(uri, timeout.Token);
            result.reader = result.ReadAsync();
            await result.SendAsync(new { protocol = "json", version = 1 });
            await result.InvokeAsync("JoinStoreGroup", storeId, terminalId.ToString());
            return result;
        }
        catch { await result.DisposeAsync(); throw; }
    }
    internal async Task<JsonElement> InvokeAsync(string method, params object[] args)
    {
        var response = await InvokeReplyAsync(method, args);
        Assert.False(response.TryGetProperty("error", out var error), error.ToString());
        return response;
    }
    internal async Task InvokeRejectedAsync(string method, params object[] args)
    {
        var response = await InvokeReplyAsync(method, args);
        Assert.True(response.TryGetProperty("error", out _), "Expected the real hub to reject this invocation.");
    }
    private async Task<JsonElement> InvokeReplyAsync(string method, params object[] args)
    {
        var id = Interlocked.Increment(ref sequence).ToString();
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        calls[id] = completion;
        try
        {
            await SendAsync(new { type = 1, invocationId = id, target = method, arguments = args });
            var response = await completion.Task.WaitAsync(TimeSpan.FromSeconds(10));
            return response;
        }
        finally { calls.TryRemove(id, out _); }
    }
    internal Task WaitForCloseAsync() => closed.Task.WaitAsync(TimeSpan.FromSeconds(12));
    internal async Task WaitForEventAsync(string eventType)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!HasEvent(eventType)) await Task.Delay(20, timeout.Token);
    }
    internal bool HasEvent(string eventType) => Events.Any(x => x.TryGetProperty("target", out var target) &&
        target.GetString() == "pos:event" && x.GetProperty("arguments")[0].GetProperty("eventType").GetString() == eventType);
    private async Task SendAsync(object value)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value) + '\u001e');
        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, stopping.Token);
    }
    private async Task ReadAsync()
    {
        var buffer = new byte[32768];
        using var pending = new MemoryStream();
        try
        {
            while (!stopping.IsCancellationRequested)
            {
                var response = await socket.ReceiveAsync(buffer, stopping.Token);
                if (response.MessageType == WebSocketMessageType.Close) break;
                for (var i = 0; i < response.Count; i++)
                {
                    if (buffer[i] != 0x1e) { pending.WriteByte(buffer[i]); continue; }
                    var message = JsonDocument.Parse(pending.ToArray()).RootElement.Clone(); pending.SetLength(0);
                    if (message.TryGetProperty("type", out var type) && type.GetInt32() == 7) return;
                    if (message.TryGetProperty("invocationId", out var id) && calls.TryGetValue(id.GetString()!, out var call)) call.TrySetResult(message);
                    else Events.Enqueue(message);
                }
            }
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
        catch (WebSocketException) { }
        finally
        {
            closed.TrySetResult();
            foreach (var call in calls.Values) call.TrySetException(new InvalidOperationException("WebSocket closed before invocation completed."));
        }
    }
    public async ValueTask DisposeAsync()
    {
        stopping.Cancel(); socket.Abort(); await reader;
        socket.Dispose(); stopping.Dispose();
    }
}
