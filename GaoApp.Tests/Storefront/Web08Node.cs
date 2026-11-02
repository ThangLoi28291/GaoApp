using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace GaoApp.Tests.Storefront;

internal static class Web08Node
{
    internal static async Task<JsonDocument> RunAsync(object input, string output, string purpose, TimeSpan? deadline = null)
    {
        var script = Path.Combine(Web08Support.Root(), "GaoApp.Tests.Browser/storefront-web08.browser.cjs");
        var start = new ProcessStartInfo("node") { WorkingDirectory = Path.Combine(Web08Support.Root(), "GaoApp.Tests.Browser"),
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(script); using var process = new Process { StartInfo = start }; Assert.True(process.Start());
        await File.WriteAllTextAsync(Path.Combine(output, "owned-browser-process.json"), JsonSerializer.Serialize(new
        {
            AtUtc = DateTime.UtcNow, process.Id, StartedAtUtc = process.StartTime.ToUniversalTime(), Executable = process.MainModule?.FileName,
            ParentOwner = Web08Support.Owner(), RuntimeRoot = output, Purpose = purpose, Script = script,
            ScriptSHA256 = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(script))), NoPrivateCapabilityInMetadata = true
        }));
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        try
        {
            await process.StandardInput.WriteAsync(JsonSerializer.Serialize(input, new JsonSerializerOptions(JsonSerializerDefaults.Web))); process.StandardInput.Close();
            using var timeout = new CancellationTokenSource(deadline ?? TimeSpan.FromMinutes(7)); await process.WaitForExitAsync(timeout.Token);
            var error = await stderr; await File.WriteAllTextAsync(Path.Combine(output, "node.stdout.log"), await stdout);
            await File.WriteAllTextAsync(Path.Combine(output, "node.stderr.log"), error);
            Assert.True(process.ExitCode == 0, "WEB08 actual browser failed. Evidence: " + output + "\n" + error);
            var report = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(output, "browser-results.json")));
            Assert.All(report.RootElement.GetProperty("results").EnumerateArray(), row => Assert.True(row.GetProperty("passed").GetBoolean(), row.ToString()));
            return report;
        }
        finally
        {
            if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
            await File.WriteAllTextAsync(Path.Combine(output, "owned-browser-ended.json"), JsonSerializer.Serialize(new
            { AtUtc = DateTime.UtcNow, process.Id, StartedAtUtc = process.StartTime.ToUniversalTime(), process.HasExited, ExitCode = process.ExitCode }));
        }
    }
}
