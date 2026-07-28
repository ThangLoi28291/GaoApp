using System.Diagnostics;
using FluentAssertions;

namespace GaoApp.Tests.Configuration;

public sealed class RepositoryHygieneContractTests
{
    [Fact]
    public void Gitignore_uses_scoped_generated_artifact_rules()
    {
        var root = FindRepositoryRoot();
        var rules = File.ReadAllLines(Path.Combine(root, ".gitignore"))
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .ToHashSet(StringComparer.Ordinal);

        rules.Should().Contain(
        [
            "/publish-phase8/",
            "/Datacode.zip",
            "/GaoApp.Infrastructure/Migrations.zip",
            "/R1*.zip",
            "/*.patch",
            "/*.diff",
            "/GaoApp.Web/wwwroot/uploads/",
            "TestResults/"
        ]);
        rules.Should().NotContain(
        [
            "*.zip",
            "*.patch",
            "/publish-*/",
            "**/wwwroot/uploads/"
        ]);
    }

    [Fact]
    public void Git_index_does_not_track_forbidden_generated_artifacts()
    {
        var root = FindRepositoryRoot();
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("ls-files");
        startInfo.ArgumentList.Add("-z");

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException(
                "Unable to start git.");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        process.ExitCode.Should().Be(
            0,
            because: error);

        var trackedPaths = output.Split(
            '\0',
            StringSplitOptions.RemoveEmptyEntries);
        var forbidden = trackedPaths.Where(IsForbidden).ToList();

        forbidden.Should().BeEmpty();
    }

    private static bool IsForbidden(string path)
        => path.StartsWith(
                "publish-phase8/",
                StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(
                "TestResults/",
                StringComparison.OrdinalIgnoreCase)
            || path.Contains(
                "/TestResults/",
                StringComparison.OrdinalIgnoreCase)
            || path.Contains(
                "/bin/",
                StringComparison.OrdinalIgnoreCase)
            || path.Contains(
                "/obj/",
                StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(
                ".zip",
                StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(
                ".patch",
                StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(
                ".diff",
                StringComparison.OrdinalIgnoreCase);

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "GaoApp.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate GaoApp repository root.");
    }
}
