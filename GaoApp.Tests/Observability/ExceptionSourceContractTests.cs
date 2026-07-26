using System.Text.RegularExpressions;

namespace GaoApp.Tests.Observability;

/// <summary>
/// Repository-wide syntax invariants are source tests because a behavioral
/// injection cannot exercise every catch/rethrow site.
/// </summary>
public sealed class ExceptionSourceContractTests
{
    private static readonly string[] RuntimeProjects =
    [
        "GaoApp.Application",
        "GaoApp.Domain",
        "GaoApp.Infrastructure",
        "GaoApp.Migrator",
        "GaoApp.Web"
    ];

    [Fact]
    public void Runtime_source_does_not_throw_caught_variable()
    {
        var violations = RuntimeSources()
            .Where(file => Regex.IsMatch(
                File.ReadAllText(file),
                @"\bthrow\s+(ex|exception)\s*;",
                RegexOptions.CultureInvariant))
            .Select(RelativePath)
            .ToList();

        Assert.True(
            violations.Count == 0,
            $"Use 'throw;' to preserve stack trace. Violations: {string.Join(", ", violations)}");
    }

    [Fact]
    public void Runtime_source_has_no_empty_or_comment_only_catch()
    {
        var catchPattern = new Regex(
            @"catch\s*(?:\([^{}]*\))?\s*(?:when\s*\([^{}]*\))?\s*\{(?<body>[^{}]*)\}",
            RegexOptions.CultureInvariant | RegexOptions.Singleline);
        var violations = new List<string>();

        foreach (var file in RuntimeSources())
        {
            var source = File.ReadAllText(file);

            foreach (Match match in catchPattern.Matches(source))
            {
                var bodyWithoutComments = Regex.Replace(
                    match.Groups["body"].Value,
                    @"//.*?$|/\*.*?\*/",
                    string.Empty,
                    RegexOptions.Multiline | RegexOptions.Singleline);

                if (string.IsNullOrWhiteSpace(bodyWithoutComments))
                {
                    var line = source[..match.Index].Count(c => c == '\n') + 1;
                    violations.Add($"{RelativePath(file)}:{line}");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            $"Empty/comment-only catch blocks: {string.Join(", ", violations)}");
    }

    [Fact]
    public void Fatal_startup_owners_log_and_rethrow()
    {
        foreach (var relativePath in new[]
                 {
                     Path.Combine("GaoApp.Web", "Program.cs"),
                     Path.Combine("GaoApp.Migrator", "Program.cs")
                 })
        {
            var source = File.ReadAllText(
                Path.Combine(FindRepositoryRoot(), relativePath));
            var catchIndex = source.LastIndexOf(
                "catch (Exception ex)",
                StringComparison.Ordinal);
            var finallyIndex = source.LastIndexOf(
                "finally",
                StringComparison.Ordinal);

            Assert.True(catchIndex >= 0 && finallyIndex > catchIndex);
            var catchBlock = source[catchIndex..finallyIndex];
            Assert.Contains("Log.Fatal(ex", catchBlock, StringComparison.Ordinal);
            Assert.Contains("throw;", catchBlock, StringComparison.Ordinal);
            Assert.DoesNotContain(
                "Environment.ExitCode",
                catchBlock,
                StringComparison.Ordinal);
        }
    }

    private static IEnumerable<string> RuntimeSources()
    {
        var root = FindRepositoryRoot();

        foreach (var project in RuntimeProjects)
        {
            foreach (var file in Directory.EnumerateFiles(
                         Path.Combine(root, project),
                         "*.cs",
                         SearchOption.AllDirectories))
            {
                if (!file.Contains(
                        $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                        StringComparison.OrdinalIgnoreCase) &&
                    !file.Contains(
                        $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                        StringComparison.OrdinalIgnoreCase))
                {
                    yield return file;
                }
            }
        }
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "GaoApp.sln")))
                return current.FullName;

            current = current.Parent;
        }

        throw new InvalidOperationException("Không tìm thấy repository root.");
    }

    private static string RelativePath(string file)
        => Path.GetRelativePath(FindRepositoryRoot(), file);
}
