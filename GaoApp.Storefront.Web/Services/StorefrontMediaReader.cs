using GaoApp.Application.Storefront;
using GaoApp.Domain.Storefront;

namespace GaoApp.Storefront.Web.Services;

public sealed record StorefrontMediaFile(Stream Content, string ContentType);
public sealed class StorefrontMediaReader(IStorefrontCatalogService catalog, IConfiguration configuration, StorefrontImageService? images = null)
{
    private static readonly SemaphoreSlim HeaderReaders = new(8);
    private static readonly SemaphoreSlim ModelReaders = new(2);
    // UploadsRoot is the physical uploads directory itself, never the whole admin wwwroot.
    public Task<StorefrontMediaFile?> OpenAsync(int id, CancellationToken ct = default) => OpenCore(id, null, ct);
    public Task<StorefrontMediaFile?> OpenSizedAsync(int id, int width, CancellationToken ct = default)
        => StorefrontVisualLimits.IsImageWidth(width) ? OpenCore(id, width, ct) : Task.FromResult<StorefrontMediaFile?>(null);
    internal Task<StorefrontMediaFile?> OpenForRequestAsync(int id, int? width, HttpContext? context, CancellationToken ct)
        => width.HasValue && !StorefrontVisualLimits.IsImageWidth(width.Value) ? Task.FromResult<StorefrontMediaFile?>(null) : OpenCore(id, width, ct, context);

    private async Task<StorefrontMediaFile?> OpenCore(int id, int? width, CancellationToken ct, HttpContext? context = null)
    {
        using var readTiming = StorefrontRequestTiming.Measure(context, "media_read");
        var root = configuration["StorefrontHost:UploadsRoot"];
        if (string.IsNullOrWhiteSpace(root) || !Path.IsPathFullyQualified(root)) return null;
        var source = await StorefrontRequestTiming.TimeAsync(context, "media_lookup", () => catalog.GetMediaAsync(id, ct));
        if (source is null) return null;
        if (width.HasValue && source.Kind != StorefrontMediaKind.Image) return null;
        var path = source.StoragePath.TrimStart('/');
        if (!path.StartsWith("uploads/", StringComparison.Ordinal) || path.Contains("..", StringComparison.Ordinal) ||
            path.Contains('\\') || path.Contains('%') || path.Contains(':') || path.Contains('?') || path.Contains('#') || path.Any(char.IsControl)) return null;
        var relative = path["uploads/".Length..];
        if (string.IsNullOrWhiteSpace(relative) || relative.Split('/').Any(x => x.Length == 0 || x.Equals("_temp", StringComparison.OrdinalIgnoreCase))) return null;
        var type = (source.Kind, Path.GetExtension(relative).ToLowerInvariant()) switch
        {
            (StorefrontMediaKind.Image, ".jpg" or ".jpeg") => "image/jpeg",
            (StorefrontMediaKind.Image, ".png") => "image/png",
            (StorefrontMediaKind.Image, ".webp") => "image/webp",
            (StorefrontMediaKind.Image, ".avif") => "image/avif",
            (StorefrontMediaKind.Image, ".gif") => "image/gif",
            (StorefrontMediaKind.Model3D, ".glb") => "model/gltf-binary",
            (StorefrontMediaKind.Model3D, ".gltf") => "model/gltf+json",
            _ => null
        };
        if (type is null || !(string.Equals(source.ContentType, type, StringComparison.OrdinalIgnoreCase) ||
            source.Kind == StorefrontMediaKind.Model3D && source.ContentType == "application/octet-stream")) return null;
        try
        {
            root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (!full.StartsWith(root + Path.DirectorySeparatorChar, comparison)) return null;
            // Reject links/reparse points on every segment, including the configured root and file.
            var current = root;
            if (!Directory.Exists(current) || (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return null;
            foreach (var part in relative.Split('/'))
            {
                current = Path.Combine(current, part);
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return null;
            }
            var file = new FileInfo(full);
            var max = source.Kind == StorefrontMediaKind.Image ? StorefrontVisualLimits.ImageInputBytes : StorefrontVisualLimits.ModelTransferBytes;
            if (!file.Exists || file.Length <= 0 || file.Length > max) return null;
            var content = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read, 16 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var transferred = false;
            try
            {
                // The file can be replaced/grown between the pre-open stat and
                // this handle. Cap images before any raw/resize fallback as well.
                var openedLength = content.Length;
                if (openedLength <= 0 || openedLength > max) return null;
                if (source.Kind == StorefrontMediaKind.Model3D)
                {
                    if (!await ModelReaders.WaitAsync(0, ct)) return null;
                    try
                    {
                        using var modelTiming = StorefrontRequestTiming.Measure(context, "media_model");
                        var bytes = new byte[checked((int)openedLength)];
                        await content.ReadExactlyAsync(bytes, ct);
                        if (StorefrontModelInspector.Inspect(bytes, type) is null) return null;
                        ct.ThrowIfCancellationRequested();
                        return new(new MemoryStream(bytes, writable: false), type);
                    }
                    finally { ModelReaders.Release(); }
                }
                // This precedes ALL raw/cache/busy/unsupported-codec fallbacks. The header
                // reader seeks over metadata and never buffers a full image on this path.
                if (!await HeaderReaders.WaitAsync(0, ct)) return null;
                StorefrontImageInfo? header;
                try { using var headerTiming = StorefrontRequestTiming.Measure(context, "media_header"); header = await StorefrontImageHeader.ReadAsync(content, ct); }
                finally { HeaderReaders.Release(); }
                if (header is not { WithinImageBudget: true } safe || safe.ContentType != type) return null;
                var raw = new StorefrontMediaFile(content, type);
                if ((width.HasValue || safe.IsAnimated) && images is not null)
                {
                    var scope = configuration["StorefrontHost:Host"] + "|" + configuration["StorefrontHost:StoreSubdomain"] + "|" + id + "|" + source.StoragePath;
                    var output = await images.ResizeForRequestAsync(raw, width ?? 960, scope, context, ct);
                    // Never expose an animated original after a failed/busy static conversion.
                    if (safe.IsAnimated && ReferenceEquals(output, raw)) return null;
                    transferred = ReferenceEquals(output, raw);
                    return output;
                }
                if (safe.IsAnimated) return null;
                transferred = true; return raw;
            }
            finally { if (!transferred) await content.DisposeAsync(); }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return null; }
    }
}
