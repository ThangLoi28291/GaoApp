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

        var fullPath = Path.Combine(_env.WebRootPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
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
        var fullPath = Path.Combine(_env.WebRootPath, relativePath.Replace('/', Path.DirectorySeparatorChar));

        if (File.Exists(fullPath))
            File.Delete(fullPath);

        return Task.CompletedTask;
    }

    public Task MoveAsync(string fromRelativePath, string toRelativePath, CancellationToken ct = default)
    {
        fromRelativePath = NormalizeRelative(fromRelativePath);
        toRelativePath = NormalizeRelative(toRelativePath);

        var fromFull = Path.Combine(_env.WebRootPath, fromRelativePath.Replace('/', Path.DirectorySeparatorChar));
        var toFull = Path.Combine(_env.WebRootPath, toRelativePath.Replace('/', Path.DirectorySeparatorChar));

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
        if (path.StartsWith("/")) path = path[1..];
        // chặn traverse
        if (path.Contains(".."))
            throw new InvalidOperationException("Invalid path.");
        return path;
    }
}
