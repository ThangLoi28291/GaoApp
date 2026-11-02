using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace GaoApp.Tests.Storefront;

[Collection("StorefrontWeb09"), Trait("Category", "StorefrontWeb09")]
public sealed class Web09PreparationTests
{
    [Theory]
    [InlineData("GaoApp.Tests.dll")]
    [InlineData("GaoApp.LabelPrintClient.dll")]
    public async Task Offline_package_metadata_rejects_actual_test_and_label_client_assembly_names_without_loading_or_publishing_them(string fileName)
    {
        var output = Web09Support.Runtime("WEB09 metadata-only package boundary negative, not a built/published package");
        var package = Web09Support.DirectoryBefore(output, "metadata-package", "Synthetic file-name witness only; no executable assembly or private key");
        await File.WriteAllTextAsync(Path.Combine(package, fileName), "WEB09 SYNTHETIC METADATA ONLY: NOT A COMPILED ASSEMBLY");
        var config = Path.Combine(Web09Support.Root(), "docs/development/storefront-web09/public-host.example.json");
        var result = await Tool("Test-StorefrontPreparation.ps1", output, "-PublicConfigurationPath", config, "-RepositoryRoot", Web09Support.Root(), "-PreparedPackagePath", package);
        Assert.Equal(2, result.Exit); using var document = JsonDocument.Parse(result.Stdout);
        Assert.Equal("BLOCKED", document.RootElement.GetProperty("status").GetString());
        var check = Assert.Single(document.RootElement.GetProperty("checks").EnumerateArray(), x => x.GetProperty("code").GetString() == "PACKAGE_METADATA_NO_PRIVATE_OR_ADMIN_INPUTS");
        Assert.False(check.GetProperty("passed").GetBoolean()); Assert.False(document.RootElement.GetProperty("published").GetBoolean());
    }

    [Fact]
    public async Task Actual_offline_closed_template_validation_is_truthful_preparation_without_runtime_or_owner_release_claim()
    {
        var output = Web09Support.Runtime("WEB09 offline tool only; no publish/app/provider or SQL command");
        var config = Path.Combine(Web09Support.Root(), "docs/development/storefront-web09/public-host.example.json");
        var result = await Tool("Test-StorefrontPreparation.ps1", output, "-PublicConfigurationPath", config, "-RepositoryRoot", Web09Support.Root());
        Assert.Equal(0, result.Exit); using var json = JsonDocument.Parse(result.Stdout); var value = json.RootElement;
        Assert.Equal("OFFLINE_CHECKS_PASSED", value.GetProperty("status").GetString());
        Assert.False(value.GetProperty("published").GetBoolean()); Assert.False(value.GetProperty("runtimeVerified").GetBoolean()); Assert.False(value.GetProperty("ownerUatVerified").GetBoolean());
        Assert.All(value.GetProperty("checks").EnumerateArray(), x => Assert.True(x.GetProperty("passed").GetBoolean()));
    }

    [Theory]
    [InlineData("release")]
    [InlineData("preview")]
    [InlineData("stringFlag")]
    [InlineData("connection")]
    public async Task Offline_closed_template_rejects_open_or_ambiguous_flags_and_never_prints_connection_value(string attack)
    {
        var output = Web09Support.Runtime("WEB09 negative offline template metadata only " + attack);
        var document = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(Web09Support.Root(), "docs/development/storefront-web09/public-host.example.json")))!.AsObject();
        var host = document["StorefrontHost"]!.AsObject();
        if (attack == "release") host["ReleaseEnabled"] = true;
        if (attack == "preview") host["PreviewEnabled"] = true;
        if (attack == "stringFlag") host["ReleaseEnabled"] = "false";
        const string secret = "WEB09-NOT-A-CONNECTION-SECRET-NEVER-CONNECT";
        if (attack == "connection") document["ConnectionStrings"] = new JsonObject { ["DefaultConnection"] = secret };
        var config = Path.Combine(output, "synthetic-input.json"); await File.WriteAllTextAsync(config, document.ToJsonString());
        var result = await Tool("Test-StorefrontPreparation.ps1", output, "-PublicConfigurationPath", config, "-RepositoryRoot", Web09Support.Root());
        Assert.Equal(2, result.Exit); Assert.DoesNotContain(secret, result.Stdout + result.Stderr);
        using var json = JsonDocument.Parse(result.Stdout); Assert.Equal("BLOCKED", json.RootElement.GetProperty("status").GetString());
        Assert.Contains(json.RootElement.GetProperty("checks").EnumerateArray(), x => !x.GetProperty("passed").GetBoolean());
    }

    [Fact]
    public async Task Recovery_tool_creates_only_pending_unsigned_worksheet_and_refuses_overwriting_that_evidence()
    {
        var output = Web09Support.Runtime("WEB09 recovery worksheet tool only, no database/backup/key directory created");
        var file = Path.Combine(output, "pending-recovery.json"); var source = "W09Plan_" + Guid.NewGuid().ToString("N"); var target = "W09Plan_" + Guid.NewGuid().ToString("N");
        string[] arguments = ["-SourceDatabase", source, "-TargetDatabase", target, "-BackupPath", Path.Combine(output, "not-created.bak"),
            "-RetainedKeyRingPath", Path.Combine(output, "not-created-keys"), "-OutputPath", file];
        var result = await Tool("New-StorefrontRecoveryPlan.ps1", output, arguments); Assert.Equal(0, result.Exit);
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(file)); var value = json.RootElement;
        Assert.Equal("PENDING_NOT_EXECUTED", value.GetProperty("status").GetString()); Assert.False(value.GetProperty("published").GetBoolean());
        Assert.False(value.GetProperty("releaseAuthorized").GetBoolean()); Assert.Equal(JsonValueKind.Null, value.GetProperty("ownerApproval").ValueKind);
        Assert.False(value.GetProperty("retainedKeys").GetProperty("copyOrExportAuthorized").GetBoolean());
        Assert.False(File.Exists(Path.Combine(output, "not-created.bak"))); Assert.False(Directory.Exists(Path.Combine(output, "not-created-keys")));
        var before = await File.ReadAllBytesAsync(file); var repeat = await Tool("New-StorefrontRecoveryPlan.ps1", output, arguments); Assert.NotEqual(0, repeat.Exit);
        Assert.Equal(before, await File.ReadAllBytesAsync(file));
    }

    [Theory]
    [InlineData("Main")]
    [InlineData("GaoAppDb")]
    [InlineData("master")]
    [InlineData("same")]
    [InlineData("unsafe;DROP_DATABASE")]
    public async Task Recovery_plan_refuses_protected_or_ambiguous_scope_before_writing_output(string scope)
    {
        var output = Web09Support.Runtime("WEB09 negative worksheet scope only, no SQL " + scope.Replace(';', '_'));
        var file = Path.Combine(output, "must-not-exist.json"); var source = scope == "same" ? "W09PlanSame" : scope;
        var target = scope == "same" ? source : "W09Plan_" + Guid.NewGuid().ToString("N");
        var result = await Tool("New-StorefrontRecoveryPlan.ps1", output, "-SourceDatabase", source, "-TargetDatabase", target,
            "-BackupPath", Path.Combine(output, "never.bak"), "-RetainedKeyRingPath", Path.Combine(output, "never-keys"), "-OutputPath", file);
        Assert.NotEqual(0, result.Exit); Assert.Contains("RECOVERY_DATABASE_SCOPE_INVALID", result.Stderr); Assert.False(File.Exists(file));
    }

    private static async Task<(int Exit, string Stdout, string Stderr)> Tool(string name, string output, params string[] arguments)
    {
        var script = Path.Combine(Web09Support.Root(), "scripts/storefront", name); Assert.True(File.Exists(script));
        var start = new ProcessStartInfo("pwsh") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = output };
        foreach (var item in new[] { "-NoProfile", "-NonInteractive", "-File", script }.Concat(arguments)) start.ArgumentList.Add(item);
        using var process = new Process { StartInfo = start }; Assert.True(process.Start());
        var stem = Path.Combine(output, "tool-" + Guid.NewGuid().ToString("N"));
        await File.WriteAllTextAsync(stem + "-owner.json", JsonSerializer.Serialize(new { AtUtc = DateTime.UtcNow, Owner = Web09Support.Owner(),
            ProcessId = process.Id, StartedUtc = process.StartTime.ToUniversalTime(), Executable = process.MainModule?.FileName, Script = script,
            OfflineOnly = true, NoPublishMigrationKeyReadOrApplicationStart = true }));
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40)); await process.WaitForExitAsync(timeout.Token);
            var result = (Exit: process.ExitCode, Stdout: await stdout, Stderr: await stderr);
            await File.WriteAllTextAsync(stem + ".stdout.log", result.Stdout); await File.WriteAllTextAsync(stem + ".stderr.log", result.Stderr); return result;
        }
        finally { if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); } }
    }
}
