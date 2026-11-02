using GaoApp.Application.Interfaces.Services.Invoices;

namespace GaoApp.Infrastructure.Services.Invoices;

public partial class InvoiceFileStorage : IBoundedInvoiceFileStorage
{
    public async Task<(byte[] Bytes, string ContentType, string FileName)?> ReadBoundedAsync(string storedPath, long maximumBytes, CancellationToken ct)
    {
        if (maximumBytes is < 1 or > 20L * 1024 * 1024) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        var relative = storedPath.Replace('\\', '/').TrimStart('/');
        if (relative.StartsWith("uploads/", StringComparison.OrdinalIgnoreCase)) relative = relative[8..];
        relative = NormalizeRelative(relative);
        var path = ResolveInsideUploadRoot(GetUploadRootFullPath(), relative);
        if (!File.Exists(path)) return null;
        for (var directory = new FileInfo(path).Directory; directory is not null; directory = directory.Parent)
            if (directory.LinkTarget is not null || directory.Attributes.HasFlag(FileAttributes.ReparsePoint))
                throw new InvalidDataException("InvoiceArtifactPathRejected");
        var info = new FileInfo(path);
        if (info.LinkTarget is not null || info.Attributes.HasFlag(FileAttributes.ReparsePoint) || info.Length > maximumBytes)
            throw new InvalidDataException("InvoiceArtifactSizeRejected");
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous);
        if (input.Length is < 1 || input.Length > maximumBytes) throw new InvalidDataException("InvoiceArtifactSizeRejected");
        await using var output = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await input.ReadAsync(buffer, ct)) > 0)
        {
            if (output.Length + read > maximumBytes) throw new InvalidDataException("InvoiceArtifactSizeRejected");
            await output.WriteAsync(buffer.AsMemory(0, read), ct);
        }
        return (output.ToArray(), ResolveContentType(info.Name), info.Name);
    }
}
