using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GaoApp.Infrastructure.Security;

/// <summary>
/// Pure canonical-path contract shared by startup validation and the runtime
/// Data Protection registration.
/// </summary>
public static class DataProtectionKeysPath
{
    public const string ConfigurationKey = "DataProtection:KeysPath";

    public static string Resolve(
        string? configuredPath,
        string contentRootPath,
        string applicationBasePath,
        bool isProduction)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            throw new InvalidOperationException(
                "Startup validation failed: DataProtection:KeysPath is missing or empty.");
        }

        if (Path.IsPathRooted(configuredPath) &&
            !Path.IsPathFullyQualified(configuredPath))
        {
            throw new InvalidOperationException(
                "Startup validation failed: DataProtection:KeysPath must be relative or fully qualified.");
        }

        if (isProduction && !Path.IsPathFullyQualified(configuredPath))
        {
            throw new InvalidOperationException(
                "Startup validation failed: Production requires a fully qualified DataProtection:KeysPath.");
        }

        try
        {
            var canonicalContentRoot = Path.GetFullPath(contentRootPath);
            var canonicalApplicationBase = Path.GetFullPath(applicationBasePath);
            var canonicalPath = Path.IsPathFullyQualified(configuredPath)
                ? Path.GetFullPath(configuredPath)
                : Path.GetFullPath(Path.Combine(canonicalContentRoot, configuredPath));

            if (isProduction &&
                (IsSameOrDescendant(canonicalPath, canonicalContentRoot) ||
                 IsSameOrDescendant(canonicalPath, canonicalApplicationBase)))
            {
                throw new InvalidOperationException(
                    "Startup validation failed: Production requires DataProtection:KeysPath outside the application publish directory.");
            }

            return canonicalPath;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex) when (
            ex is ArgumentException or
            NotSupportedException or
            PathTooLongException)
        {
            throw new InvalidOperationException(
                "Startup validation failed: DataProtection:KeysPath is not a valid path.",
                ex);
        }
    }

    private static bool IsSameOrDescendant(string candidatePath, string rootPath)
    {
        var root = Path.GetFullPath(rootPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var candidate = Path.GetFullPath(candidatePath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        return string.Equals(candidate, root, comparison) ||
               candidate.StartsWith(
                   root + Path.DirectorySeparatorChar,
                   comparison);
    }
}

/// <summary>
/// Resolve duy nhất cho DataProtection:KeysPath.
/// Không tạo thư mục và không ghi file; mọi side effect thuộc startup validation.
/// </summary>
public interface IDataProtectionKeysPathResolver
{
    string Resolve();
}

public sealed class DataProtectionKeysPathResolver : IDataProtectionKeysPathResolver
{
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;

    public DataProtectionKeysPathResolver(
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        _configuration = configuration;
        _environment = environment;
    }

    public string Resolve()
        => DataProtectionKeysPath.Resolve(
            _configuration[DataProtectionKeysPath.ConfigurationKey],
            _environment.ContentRootPath,
            AppContext.BaseDirectory,
            _environment.IsProduction());
}

public interface IDataProtectionKeysDirectoryValidator
{
    void Validate(string canonicalPath);
}

/// <summary>
/// Performs the only Data Protection key-directory side effect used at startup.
/// It never enumerates, changes, or removes an existing key file.
/// </summary>
public sealed class DataProtectionKeysDirectoryValidator
    : IDataProtectionKeysDirectoryValidator
{
    public void Validate(string canonicalPath)
    {
        string? probePath = null;
        Exception? validationFailure = null;
        Exception? cleanupFailure = null;

        try
        {
            if (File.Exists(canonicalPath))
            {
                throw new IOException(
                    "The configured Data Protection keys path points to a file.");
            }

            Directory.CreateDirectory(canonicalPath);
            probePath = Path.Combine(
                canonicalPath,
                $".startup_dataprotection_probe_{Guid.NewGuid():N}.tmp");

            using var stream = new FileStream(
                probePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None);
            stream.WriteByte(0);
        }
        catch (Exception ex)
        {
            validationFailure = ex;
        }
        finally
        {
            if (probePath is not null)
            {
                try
                {
                    File.Delete(probePath);
                }
                catch (Exception ex)
                {
                    cleanupFailure = ex;
                }
            }
        }

        if (validationFailure is not null)
        {
            throw new InvalidOperationException(
                "Startup validation failed: DataProtection:KeysPath cannot be created or written.",
                validationFailure);
        }

        if (cleanupFailure is not null)
        {
            throw new InvalidOperationException(
                "Startup validation failed: DataProtection:KeysPath probe cleanup failed.",
                cleanupFailure);
        }
    }
}

/// <summary>
/// Chỉ được khởi tạo sau khi startup validation đã kiểm tra thư mục thành công.
/// Data Protection key repository không được cấu hình trước thời điểm này.
/// </summary>
public sealed class DataProtectionKeysPathState
{
    private readonly object _sync = new();
    private string? _resolvedPath;

    public void Initialize(string resolvedPath)
    {
        if (string.IsNullOrWhiteSpace(resolvedPath))
        {
            throw new ArgumentException(
                "Resolved Data Protection keys path is required.",
                nameof(resolvedPath));
        }

        var normalizedPath = Path.GetFullPath(resolvedPath);

        lock (_sync)
        {
            if (_resolvedPath is null)
            {
                _resolvedPath = normalizedPath;
                return;
            }

            var comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            if (!string.Equals(_resolvedPath, normalizedPath, comparison))
            {
                throw new InvalidOperationException(
                    "DataProtection:KeysPath đã được khởi tạo với một đường dẫn khác.");
            }
        }
    }

    public string GetRequiredPath()
    {
        lock (_sync)
        {
            return _resolvedPath ?? throw new InvalidOperationException(
                "DataProtection key store được yêu cầu trước khi startup validation hoàn tất.");
        }
    }
}

/// <summary>
/// Gắn FileSystemXmlRepository theo cách lazy.
/// Startup validation phải khởi tạo DataProtectionKeysPathState trước đó.
/// </summary>
internal sealed class ConfigureDataProtectionKeyManagementOptions
    : IConfigureOptions<KeyManagementOptions>
{
    private readonly IDataProtectionKeysPathResolver _keysPathResolver;
    private readonly DataProtectionKeysPathState _keysPathState;
    private readonly ILoggerFactory _loggerFactory;

    public ConfigureDataProtectionKeyManagementOptions(
        IDataProtectionKeysPathResolver keysPathResolver,
        DataProtectionKeysPathState keysPathState,
        ILoggerFactory loggerFactory)
    {
        _keysPathResolver = keysPathResolver;
        _keysPathState = keysPathState;
        _loggerFactory = loggerFactory;
    }

    public void Configure(KeyManagementOptions options)
    {
        var runtimePath = _keysPathResolver.Resolve();
        var validatedPath = _keysPathState.GetRequiredPath();
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (!string.Equals(runtimePath, validatedPath, comparison))
        {
            throw new InvalidOperationException(
                "DataProtection:KeysPath changed after startup validation.");
        }

        options.XmlRepository = new FileSystemXmlRepository(
            new DirectoryInfo(runtimePath),
            _loggerFactory);
    }
}
