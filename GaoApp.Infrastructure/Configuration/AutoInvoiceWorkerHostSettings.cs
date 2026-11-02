using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.CommandLine;
using Microsoft.Extensions.Configuration.EnvironmentVariables;

namespace GaoApp.Infrastructure.Configuration;

/// <summary>Host secrets survive replacing a Debug build or published release.</summary>
public static class AutoInvoiceWorkerHostSettings
{
    public const string PathKey = "Worker:HostSettingsPath";
    public const string WindowsHostDirectory = @"C:\GaoAppData\DeployConfig";

    public static string? Load(ConfigurationManager configuration, string environmentName,
        string applicationDirectory, string? defaultHostDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(environmentName) ||
            environmentName.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '.' and not '_' and not '-'))
            throw new InvalidOperationException("Worker environment name is invalid.");

        // The short command-line option takes precedence over the environment
        // variable Worker__HostSettingsPath. No secret is passed on the command line.
        var explicitPath = configuration["host-settings"] ?? configuration[PathKey];
        var directory = defaultHostDirectory ?? (OperatingSystem.IsWindows()
            ? WindowsHostDirectory
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "GaoApp", "DeployConfig"));
        var path = string.IsNullOrWhiteSpace(explicitPath)
            ? Path.Combine(directory, $"worker.appsettings.{environmentName}.json")
            : explicitPath;
        if (!Path.IsPathFullyQualified(path))
            throw new InvalidOperationException("Worker host settings must use an absolute path outside the application directory.");
        path = Path.GetFullPath(path);
        var root = Path.GetFullPath(applicationDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (string.Equals(path, root, comparison) || path.StartsWith(root + Path.DirectorySeparatorChar, comparison))
            throw new InvalidOperationException("Worker host settings must remain outside the application directory so publishing cannot overwrite them.");
        if (!File.Exists(path))
        {
            if (!string.IsNullOrWhiteSpace(explicitPath))
                throw new FileNotFoundException("The explicitly configured Worker host settings file does not exist.", path);
            // Existing development JSON and environment-only installations
            // remain supported. Published defaults deliberately contain no secrets.
            return null;
        }

        // Preserve the default .NET precedence: JSON < host JSON < environment
        // variables < command line. Adding JSON at the end would silently
        // override service/operator environment settings.
        // Generic Host adds command line twice: early for host settings and
        // again after JSON for application settings. Insert before the final
        // application overrides, never before the early host command line.
        var sources = configuration.Sources.ToList();
        var index = sources.FindLastIndex(source =>
            source is EnvironmentVariablesConfigurationSource { Prefix: null or "" });
        if (index < 0) index = sources.FindLastIndex(source => source is CommandLineConfigurationSource);
        configuration.AddJsonFile(path, optional: false, reloadOnChange: false);
        var hostSource = configuration.Sources[^1];
        configuration.Sources.RemoveAt(configuration.Sources.Count - 1);
        configuration.Sources.Insert(index < 0 ? configuration.Sources.Count : index, hostSource);
        return path;
    }
}
