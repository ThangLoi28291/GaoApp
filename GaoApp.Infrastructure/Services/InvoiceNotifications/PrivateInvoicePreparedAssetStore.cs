using System.Security.Cryptography;
using System.Text.RegularExpressions;
using GaoApp.Application.InvoiceNotifications;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;

namespace GaoApp.Infrastructure.Services.InvoiceNotifications;

public sealed class PrivateInvoicePreparedAssetStore : IInvoicePreparedAssetReader
{
    private readonly string root;
    private readonly long quota;
    private readonly SemaphoreSlim gate = new(1, 1);
    public PrivateInvoicePreparedAssetStore(IOptions<InvoiceNotificationArtifactOptions> options, IHostEnvironment environment, IConfiguration? configuration = null)
    {
        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.RootDirectory) || settings.MaximumStoredBytes < 50L * 1024 * 1024)
            throw new InvalidOperationException("InvoiceArtifactConfigurationInvalid");
        root = Path.GetFullPath(settings.RootDirectory, environment.ContentRootPath);
        var webRoot = Path.GetFullPath(Path.Combine(environment.ContentRootPath, "wwwroot"));
        if (root.Equals(webRoot, StringComparison.OrdinalIgnoreCase) || root.StartsWith(webRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || Path.GetPathRoot(root) == root)
            throw new InvalidOperationException("InvoiceArtifactsMustBePrivate");
        var actualWebRoot = environment is IWebHostEnvironment web && !string.IsNullOrWhiteSpace(web.WebRootPath) ? Path.GetFullPath(web.WebRootPath) : webRoot;
        var uploads = configuration?["Storage:UploadRoot"];
        var publicUploads = Path.GetFullPath(string.IsNullOrWhiteSpace(uploads) ? "wwwroot/uploads" : uploads, environment.ContentRootPath);
        foreach (var publicRoot in new[] { actualWebRoot, publicUploads })
            if (root.Equals(publicRoot, StringComparison.OrdinalIgnoreCase) || root.StartsWith(publicRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                publicRoot.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("InvoiceArtifactsMustBePrivate");
        quota = settings.MaximumStoredBytes;
    }

    internal async Task<InvoicePreparedPart> SaveAsync(string kind, string fileName, string contentType, byte[] bytes, CancellationToken ct)
    {
        fileName = InvoiceNotificationMediaPolicy.NormalizeFileName(fileName);
        var maximum = kind == "Image" ? 10L * 1024 * 1024 : 20L * 1024 * 1024;
        if (kind is not ("Image" or "File") || bytes.LongLength is < 1 || bytes.LongLength > maximum ||
            InvoiceNotificationMediaPolicy.Validate(kind, fileName, bytes) != contentType)
            throw new InvalidDataException("InvoiceArtifactInvalid");
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        await gate.WaitAsync(ct);
        try
        {
            Directory.CreateDirectory(root); EnsureDirectory();
            var destination = Resolve(hash);
            if (File.Exists(destination))
            {
                var existing = await ReadCheckedAsync(hash, bytes.LongLength, hash, ct);
                if (!existing.AsSpan().SequenceEqual(bytes)) throw new InvalidDataException("InvoiceArtifactConflict");
            }
            else
            {
                var existingBytes = new DirectoryInfo(root).EnumerateFiles().Sum(f => f.Length);
                if (existingBytes > quota - bytes.LongLength) throw new InvalidDataException("InvoiceArtifactQuotaExceeded");
                var temporary = Path.Combine(root, "." + Guid.NewGuid().ToString("N") + ".tmp");
                try
                {
                    await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.WriteThrough))
                    { await output.WriteAsync(bytes, ct); await output.FlushAsync(ct); }
                    // Content-addressed files are immutable. A competing writer may publish identical content.
                    try { File.Move(temporary, destination, overwrite: false); }
                    catch (IOException) when (File.Exists(destination))
                    { await ReadCheckedAsync(hash, bytes.LongLength, hash, ct); }
                    File.SetAttributes(destination, FileAttributes.ReadOnly);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
        }
        finally { gate.Release(); }
        return new(kind, null, fileName, contentType, bytes.LongLength, hash, hash);
    }

    public async Task<byte[]> ReadAsync(InvoiceNotificationWorkItem item, InvoicePreparedPart part, CancellationToken ct)
    {
        if (item.StoreId <= 0 || item.CustomerId <= 0 || item.Snapshot.StoreId != item.StoreId || item.Snapshot.CustomerId != item.CustomerId ||
            item.Binding.StoreId != item.StoreId || item.Binding.CustomerId != item.CustomerId || item.Prepared is null ||
            !item.Prepared.Parts.Contains(part) || part.Kind is not ("Image" or "File") || part.StorageKey is null ||
            part.ByteLength is not > 0 || part.Sha256 is null || part.ContentType is null || part.FileName is null)
            throw new InvalidDataException("InvoiceArtifactScopeRejected");
        var bytes = await ReadCheckedAsync(part.StorageKey, part.ByteLength.Value, part.Sha256, ct);
        if (InvoiceNotificationMediaPolicy.Validate(part.Kind, part.FileName, bytes) != part.ContentType)
            throw new InvalidDataException("InvoiceArtifactInvalid");
        return bytes;
    }
    private string Resolve(string key)
    {
        if (!Regex.IsMatch(key, "\\A[a-f0-9]{64}\\z")) throw new InvalidDataException("InvoiceArtifactKeyInvalid");
        return Path.Combine(root, key);
    }
    private void EnsureDirectory()
    {
        for (var d = new DirectoryInfo(root); d is not null; d = d.Parent)
            if (d.LinkTarget is not null || d.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new InvalidDataException("InvoiceArtifactPathRejected");
    }
    private async Task<byte[]> ReadCheckedAsync(string key, long expectedLength, string expectedHash, CancellationToken ct)
    {
        EnsureDirectory();
        if (expectedLength is < 1 or > 20L * 1024 * 1024 || !key.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("InvoiceArtifactMetadataInvalid");
        var file = new FileInfo(Resolve(key));
        if (!file.Exists || file.LinkTarget is not null || file.Attributes.HasFlag(FileAttributes.ReparsePoint) || file.Length != expectedLength)
            throw new InvalidDataException("InvoiceArtifactMissingOrChanged");
        var bytes = await File.ReadAllBytesAsync(file.FullName, ct);
        if (bytes.LongLength != expectedLength || !Convert.ToHexString(SHA256.HashData(bytes)).Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("InvoiceArtifactMissingOrChanged");
        return bytes;
    }
}
