using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using GaoApp.Application.DTOs.Delivery;
using GaoApp.Domain.Delivery;
using GaoApp.Tests.Security;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Delivery;

[Collection("DeliveryD08")]
public sealed class DeliveryTripCompletionHttpTests(DeliveryD02Fixture fixture)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Trips_complete_only_after_all_active_orders_are_closed_and_reads_never_repost(bool cancelSecond)
    {
        using var c = await DeliveryD08Case.CreateAsync(fixture);
        var id = c.Trip.TripId;
        var listPath = "/admin/api/delivery-trips?take=100&courierUserId=" + c.Trip.Courier.UserId;
        async Task<DeliveryTripPageDto> List(string state = "") => (await c.Client.Http.GetFromJsonAsync<DeliveryTripPageDto>(listPath + "&state=" + state))!;
        async Task<DeliveryTripDetailDto> Detail() => (await c.Client.Http.GetFromJsonAsync<DeliveryTripDetailDto>(c.Trip.Path()))!;
        Assert.False((await Detail()).IsCompleted);
        Assert.False((await List()).Items.Single(x => x.TripId == id).IsCompleted);
        Assert.DoesNotContain((await List("Completed")).Items, x => x.TripId == id);

        await c.ConfirmAsync(await c.RequestAsync());
        var partial = await c.WorkspaceAsync();
        Assert.NotNull(partial.Trip.ReturnToStore);
        Assert.False(partial.Trip.IsCompleted);
        Assert.False((await Detail()).IsCompleted);
        Assert.False((await List()).Items.Single(x => x.TripId == id).IsCompleted);
        Assert.Contains((await List("Departed")).Items, x => x.TripId == id);
        Assert.DoesNotContain((await List("Completed")).Items, x => x.TripId == id);

        var second = partial.Trip.Orders.Single(x => x.IsActive && x.DeliveryOrderId != c.Id);
        if (cancelSecond)
        {
            await c.Client.JsonAsync(HttpMethod.Post, c.Trip.Path("orders/" + second.DeliveryOrderId + "/cancel-request"),
                new DeliveryTripCancellationRequest(Guid.NewGuid(), partial.Trip.Version, second.Version, "Khách hủy nhận hàng"));
            partial = await c.WorkspaceAsync();
            Assert.False(partial.Trip.IsCompleted);
            Assert.Equal("CancellationPending", partial.Trip.Orders.Single(x => x.DeliveryOrderId == second.DeliveryOrderId).State);
        }
        await CloseSecond(c, second.DeliveryOrderId, cancelSecond);
        var beforeReads = await c.SnapshotAsync();
        var closed = await Detail();
        Assert.True(closed.IsCompleted);
        Assert.Equal("Departed", closed.State); // Dispatch proof stays historical; completion is a current projection.
        Assert.True((await c.WorkspaceAsync()).Trip.IsCompleted);
        Assert.All(closed.Orders.Where(x => x.IsActive), row => Assert.Contains(row.State, new[] { "Settled", "Cancelled" }));
        Assert.True((await List()).Items.Single(x => x.TripId == id).IsCompleted);
        Assert.True((await List("Completed")).Items.Single(x => x.TripId == id).IsCompleted);
        Assert.DoesNotContain((await List("Departed")).Items, x => x.TripId == id);
        Assert.True((await Detail()).IsCompleted);
        Assert.Equal(beforeReads, await c.SnapshotAsync());
        await using (var db = c.Context()) Assert.Equal(DeliveryTripState.Departed, (await db.DeliveryTrips.SingleAsync(x => x.Id == id)).State);
        if (!cancelSecond)
        {
            await RunBrowser(c);
            Assert.Equal(beforeReads, await c.SnapshotAsync());
        }
    }

    private static async Task CloseSecond(DeliveryD08Case c, int orderId, bool cancel)
    {
        var workspace = await c.WorkspaceAsync(); var row = workspace.Trip.Orders.Single(x => x.DeliveryOrderId == orderId);
        var lines = row.Lines.Select(x => new DeliveryHandoverLineRequest(x.LineId,
            cancel ? "0" : x.DispatchedBaseQuantityText, cancel ? x.DispatchedBaseQuantityText : "0")).ToArray();
        var path = c.Trip.Path("handover/" + orderId);
        using var previewResponse = await c.Client.Http.PostAsJsonAsync(path + "/preview", new DeliveryHandoverPreviewRequest(workspace.Trip.Version, row.Version, lines));
        Assert.True(previewResponse.IsSuccessStatusCode, await previewResponse.Content.ReadAsStringAsync());
        var preview = (await previewResponse.Content.ReadFromJsonAsync<DeliveryHandoverPreviewDto>())!;
        var request = new DeliveryHandoverRequest(Guid.NewGuid(), workspace.Trip.Version, row.Version,
            c.Closer.Store.TerminalId, c.ShiftId, c.Trip.Courier.UserId, true, cancel ? "NotDelivered" : "Full",
            cancel ? "Khách hủy, nhận trả đủ hàng" : null, null, lines, "Cash", preview.PayableText, preview.PayableText, "0", false);
        using var response = await c.Client.Http.PostAsJsonAsync(path, request);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var ack = (await response.Content.ReadFromJsonAsync<DeliveryHandoverAck>())!;
        Assert.Equal(cancel ? "Cancelled" : "Settled", ack.State);
    }

    private static async Task RunBrowser(DeliveryD08Case c)
    {
        var root = FullApplicationFixture.SourceRoot();
        var start = new ProcessStartInfo("node") { WorkingDirectory=root, UseShellExecute=false, CreateNoWindow=true,
            RedirectStandardInput=true, RedirectStandardOutput=true, RedirectStandardError=true };
        start.Environment["NODE_PATH"] = Path.Combine(root,"Logs/pos-offline-browser-deps/node_modules");
        start.ArgumentList.Add(Path.Combine(root,"GaoApp.Tests.Browser/delivery-trip-completion.browser.cjs"));
        using var process=Process.Start(start)!;
        var output=process.StandardOutput.ReadToEndAsync(); var error=process.StandardError.ReadToEndAsync();
        await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { baseUrl=$"http://{c.Closer.Store.Host}:{c.Trip.Web.Address.Port}",
            user=c.Closer.Name, password=c.Closer.Password, terminalId=c.Closer.Store.TerminalId, tripId=c.Trip.TripId,
            evidenceRoot=Path.Combine(root,"Logs/delivery-trip-completion-20261009") }));
        process.StandardInput.Close(); using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(2));
        try { await process.WaitForExitAsync(timeout.Token); } catch { if(!process.HasExited)process.Kill(true); throw; }
        Assert.True(process.ExitCode==0,await output+await error);
    }
}
