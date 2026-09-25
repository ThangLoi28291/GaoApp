using GaoApp.Web.Services.Acb;

namespace GaoApp.Tests.Security;

public sealed class AcbWakeCoalescingTests
{
    [Fact]
    public async Task Concurrent_notifications_coalesce_and_next_batch_can_wake_again()
    {
        using var signal = new AcbCallbackSignal();
        await Task.WhenAll(Enumerable.Range(0, 50).Select(_ => Task.Run(signal.Wake)));
        Assert.True(await signal.WaitAsync(default));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => signal.WaitAsync(cancelled.Token));
        signal.Wake();
        Assert.True(await signal.WaitAsync(default));
    }
}
