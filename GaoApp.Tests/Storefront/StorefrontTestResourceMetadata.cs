using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace GaoApp.Tests.Storefront;

// Test resource evidence only. No provider replacement, assertions, key reads,
// cleanup, production conditionals or business behavior is introduced here.
internal static class StorefrontTestResourceMetadata
{
    private const string Variable = "GAOAPP_TEST_RESOURCE_METADATA_ROOT";
    private static string? Sink([CallerFilePath] string source = "")
    {
        var configured = Environment.GetEnvironmentVariable(Variable);
        if (string.IsNullOrWhiteSpace(configured)) return null;
        try
        {
            var repo = Directory.GetParent(Path.GetDirectoryName(source)!)!.Parent!.FullName;
            var expected = Path.GetFullPath(Path.Combine(repo, ".artifacts/storefront-rollout/WEB08/tests/ownership"));
            if (!string.Equals(Path.GetFullPath(configured), expected, StringComparison.OrdinalIgnoreCase)) return null;
            Directory.CreateDirectory(expected); return expected;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        { Console.Error.WriteLine("WEB08 resource metadata sink gap: " + error.GetType().Name); return null; }
    }
    private static object Owner(string source)
    {
        using var current = Process.GetCurrentProcess();
        return new { PID = current.Id, StartedUtc = current.StartTime.ToUniversalTime().ToString("O"), Name = current.ProcessName,
            Executable = Environment.ProcessPath, Source = source, SourceSHA256 = File.Exists(source) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source))) : null };
    }
    private static void Save(string kind, string source, object data)
    {
        var sink = Sink(); if (sink is null) return;
        try
        {
            File.WriteAllText(Path.Combine(sink, DateTime.UtcNow.ToString("yyyyMMddHHmmssfffffff") + "-" + Guid.NewGuid().ToString("N") + ".json"),
                JsonSerializer.Serialize(new { AtUtc = DateTime.UtcNow, Kind = kind, Owner = Owner(source), Data = data,
                    KeyContentsRead = false, CleanupPerformed = false, NoOwnershipInferredFromTime = true }));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            Console.Error.WriteLine("WEB08 resource metadata capture gap: " + kind + "/" + error.GetType().Name);
        }
    }
    internal static void BeforeDirectory(string path, string purpose, [CallerFilePath] string source = "")
    {
        if (Sink() is null) return;
        try
        {
            var full = Path.GetFullPath(path); var exists = Directory.Exists(full) || File.Exists(full); var reparse = false;
            for (var cursor = full; !string.IsNullOrEmpty(cursor); cursor = Path.GetDirectoryName(cursor)!)
                if ((Directory.Exists(cursor) || File.Exists(cursor)) && (File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0) reparse = true;
            Save("BeforeDirectory", source, new { Path = full, Purpose = purpose, InitiallyAbsent = !exists, ReparseObserved = reparse });
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { Save("BeforeDirectoryGap", source, new { Purpose = purpose, ErrorType = error.GetType().Name }); }
    }
    internal static void Provider(string directory, string application, [CallerFilePath] string source = "")
    {
        if (Sink() is null) return;
        Save("ConfiguredInProcessProvider", source, new { KeyRoot = Path.GetFullPath(directory), Application = application,
            ProducerIsCurrentProcess = true, PerFileCreatorClaim = false });
    }
    internal static void Producer(Process process, string? keyRoot, string database, [CallerFilePath] string source = "")
    {
        if (Sink() is null) return;
        try
        {
            Save("ConfiguredOwnedHostProducer", source, new { PID = process.Id, StartedUtc = process.StartTime.ToUniversalTime().ToString("O"),
                Name = process.ProcessName, Executable = process.MainModule?.FileName, AssignedKeyRoot = keyRoot, GuardedDatabase = database,
                CallerOwnsStartedProcessHandle = true, OsParentCapture = "External live-chain capture required", PerFileCreatorClaim = false });
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException)
        { Save("HostProducerCaptureGap", source, new { AssignedKeyRoot = keyRoot, GuardedDatabase = database, ErrorType = error.GetType().Name }); }
    }
}
