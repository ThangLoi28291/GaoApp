using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GaoApp.Domain.Storefront;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Storefront;

[Collection("StorefrontWeb05"), Trait("Category", "StorefrontWeb05"), Trait("Browser", "Chromium")]
public sealed class Web05BrowserTests(Web01Fixture fixture)
{
    [Fact]
    public async Task Actual_mobile_QR_checkout_copy_download_reopen_page_closed_payment_and_underpayment_review_have_real_browser_evidence()
    {
        var review = await Web05Prepared.CreateAsync(fixture); var c = review.Case; string account;
        await using (var db = c.Context()) account = (await db.StoreBankAccounts.SingleAsync(x => x.Id == c.Seed.BankAccountId)).AccountNumber;
        var output = Web05Support.Artifact("browser/" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(output); var log = "";
        await using (var host = await Web05PublicProcess.StartAsync(c))
        {
            log = host.LogPath;
            var input = new { host.BaseUrl, output, account, primary = new { c.Seed.ProductId, c.Seed.VariantId, c.Seed.UnitId }, reviewGuestLink = review.Placed.GuestLink };
            var start = new ProcessStartInfo("node") { WorkingDirectory = Path.Combine(Web03Support.Root(), "GaoApp.Tests.Browser"), UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add(Path.Combine(Web03Support.Root(), "GaoApp.Tests.Browser/storefront-web05.browser.cjs")); using var process = new Process { StartInfo = start }; Assert.True(process.Start());
            await File.WriteAllTextAsync(Path.Combine(output, "owned-browser-process.json"), JsonSerializer.Serialize(new { process.Id, Started = DateTime.UtcNow }));
            var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync(); await process.StandardInput.WriteAsync(JsonSerializer.Serialize(input, new JsonSerializerOptions(JsonSerializerDefaults.Web))); process.StandardInput.Close();
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(6)); var exited = process.WaitForExitAsync(timeout.Token); int primaryIntent = 0; string primaryReference = ""; bool paid = false; bool reviewed = false;
            try
            {
                while (!exited.IsCompleted)
                {
                    if (primaryIntent == 0 && File.Exists(Path.Combine(output, "checkpoint-queued")))
                    {
                        await using var db = c.Context(); var intent = await db.Set<StorefrontPaymentIntent>().AsNoTracking().SingleAsync(x => x.Id != review.IntentId); primaryIntent = intent.Id; primaryReference = intent.ProviderReference;
                        Assert.Equal(2, await db.StorefrontOrders.CountAsync()); Assert.Empty(await db.POSShifts.ToListAsync()); Assert.Empty(await db.Orders.ToListAsync()); Assert.Empty(await db.OrderPayments.ToListAsync());
                        await c.ProcessAsync(primaryIntent, review.Bank); await File.WriteAllTextAsync(Path.Combine(output, "checkpoint-bank-ready"), "Real trusted processor prepared existing browser-created intent through fake transport only.");
                    }
                    if (!paid && File.Exists(Path.Combine(output, "checkpoint-page-closed")))
                    {
                        Assert.True(primaryIntent > 0); review.Bank.Pay(primaryReference, 45_000m); await c.ProcessAsync(primaryIntent, review.Bank);
                        await using var db = c.Context(); Assert.Equal(StorefrontPaymentState.Confirmed, (await db.StorefrontOrders.SingleAsync(x => x.Id != review.OrderId)).PaymentState);
                        paid = true; await File.WriteAllTextAsync(Path.Combine(output, "checkpoint-paid"), "Verified receipt and allocation committed while customer page was closed; no POS context.");
                    }
                    if (!reviewed && File.Exists(Path.Combine(output, "checkpoint-review-ready")))
                    {
                        review.Bank.Pay(review.Reference, 1m); var probe = new Web05SaveFailureProbe(); await c.ProcessAsync(review.IntentId, review.Bank, probe);
                        await probe.WriteAsync(c, review.IntentId, review.Bank, "actual-browser-underpayment"); await using var db = c.Context();
                        Assert.Equal(StorefrontPaymentState.Review, (await db.StorefrontOrders.SingleAsync(x => x.Id == review.OrderId)).PaymentState);
                        reviewed = true; await File.WriteAllTextAsync(Path.Combine(output, "checkpoint-reviewed"), "Full underpayment retained Review; no allocation for review order.");
                    }
                    await Task.WhenAny(exited, Task.Delay(100, timeout.Token));
                }
                await exited; var error = await stderr; await File.WriteAllTextAsync(Path.Combine(output, "node.stdout.log"), await stdout); await File.WriteAllTextAsync(Path.Combine(output, "node.stderr.log"), error);
                Assert.True(process.ExitCode == 0, "WEB05 actual browser failed: " + error + " Evidence: " + output); Assert.True(primaryIntent > 0 && paid && reviewed);
                using var report = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(output, "browser-results.json"))); Assert.All(report.RootElement.GetProperty("results").EnumerateArray(), row => Assert.True(row.GetProperty("passed").GetBoolean()));
            }
            finally { if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); } }
        }
        await using var check = c.Context(); var orders = await check.StorefrontOrders.ToListAsync(); Assert.Equal(2, orders.Count);
        Assert.Single(orders, x => x.PaymentState == StorefrontPaymentState.Confirmed); Assert.Single(orders, x => x.PaymentState == StorefrontPaymentState.Review);
        Assert.Equal(45_000m, Assert.Single(await check.Set<StorefrontPaymentAllocation>().ToListAsync()).Amount); Assert.Empty(await check.Orders.ToListAsync()); Assert.Empty(await check.OrderPayments.ToListAsync()); Assert.Empty(await check.InventoryTransactions.ToListAsync()); Assert.Empty(await check.AdminMenuItems.ToListAsync());
        Assert.Equal(2, review.Bank.Orders.Count); Assert.Equal(2, review.Bank.InitiateCalls);
        var hashes = orders.Select(x => x.GuestTokenHash).ToHashSet(StringComparer.Ordinal);
        var leaked = Regex.Matches(await File.ReadAllTextAsync(log), "(?<![A-Za-z0-9_-])[A-Za-z0-9_-]{43}(?![A-Za-z0-9_-])").Any(x => hashes.Contains(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(x.Value)))));
        Assert.False(leaked, "Owned public host log must not contain a valid guest capability.");
    }
}
