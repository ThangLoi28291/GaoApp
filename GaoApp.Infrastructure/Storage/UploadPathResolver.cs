using GaoApp.Application.Common.Options;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;

namespace GaoApp.Infrastructure.Storage;

public sealed class UploadPathResolver
{
    public string Root { get; }
    public string? LegacyRoot { get; }

    public UploadPathResolver(IWebHostEnvironment env, IOptions<StorageOptions> options)
    {
        var configured = options.Value.UploadRoot;
        if (string.IsNullOrWhiteSpace(configured))
            throw new InvalidOperationException("Storage:UploadRoot is required.");
        Root = Path.GetFullPath(Path.IsPathRooted(configured) ? configured : Path.Combine(env.ContentRootPath, configured));
        LegacyRoot = string.IsNullOrWhiteSpace(env.WebRootPath) ? null : Path.GetFullPath(Path.Combine(env.WebRootPath, "uploads"));
    }

    public static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Contains('\\') || path.Contains(':') || path.Contains('\0'))
            throw new InvalidOperationException("Invalid upload path.");
        path = path.TrimStart('/');
        var segments = path.Split('/');
        if (segments.Length < 2 || segments[0] != "uploads" ||
            segments.Any(s => string.IsNullOrWhiteSpace(s) || s is "." or ".." || s.EndsWith('.') || s.EndsWith(' ')))
            throw new InvalidOperationException("Invalid upload path.");
        return path;
    }

    public string Resolve(string virtualPath) => ResolveUnderRoot(Root, Normalize(virtualPath)[8..]);

    public static string ResolveUnderRoot(string root, string relative)
    {
        root = Path.GetFullPath(root);
        var path = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, comparison))
            throw new InvalidOperationException("Upload path is outside storage root.");
        // Administrators control the directories; refuse links/junctions to other locations.
        for (var item = new FileInfo(path); item != null; item = item.Directory is { } parent ? new FileInfo(parent.FullName) : null)
        {
            if ((File.Exists(item.FullName) || Directory.Exists(item.FullName)) &&
                (File.GetAttributes(item.FullName) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Links are not allowed in upload storage paths.");
        }
        return path;
    }
}
