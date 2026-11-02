using System.Text;
using GaoApp.Web.Services.Assistant;
using Microsoft.Extensions.Options;

namespace GaoApp.Web.Services.CustomerService;

// The directory is private host configuration, never a directory inside a release.
// Path errors deliberately contain neither the supplied path nor credential content.
public static class CustomerChannelCredentialSafety
{
    public const string ReservedDirectory = "ChannelCredentials";
    public const int MaximumBytes = 32768;
    public const string ConfigurationError = "CustomerChannels credentials require accessible private files outside release/public/upload paths.";

    public static IEnumerable<string> Files(CustomerChannelConnection connection) =>
        new[] { connection.ManagedAccessToken is null ? connection.AccessTokenFile : null, connection.AppSecretFile, connection.VerifyTokenFile }
            .Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!);

    private static string Absolute(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 2048 || !Path.IsPathFullyQualified(path) ||
            path.Any(char.IsControl) || path.Split('/', '\\').Any(p => p is "." or "..") ||
            OperatingSystem.IsWindows() && (path.StartsWith("\\\\", StringComparison.Ordinal) || path.Contains('~') || path[2..].Contains(':') ||
                path.Split('/', '\\').Any(p => p.EndsWith(' ') || p.EndsWith('.'))))
            throw new InvalidOperationException(ConfigurationError);
        var full = Path.GetFullPath(path);
        // Windows also accepts //server/share and mixed separators as UNC.
        if (OperatingSystem.IsWindows() && full.StartsWith("\\\\", StringComparison.Ordinal))
            throw new InvalidOperationException(ConfigurationError);
        AssistantStorageSafety.NoLinks(full);
        return full;
    }

    public static string PathFor(string path, CustomerChannelOptions options, IWebHostEnvironment environment, IConfiguration? configuration = null)
    {
        if (!options.Enabled) throw new InvalidOperationException(ConfigurationError);
        var root = Absolute(options.CredentialDirectory);
        if (!Directory.Exists(root)) throw new InvalidOperationException(ConfigurationError);
        var excluded = new List<string> { environment.ContentRootPath, environment.WebRootPath };
        var upload = configuration?["Storage:UploadRoot"];
        if (!string.IsNullOrWhiteSpace(upload)) excluded.Add(Path.GetFullPath(upload, environment.ContentRootPath));
        excluded.AddRange(options.PublicRoots);
        foreach (var item in excluded.Where(p => !string.IsNullOrWhiteSpace(p)))
        {
            var publicRoot = Absolute(item);
            if (AssistantStorageSafety.Inside(root, publicRoot) || AssistantStorageSafety.Inside(publicRoot, root))
                throw new InvalidOperationException(ConfigurationError);
        }
        var file = Absolute(path);
        if (file == root || !AssistantStorageSafety.Inside(file, root)) throw new InvalidOperationException(ConfigurationError);
        return file;
    }

    public static string Read(string path, CustomerChannelOptions options, IWebHostEnvironment environment, IConfiguration? configuration = null)
    {
        var file = PathFor(path, options, environment, configuration);
        // Bound both the file metadata and the stream, including a file enlarged after opening.
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length is <16 or >MaximumBytes) throw new InvalidOperationException(ConfigurationError);
        var bytes = new byte[MaximumBytes + 1];
        var count = 0;
        while (count < bytes.Length)
        {
            var read = stream.Read(bytes, count, bytes.Length - count);
            if (read == 0) break;
            count += read;
        }
        if (count > MaximumBytes) throw new InvalidOperationException(ConfigurationError);
        _ = PathFor(path, options, environment, configuration);
        var value = new UTF8Encoding(false, true).GetString(bytes, 0, count).TrimStart('\uFEFF').Trim();
        if (value.Length is <16 or >8192 || value.Any(char.IsControl)) throw new InvalidOperationException(ConfigurationError);
        return value;
    }

    public static void Validate(CustomerChannelOptions options, IWebHostEnvironment environment, IConfiguration? configuration = null, bool readEnabledFiles = true)
    {
        if (!options.Enabled) return;
        try
        {
            if (!options.Valid() || options.PublicRoots.Count > 1000) throw new InvalidOperationException(ConfigurationError);
            if (readEnabledFiles && options.Connections.Any(c => Files(c).Any()))
                foreach (var root in new[] { environment.WebRootPath, configuration?["Storage:UploadRoot"] }.Where(p => !string.IsNullOrWhiteSpace(p)))
                    RejectPublicLinks(Path.GetFullPath(root!, environment.ContentRootPath));
            foreach (var connection in options.Connections)
                foreach (var file in Files(connection))
                    if (readEnabledFiles && connection.Enabled) _ = Read(file, options, environment, configuration);
                    else _ = PathFor(file, options, environment, configuration);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or System.Security.SecurityException)
        { throw new InvalidOperationException(ConfigurationError); }
    }

    private static void RejectPublicLinks(string root)
    {
        AssistantStorageSafety.NoLinks(root);
        if (!Directory.Exists(root)) return;
        var pending = new Stack<string>(); pending.Push(root);
        while (pending.TryPop(out var directory))
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException(ConfigurationError);
                if ((attributes & FileAttributes.Directory) != 0) pending.Push(entry);
            }
    }
}

public sealed class CustomerChannelCredentialValidator(IWebHostEnvironment environment, IConfiguration configuration) : IValidateOptions<CustomerChannelOptions>
{
    public ValidateOptionsResult Validate(string? name, CustomerChannelOptions options)
    {
        try { CustomerChannelCredentialSafety.Validate(options, environment, configuration); return ValidateOptionsResult.Success; }
        catch (InvalidOperationException) { return ValidateOptionsResult.Fail(CustomerChannelCredentialSafety.ConfigurationError); }
    }
}
