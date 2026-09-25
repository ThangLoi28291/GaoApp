using GaoApp.Application.Common.Abstractions;
using Microsoft.Extensions.Logging;

namespace GaoApp.Infrastructure.Storage;

public sealed class LocalFileStorageService(UploadPathResolver paths, ILogger<LocalFileStorageService> logger) : IFileStorageService
{
    public async Task<string> SaveAsync(Stream content, string relativePath, CancellationToken ct = default)
    {
        relativePath = UploadPathResolver.Normalize(relativePath);
        var fullPath = paths.Resolve(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var temporary = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var fs = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, true))
                await content.CopyToAsync(fs, ct);
            ct.ThrowIfCancellationRequested();
            File.Move(temporary, fullPath, overwrite: true);
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { logger.LogWarning(ex, "Could not remove interrupted upload temporary file."); }
        }
        return relativePath;
    }

    public Task DeleteAsync(string relativePath, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        relativePath = UploadPathResolver.Normalize(relativePath);
        if (!Directory.Exists(paths.Root)) throw new DirectoryNotFoundException("Upload storage root is unavailable.");
        DeleteIfPresent(paths.Resolve(relativePath));
        // Remove the old public webroot copy as well, if middleware can serve it.
        if (paths.LegacyRoot != null && !string.Equals(paths.Root, paths.LegacyRoot, StringComparison.OrdinalIgnoreCase) &&
            (relativePath.StartsWith("uploads/products/", StringComparison.Ordinal) || relativePath.StartsWith("uploads/_temp/", StringComparison.Ordinal)))
            DeleteIfPresent(UploadPathResolver.ResolveUnderRoot(paths.LegacyRoot, relativePath[8..]));
        return Task.CompletedTask;
    }

    public Task MoveAsync(string fromRelativePath, string toRelativePath, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var source = paths.Resolve(fromRelativePath);
        var target = paths.Resolve(toRelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Move(source, target, overwrite: true);
        return Task.CompletedTask;
    }

    public string ToPublicUrl(string relativePath) => "/" + UploadPathResolver.Normalize(relativePath);

    private static void DeleteIfPresent(string path)
    {
        try { File.Delete(path); }
        catch (DirectoryNotFoundException)
        {
            // Deleting an already absent nested directory has reached the requested state.
            return;
        }
    }
}
