using System.Diagnostics;
using System.Text.Json;
using GaoApp.Domain.Delivery;
using GaoApp.Tests.Security;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Delivery;

[Collection("DeliveryD06")]
public sealed class DeliverySimpleWorkspaceBrowserTests(DeliveryD02Fixture fixture)
{
    [Fact]
    public async Task Desktop_and_phone_complete_the_same_counter_workflow_with_exact_retry_and_real_stock()
    {
        using var desktop = await DeliveryD06Order.CreateAsync(fixture, quantity: 10, distinctSource: true);
        using var phone = await DeliveryD06Order.CreateAsync(fixture, quantity: 10, distinctSource: true);
        var courier = await fixture.Web.AddAccountAsync(desktop.Account.Store);
        var root = FullApplicationFixture.SourceRoot();
        var output = Path.Combine(root, "Logs", "delivery-simple-workspace-20261029", "browser");
        Directory.CreateDirectory(output);
        object Account(DeliveryD06Order order) => new { name = order.Account.Name, password = order.Account.Password,
            terminalId = order.Account.Store.TerminalId, id = order.Id, code = order.Picking.Source.Detail.Code,
            baseUrl = $"http://{order.Account.Store.Host}:{fixture.Web.Address.Port}" };
        var start = new ProcessStartInfo("node") { WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.Environment["NODE_PATH"] = Path.Combine(root, "Logs", "pos-offline-browser-deps", "node_modules");
        start.ArgumentList.Add(Path.Combine(root, "GaoApp.Tests.Browser", "delivery-workspace.browser.cjs"));
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { desktop = Account(desktop), phone = Account(phone), courierId = courier.UserId, output }));
        process.StandardInput.Close();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(6));
        try { await process.WaitForExitAsync(timeout.Token); } catch { if (!process.HasExited) process.Kill(true); throw; }
        var log = await stdout + "\n" + await stderr;
        await File.WriteAllTextAsync(Path.Combine(output, "browser.log"), log);
        Assert.True(process.ExitCode == 0, log);
        foreach (var order in new[] { desktop, phone })
        {
            await using var db = order.Picking.Source.Context();
            Assert.Equal(DeliveryState.Settled, (await db.DeliveryOrders.SingleAsync(x => x.Id == order.Id)).State);
            var journal = await db.DeliveryJournalEntries.SingleAsync(x => x.DeliveryOrderId == order.Id && x.Kind == DeliveryJournalKind.Settlement);
            Assert.Equal(160000, journal.MoneyAmount);
            var sale = await db.Orders.Include(x => x.Lines).Include(x => x.Payments).SingleAsync(x => x.Id == journal.SaleOrderId);
            Assert.Equal(8, sale.Lines.Sum(x => x.BaseQuantity)); Assert.Equal(160000, Assert.Single(sale.Payments).Amount);
            Assert.Equal(2, await db.DeliveryReturnedCostFragments.Where(x => x.DeliveryOrderId == order.Id).SumAsync(x => x.BaseQuantity));
            Assert.Equal(1, await db.DeliveryRevisions.CountAsync(x => x.DeliveryOrderId == order.Id && x.Action == "counter-handover"));
        }
    }
}
