using System.Diagnostics;
using System.Text.Json;

namespace GaoApp.Tests.Security;

internal static class PublishedTestRelease
{
    private static readonly Lazy<Task<string>> PreparedRelease = new(PrepareAsync);

    internal static Task<string> GetAsync() => PreparedRelease.Value;

    private static async Task<string> PrepareAsync()
    {
        var configured = Environment.GetEnvironmentVariable("GAOAPP_TEST_RELEASE");
        if (!string.IsNullOrWhiteSpace(configured)) return Path.GetFullPath(configured);

        // F5 / Test Explorer has no release environment variable. Build one verified pair per test run.
        // The existing publish script checks the package and never deploys or starts the application.
        var root = FullApplicationFixture.SourceRoot();
        var evidenceRoot = Path.Combine(root, "TestResults", "security-phase6");
        Directory.CreateDirectory(evidenceRoot);
        var logPath = Path.Combine(evidenceRoot, "automatic-publish.log");
        var resultPath = Path.Combine(evidenceRoot, $"automatic-publish-{Guid.NewGuid():N}.json");
        var powerShellCore = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PowerShell", "7", "pwsh.exe");
        var start = new ProcessStartInfo(File.Exists(powerShellCore) ? powerShellCore : "powershell.exe")
        {
            WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true
        };
        // Detached build servers can retain the native command's console handles under Windows PowerShell.
        start.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        start.Environment["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0";
        start.Environment["UseSharedCompilation"] = "false";
        // PowerShell 7 module paths inherited by Windows PowerShell can hide its built-in cmdlets.
        start.Environment.Remove("PSModulePath");
        foreach (var argument in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass",
            "-File", Path.Combine(root, "scripts", "prepare-published-test-release.ps1"),
            "-LogPath", logPath, "-ResultPath", resultPath }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the test release publisher.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw new TimeoutException("Publishing the isolated test release exceeded 10 minutes.");
        }
        // MSBuild can keep inherited output handles alive after PowerShell exits. A result file
        // avoids waiting on redirected pipe EOF and leaves a live log when publishing fails.
        if (!File.Exists(resultPath))
            throw new InvalidOperationException($"The test release publisher did not produce a result. See {logPath}.");
        using var result = JsonDocument.Parse(await File.ReadAllTextAsync(resultPath));
        if (process.ExitCode != 0 || !result.RootElement.GetProperty("success").GetBoolean())
            throw new InvalidOperationException($"Publishing the isolated test release failed. See {logPath}.{Environment.NewLine}{result.RootElement.GetProperty("error").GetString()}");
        var release = Path.GetFullPath(result.RootElement.GetProperty("releasePath").GetString()!);
        var allowed = Path.Combine(root, "publish", "releases") + Path.DirectorySeparatorChar;
        if (!release.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) || !File.Exists(Path.Combine(release, "release-manifest.json")))
            throw new InvalidOperationException("The publisher did not return a verified workspace release.");
        return release;
    }
}
