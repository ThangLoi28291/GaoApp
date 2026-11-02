using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Storefront;

[Collection("StorefrontWeb05"), Trait("Category", "StorefrontWeb05")]
public sealed class Web05ReportHttpTests(Web01Fixture fixture)
{
    [Fact]
    public async Task Actual_report_HTTP_and_CSV_keep_bank_day_precision_exact_money_above_JS_integer_limit_and_unattributed_review_bucket_separate()
    {
        var seed = await fixture.CaseAsync(); await Web05CashFlowTests.SnapshotIsolation(seed); var day = DateTime.UtcNow.AddHours(7).Date; var date = day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        const decimal amount = 9_007_199_254_740_993m; await Web05CashFlowTests.Receipt(seed, amount, date); await Web05CashFlowTests.Receipt(seed, amount, "");
        await using var host = await Web05ReportHost.StartAsync(seed); using var client = host.Client(); var query = "?fromDate=" + date + "&toDate=" + date;
        using var response = await client.GetAsync("/admin/reports/operations/cashflow/data" + query); Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.True(response.Headers.CacheControl?.NoStore);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()); var rows = json.RootElement.GetProperty("items").EnumerateArray().ToArray(); var row = Assert.Single(rows);
        Assert.Equal(amount, row.GetProperty("amount").GetDecimal()); Assert.Equal("9.007.199.254.740.993", row.GetProperty("amountText").GetString()); Assert.Equal(1, row.GetProperty("timePrecision").GetInt32());
        var time = row.GetProperty("timeText").GetString(); Assert.Contains(day.ToString("dd/MM/yyyy"), time); Assert.Contains("ngày ngân hàng", time); Assert.DoesNotContain(":", time);
        var bank = Assert.Single(json.RootElement.GetProperty("summary").EnumerateArray(), x => x.GetProperty("fund").GetString() == "bank"); Assert.Equal(amount, bank.GetProperty("inflow").GetDecimal()); Assert.Equal("9.007.199.254.740.993", bank.GetProperty("inflowText").GetString());
        var review = Assert.Single(json.RootElement.GetProperty("bankReceiptReviews").EnumerateArray()); Assert.Equal(amount, review.GetProperty("amount").GetDecimal()); Assert.Equal("9.007.199.254.740.993", review.GetProperty("amountText").GetString());
        using var export = await client.GetAsync("/admin/reports/operations/cashflow/export" + query); Assert.Equal(HttpStatusCode.OK, export.StatusCode); Assert.Equal("text/csv", export.Content.Headers.ContentType?.MediaType);
        var bytes = await export.Content.ReadAsByteArrayAsync(); Assert.True(bytes.AsSpan(0, 3).SequenceEqual(new byte[] { 239, 187, 191 })); var csv = Encoding.UTF8.GetString(bytes);
        Assert.Contains("9007199254740993", csv); Assert.DoesNotContain("9007199254740992", csv); Assert.Contains("ngày ngân hàng", csv); Assert.Contains("Chưa xác minh", csv);
        await File.WriteAllTextAsync(Web05Support.Artifact("report-http/" + Guid.NewGuid().ToString("N") + ".csv"), csv);
    }

    [Fact]
    public async Task Actual_report_controller_permissions_and_tenant_filter_reject_anonymous_foreign_actor_and_posted_store_override()
    {
        var own = await fixture.CaseAsync(); var foreign = await fixture.CaseAsync(); await Web05CashFlowTests.SnapshotIsolation(own);
        var day = DateTime.UtcNow.AddHours(7).Date.ToString("yyyy-MM-dd"); await Web05CashFlowTests.Receipt(own, 100m, day); await Web05CashFlowTests.Receipt(foreign, 999m, day);
        await using var host = await Web05ReportHost.StartAsync(own); var route = "/admin/reports/operations/cashflow/data?fromDate=" + day + "&toDate=" + day + "&StoreId=" + foreign.StoreId;
        using (var anonymous = host.Client(false)) { using var denied = await anonymous.GetAsync(route); Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode); }
        using (var stranger = host.Client(false)) { stranger.DefaultRequestHeaders.Add("X-Web05-User", foreign.UserId.ToString()); using var denied = await stranger.GetAsync(route); Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode); }
        using var client = host.Client(); using var response = await client.GetAsync(route); Assert.Equal(HttpStatusCode.OK, response.StatusCode); using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(100m, Assert.Single(json.RootElement.GetProperty("items").EnumerateArray()).GetProperty("amount").GetDecimal());
        await using var db = own.Context(); Assert.Empty(await db.AdminMenuItems.ToListAsync());
    }

    [Fact, Trait("Browser", "Chromium")]
    public async Task Actual_production_report_workspace_and_JS_show_exact_large_money_and_bank_day_then_download_exact_CSV_without_admin_bootstrap()
    {
        var seed = await fixture.CaseAsync(); await Web05CashFlowTests.SnapshotIsolation(seed); var date = DateTime.UtcNow.AddHours(7).Date.ToString("yyyy-MM-dd");
        await Web05CashFlowTests.Receipt(seed, 9_007_199_254_740_993m, date); await Web05CashFlowTests.Receipt(seed, 9_007_199_254_740_993m, "");
        await using var host = await Web05ReportHost.StartAsync(seed); var output = Web05Support.Artifact("report-browser/" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(output);
        var start = new ProcessStartInfo("node") { WorkingDirectory = Path.Combine(Web03Support.Root(), "GaoApp.Tests.Browser"), UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(Path.Combine(Web03Support.Root(), "GaoApp.Tests.Browser/storefront-web05-report.browser.cjs")); using var process = new Process { StartInfo = start }; Assert.True(process.Start());
        await File.WriteAllTextAsync(Path.Combine(output, "owned-browser-process.json"), JsonSerializer.Serialize(new { process.Id, Started = DateTime.UtcNow })); var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        await process.StandardInput.WriteAsync(JsonSerializer.Serialize(new { host.BaseUrl, host.UserId, output, date }, new JsonSerializerOptions(JsonSerializerDefaults.Web))); process.StandardInput.Close();
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(2)); var error = await stderr; await File.WriteAllTextAsync(Path.Combine(output, "node.stdout.log"), await stdout); await File.WriteAllTextAsync(Path.Combine(output, "node.stderr.log"), error);
            Assert.True(process.ExitCode == 0, "WEB05 report browser failed: " + error + " Evidence: " + output);
        }
        finally { if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); } }
    }
}
