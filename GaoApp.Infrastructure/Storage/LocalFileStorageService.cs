using GaoApp.Application.Common.Abstractions;
using Microsoft.AspNetCore.Hosting;

namespace GaoApp.Infrastructure.Storage;

public sealed class LocalFileStorageService : IFileStorageService
{
    private readonly IWebHostEnvironment _env;

    public LocalFileStorageService(IWebHostEnvironment env)
    {
        _env = env;
    }

    public async Task<string> SaveAsync(Stream content, string relativePath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            throw new ArgumentException("relativePath is required.", nameof(relativePath));

        relativePath = NormalizeRelative(relativePath);
        var fullPath = ResolveInsideWebRoot(relativePath);
        var dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        // overwrite allowed (tùy bạn). Nếu muốn tránh overwrite thì tự generate tên khác.
        await using var fs = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true);
        await content.CopyToAsync(fs, ct);

        return relativePath;
    }

    public Task DeleteAsync(string relativePath, CancellationToken ct = default)
    {
        relativePath = NormalizeRelative(relativePath);
        var fullPath = ResolveInsideWebRoot(relativePath);

        if (File.Exists(fullPath))
            File.Delete(fullPath);

        return Task.CompletedTask;
    }

    public Task MoveAsync(string fromRelativePath, string toRelativePath, CancellationToken ct = default)
    {
        fromRelativePath = NormalizeRelative(fromRelativePath);
        toRelativePath = NormalizeRelative(toRelativePath);

        var fromFull = ResolveInsideWebRoot(fromRelativePath);
        var toFull = ResolveInsideWebRoot(toRelativePath);

        var dir = Path.GetDirectoryName(toFull);
        if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        if (!File.Exists(fromFull))
            throw new FileNotFoundException("Source file not found.", fromFull);

        // Nếu đích đã tồn tại: overwrite
        if (File.Exists(toFull))
            File.Delete(toFull);

        File.Move(fromFull, toFull);

        return Task.CompletedTask;
    }

    public string ToPublicUrl(string relativePath)
    {
        relativePath = NormalizeRelative(relativePath);
        return "/" + relativePath; // wwwroot served => "/uploads/..."
    }

    private static string NormalizeRelative(string path)
    {
        path = path.Trim().Replace('\\', '/');
        path = path.TrimStart('/');

        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 ||
            Path.IsPathRooted(path) ||
            segments.Any(x => x is "." or ".."))
        {
            throw new InvalidOperationException("Invalid path.");
        }

        return path;
    }

    private string ResolveInsideWebRoot(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(_env.WebRootPath))
            throw new InvalidOperationException("WebRootPath chưa được cấu hình.");

        var root = Path.GetFullPath(_env.WebRootPath);
        var candidate = Path.GetFullPath(Path.Combine(
            root,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));

        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;

        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (!candidate.StartsWith(rootWithSeparator, comparison))
            throw new InvalidOperationException("Invalid path.");

        return candidate;
    }
}
