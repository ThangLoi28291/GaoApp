using System.Reflection;

namespace GaoApp.Tests.Security;

internal static class TestApplicationBuild
{
    internal static string WebAssemblyPath()
    {
        var output = new DirectoryInfo(AppContext.BaseDirectory);
        string webDll;
        if (string.Equals(output.Parent?.Name, "GaoApp.Tests", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(output.Parent?.Name, "PosOffline.Browser", StringComparison.OrdinalIgnoreCase))
        {
            // SDK artifacts layout: test/browser runner and Web builds are siblings.
            webDll = Path.Combine(output.Parent!.Parent!.FullName, "GaoApp.Web", output.Name, "GaoApp.Web.dll");
        }
        else
        {
            // Visual Studio / ordinary dotnet build: GaoApp.Tests/bin/Release/net8.0.
            var configuration = typeof(TestApplicationBuild).Assembly
                .GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration
                ?? throw new InvalidOperationException("Test build configuration is unavailable.");
            webDll = Path.Combine(FullApplicationFixture.SourceRoot(), "GaoApp.Web", "bin", configuration, output.Name, "GaoApp.Web.dll");
        }
        if (!File.Exists(webDll))
            throw new FileNotFoundException("Build GaoApp.Tests and its Web project reference in the same configuration before running integration tests.", webDll);
        return webDll;
    }
}
